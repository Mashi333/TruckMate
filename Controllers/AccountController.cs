using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TruckMate.Data;
using TruckMate.Domain;
using TruckMate.Models;
using TruckMate.Services;

namespace TruckMate.Controllers;

public class AccountController : Controller
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher<Operator> _hasher;
    private readonly AuditService _audit;

    public AccountController(AppDbContext db, IPasswordHasher<Operator> hasher, AuditService audit)
    {
        _db = db;
        _hasher = hasher;
        _audit = audit;
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", User.HomeController());
        ViewData["ReturnUrl"] = returnUrl;
        return View(new LoginViewModel());
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        if (!ModelState.IsValid) return View(model);

        var username = (model.Username ?? string.Empty).Trim();
        var op = await _db.Operators.FirstOrDefaultAsync(x => x.Username == username);

        if (op is null)
        {
            ModelState.AddModelError(string.Empty, "Incorrect username or password. Check your details and try again.");
            return View(model);
        }

        var result = _hasher.VerifyHashedPassword(op, op.PasswordHash, model.Password ?? string.Empty);
        if (result == PasswordVerificationResult.Failed || !op.IsActive)
        {
            ModelState.AddModelError(string.Empty, "Incorrect username or password. Check your details and try again.");
            return View(model);
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, op.OperatorId.ToString()),
            new(ClaimTypes.Name, op.FullName),
            new(ClaimTypes.Role, op.Role.ToString()),
            new("username", op.Username)
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction("Index", op.Role.ToString());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult ForgotPassword() => View(new ForgotPasswordViewModel());

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var username = (model.Username ?? string.Empty).Trim();
        var op = await _db.Operators.FirstOrDefaultAsync(x => x.Username == username);
        if (op is not null)
        {
            _audit.Record(op.OperatorId, "ResetRequest", "operators", op.OperatorId,
                newValues: new { requestedBy = username, at = DateTime.UtcNow });
            await _db.SaveChangesAsync();
        }

        ViewData["Submitted"] = true;
        ViewData["SubmittedUsername"] = username;
        return View(new ForgotPasswordViewModel());
    }

    [Authorize]
    [HttpGet]
    public IActionResult AccessDenied() => View();
}
