using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using TruckMate.Domain;

namespace TruckMate.Services;

public class TicketPdfService
{
    private readonly BrandingService _branding;

    public TicketPdfService(BrandingService branding)
    {
        _branding = branding;
    }

    public byte[] BuildTicket(WeighingTransaction t, AppSettings settings, bool duplicate, string? reprintReason)
    {
        var copyLabel = duplicate ? "DUPLICATE COPY" : "ORIGINAL COPY";
        var net = t.NetWeight;
        var logo = _branding.GetLogoBytes();

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A5);
                page.Margin(20);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Column(col =>
                {
                    if (logo is not null)
                    {
                        col.Item().AlignCenter().Height(42).Image(logo).FitHeight();
                    }
                    col.Item().AlignCenter().Text((settings.CompanyName + " WEIGHBRIDGE TICKET").ToUpperInvariant())
                        .FontSize(14).Bold();
                    col.Item().AlignCenter().Text(copyLabel).FontSize(10).Bold();
                    col.Item().PaddingTop(4).LineHorizontal(1);
                });

                page.Content().PaddingVertical(8).Column(main =>
                {
                    main.Item().Row(body =>
                    {
                        body.RelativeItem().Column(left =>
                        {
                            left.Spacing(3);
                            Row(left, "TRANSACTION NO", t.TicketNo);
                            Row(left, "VEHICLE NO", t.VehicleNo);
                            Row(left, "MODE", $"{t.Direction} - {t.SubType}");
                            Row(left, "SUPPLIER", t.Supplier ?? "");
                            Row(left, "MATERIAL", t.Material?.Name ?? "");

                            left.Item().PaddingTop(10);

                            Row(left, "1ST WEIGHT (kg)", FormatNum(t.FirstWeight));
                            Row(left, "2ND WEIGHT (kg)", t.SecondWeight is null ? "" : FormatNum(t.SecondWeight.Value));
                            Row(left, "NET WEIGHT (kg)", net is null ? "" : FormatNum(net.Value));

                            left.Item().PaddingTop(10);

                            Row(left, "1ST REMARK", t.Remark ?? "");
                            Row(left, "2ND REMARK", t.SecondRemark ?? "");
                        });

                        body.ConstantItem(20);

                        body.RelativeItem().Column(right =>
                        {
                            right.Spacing(3);
                            Row(right, "TRANSPORTER", t.Transporter ?? "");
                            Row(right, "DRIVER", t.DriverDetails ?? "");
                            Row(right, "DATE/TIME IN", FormatDt(t.FirstWeighedAt));
                            Row(right, "DATE/TIME OUT", t.SecondWeighedAt is null ? "" : FormatDt(t.SecondWeighedAt.Value));
                        });
                    });

                    if (duplicate && !string.IsNullOrWhiteSpace(reprintReason))
                    {
                        main.Item().PaddingTop(6).Text("REPRINT REASON: " + reprintReason).FontSize(9).Italic();
                    }

                    main.Item().PaddingTop(16).Row(sig =>
                    {
                        sig.RelativeItem().Column(c => { c.Item().Text("OPERATOR SIGNATURE").FontSize(8); c.Item().PaddingTop(14).LineHorizontal(1); });
                        sig.ConstantItem(12);
                        sig.RelativeItem().Column(c => { c.Item().Text("DRIVER SIGNATURE").FontSize(8); c.Item().PaddingTop(14).LineHorizontal(1); });
                        sig.ConstantItem(12);
                        sig.RelativeItem().Column(c => { c.Item().Text("CHECKED BY SUPERVISOR").FontSize(8); c.Item().PaddingTop(14).LineHorizontal(1); });
                    });
                });

                page.Footer().AlignCenter().Text(settings.TicketFooterText).FontSize(7).FontColor(Colors.Grey.Darken1);
            });
        }).GeneratePdf();
    }

    private static void Row(ColumnDescriptor col, string label, string value)
    {
        col.Item().Row(r =>
        {
            r.ConstantItem(120).Text(label + " :").Bold();
            r.RelativeItem().Text(value);
        });
    }

    private static string FormatDt(DateTime dt)
        => dt.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    private static string FormatKg(decimal kg)
        => FormatNum(kg) + " kg";

    private static string FormatNum(decimal kg)
        => kg.ToString("#,##0.###", CultureInfo.InvariantCulture);
}
