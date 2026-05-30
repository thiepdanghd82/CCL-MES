using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Infrastructure;

public static class DbSeeder
{
    public static async Task SeedAsync(MesDbContext db)
    {
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
}
