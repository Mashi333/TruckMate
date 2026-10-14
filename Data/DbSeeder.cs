using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TruckMate.Domain;

namespace TruckMate.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db, IPasswordHasher<Operator> hasher)
    {
        await db.Database.EnsureCreatedAsync();

        var materials = await SeedMaterialsAsync(db);
        await SeedMasterItemsAsync(db);
        var ops = await SeedOperatorsAsync(db, hasher);
        await SeedSettingsAsync(db);

        if (!await db.WeighingTransactions.AnyAsync())
        {
            await SeedTransactionsAsync(db, materials, ops);
        }
    }

    private static async Task<Dictionary<string, Material>> SeedMaterialsAsync(AppDbContext db)
    {
        if (!await db.Materials.AnyAsync())
        {
            db.Materials.AddRange(
                new Material { Name = "A1" },
                new Material { Name = "test" },
                new Material { Name = "Rice" },
                new Material { Name = "Wheat" });
            await db.SaveChangesAsync();
        }
        return await db.Materials.ToDictionaryAsync(m => m.Name, m => m);
    }

    private static async Task SeedMasterItemsAsync(AppDbContext db)
    {
        if (await db.MasterItems.AnyAsync()) return;

        void Add(MasterItemType type, params string[] names)
        {
            foreach (var n in names)
                db.MasterItems.Add(new MasterItem { Type = type, Name = n });
        }

        Add(MasterItemType.SUPPLIER, "A2", "tEST", "ABC Suppliers");
        Add(MasterItemType.TRANSPORTER, "QQQ", "Rapid Logistics");
        Add(MasterItemType.DRIVER, "AAA", "B", "Sunil");
        Add(MasterItemType.SUBTYPE,
            "Direct import purchasing", "Local Purchasing", "Shipment",
            "Sales", "Empty Damage Bag sale", "Material sale (credit)");
        await db.SaveChangesAsync();
    }

    private static async Task<Dictionary<string, Operator>> SeedOperatorsAsync(AppDbContext db, IPasswordHasher<Operator> hasher)
    {
        if (!await db.Operators.AnyAsync())
        {
            var ops = new List<Operator>
            {
                new() { Username = "operator", FullName = "Operator One", Role = OperatorRole.Operator },
                new() { Username = "operator2", FullName = "Operator Two", Role = OperatorRole.Operator },
                new() { Username = "admin", FullName = "Admin User", Role = OperatorRole.Admin },
                new() { Username = "superadmin", FullName = "Super Admin", Role = OperatorRole.SuperAdmin }
            };

            foreach (var op in ops)
            {
                op.PasswordHash = hasher.HashPassword(op, DefaultPassword(op.Role));
                db.Operators.Add(op);
            }
            await db.SaveChangesAsync();
        }
        return await db.Operators.ToDictionaryAsync(o => o.Username, o => o);
    }

    public static string DefaultPassword(OperatorRole role) => role switch
    {
        OperatorRole.SuperAdmin => "Super@123",
        OperatorRole.Admin => "Admin@123",
        _ => "Operator@123"
    };

    private static async Task SeedSettingsAsync(AppDbContext db)
    {
        if (await db.SystemSettings.AnyAsync()) return;
        var s = new Services.AppSettings();
        db.SystemSettings.AddRange(
            new SystemSetting { Key = Services.SettingKeys.PendingExpiryDays, Value = s.PendingExpiryDays.ToString() },
            new SystemSetting { Key = Services.SettingKeys.RequireReasonForManualSave, Value = s.RequireReasonForManualSave.ToString() },
            new SystemSetting { Key = Services.SettingKeys.RequireReasonForReprint, Value = s.RequireReasonForReprint.ToString() },
            new SystemSetting { Key = Services.SettingKeys.TicketFooterText, Value = s.TicketFooterText },
            new SystemSetting { Key = Services.SettingKeys.PdfSaveFolder, Value = s.PdfSaveFolder },
            new SystemSetting { Key = Services.SettingKeys.AutoSavePdfWhenNotPrinted, Value = s.AutoSavePdfWhenNotPrinted.ToString() },
            new SystemSetting { Key = Services.SettingKeys.ScalePort, Value = s.ScalePort },
            new SystemSetting { Key = Services.SettingKeys.IndicatorPort, Value = s.IndicatorPort },
            new SystemSetting { Key = Services.SettingKeys.Camera, Value = s.Camera },
            new SystemSetting { Key = Services.SettingKeys.BarrierController, Value = s.BarrierController },
            new SystemSetting { Key = Services.SettingKeys.CompanyName, Value = s.CompanyName });
        await db.SaveChangesAsync();
    }

    private static async Task SeedTransactionsAsync(AppDbContext db, Dictionary<string, Material> materials, Dictionary<string, Operator> ops)
    {
        var op1 = ops["operator"];
        var now = DateTime.UtcNow;

        long id = 100;
        WeighingTransaction T(string ticket, string vehicle, Direction dir, string subType, string supplier,
            string material, string? transporter, string? driver, decimal first, int daysAgoFirst,
            decimal? second = null, int? daysAgoSecond = null, decimal? net = null,
            TransactionStatus status = TransactionStatus.PENDING, string? remark = null, string? secondRemark = null)
        {
            id++;
            var firstAt = now.AddDays(-daysAgoFirst);
            var tx = new WeighingTransaction
            {
                TransactionId = id,
                TicketNo = ticket,
                VehicleNo = vehicle,
                Direction = dir,
                SubType = subType,
                Supplier = supplier,
                MaterialId = materials[material].MaterialId,
                Transporter = transporter,
                DriverDetails = driver,
                TransactionType = dir == Direction.IN ? TransactionType.WAREHOUSE : TransactionType.SUPPLY,
                OrderNo = "JOB-" + ticket.Replace("-", ""),
                Remark = remark ?? "ABC",
                FirstWeight = first,
                FirstWeighedAt = firstAt,
                FirstOperatorId = op1.OperatorId,
                SecondWeight = second,
                SecondWeighedAt = daysAgoSecond is null ? null : now.AddDays(-daysAgoSecond.Value),
                SecondOperatorId = second is null ? null : op1.OperatorId,
                SecondRemark = secondRemark,
                NetWeight = net,
                Status = status,
                RowVersion = second is null ? 1 : 2,
                CreatedAt = firstAt,
                UpdatedAt = now
            };
            return tx;
        }

        db.WeighingTransactions.AddRange(
            T("26-00026", "LP-7516", Direction.OUT, "Empty Damage Bag sale", "A2", "A1", null, null, 50m, 2, status: TransactionStatus.PENDING),
            T("26-00025", "LK-05", Direction.OUT, "Empty Damage Bag sale", "tEST", "test", null, null, 50m, 6, 50m, 5, 0m, TransactionStatus.COMPLETED),
            T("26-00024", "LK-04", Direction.IN, "Direct import purchasing", "tEST", "test", "QQQ", "AAA", 50m, 8, 50m, 7, 0m, TransactionStatus.COMPLETED, secondRemark: "TEST1"),
            T("26-00023", "LK-03", Direction.OUT, "Material sale (credit)", "tEST", "test", null, null, 50m, 13, 50m, 12, 0m, TransactionStatus.COMPLETED),
            T("26-00022", "LK-06", Direction.IN, "Direct import purchasing", "tEST", "test", null, null, 1100m, 4, status: TransactionStatus.PENDING),
            T("26-00021", "LK-02", Direction.IN, "Direct import purchasing", "tEST", "test", null, null, 50m, 15, 50m, 14, 0m, TransactionStatus.COMPLETED),
            T("26-00020", "LK-01", Direction.IN, "Local Purchasing", "tEST", "test", null, null, 50m, 16, 50m, 15, 0m, TransactionStatus.COMPLETED),
            T("26-00019", "LK-07", Direction.IN, "Direct import purchasing", "tEST", "test", null, null, 980m, 9, status: TransactionStatus.NOT_COMPLETED),
            T("26-00010", "LP-1", Direction.OUT, "Sales", "tEST", "test", null, null, 170m, 20, 90m, 19, 80m, TransactionStatus.COMPLETED));

        await db.SaveChangesAsync();
    }
}
