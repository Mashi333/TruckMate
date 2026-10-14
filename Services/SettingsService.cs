using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TruckMate.Data;

namespace TruckMate.Services;

public class SettingsService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SettingsService> _logger;
    private AppSettings _cache = new();

    public SettingsService(IServiceScopeFactory scopeFactory, ILogger<SettingsService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public AppSettings Current => _cache;

    public async Task ReloadAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.SystemSettings.AsNoTracking().ToDictionaryAsync(x => x.Key, x => x.Value);

        var s = new AppSettings();
        s.PendingExpiryDays = GetInt(rows, SettingKeys.PendingExpiryDays, s.PendingExpiryDays);
        s.RequireReasonForManualSave = GetBool(rows, SettingKeys.RequireReasonForManualSave, s.RequireReasonForManualSave);
        s.RequireReasonForReprint = GetBool(rows, SettingKeys.RequireReasonForReprint, s.RequireReasonForReprint);
        s.TicketFooterText = GetStr(rows, SettingKeys.TicketFooterText, s.TicketFooterText);
        s.PdfSaveFolder = GetStr(rows, SettingKeys.PdfSaveFolder, s.PdfSaveFolder);
        s.AutoSavePdfWhenNotPrinted = GetBool(rows, SettingKeys.AutoSavePdfWhenNotPrinted, s.AutoSavePdfWhenNotPrinted);
        s.ScalePort = GetStr(rows, SettingKeys.ScalePort, s.ScalePort);
        s.IndicatorPort = GetStr(rows, SettingKeys.IndicatorPort, s.IndicatorPort);
        s.Camera = GetStr(rows, SettingKeys.Camera, s.Camera);
        s.BarrierController = GetStr(rows, SettingKeys.BarrierController, s.BarrierController);
        s.CompanyName = GetStr(rows, SettingKeys.CompanyName, s.CompanyName);

        _cache = s;
        _logger.LogInformation("Settings loaded. ExpiryDays={Days}", s.PendingExpiryDays);
    }

    public async Task SaveAsync(AppSettings settings)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await Upsert(db, SettingKeys.PendingExpiryDays, settings.PendingExpiryDays.ToString(CultureInfo.InvariantCulture));
        await Upsert(db, SettingKeys.RequireReasonForManualSave, settings.RequireReasonForManualSave.ToString());
        await Upsert(db, SettingKeys.RequireReasonForReprint, settings.RequireReasonForReprint.ToString());
        await Upsert(db, SettingKeys.TicketFooterText, settings.TicketFooterText);
        await Upsert(db, SettingKeys.PdfSaveFolder, settings.PdfSaveFolder);
        await Upsert(db, SettingKeys.AutoSavePdfWhenNotPrinted, settings.AutoSavePdfWhenNotPrinted.ToString());
        await Upsert(db, SettingKeys.ScalePort, settings.ScalePort);
        await Upsert(db, SettingKeys.IndicatorPort, settings.IndicatorPort);
        await Upsert(db, SettingKeys.Camera, settings.Camera);
        await Upsert(db, SettingKeys.BarrierController, settings.BarrierController);
        await Upsert(db, SettingKeys.CompanyName, settings.CompanyName);

        await db.SaveChangesAsync();
        await ReloadAsync();
    }

    private static async Task Upsert(AppDbContext db, string key, string value)
    {
        var row = await db.SystemSettings.FirstOrDefaultAsync(x => x.Key == key);
        if (row is null)
        {
            db.SystemSettings.Add(new Domain.SystemSetting { Key = key, Value = value });
        }
        else
        {
            row.Value = value;
        }
    }

    private static string GetStr(Dictionary<string, string> rows, string key, string fallback)
        => rows.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

    private static int GetInt(Dictionary<string, string> rows, string key, int fallback)
        => rows.TryGetValue(key, out var v) && int.TryParse(v, out var i) ? i : fallback;

    private static bool GetBool(Dictionary<string, string> rows, string key, bool fallback)
        => rows.TryGetValue(key, out var v) && bool.TryParse(v, out var b) ? b : fallback;
}
