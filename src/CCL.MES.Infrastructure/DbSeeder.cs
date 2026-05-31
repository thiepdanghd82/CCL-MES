using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Infrastructure;

public static class DbSeeder
{
    public static async Task SeedAsync(MesDbContext db)
    {
        // NB: the Phase 2 admin/admin demo account is seeded from the Web
        // project (Program.cs) because PasswordHasher<User> lives in
        // Microsoft.AspNetCore.Identity and Infrastructure is a plain
        // class lib — we don't want to drag AspNetCore.App into it.

        // Phase 6 Bước 7 — IQC demo seed runs INDEPENDENTLY of the
        // WO seed below (each block has its own .Any() idempotent gate).
        // Without this split, IQC seed would never fire on existing
        // DBs because the WO-block early-return below skips everything.
        await SeedDemoIqcAsync(db);

        if (await db.WorkOrders.AnyAsync()) return;

        // Máy
        var machine = new Machine
        {
            Code = "ACNC3", Name = "CNC 3-Heads", Type = "CNC",
            CurrentState = ProductionEventType.Idle, IdealCycleTimeSec = 0.4
        };
        db.Machines.Add(machine);

        // Lý do dừng máy
        db.DowntimeReasons.AddRange(
            new DowntimeReason { Code = "SETUP",  Name = "Cài đặt / cân máy", Category = "Planned" },
            new DowntimeReason { Code = "MATERIAL", Name = "Chờ vật tư", Category = "Unplanned" },
            new DowntimeReason { Code = "BREAKDOWN", Name = "Hỏng máy", Category = "Unplanned" },
            new DowntimeReason { Code = "QC_HOLD", Name = "Giữ do QC", Category = "Unplanned" });
        await db.SaveChangesAsync();

        var customer = new Customer { Code = "BRADY", Name = "Brady Asia" };
        var product = new Product { ProductCode = "BRD-7656-D", Name = "PCB ID Label 20x8mm", Customer = customer };
        customer.Products.Add(product);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var spec = new Spec { ProductId = product.Id, SpecCode = "SPEC-BRD-7656-D", Title = "PCB ID Label 20x8mm" };
        var ver = new SpecVersion
        {
            VersionNo = 1, Status = SpecStatus.Approved, EffectiveDate = DateTime.UtcNow,
            ApprovedBy = "qa.lead", ApprovedAt = DateTime.UtcNow,
            Parameters =
            {
                new SpecParameter { ParamName = "Width",  Nominal = "20", TolMin = "19.9", TolMax = "20.1", Uom = "mm", IsCritical = true },
                new SpecParameter { ParamName = "Height", Nominal = "8",  TolMin = "7.9",  TolMax = "8.1",  Uom = "mm", IsCritical = true },
                new SpecParameter { ParamName = "Process", Nominal = "Silkscreen + Diecut", Uom = "", IsCritical = false }
            }
        };
        spec.Versions.Add(ver);
        db.Specs.Add(spec);
        await db.SaveChangesAsync();

        // Work Instruction (bước OP Setting)
        var wi = new WorkInstruction
        {
            Title = "WI - Cài đặt máy in nhãn BRD-7656-D",
            ProductId = product.Id, ProcessStep = ProcessStepCode.OpSetting,
            MachineCode = "ACNC3", VersionNo = 1, Status = WiStatus.Approved, EffectiveDate = DateTime.UtcNow,
            Steps =
            {
                new WiStepDetail { Sequence = 1, Description = "Kiểm tra plate & cutter đúng mã BRD-7656-D." },
                new WiStepDetail { Sequence = 2, Description = "Lắp cuộn vật liệu, căn lề theo spec Width 20mm.", WarningNote = "Mang găng tay sạch khi thao tác." },
                new WiStepDetail { Sequence = 3, Description = "Cân chỉnh 3 đầu CNC, chạy mẫu thử 5 cái." },
                new WiStepDetail { Sequence = 4, Description = "Đối chiếu mẫu với spec, xác nhận trước khi gọi IPQC." }
            }
        };
        db.WorkInstructions.Add(wi);
        await db.SaveChangesAsync();

        var wo = new WorkOrder
        {
            WoNo = "WO-26-3683",
            CustomerId = customer.Id, ProductId = product.Id, ProductName = product.Name,
            SpecVersionId = ver.Id, MachineCode = "ACNC3", MachineName = "CNC 3-Heads",
            TargetQty = 12000, Uom = "pcs",
            CurrentStep = ProcessStepCode.PrePressCheck, Status = WoStatus.Draft,
            PlannedStart = DateTime.UtcNow
        };
        db.WorkOrders.Add(wo);
        await db.SaveChangesAsync();
    }

    private static async Task SeedDemoIqcAsync(MesDbContext db)
    {
        if (await db.IqcInspections.AnyAsync()) return;

        var refDate = DateTime.UtcNow.Date;
        // Resolve RawMaterial.Id for 3 realistic part numbers if catalog
        // has them. Hybrid FK: if catalog miss, FK stays null + PartNo
        // text survives (matches Q1 design).
        async Task<long?> ResolveAsync(string partNo)
        {
            var rm = await db.RawMaterials.FirstOrDefaultAsync(x => x.PartNo == partNo);
            return rm?.Id;
        }

        var seed = new[]
        {
            // 1 Pending — đợi QC approve
            new IqcInspection
            {
                RawMaterialId = await ResolveAsync("RM-PVC-001"),
                PartNo = "RM-PVC-001",
                BatchNumber = "BATCH-2026-05-A12",
                LotNumber = "LOT-001",
                ReceivedDate = refDate.AddDays(-1),
                SupplierName = "Avery Dennison VN",
                Quantity = 250.5,
                UomQty = "kg",
                InspectorId = "qc",
                SampleSize = 10,
                Result = QcResult.Pending,
                Details =
                {
                    new IqcResultDetail { ItemName = "Visual",   Pass = true,  Qty = 10 },
                    new IqcResultDetail { ItemName = "Width",    MeasuredValue = "300mm", Pass = true, Qty = 10 },
                    new IqcResultDetail { ItemName = "Adhesion", Pass = true,  Qty = 10 },
                }
            },
            // 2 Pass — đã approved
            new IqcInspection
            {
                RawMaterialId = await ResolveAsync("RM-INK-002"),
                PartNo = "RM-INK-002",
                BatchNumber = "INK-2026-05-007",
                ReceivedDate = refDate.AddDays(-3),
                SupplierName = "Toyo Ink VN",
                Quantity = 50,
                UomQty = "L",
                InspectorId = "qc",
                SampleSize = 5,
                Result = QcResult.Pass,
                ApprovedBy = "qc",
                ApprovedAt = refDate.AddDays(-2),
                Details =
                {
                    new IqcResultDetail { ItemName = "Viscosity", MeasuredValue = "120 cps", Pass = true, Qty = 5 },
                    new IqcResultDetail { ItemName = "Color",     MeasuredValue = "Pantone 285C", Pass = true, Qty = 5 },
                }
            },
            // 3 Fail — out-of-spec, operator quarantine ngoài app
            new IqcInspection
            {
                RawMaterialId = await ResolveAsync("RM-CORE-003"),
                PartNo = "RM-CORE-003",
                BatchNumber = "CORE-2026-04-099",
                ReceivedDate = refDate.AddDays(-7),
                SupplierName = "Vietnam Paper Tube Co.",
                Quantity = 100,
                UomQty = "pcs",
                InspectorId = "qc",
                SampleSize = 20,
                Result = QcResult.Fail,
                ApprovedBy = "qc",
                ApprovedAt = refDate.AddDays(-6),
                Details =
                {
                    new IqcResultDetail { ItemName = "Outer Diameter", MeasuredValue = "76.5mm", Pass = false, DefectCode = "DIM-OOT", Qty = 3 },
                    new IqcResultDetail { ItemName = "Wall Thickness", MeasuredValue = "3.2mm", Pass = true, Qty = 20 },
                }
            },
        };
        db.IqcInspections.AddRange(seed);
        await db.SaveChangesAsync();
    }
}
