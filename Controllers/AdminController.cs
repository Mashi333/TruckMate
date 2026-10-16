using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TruckMate.Data;
using TruckMate.Domain;
using TruckMate.Services;

namespace TruckMate.Controllers;

[Authorize(Roles = "Admin,SuperAdmin")]
public class AdminController : Controller
{
    private readonly AppDbContext _db;
    private readonly SettingsService _settings;
    private readonly AuditService _audit;

    public AdminController(AppDbContext db, SettingsService settings, AuditService audit)
    {
        _db = db;
        _settings = settings;
        _audit = audit;
    }

    public async Task<IActionResult> Index()
    {
        var all = await _db.WeighingTransactions.ToListAsync();
        var today = DateTime.Now.Date;

        ViewBag.WeighedToday = all.Count(t => t.FirstWeighedAt.ToLocalTime().Date == today);
        ViewBag.Pending = all.Count(t => t.Status == TransactionStatus.PENDING && !t.IsCancelled);
        ViewBag.Completed = all.Count(t => t.Status == TransactionStatus.COMPLETED && !t.IsCancelled);
        ViewBag.NotCompleted = all.Count(t => t.Status == TransactionStatus.NOT_COMPLETED && !t.IsCancelled);
        ViewBag.Cancelled = all.Count(t => t.IsCancelled);

        var days = Enumerable.Range(0, 7).Select(i => DateTime.Now.Date.AddDays(-6 + i)).ToList();
        ViewBag.Days = days;
        ViewBag.PerDay = days.Select(d => all.Count(t => t.FirstWeighedAt.ToLocalTime().Date == d)).ToList();
        ViewBag.InCount = all.Count(t => t.Direction == Direction.IN && !t.IsCancelled);
        ViewBag.OutCount = all.Count(t => t.Direction == Direction.OUT && !t.IsCancelled);

        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Records(string status = "All", string? search = null, DateTime? from = null, DateTime? to = null, long? id = null)
    {
        var q = _db.WeighingTransactions.Include(t => t.Material).AsQueryable();
        if (Enum.TryParse<TransactionStatus>(status, out var st)) q = q.Where(t => t.Status == st && !t.IsCancelled);
        if (status == "Cancelled") q = _db.WeighingTransactions.Include(t => t.Material).Where(t => t.IsCancelled);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(t => t.TicketNo.Contains(s) || t.VehicleNo.Contains(s.ToUpper()));
        }
        var list = await q.OrderByDescending(t => t.FirstWeighedAt).Take(200).ToListAsync();

        ViewBag.Status = status;
        ViewBag.Search = search;
        ViewBag.Total = await _db.WeighingTransactions.CountAsync();
        ViewBag.Selected = id is null ? null : await _db.WeighingTransactions
            .Include(t => t.Material).Include(t => t.FirstOperator)
            .FirstOrDefaultAsync(t => t.TransactionId == id);
        return View(list);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelRecord(long id, string? reason)
    {
        var tx = await _db.WeighingTransactions.FirstOrDefaultAsync(t => t.TransactionId == id);
        if (tx is null) return NotFound();
        if (string.IsNullOrWhiteSpace(reason))
        {
            TempData["Error"] = "A reason is required to cancel a record.";
            return RedirectToAction(nameof(Records), new { id });
        }

        var old = new { cancelled = tx.IsCancelled, status = tx.Status.ToString() };
        tx.IsCancelled = true;
        tx.CancelReason = reason;
        tx.RowVersion += 1;
        tx.UpdatedAt = DateTime.UtcNow;
        _audit.Record(User.Id(), "Cancelled", "weighing_transactions", tx.TransactionId,
            oldValues: old, newValues: new { cancelled = true, reason });
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            TempData["Error"] = "This record was just updated by someone else. Check it and try again.";
            return RedirectToAction(nameof(Records), new { id });
        }

        TempData["Success"] = $"Record {tx.TicketNo} cancelled.";
        return RedirectToAction(nameof(Records));
    }

    [HttpGet]
    public async Task<IActionResult> MasterData(string tab = "suppliers")
    {
        ViewBag.Tab = tab;
        ViewBag.Suppliers = await _db.MasterItems.Where(x => x.Type == MasterItemType.SUPPLIER).OrderBy(x => x.Name).ToListAsync();
        ViewBag.Transporters = await _db.MasterItems.Where(x => x.Type == MasterItemType.TRANSPORTER).OrderBy(x => x.Name).ToListAsync();
        ViewBag.Drivers = await _db.MasterItems.Where(x => x.Type == MasterItemType.DRIVER).OrderBy(x => x.Name).ToListAsync();
        ViewBag.SubTypes = await _db.MasterItems.Where(x => x.Type == MasterItemType.SUBTYPE).OrderBy(x => x.Name).ToListAsync();
        ViewBag.Materials = await _db.Materials.OrderBy(x => x.Name).ToListAsync();
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddMasterItem(string tab, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["Error"] = "Enter a name.";
            return RedirectToAction(nameof(MasterData), new { tab });
        }
        var trimmed = name.Trim();

        if (tab == "materials")
        {
            if (!await _db.Materials.AnyAsync(m => m.Name == trimmed)) _db.Materials.Add(new Material { Name = trimmed });
        }
        else
        {
            var type = TypeForTab(tab);
            if (!await _db.MasterItems.AnyAsync(x => x.Type == type && x.Name == trimmed))
                _db.MasterItems.Add(new MasterItem { Type = type, Name = trimmed });
        }
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Added {trimmed}.";
        return RedirectToAction(nameof(MasterData), new { tab });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleMasterItem(string tab, int id)
    {
        if (tab == "materials")
        {
            var m = await _db.Materials.FirstOrDefaultAsync(x => x.MaterialId == id);
            if (m is not null) m.IsActive = !m.IsActive;
        }
        else
        {
            var item = await _db.MasterItems.FirstOrDefaultAsync(x => x.MasterItemId == id);
            if (item is not null) item.IsActive = !item.IsActive;
        }
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(MasterData), new { tab });
    }

    [HttpGet]
    public async Task<IActionResult> Expired()
    {
        var list = await _db.WeighingTransactions.Include(t => t.Material)
            .Where(t => t.Status == TransactionStatus.NOT_COMPLETED && !t.IsCancelled)
            .OrderBy(t => t.FirstWeighedAt)
            .ToListAsync();
        ViewBag.ExpiryDays = _settings.Current.PendingExpiryDays;
        return View(list);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reopen(long id, string? reason)
    {
        var tx = await _db.WeighingTransactions.FirstOrDefaultAsync(t => t.TransactionId == id);
        if (tx is null) return NotFound();
        if (tx.Status != TransactionStatus.NOT_COMPLETED)
        {
            TempData["Error"] = "Only Not completed records can be reopened.";
            return RedirectToAction(nameof(Expired));
        }

        tx.Status = TransactionStatus.PENDING;
        tx.RowVersion += 1;
        tx.UpdatedAt = DateTime.UtcNow;
        _audit.Record(User.Id(), "Reopened", "weighing_transactions", tx.TransactionId,
            oldValues: new { status = "NOT_COMPLETED" },
            newValues: new { status = "PENDING", reason });
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            TempData["Error"] = "This record was just updated by someone else. Check it and try again.";
            return RedirectToAction(nameof(Expired));
        }

        TempData["Success"] = $"Record {tx.TicketNo} reopened to Pending.";
        return RedirectToAction(nameof(Expired));
    }

    public IActionResult Reports() => RedirectToAction("Index", "Reports");

    private static MasterItemType TypeForTab(string tab) => tab switch
    {
        "transporters" => MasterItemType.TRANSPORTER,
        "drivers" => MasterItemType.DRIVER,
        "subtypes" => MasterItemType.SUBTYPE,
        _ => MasterItemType.SUPPLIER
    };
}
