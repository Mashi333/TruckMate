using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TruckMate.Data;
using TruckMate.Domain;
using TruckMate.Services;

namespace TruckMate.Controllers;

[Authorize(Roles = "SuperAdmin")]
public class SuperAdminController : Controller
{
    private readonly AppDbContext _db;
    private readonly SettingsService _settings;
    private readonly AuditService _audit;
    private readonly IPasswordHasher<Operator> _hasher;
    private readonly IScaleService _scale;
    private readonly BrandingService _branding;

    public SuperAdminController(AppDbContext db, SettingsService settings, AuditService audit,
        IPasswordHasher<Operator> hasher, IScaleService scale, BrandingService branding)
    {
        _db = db;
        _settings = settings;
        _audit = audit;
        _hasher = hasher;
        _scale = scale;
        _branding = branding;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var users = await _db.Operators.OrderBy(o => o.OperatorId).ToListAsync();
        return View(users);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateUser(string fullName, string username, string password, OperatorRole role, bool isActive = true)
    {
        fullName = (fullName ?? "").Trim();
        username = (username ?? "").Trim();

        if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            TempData["Error"] = "Full name, username and temporary password are required.";
            return RedirectToAction(nameof(Index));
        }

        if (await _db.Operators.AnyAsync(o => o.Username == username))
        {
            TempData["Error"] = $"Username '{username}' already exists.";
            return RedirectToAction(nameof(Index));
        }

        var op = new Operator
        {
            Username = username,
            FullName = fullName,
            Role = role,
            IsActive = isActive,
            CreatedAt = DateTime.UtcNow
        };
        op.PasswordHash = _hasher.HashPassword(op, password);
        _db.Operators.Add(op);
        await _db.SaveChangesAsync();

        _audit.Record(User.Id(), "CreateUser", "operators", op.OperatorId,
            newValues: new { op.Username, op.FullName, role = op.Role.ToString(), op.IsActive });
        await _db.SaveChangesAsync();

        TempData["Success"] = $"User {username} created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleUser(int id)
    {
        var op = await _db.Operators.FirstOrDefaultAsync(o => o.OperatorId == id);
        if (op is null) return NotFound();
        op.IsActive = !op.IsActive;
        _audit.Record(User.Id(), op.IsActive ? "ActivateUser" : "DeactivateUser", "operators", op.OperatorId,
            newValues: new { op.IsActive });
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public IActionResult Settings()
    {
        ViewBag.Settings = _settings.Current;
        return View(_settings.Current);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Settings(AppSettings model)
    {
        if (model.PendingExpiryDays < 1) model.PendingExpiryDays = 1;
        await _settings.SaveAsync(model);
        _audit.Record(User.Id(), "UpdateSettings", "system_settings", 0, newValues: new { model.PendingExpiryDays });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Settings saved.";
        return RedirectToAction(nameof(Settings));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadLogo(IFormFile? logo)
    {
        if (logo is null || logo.Length == 0)
        {
            TempData["Error"] = "Please choose a logo file to upload.";
            return RedirectToAction(nameof(Settings));
        }

        try
        {
            var url = _branding.Save(logo);
            _audit.Record(User.Id(), "UpdateCompanyLogo", "branding", 0, newValues: new { url });
            await _db.SaveChangesAsync();
            TempData["Success"] = "Company logo updated.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Settings));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveLogo()
    {
        _branding.Remove();
        _audit.Record(User.Id(), "RemoveCompanyLogo", "branding", 0);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Company logo removed.";
        return RedirectToAction(nameof(Settings));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult TestConnections()
    {
        var snap = _scale.GetSnapshot();
        TempData["Success"] = snap.Online
            ? "Connections OK (simulated): indicator online, barriers ready, light ready, camera ready."
            : "Indicator offline: check the cable or restart the indicator.";
        return RedirectToAction(nameof(Settings));
    }

    [HttpGet]
    public async Task<IActionResult> Audit(string? user = null, string? action = null)
    {
        var q = _db.AuditLogs.Include(a => a.User).AsQueryable();
        if (!string.IsNullOrWhiteSpace(user)) q = q.Where(a => a.User != null && a.User.FullName.Contains(user));
        if (!string.IsNullOrWhiteSpace(action)) q = q.Where(a => a.Action.Contains(action));
        var list = await q.OrderByDescending(a => a.CreatedAt).Take(200).ToListAsync();
        ViewBag.User = user;
        ViewBag.Action = action;
        return View(list);
    }
}
