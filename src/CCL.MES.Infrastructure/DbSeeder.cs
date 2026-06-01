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

        // Phase 8 PR #28 — ProcessCatalog seed. Independent of WO seed
        // (own .Any() gate) so migration to existing DBs picks up 17 codes
        // even when WO/Product seed already exists.
        await SeedProcessCatalogAsync(db);

        // Phase 8 PR-D-4 — ReasonCode seed for QC Capture NG reasons + pause
        // codes (CMES sibling parity, 12 codes). Independent .Any() gate.
        await SeedReasonCodesAsync(db);

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

        // Phase 8 PR #28 — Spec/SpecVersion/SpecParameter seed REWRITTEN to
        // ProductRevision + SpecPrint shape. 3 legacy params (Width/Height/
        // Process) folded vào SpecPrint.ColorSpecJson as JSON array for
        // forensic preservation (migration migrate 1 spec sequence ↔ this
        // greenfield seed sequence trùng output).
        var revision = new ProductRevision
        {
            ProductId = product.Id,
            SpecCode = "SPEC-BRD-7656-D",
            Title = "PCB ID Label 20x8mm",
            RevisionCode = "A",
            Status = ProductRevisionStatus.Approved,
            EffectiveFrom = DateTime.UtcNow,
            ApprovedBy = "qa.lead",
            ApprovedAt = DateTime.UtcNow,
            ChangeSummary = "Initial spec baseline (SpecHub revision model)",
            Print = new SpecPrint
            {
                ProcessCode = "SILKSCREEN",
                NumColors = 0,
                ColorSpecJson = "[" +
                    "{\"param_name\":\"Width\",\"nominal\":\"20\",\"tol_min\":\"19.9\",\"tol_max\":\"20.1\",\"uom\":\"mm\",\"is_critical\":true}," +
                    "{\"param_name\":\"Height\",\"nominal\":\"8\",\"tol_min\":\"7.9\",\"tol_max\":\"8.1\",\"uom\":\"mm\",\"is_critical\":true}," +
                    "{\"param_name\":\"Process\",\"nominal\":\"Silkscreen + Diecut\",\"uom\":\"\",\"is_critical\":false}" +
                "]"
            }
        };
        db.ProductRevisions.Add(revision);
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
            ProductRevisionId = revision.Id, MachineCode = "ACNC3", MachineName = "CNC 3-Heads",
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

    /// <summary>
    /// Phase 8 PR #28 — seed 17 ProcessCatalog codes derived from SpecHub
    /// `docs/02-data-model.md` §process_catalog seed block. Idempotent gate
    /// on .Any() — fires once per fresh DB.
    /// Code-string remains the stable lookup token; SpecPrint.ProcessCode +
    /// SpecDiecut.CutProcessCode + SpecFinishing.FinishingProcessesJson đều
    /// reference qua chuỗi này. Admin UI Phase 9 sẽ wire CRUD vào Library tab.
    /// </summary>
    private static async Task SeedProcessCatalogAsync(MesDbContext db)
    {
        if (await db.ProcessCatalogs.AnyAsync()) return;

        db.ProcessCatalogs.AddRange(
            // Print processes
            new ProcessCatalog { Code = "FLEXO",         Category = ProcessCategory.Print,     DisplayNameEn = "Flexography",            DisplayNameVi = "In Flexo",            DisplayOrder = 10 },
            new ProcessCatalog { Code = "LETTERPRESS",   Category = ProcessCategory.Print,     DisplayNameEn = "Letterpress",            DisplayNameVi = "In Letterpress",      DisplayOrder = 20 },
            new ProcessCatalog { Code = "INDIGO",        Category = ProcessCategory.Print,     DisplayNameEn = "HP Indigo (digital)",    DisplayNameVi = "In Indigo",           DisplayOrder = 30 },
            new ProcessCatalog { Code = "INDIGO_PRIMER", Category = ProcessCategory.Print,     DisplayNameEn = "HP Indigo + primer",     DisplayNameVi = "In Indigo có primer", DisplayOrder = 40 },
            new ProcessCatalog { Code = "SILKSCREEN",    Category = ProcessCategory.Print,     DisplayNameEn = "Silkscreen",             DisplayNameVi = "In lụa",              DisplayOrder = 50 },
            new ProcessCatalog { Code = "DIGITAL_UV",    Category = ProcessCategory.Print,     DisplayNameEn = "Digital UV inkjet",      DisplayNameVi = "In UV kỹ thuật số",   DisplayOrder = 60 },
            // Cut processes
            new ProcessCatalog { Code = "FLATBED_CUT",   Category = ProcessCategory.Cut,       DisplayNameEn = "Flatbed die cut",        DisplayNameVi = "Cắt flatbed",         DisplayOrder = 110 },
            new ProcessCatalog { Code = "ROTARY_CUT",    Category = ProcessCategory.Cut,       DisplayNameEn = "Rotary die cut (solid)", DisplayNameVi = "Cắt rotary",          DisplayOrder = 120 },
            new ProcessCatalog { Code = "RDC",           Category = ProcessCategory.Cut,       DisplayNameEn = "Rotary die cut (magnetic)", DisplayNameVi = "RDC (magnetic die)", DisplayOrder = 130 },
            new ProcessCatalog { Code = "POWERPUNCH",    Category = ProcessCategory.Cut,       DisplayNameEn = "Powerpunch semi-rotary", DisplayNameVi = "Cắt Powerpunch",      DisplayOrder = 140 },
            new ProcessCatalog { Code = "CNC",           Category = ProcessCategory.Cut,       DisplayNameEn = "CNC routing",            DisplayNameVi = "Cắt CNC",             DisplayOrder = 150 },
            new ProcessCatalog { Code = "LASER_CUT",     Category = ProcessCategory.Cut,       DisplayNameEn = "Laser cutting",          DisplayNameVi = "Cắt laser",           DisplayOrder = 160 },
            new ProcessCatalog { Code = "KISS_CUT",      Category = ProcessCategory.Cut,       DisplayNameEn = "Kiss cut",               DisplayNameVi = "Cắt kiss-cut",        DisplayOrder = 170 },
            // Finishing processes
            new ProcessCatalog { Code = "VARNISH",       Category = ProcessCategory.Finishing, DisplayNameEn = "Varnish coating",        DisplayNameVi = "Phủ varnish",         DisplayOrder = 210 },
            new ProcessCatalog { Code = "LAMINATION",    Category = ProcessCategory.Finishing, DisplayNameEn = "Lamination",             DisplayNameVi = "Cán màng",            DisplayOrder = 220 },
            new ProcessCatalog { Code = "FOIL_STAMP",    Category = ProcessCategory.Finishing, DisplayNameEn = "Hot foil stamping",      DisplayNameVi = "Ép nhũ",              DisplayOrder = 230 },
            new ProcessCatalog { Code = "EMBOSS",        Category = ProcessCategory.Finishing, DisplayNameEn = "Embossing / debossing",  DisplayNameVi = "Dập nổi / dập chìm",  DisplayOrder = 240 }
        );
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Phase 8 PR-D-4 — seed 12 ReasonCode rows from CMES sibling
    /// `work_orders.seed.json` (8 Pause causes ML-* + 4 Scrap causes SC-*).
    /// Idempotent .Any() gate fires once per fresh DB. Admin CRUD UI deferred
    /// to a future Library tab — for now this is the canonical source of NG
    /// reason codes referenced by SpecQcCapture.NgReasonCode (loose string
    /// FK per CMES pattern, validated in service layer at capture time).
    /// </summary>
    private static async Task SeedReasonCodesAsync(MesDbContext db)
    {
        if (await db.ReasonCodes.AnyAsync()) return;

        db.ReasonCodes.AddRange(
            // Pause causes (ML-* — Machine Lost time).
            new ReasonCode { Code = "ML-MAT",   LabelEn = "Material loading / changeover", LabelVi = "Nạp / đổi vật liệu",         Kind = ReasonCodeKind.Pause, Sort = 10  },
            new ReasonCode { Code = "ML-INK",   LabelEn = "Ink change",                    LabelVi = "Thay mực",                    Kind = ReasonCodeKind.Pause, Sort = 20  },
            new ReasonCode { Code = "ML-PLATE", LabelEn = "Plate / cylinder change",       LabelVi = "Đổi bản / trục",              Kind = ReasonCodeKind.Pause, Sort = 30  },
            new ReasonCode { Code = "ML-QC",    LabelEn = "QC hold",                       LabelVi = "Giữ do QC",                   Kind = ReasonCodeKind.Pause, Sort = 40  },
            new ReasonCode { Code = "ML-BREAK", LabelEn = "Operator break",                LabelVi = "Nghỉ giải lao",               Kind = ReasonCodeKind.Pause, Sort = 50  },
            new ReasonCode { Code = "ML-MTC",   LabelEn = "Maintenance",                   LabelVi = "Bảo trì",                     Kind = ReasonCodeKind.Pause, Sort = 60  },
            new ReasonCode { Code = "ML-MEET",  LabelEn = "Shift handover / meeting",      LabelVi = "Giao ca / họp",               Kind = ReasonCodeKind.Pause, Sort = 70  },
            new ReasonCode { Code = "ML-UTIL",  LabelEn = "Power / air loss",              LabelVi = "Mất điện / khí",              Kind = ReasonCodeKind.Pause, Sort = 80  },
            // Scrap / NG causes (SC-* — used for SpecQcCapture FAIL result).
            new ReasonCode { Code = "SC-COLOR", LabelEn = "Colour ΔE out of spec",         LabelVi = "Lệch màu ΔE quá ngưỡng",      Kind = ReasonCodeKind.Scrap, Sort = 10  },
            new ReasonCode { Code = "SC-REG",   LabelEn = "Registration / mis-print",      LabelVi = "Lệch định vị / in lệch",      Kind = ReasonCodeKind.Scrap, Sort = 20  },
            new ReasonCode { Code = "SC-DIE",   LabelEn = "Die-cut burr / break",          LabelVi = "Cắt bế xước / gãy",           Kind = ReasonCodeKind.Scrap, Sort = 30  },
            new ReasonCode { Code = "SC-BAR",   LabelEn = "Barcode grade below B",         LabelVi = "Barcode kém (dưới B)",        Kind = ReasonCodeKind.Scrap, Sort = 40  }
        );
        await db.SaveChangesAsync();
    }
}
