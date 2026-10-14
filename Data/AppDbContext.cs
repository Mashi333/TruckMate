using Microsoft.EntityFrameworkCore;
using TruckMate.Domain;

namespace TruckMate.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Operator> Operators => Set<Operator>();
    public DbSet<Material> Materials => Set<Material>();
    public DbSet<MasterItem> MasterItems => Set<MasterItem>();
    public DbSet<WeighingTransaction> WeighingTransactions => Set<WeighingTransaction>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<ScaleCheck> ScaleChecks => Set<ScaleCheck>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Operator>(e =>
        {
            e.ToTable("operators");
            e.HasKey(x => x.OperatorId);
            e.Property(x => x.Username).HasMaxLength(50).IsRequired();
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.FullName).HasMaxLength(100).IsRequired();
            e.Property(x => x.PasswordHash).IsRequired();
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.IsActive).HasDefaultValue(true);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        b.Entity<Material>(e =>
        {
            e.ToTable("materials");
            e.HasKey(x => x.MaterialId);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.IsActive).HasDefaultValue(true);
        });

        b.Entity<MasterItem>(e =>
        {
            e.ToTable("master_items");
            e.HasKey(x => x.MasterItemId);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.HasIndex(x => new { x.Type, x.Name }).IsUnique();
            e.Property(x => x.IsActive).HasDefaultValue(true);
        });

        b.Entity<WeighingTransaction>(e =>
        {
            e.ToTable("weighing_transactions", t =>
            {
                t.HasCheckConstraint("second_round_complete",
                    "(Status = 'COMPLETED' AND SecondWeight IS NOT NULL AND SecondWeighedAt IS NOT NULL AND SecondOperatorId IS NOT NULL AND NetWeight IS NOT NULL) OR " +
                    "(Status IN ('PENDING','NOT_COMPLETED') AND SecondWeight IS NULL AND NetWeight IS NULL)");
            });
            e.HasKey(x => x.TransactionId);
            e.Property(x => x.TicketNo).HasMaxLength(20).IsRequired();
            e.HasIndex(x => x.TicketNo).IsUnique();
            e.Property(x => x.VehicleNo).HasMaxLength(20).IsRequired();
            e.Property(x => x.Direction).HasConversion<string>().HasMaxLength(3);
            e.Property(x => x.SubType).HasMaxLength(100);
            e.Property(x => x.Supplier).HasMaxLength(100);
            e.Property(x => x.Transporter).HasMaxLength(100);
            e.Property(x => x.DriverDetails).HasColumnType("text");
            e.Property(x => x.TransactionType).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.OrderNo).HasMaxLength(50).IsRequired();
            e.Property(x => x.Remark).HasColumnType("text");
            e.Property(x => x.SecondRemark).HasColumnType("text");
            e.Property(x => x.FirstWeight).HasColumnType("decimal(18,3)");
            e.Property(x => x.SecondWeight).HasColumnType("decimal(18,3)");
            e.Property(x => x.NetWeight).HasColumnType("decimal(18,3)");
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(15).HasDefaultValue(TransactionStatus.PENDING);
            e.Property(x => x.IsCancelled).HasDefaultValue(false);
            e.Property(x => x.CancelReason).HasColumnType("text");
            e.Property(x => x.RowVersion).HasDefaultValue(1);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            e.HasOne(x => x.Material).WithMany(m => m.Transactions).HasForeignKey(x => x.MaterialId);
            e.HasOne(x => x.FirstOperator).WithMany(o => o.FirstWeighings).HasForeignKey(x => x.FirstOperatorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.SecondOperator).WithMany(o => o.SecondWeighings).HasForeignKey(x => x.SecondOperatorId).OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => new { x.VehicleNo, x.Status });
            e.HasIndex(x => new { x.Status, x.FirstWeighedAt });
            e.HasIndex(x => x.VehicleNo).IsUnique().HasFilter("Status = 'PENDING' AND IsCancelled = 0").HasDatabaseName("uq_wt_one_pending_per_vehicle");
        });

        b.Entity<Ticket>(e =>
        {
            e.ToTable("tickets");
            e.HasKey(x => x.TicketId);
            e.Property(x => x.OutputType).HasConversion<string>().HasMaxLength(5);
            e.Property(x => x.PdfPath).HasMaxLength(500);
            e.Property(x => x.ReprintReason).HasColumnType("text");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.HasOne(x => x.Transaction).WithMany(t => t.Tickets).HasForeignKey(x => x.TransactionId);
            e.HasOne(x => x.Operator).WithMany().HasForeignKey(x => x.OperatorId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ScaleCheck>(e =>
        {
            e.ToTable("scale_checks");
            e.HasKey(x => x.CheckId);
            e.Property(x => x.VehicleNo).HasMaxLength(20).IsRequired();
            e.Property(x => x.ScaleValue).HasColumnType("decimal(18,3)");
            e.Property(x => x.CheckedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.HasOne(x => x.Transaction).WithMany(t => t.ScaleChecks).HasForeignKey(x => x.TransactionId);
            e.HasOne(x => x.Operator).WithMany().HasForeignKey(x => x.OperatorId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<AuditLog>(e =>
        {
            e.ToTable("audit_logs");
            e.HasKey(x => x.AuditId);
            e.Property(x => x.Action).HasMaxLength(50).IsRequired();
            e.Property(x => x.TableName).HasMaxLength(50).IsRequired();
            e.Property(x => x.OldValues).HasColumnType("text");
            e.Property(x => x.NewValues).HasColumnType("text");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TableName, x.RecordId });
        });

        b.Entity<SystemSetting>(e =>
        {
            e.ToTable("system_settings");
            e.HasKey(x => x.SystemSettingId);
            e.Property(x => x.Key).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.Key).IsUnique();
            e.Property(x => x.Value).HasColumnType("text");
        });
    }
}
