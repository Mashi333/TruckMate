using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Infrastructure;
using TruckMate.Data;
using TruckMate.Domain;
using TruckMate.Services;

var builder = WebApplication.CreateBuilder(args);

QuestPDF.Settings.License = LicenseType.Community;

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=truckmate.db"));

builder.Services.AddScoped<IPasswordHasher<Operator>, PasswordHasher<Operator>>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<TicketNumberService>();
builder.Services.AddSingleton<TicketPdfService>();
builder.Services.AddSingleton<IScaleService, MockScaleService>();
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddSingleton<BrandingService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddHostedService<PendingExpiryService>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(opt =>
    {
        opt.LoginPath = "/Account/Login";
        opt.LogoutPath = "/Account/Logout";
        opt.AccessDeniedPath = "/Account/AccessDenied";
        opt.ExpireTimeSpan = TimeSpan.FromHours(10);
        opt.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();
builder.Services.AddControllersWithViews();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

using (var scope = app.Services.CreateScope())
{
    var sp = scope.ServiceProvider;
    var db = sp.GetRequiredService<AppDbContext>();
    var hasher = sp.GetRequiredService<IPasswordHasher<Operator>>();
    await DbSeeder.SeedAsync(db, hasher);
    await sp.GetRequiredService<SettingsService>().ReloadAsync();
}

if (args.Contains("--selftest"))
{
    return await TruckMate.SelfTest.RunAsync(app);
}

app.Run();
return 0;
