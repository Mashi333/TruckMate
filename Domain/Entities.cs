namespace TruckMate.Domain;

public class Operator
{
    public int OperatorId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public OperatorRole Role { get; set; } = OperatorRole.Operator;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<WeighingTransaction> FirstWeighings { get; set; } = new List<WeighingTransaction>();
    public ICollection<WeighingTransaction> SecondWeighings { get; set; } = new List<WeighingTransaction>();
}

public class Material
{
    public int MaterialId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public ICollection<WeighingTransaction> Transactions { get; set; } = new List<WeighingTransaction>();
}

public class MasterItem
{
    public int MasterItemId { get; set; }
    public MasterItemType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class WeighingTransaction
{
    public long TransactionId { get; set; }
    public string TicketNo { get; set; } = string.Empty;
    public string VehicleNo { get; set; } = string.Empty;

    public Direction Direction { get; set; } = Direction.IN;
    public string SubType { get; set; } = string.Empty;
    public string? Supplier { get; set; }
    public int MaterialId { get; set; }
    public Material? Material { get; set; }
    public string? Transporter { get; set; }
    public string? DriverDetails { get; set; }
    public TransactionType TransactionType { get; set; } = TransactionType.WAREHOUSE;
    public string OrderNo { get; set; } = string.Empty;
    public string? Remark { get; set; }

    public decimal FirstWeight { get; set; }
    public DateTime FirstWeighedAt { get; set; } = DateTime.UtcNow;
    public int FirstOperatorId { get; set; }
    public Operator? FirstOperator { get; set; }

    public decimal? SecondWeight { get; set; }
    public DateTime? SecondWeighedAt { get; set; }
    public int? SecondOperatorId { get; set; }
    public Operator? SecondOperator { get; set; }
    public string? SecondRemark { get; set; }

    public decimal? NetWeight { get; set; }

    public TransactionStatus Status { get; set; } = TransactionStatus.PENDING;
    public bool IsCancelled { get; set; }
    public string? CancelReason { get; set; }
    public int RowVersion { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Ticket> Tickets { get; set; } = new List<Ticket>();
    public ICollection<ScaleCheck> ScaleChecks { get; set; } = new List<ScaleCheck>();
}

public class Ticket
{
    public long TicketId { get; set; }
    public long TransactionId { get; set; }
    public WeighingTransaction? Transaction { get; set; }
    public short RoundNo { get; set; }
    public TicketOutputType OutputType { get; set; }
    public bool IsReprint { get; set; }
    public string? ReprintReason { get; set; }
    public string? PdfPath { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int OperatorId { get; set; }
    public Operator? Operator { get; set; }
}

public class ScaleCheck
{
    public long CheckId { get; set; }
    public long? TransactionId { get; set; }
    public WeighingTransaction? Transaction { get; set; }
    public string VehicleNo { get; set; } = string.Empty;
    public short RoundNo { get; set; }
    public decimal ScaleValue { get; set; }
    public bool Passed { get; set; }
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
    public int OperatorId { get; set; }
    public Operator? Operator { get; set; }
}

public class AuditLog
{
    public long AuditId { get; set; }
    public int? UserId { get; set; }
    public Operator? User { get; set; }
    public string Action { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public long RecordId { get; set; }
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class SystemSetting
{
    public int SystemSettingId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
