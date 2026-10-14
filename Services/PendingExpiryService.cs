using Microsoft.EntityFrameworkCore;
using TruckMate.Data;
using TruckMate.Domain;

namespace TruckMate.Services;

public class PendingExpiryService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PendingExpiryService> _logger;

    public PendingExpiryService(IServiceScopeFactory scopeFactory, ILogger<PendingExpiryService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pending expiry job failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
            catch (TaskCanceledException) { break; }
        }
    }

    public static async Task<int> RunOnceAsync(IServiceScopeFactory scopeFactory, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var settings = scope.ServiceProvider.GetRequiredService<SettingsService>().Current;
        var cutoff = DateTime.UtcNow.AddDays(-settings.PendingExpiryDays);

        var expired = await db.WeighingTransactions
            .Where(t => t.Status == TransactionStatus.PENDING && !t.IsCancelled && t.FirstWeighedAt < cutoff)
            .ToListAsync(ct);

        foreach (var t in expired)
        {
            t.Status = TransactionStatus.NOT_COMPLETED;
            t.RowVersion += 1;
            t.UpdatedAt = DateTime.UtcNow;
            db.AuditLogs.Add(new AuditLog
            {
                UserId = null,
                Action = "NotCompleted",
                TableName = "weighing_transactions",
                RecordId = t.TransactionId,
                OldValues = "{\"status\":\"PENDING\"}",
                NewValues = "{\"status\":\"NOT_COMPLETED\"}",
                CreatedAt = DateTime.UtcNow
            });
        }

        if (expired.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            scope.ServiceProvider.GetRequiredService<ILogger<PendingExpiryService>>()
                .LogInformation("Marked {Count} record(s) as NOT_COMPLETED.", expired.Count);
        }

        return expired.Count;
    }

    private Task RunOnceAsync(CancellationToken ct) => RunOnceAsync(_scopeFactory, ct);
}
