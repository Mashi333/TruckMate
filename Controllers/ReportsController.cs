using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TruckMate.Data;
using TruckMate.Domain;
using TruckMate.Models;
using TruckMate.Services;

namespace TruckMate.Controllers;

[Authorize(Roles = "Operator,Admin,SuperAdmin")]
public class ReportsController : Controller
{
    private readonly AppDbContext _db;
    private readonly SettingsService _settings;
    private readonly ReportService _report;

    public ReportsController(AppDbContext db, SettingsService settings, ReportService report)
    {
        _db = db;
        _settings = settings;
        _report = report;
    }

    [HttpGet]
    public async Task<IActionResult> Index(ReportFilter filter, bool searched = true)
    {
        var vm = await BuildAsync(filter);
        ViewBag.Searched = searched;
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(ReportFilter filter)
    {
        var vm = await BuildAsync(filter);
        ViewBag.Searched = true;
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExportPdf(ReportFilter filter)
    {
        var vm = await BuildAsync(filter);
        var bytes = _report.BuildPdf(vm, _settings.Current);
        return File(bytes, "application/pdf", vm.DocumentNo + ".pdf");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExportExcel(ReportFilter filter)
    {
        var vm = await BuildAsync(filter);
        var bytes = _report.BuildCsv(vm);
        return File(bytes, "text/csv", vm.DocumentNo + ".csv");
    }

    private async Task<ReportResultViewModel> BuildAsync(ReportFilter filter)
    {
        filter.Suppliers = await Options(MasterItemType.SUPPLIER);
        filter.Transporters = await Options(MasterItemType.TRANSPORTER);
        filter.Materials = await _db.Materials.OrderBy(m => m.Name)
            .Select(m => new SelectListItem(m.Name, m.Name)).ToListAsync();
        filter.Directions = new List<SelectListItem>
        {
            new("IN", "IN"), new("OUT", "OUT")
        };
        filter.Statuses = Enum.GetNames<TransactionStatus>()
            .Select(s => new SelectListItem(s, s)).ToList();

        var all = await _db.WeighingTransactions.Include(t => t.Material)
            .OrderByDescending(t => t.FirstWeighedAt).ToListAsync();

        IEnumerable<WeighingTransaction> q = all;
        if (filter.From is not null) { var from = filter.From.Value.Date; q = q.Where(t => t.FirstWeighedAt.ToLocalTime().Date >= from); }
        if (filter.To is not null) { var to = filter.To.Value.Date; q = q.Where(t => t.FirstWeighedAt.ToLocalTime().Date <= to); }
        if (!string.IsNullOrWhiteSpace(filter.VehicleNo)) q = q.Where(t => t.VehicleNo.Contains(filter.VehicleNo.Trim().ToUpperInvariant()));
        if (!string.IsNullOrWhiteSpace(filter.Direction)) q = q.Where(t => t.Direction.ToString() == filter.Direction);
        if (!string.IsNullOrWhiteSpace(filter.Supplier)) q = q.Where(t => t.Supplier == filter.Supplier);
        if (!string.IsNullOrWhiteSpace(filter.Material)) q = q.Where(t => t.Material != null && t.Material.Name == filter.Material);
        if (!string.IsNullOrWhiteSpace(filter.Transporter)) q = q.Where(t => t.Transporter == filter.Transporter);
        if (Enum.TryParse<TransactionStatus>(filter.Status, out var st)) q = q.Where(t => t.Status == st);

        var rows = q.ToList();
        var now = DateTime.Now;
        return new ReportResultViewModel
        {
            Filter = filter,
            Rows = rows,
            TotalRecords = rows.Count,
            CountPending = rows.Count(t => t.Status == TransactionStatus.PENDING),
            CountCompleted = rows.Count(t => t.Status == TransactionStatus.COMPLETED),
            CountNotCompleted = rows.Count(t => t.Status == TransactionStatus.NOT_COMPLETED),
            TotalNetWeight = rows.Sum(t => t.NetWeight ?? 0m),
            DocumentNo = TicketNumberService.ReportDocumentNo(now),
            GeneratedOn = now
        };
    }

    private async Task<List<SelectListItem>> Options(MasterItemType type)
        => await _db.MasterItems.Where(x => x.Type == type).OrderBy(x => x.Name)
            .Select(x => new SelectListItem(x.Name, x.Name)).ToListAsync();
}
