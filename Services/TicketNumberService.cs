using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TruckMate.Data;

namespace TruckMate.Services;

public class TicketNumberService
{
    private readonly AppDbContext _db;

    public TicketNumberService(AppDbContext db) => _db = db;

    public async Task<string> NextTicketNoAsync(DateTime whenUtc)
    {
        var year = whenUtc.Year % 100;
        var prefix = year.ToString("00", CultureInfo.InvariantCulture) + "-";

        var existing = await _db.WeighingTransactions
            .IgnoreQueryFilters()
            .Where(t => t.TicketNo.StartsWith(prefix))
            .Select(t => t.TicketNo)
            .ToListAsync();

        var max = 0;
        foreach (var t in existing)
        {
            var tail = t.Substring(prefix.Length).TrimStart('0');
            if (int.TryParse(tail, out var n) && n > max) max = n;
        }

        return prefix + (max + 1).ToString("00000", CultureInfo.InvariantCulture);
    }

    public static string ReportDocumentNo(DateTime whenUtc)
        => "OPR-" + whenUtc.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);
}
