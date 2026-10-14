using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TruckMate.Data;
using TruckMate.Domain;
using TruckMate.Models;
using TruckMate.Services;

namespace TruckMate.Controllers;

[Authorize]
public class OperatorController : Controller
{
    private readonly AppDbContext _db;
    private readonly IScaleService _scale;
    private readonly SettingsService _settings;
    private readonly AuditService _audit;
    private readonly TicketNumberService _tickets;
    private readonly TicketPdfService _pdf;

    public OperatorController(AppDbContext db, IScaleService scale, SettingsService settings,
        AuditService audit, TicketNumberService tickets, TicketPdfService pdf)
    {
        _db = db;
        _scale = scale;
        _settings = settings;
        _audit = audit;
        _tickets = tickets;
        _pdf = pdf;
    }

    public static string NormalizeVehicle(string vehicleNo)
        => (vehicleNo ?? string.Empty).Trim().ToUpperInvariant().Replace(" ", string.Empty).Replace("\t", string.Empty);

    public async Task<IActionResult> Index()
    {
        var pending = await _db.WeighingTransactions
            .Include(t => t.Material)
            .Where(t => t.Status == TransactionStatus.PENDING && !t.IsCancelled)
            .OrderByDescending(t => t.FirstWeighedAt)
            .Take(20)
            .ToListAsync();
        ViewBag.Settings = _settings.Current;
        ViewBag.Company = _settings.Current.CompanyName;
        return View(pending);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Proceed(string vehicleNo)
    {
        var vehicle = NormalizeVehicle(vehicleNo);
        if (string.IsNullOrWhiteSpace(vehicle))
        {
            TempData["Error"] = "Enter a vehicle number to proceed.";
            return RedirectToAction(nameof(Index));
        }

        var snap = _scale.GetSnapshot();
        var pending = await _db.WeighingTransactions
            .FirstOrDefaultAsync(t => t.VehicleNo == vehicle && t.Status == TransactionStatus.PENDING);
        var round = (short)(pending is null ? 1 : 2);

        var passed = snap.Online && snap.Weight == 0m;

        var opId = User.Id() ?? 0;
        _db.ScaleChecks.Add(new ScaleCheck
        {
            TransactionId = pending?.TransactionId,
            VehicleNo = vehicle,
            RoundNo = round,
            ScaleValue = snap.Weight ?? 0m,
            Passed = passed,
            CheckedAt = DateTime.UtcNow,
            OperatorId = opId
        });
        await _db.SaveChangesAsync();

        if (!passed)
        {
            TempData["Error"] = $"Scale is not 0 ({snap.Weight:N0} kg) or the indicator is offline. The vehicle cannot enter. Clear the scale and recheck.";
            return RedirectToAction(nameof(Index));
        }

        return round == 2
            ? RedirectToAction(nameof(SecondWeight), new { vehicleNo = vehicle })
            : RedirectToAction(nameof(FirstWeight), new { vehicleNo = vehicle });
    }

    [HttpGet]
    public async Task<IActionResult> FirstWeight(string? vehicleNo)
    {
        var vm = new FirstWeightViewModel { VehicleNo = NormalizeVehicle(vehicleNo ?? "") };
        await LoadRefDataAsync(vm);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> FirstWeight(FirstWeightViewModel vm)
    {
        await LoadRefDataAsync(vm);
        await EnsureRefSelectionsValidAsync(vm);

        if (!ModelState.IsValid) return View(vm);

        var vehicle = NormalizeVehicle(vm.VehicleNo);
        var snap = _scale.GetSnapshot();

        if (!snap.Online || snap.Weight is null)
        {
            ModelState.AddModelError(string.Empty, "No signal from the indicator. Check the cable or restart the indicator, then recheck.");
            return View(vm);
        }

        var existingPending = await _db.WeighingTransactions
            .AnyAsync(t => t.VehicleNo == vehicle && t.Status == TransactionStatus.PENDING);
        if (existingPending)
        {
            ModelState.AddModelError(string.Empty, $"A pending record already exists for {vehicle}. Start the 2nd weight instead.");
            return View(vm);
        }

        var now = DateTime.UtcNow;
        var tx = new WeighingTransaction
        {
            TicketNo = await _tickets.NextTicketNoAsync(now),
            VehicleNo = vehicle,
            Direction = vm.Direction,
            SubType = vm.SubType,
            Supplier = vm.Supplier,
            MaterialId = vm.MaterialId!.Value,
            Transporter = vm.Transporter,
            DriverDetails = vm.Driver,
            TransactionType = vm.Direction == Direction.IN ? TransactionType.WAREHOUSE : TransactionType.SUPPLY,
            OrderNo = vm.OrderNo.Trim(),
            Remark = vm.Remark,
            FirstWeight = snap.Weight.Value,
            FirstWeighedAt = now,
            FirstOperatorId = User.Id() ?? 0,
            Status = TransactionStatus.PENDING,
            RowVersion = 1,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.WeighingTransactions.Add(tx);
        await _db.SaveChangesAsync();

        _audit.Record(User.Id(), "FirstWeight", "weighing_transactions", tx.TransactionId,
            newValues: new { tx.TicketNo, tx.VehicleNo, tx.FirstWeight, status = tx.Status.ToString() });
        await _db.SaveChangesAsync();

        TempData["Success"] = $"First weight {snap.Weight.Value:N0} kg captured for {vehicle} ({tx.TicketNo}). Status: Pending.";
        return RedirectToAction(nameof(Ticket), new { id = tx.TransactionId, round = 1 });
    }

    [HttpGet]
    public async Task<IActionResult> SecondWeight(string? vehicleNo, long? id)
    {
        var tx = await FindPendingAsync(vehicleNo, id);
        if (tx is null)
        {
            TempData["Error"] = string.IsNullOrWhiteSpace(vehicleNo)
                ? "No pending record found. Check the vehicle number."
                : $"No pending record found for {NormalizeVehicle(vehicleNo!)}. The operator should re-enter or check the vehicle number.";
            return RedirectToAction(nameof(Index));
        }
        return View(BuildSecondWeightVm(tx));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SecondWeight(SecondWeightViewModel vm)
    {
        var tx = await _db.WeighingTransactions
            .Include(t => t.Material).Include(t => t.FirstOperator)
            .FirstOrDefaultAsync(t => t.TransactionId == vm.TransactionId);

        if (tx is null || tx.Status != TransactionStatus.PENDING)
        {
            TempData["Error"] = "No pending record found for this transaction.";
            return RedirectToAction(nameof(Index));
        }

        if (vm.TransactionId != tx.TransactionId)
        {
            TempData["Error"] = "Transaction mismatch.";
            return RedirectToAction(nameof(Index));
        }

        if (_settings.Current.RequireReasonForManualSave && vm.ManualSave && string.IsNullOrWhiteSpace(vm.ManualReason))
        {
            ModelState.AddModelError(nameof(vm.ManualReason), "A reason is required for a manual save.");
        }

        var snap = _scale.GetSnapshot();
        if (!snap.Online || snap.Weight is null)
        {
            ModelState.AddModelError(string.Empty, "No signal from the indicator. Check the cable or restart the indicator, then recheck.");
        }

        if (!ModelState.IsValid)
        {
            var rebuilt = BuildSecondWeightVm(tx);
            rebuilt.SecondRemark = vm.SecondRemark;
            rebuilt.ManualSave = vm.ManualSave;
            rebuilt.ManualReason = vm.ManualReason;
            return View(rebuilt);
        }

        var net = Math.Abs(tx.FirstWeight - snap.Weight!.Value);
        var now = DateTime.UtcNow;
        var old = new { status = tx.Status.ToString() };

        tx.SecondWeight = snap.Weight.Value;
        tx.SecondWeighedAt = now;
        tx.SecondOperatorId = User.Id() ?? 0;
        tx.SecondRemark = vm.SecondRemark;
        tx.NetWeight = net;
        tx.Status = TransactionStatus.COMPLETED;
        tx.RowVersion += 1;
        tx.UpdatedAt = now;

        _audit.Record(User.Id(), vm.ManualSave ? "SecondWeightManual" : "SecondWeight",
            "weighing_transactions", tx.TransactionId,
            oldValues: old,
            newValues: new { tx.SecondWeight, tx.NetWeight, status = tx.Status.ToString(), manual = vm.ManualSave, reason = vm.ManualReason });

        await _db.SaveChangesAsync();

        TempData["Success"] = $"2nd weight {snap.Weight.Value:N0} kg saved. Net weight {net:N0} kg. Status: Completed.";
        return RedirectToAction(nameof(Ticket), new { id = tx.TransactionId, round = 2 });
    }

    [HttpGet]
    public async Task<IActionResult> Ticket(long id, int round = 1)
    {
        var tx = await _db.WeighingTransactions
            .Include(t => t.Material).Include(t => t.FirstOperator).Include(t => t.SecondOperator)
            .Include(t => t.Tickets)
            .FirstOrDefaultAsync(t => t.TransactionId == id);
        if (tx is null) return NotFound();
        ViewBag.Round = round == 2 ? 2 : 1;
        ViewBag.Settings = _settings.Current;
        return View(tx);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> IssueTicket(long id, int round, string outputType, bool duplicate = false)
    {
        var tx = await _db.WeighingTransactions
            .Include(t => t.Material).FirstOrDefaultAsync(t => t.TransactionId == id);
        if (tx is null) return NotFound();

        var s = _settings.Current;

        var isPdf = string.Equals(outputType, "PDF", StringComparison.OrdinalIgnoreCase);
        var bytes = _pdf.BuildTicket(tx, s, duplicate, null);

        var ticket = new Ticket
        {
            TransactionId = tx.TransactionId,
            RoundNo = (short)Math.Clamp(round, 1, 2),
            OutputType = isPdf ? TicketOutputType.PDF : TicketOutputType.PRINT,
            IsReprint = duplicate,
            CreatedAt = DateTime.UtcNow,
            OperatorId = User.Id() ?? 0
        };

        if (isPdf)
        {
            var path = SavePdf(bytes, tx, s, duplicate);
            ticket.PdfPath = path;
            _db.Tickets.Add(ticket);
            _audit.Record(User.Id(), duplicate ? "Reprint" : "PdfSaved", "tickets", tx.TransactionId,
                newValues: new { tx.TicketNo, outputType, path });
            await _db.SaveChangesAsync();
            TempData["Info"] = $"PDF saved: {path}";
            return RedirectToAction(nameof(Ticket), new { id, round });
        }

        _db.Tickets.Add(ticket);
        _audit.Record(User.Id(), duplicate ? "Reprint" : "TicketPrinted", "tickets", tx.TransactionId,
            newValues: new { tx.TicketNo, outputType });
        await _db.SaveChangesAsync();

        return File(bytes, "application/pdf", TickFileName(tx, duplicate));
    }

    [HttpGet]
    public async Task<IActionResult> Reprint(string? search, bool showResults = false)
    {
        var q = _db.WeighingTransactions.Include(t => t.Material).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(t => t.TicketNo.Contains(s) || t.VehicleNo.Contains(s.ToUpper()));
        }
        var list = await q.OrderByDescending(t => t.FirstWeighedAt).Take(25).ToListAsync();

        var reprintCounts = await _db.Tickets.Where(t => t.IsReprint && t.OutputType == TicketOutputType.PRINT)
            .GroupBy(t => t.TransactionId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);

        ViewBag.ReprintCounts = reprintCounts;
        ViewBag.Settings = _settings.Current;
        ViewBag.Search = search;
        return View(list);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PrintDuplicate(long id)
    {
        var tx = await _db.WeighingTransactions.Include(t => t.Material).FirstOrDefaultAsync(t => t.TransactionId == id);
        if (tx is null) return NotFound();

        var s = _settings.Current;

        var bytes = _pdf.BuildTicket(tx, s, duplicate: true, reprintReason: null);
        var round = tx.Status == TransactionStatus.COMPLETED ? (short)2 : (short)1;
        _db.Tickets.Add(new Ticket
        {
            TransactionId = tx.TransactionId,
            RoundNo = round,
            OutputType = TicketOutputType.PRINT,
            IsReprint = true,
            CreatedAt = DateTime.UtcNow,
            OperatorId = User.Id() ?? 0
        });
        _audit.Record(User.Id(), "Reprint", "tickets", tx.TransactionId, newValues: new { tx.TicketNo });
        await _db.SaveChangesAsync();

        return File(bytes, "application/pdf", TickFileName(tx, true));
    }

    [HttpGet]
    public async Task<IActionResult> Transactions(string status = "All", string? search = null)
    {
        var q = _db.WeighingTransactions.Include(t => t.Material).Where(t => !t.IsCancelled);
        if (Enum.TryParse<TransactionStatus>(status, out var st)) q = q.Where(t => t.Status == st);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(t => t.TicketNo.Contains(s) || t.VehicleNo.Contains(s.ToUpper()));
        }
        var list = await q.OrderByDescending(t => t.FirstWeighedAt).Take(100).ToListAsync();
        ViewBag.Status = status;
        ViewBag.Search = search;
        ViewBag.Total = await _db.WeighingTransactions.CountAsync();
        return View(list);
    }

    [HttpGet]
    public async Task<IActionResult> PdfLog(string type = "All")
    {
        ViewBag.Folder = ResolvePdfFolder(_settings.Current);
        var q = _db.Tickets.Include(t => t.Transaction).AsQueryable();
        if (type == "Tickets") q = q.Where(t => t.OutputType == TicketOutputType.PDF);
        var list = await q.OrderByDescending(t => t.CreatedAt).Take(100).ToListAsync();
        ViewBag.Type = type;
        return View(list);
    }

    [HttpGet]
    public async Task<IActionResult> TicketPdfFile(long id)
    {
        var t = await _db.Tickets.FirstOrDefaultAsync(x => x.TicketId == id && x.OutputType == TicketOutputType.PDF);
        if (t?.PdfPath is null || !System.IO.File.Exists(t.PdfPath)) return NotFound();
        var bytes = await System.IO.File.ReadAllBytesAsync(t.PdfPath);
        return File(bytes, "application/pdf", Path.GetFileName(t.PdfPath));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddMaterial(string name)
    {
        var n = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(n)) return BadRequest(new { message = "Enter a material name." });

        var existing = await _db.Materials.FirstOrDefaultAsync(m => m.Name == n);
        if (existing is not null)
        {
            if (!existing.IsActive)
            {
                existing.IsActive = true;
                await _db.SaveChangesAsync();
            }
            return Json(new { id = existing.MaterialId, name = existing.Name });
        }

        var material = new Material { Name = n, IsActive = true };
        _db.Materials.Add(material);
        await _db.SaveChangesAsync();

        _audit.Record(User.Id(), "MaterialAdded", "materials", material.MaterialId, newValues: new { material.Name });
        await _db.SaveChangesAsync();

        return Json(new { id = material.MaterialId, name = material.Name });
    }

    private async Task<WeighingTransaction?> FindPendingAsync(string? vehicleNo, long? id)
    {
        if (id is not null)
            return await _db.WeighingTransactions.Include(t => t.Material).Include(t => t.FirstOperator)
                .FirstOrDefaultAsync(t => t.TransactionId == id && t.Status == TransactionStatus.PENDING);
        if (!string.IsNullOrWhiteSpace(vehicleNo))
        {
            var v = NormalizeVehicle(vehicleNo);
            return await _db.WeighingTransactions.Include(t => t.Material).Include(t => t.FirstOperator)
                .FirstOrDefaultAsync(t => t.VehicleNo == v && t.Status == TransactionStatus.PENDING);
        }
        return null;
    }

    private static SecondWeightViewModel BuildSecondWeightVm(WeighingTransaction tx) => new()
    {
        TransactionId = tx.TransactionId,
        TicketNo = tx.TicketNo,
        VehicleNo = tx.VehicleNo,
        DirectionAndSubType = $"{tx.Direction} / {tx.SubType}",
        Supplier = tx.Supplier,
        Material = tx.Material?.Name,
        TransporterDriver = $"{tx.Transporter} / {tx.DriverDetails}".Trim(' ', '/'),
        FirstWeight = tx.FirstWeight,
        FirstWeighedAt = tx.FirstWeighedAt,
        FirstOperator = tx.FirstOperator?.FullName ?? ""
    };

    private async Task LoadRefDataAsync(FirstWeightViewModel vm)
    {
        vm.Materials = await _db.Materials.Where(m => m.IsActive).OrderBy(m => m.Name)
            .Select(m => new SelectListItem(m.Name, m.MaterialId.ToString())).ToListAsync();

        async Task<List<SelectListItem>> Items(MasterItemType t) =>
            await _db.MasterItems.Where(x => x.Type == t && x.IsActive).OrderBy(x => x.Name)
                .Select(x => new SelectListItem(x.Name, x.Name)).ToListAsync();

        vm.Suppliers = await Items(MasterItemType.SUPPLIER);
        vm.Transporters = await Items(MasterItemType.TRANSPORTER);
        vm.Drivers = await Items(MasterItemType.DRIVER);
        vm.SubTypes = await Items(MasterItemType.SUBTYPE);

        vm.Directions = new List<SelectListItem>
        {
            new("IN", Direction.IN.ToString()),
            new("OUT", Direction.OUT.ToString())
        };
    }

    private async Task EnsureRefSelectionsValidAsync(FirstWeightViewModel vm)
    {
        if (vm.MaterialId is not null && !await _db.Materials.AnyAsync(m => m.MaterialId == vm.MaterialId && m.IsActive))
            ModelState.AddModelError(nameof(vm.MaterialId), "Select a material.");
        if (!string.IsNullOrWhiteSpace(vm.SubType) && !await _db.MasterItems.AnyAsync(x => x.Type == MasterItemType.SUBTYPE && x.Name == vm.SubType))
            ModelState.AddModelError(nameof(vm.SubType), "Select a sub type.");
        if (!string.IsNullOrWhiteSpace(vm.Supplier) && !await _db.MasterItems.AnyAsync(x => x.Type == MasterItemType.SUPPLIER && x.Name == vm.Supplier))
            ModelState.AddModelError(nameof(vm.Supplier), "Select a supplier.");
    }

    private static string TickFileName(WeighingTransaction tx, bool duplicate)
        => tx.TicketNo + (duplicate ? "-duplicate" : "") + ".pdf";

    internal static string ResolvePdfFolder(AppSettings s)
    {
        var folder = s.PdfSaveFolder;
        if (!Path.IsPathRooted(folder))
            folder = Path.Combine(AppContext.BaseDirectory, folder);
        return folder;
    }

    private static string SavePdf(byte[] bytes, WeighingTransaction tx, AppSettings s, bool duplicate)
    {
        var folder = ResolvePdfFolder(s);
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, TickFileName(tx, duplicate));
        System.IO.File.WriteAllBytes(file, bytes);
        return file;
    }
}
