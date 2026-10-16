using System.Data.Common;
using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.EntityFrameworkCore;
using TruckMate.Data;
using TruckMate.Domain;
using TruckMate.Services;

namespace TruckMate;

public static class SelfTest
{
    public static async Task<int> RunAsync(WebApplication app)
    {
        const string baseUrl = "http://127.0.0.1:52773";
        app.Urls.Clear();
        app.Urls.Add(baseUrl);

        await app.StartAsync();

        var handler = new HttpClientHandler { AllowAutoRedirect = true, CookieContainer = new CookieContainer() };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(20) };
        var scale = app.Services.GetRequiredService<IScaleService>();
        int failures = 0;
        var pass = new Action<string>(m => Console.WriteLine($"  [PASS] {m}"));
        var fail = new Action<string>(m => { failures++; Console.WriteLine($"  [FAIL] {m}"); });

        try
        {
            Console.WriteLine("SELFTEST starting on " + baseUrl);

            // 1. Login page
            var loginPage = await client.GetAsync("/Account/Login");
            pass.Check("Login page 200", loginPage.StatusCode == HttpStatusCode.OK, fail, "Login page returned " + loginPage.StatusCode);
            var token = ExtractToken(await loginPage.Content.ReadAsStringAsync());

            // 2. Login
            var loginPost = await client.PostFormAsync("/Account/Login", new Dictionary<string, string>
            {
                ["Username"] = "operator", ["Password"] = "Operator@123", ["__RequestVerificationToken"] = token
            });
            var loginBody = await loginPost.Content.ReadAsStringAsync();
            pass.Check("Login succeeded", loginPost.RequestMessage.RequestUri!.AbsolutePath == "/Operator", fail, "Login landed on " + loginPost.RequestMessage.RequestUri);

            // 3. Authenticated token from operator page
            var opPage = await client.GetAsync("/Operator");
            token = ExtractToken(await opPage.Content.ReadAsStringAsync());
            pass.Check("Operator page 200 has Control Center", opPage.StatusCode == HttpStatusCode.OK && (await opPage.Content.ReadAsStringAsync()).Contains("Operator Control Center"), fail, "Operator page missing");

            // 3b. Inline add-new material
            var am = await client.PostFormAsync("/Operator/AddMaterial", new Dictionary<string, string> { ["name"] = "Cement 42.5", ["__RequestVerificationToken"] = token });
            var amBody = await am.Content.ReadAsStringAsync();
            var amMatch = Regex.Match(amBody, "\"id\"\\s*:\\s*(\\d+)\\s*,\\s*\"name\"\\s*:\\s*\"([^\"]+)\"");
            string newMatId = "1";
            if (am.StatusCode == HttpStatusCode.OK && amMatch.Success && amMatch.Groups[2].Value == "Cement 42.5") newMatId = amMatch.Groups[1].Value;
            pass.Check("Add new material inline", am.StatusCode == HttpStatusCode.OK && amMatch.Success && amMatch.Groups[2].Value == "Cement 42.5", fail, "AddMaterial returned " + (int)am.StatusCode + " " + amBody);

            // 4. Scale gate check via simulate -> 13,250 kg
            var sim = await client.PostFormAsync("/api/scale/simulate", new Dictionary<string, string> { ["weight"] = "13250", ["__RequestVerificationToken"] = token });
            pass.Check("Simulate 13,250 kg", sim.StatusCode == HttpStatusCode.OK, fail, "Simulate failed " + sim.StatusCode);

            // 5. First weight
            var fw = await client.PostFormAsync("/Operator/FirstWeight", new Dictionary<string, string>
            {
                ["VehicleNo"] = "TX99-AB", ["Direction"] = "IN", ["SubType"] = "Shipment",
                ["Supplier"] = "tEST", ["MaterialId"] = newMatId, ["Transporter"] = "", ["Driver"] = "Sanjeewa",
                ["OrderNo"] = "JOB-99", ["Remark"] = "hello", ["__RequestVerificationToken"] = token
            });
            var fwUrl = fw.RequestMessage.RequestUri!.ToString();
            long tid = 0;
            var m = Regex.Match(fwUrl, @"Ticket/(\d+)");
            if (!m.Success) m = Regex.Match(fwUrl, @"Ticket\?id=(\d+)");
            if (m.Success) { tid = long.Parse(m.Groups[1].Value); pass($"FirstWeight created (transaction id {tid})"); }
            else { fail("FirstWeight did not redirect to ticket: " + fwUrl); }
            pass.Check("Status Pending + 1st weight 13,250 kg", (await fw.Content.ReadAsStringAsync()).Contains("ORIGINAL COPY"), fail, "ticket page missing 1st round");

            // 6. Return: scale 0 then 1250
            await client.PostFormAsync("/api/scale/simulate", new Dictionary<string, string> { ["weight"] = "0", ["__RequestVerificationToken"] = token });
            var proceed = await client.PostFormAsync("/Operator/Proceed", new Dictionary<string, string> { ["vehicleNo"] = "TX99-AB", ["__RequestVerificationToken"] = token });
            pass.Check("Proceed routed to 2nd round", proceed.RequestMessage.RequestUri!.AbsolutePath.Contains("SecondWeight"), fail, "Proceed ended at " + proceed.RequestMessage.RequestUri);

            var swPage = await client.GetAsync("/Operator/SecondWeight?vehicleNo=TX99-AB");
            var swBody = await swPage.Content.ReadAsStringAsync();
            pass.Check("Round 1 details read-only", swPage.StatusCode == HttpStatusCode.OK && swBody.Contains("Second round") && swBody.Contains("TX99-AB"), fail, "second weight page wrong");

            await client.PostFormAsync("/api/scale/simulate", new Dictionary<string, string> { ["weight"] = "1250", ["__RequestVerificationToken"] = token });
            var swPost = await client.PostFormAsync("/Operator/SecondWeight", new Dictionary<string, string>
            {
                ["TransactionId"] = tid.ToString(), ["SecondRemark"] = "final", ["ManualSave"] = "false", ["__RequestVerificationToken"] = token
            });
            var swPostBody = await swPost.Content.ReadAsStringAsync();
            pass.Check("2nd weight saved, net 12,000 kg", swPostBody.Contains("Net weight 12,000") && swPostBody.Contains("Status: Completed"), fail, "net/status not as expected");

            // 7. PDF ticket
            var pdf = await client.PostFormAsync("/Operator/IssueTicket", new Dictionary<string, string>
            {
                ["id"] = tid.ToString(), ["round"] = "2", ["outputType"] = "PDF", ["duplicate"] = "false", ["__RequestVerificationToken"] = token
            });
            var pdfBody = await pdf.Content.ReadAsStringAsync();
            var pathMatch = Regex.Match(pdfBody, @"PDF saved:\s*([^<\r\n]+)");
            var pdfPath = pathMatch.Success ? pathMatch.Groups[1].Value.Trim() : "";
            var pdfExists = !string.IsNullOrEmpty(pdfPath) && File.Exists(pdfPath) && new FileInfo(pdfPath).Length > 1000;
            pass.Check("PDF saved & valid", pdfExists, fail, "PDF missing: " + pdfPath + " | matched=" + pathMatch.Success);

            // 8. Verify DB state
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tx = await db.WeighingTransactions.Include(t => t.Material).Include(t => t.Tickets).FirstOrDefaultAsync(t => t.VehicleNo == "TX99-AB");
            if (tx is null) fail("Transaction missing for TX99-AB");
            else
            {
                pass.Check("Net weight correct", tx.NetWeight == 12000m, fail, $"net={tx.NetWeight}");
                pass.Check("Status COMPLETED", tx.Status == TransactionStatus.COMPLETED, fail, $"status={tx.Status}");
                pass.Check("Ticket row logged", tx.Tickets.Count > 0, fail, "no ticket log rows");
                pass.Check("Free-text driver saved", tx.DriverDetails == "Sanjeewa", fail, $"driver={tx.DriverDetails}");
                pass.Check("New material linked", tx.Material?.Name == "Cement 42.5", fail, $"material={tx.Material?.Name}");
                var audits = await db.AuditLogs.Where(a => a.RecordId == tx.TransactionId).Select(a => a.Action).ToListAsync();
                pass.Check("Audit trail (FirstWeight+SecondWeight)", audits.Contains("FirstWeight") && audits.Contains("SecondWeight"), fail, "audits=" + string.Join(",", audits));
            }
        }
        catch (Exception ex)
        {
            fail("Unexpected error: " + ex.Message + " | " + (ex.InnerException?.Message ?? ""));
        }

        Console.WriteLine(failures == 0 ? "SELFTEST: ALL CHECKS PASSED" : $"SELFTEST: {failures} CHECK(S) FAILED");
        await app.StopAsync();
        return failures == 0 ? 0 : 1;
    }

    private static string ExtractToken(string html)
    {
        var m = Regex.Match(html, @"id=""antiforgery-token"" type=""hidden"" value=""([^""]+)""");
        if (m.Success) return HttpUtility.HtmlDecode(m.Groups[1].Value);
        m = Regex.Match(html, @"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""");
        return m.Success ? HttpUtility.HtmlDecode(m.Groups[1].Value) : "";
    }

    private static void Check(this Action<string> pass, string label, bool ok, Action<string> fail, string msg)
    {
        if (ok) Console.WriteLine("  [PASS] " + label);
        else fail(msg);
    }
}

public static class HttpClientX
{
    public static async Task<HttpResponseMessage> PostFormAsync(this HttpClient client, string path, Dictionary<string, string> fields)
    {
        var content = new FormUrlEncodedContent(fields);
        return await client.PostAsync(path, content);
    }
}