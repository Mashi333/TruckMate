using System.Text.Json;
using TruckMate.Data;
using TruckMate.Domain;

namespace TruckMate.Services;

public class AuditService
{
    private readonly AppDbContext _db;

    public AuditService(AppDbContext db) => _db = db;

    public void Record(int? userId, string action, string tableName, long recordId, object? oldValues = null, object? newValues = null)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = action,
            TableName = tableName,
            RecordId = recordId,
            OldValues = oldValues is null ? null : JsonSerializer.Serialize(oldValues),
            NewValues = newValues is null ? null : JsonSerializer.Serialize(newValues),
            CreatedAt = DateTime.UtcNow
        });
    }
}
