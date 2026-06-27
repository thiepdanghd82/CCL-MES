using CCL.MES.Application.Services;
using CCL.MES.Domain;
using CCL.MES.Domain.Auth;
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

        // P10.7a-2.1 — recovery surface seeds: 6 Recovery-kind reason codes
        // (admin SYS_RECOVERY justification vocabulary) + the sys-recovery
        // user (audit-only attribution for the force-phase endpoint).
        // Per-row idempotency so re-seeds on existing DBs are NOOP.
        await SeedRecoveryDataAsync(db);

        // Phương án C — Bước 1: thư viện hạng mục kiểm. Best-effort: resolve CSV
        // (env MES_QC_LIBRARY_CSV > walk-up tìm IPQC_Library_CMES_v3.csv). Idempotent;
        // bỏ qua nếu không tìm thấy file (deploy không kèm CSV → dùng python importer).
        try
        {
            var csv = ResolveQcLibraryCsvPath();
            if (csv is not null)
            {
                var r = await SeedCheckItemLibraryFromFileAsync(db, csv);
                Console.WriteLine($"[seed] check_item_library inserted={r.LibInserted} updated={r.LibUpdated} reason_added={r.ReasonAdded} (src={Path.GetFileName(csv)})");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[seed] check_item_library skipped — {ex.GetType().Name}: {ex.Message}");
        }

        // Phương án C — Bước 6: map process→QC line (data-driven, quyết định #5).
        try
        {
            var m = await SeedProcessLineMapAsync(db);
            Console.WriteLine($"[seed] process_line_map inserted={m.Inserted} updated={m.Updated} total={m.Total}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[seed] process_line_map skipped — {ex.GetType().Name}: {ex.Message}");
        }

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
    /// P10.7b-3 hotfix — per-code idempotency (was global .Any() short-circuit
    /// that skipped Pause/Scrap on any DB already carrying Recovery codes
    /// from <see cref="SeedRecoveryDataAsync"/>, leaving the Hybrid PREPRESS
    /// dashboard with no valid Scrap codes to choose for NG).
    /// Promoted to public so the Hybrid Api host's boot probe can call it
    /// directly alongside <see cref="SeedRecoveryDataAsync"/>.
    /// </summary>
    public static async Task SeedReasonCodesAsync(MesDbContext db)
    {
        var existing = await db.ReasonCodes
            .Where(r => r.Kind == ReasonCodeKind.Pause || r.Kind == ReasonCodeKind.Scrap)
            .Select(r => r.Code)
            .ToListAsync();
        var existingSet = new HashSet<string>(existing, StringComparer.Ordinal);

        var codes = new[]
        {
            // Pause causes (ML-* — Machine Lost time).
            new ReasonCode { Code = "ML-MAT",   LabelEn = "Material loading / changeover", LabelVi = "Nạp / đổi vật liệu",         Kind = ReasonCodeKind.Pause, Sort = 10  },
            new ReasonCode { Code = "ML-INK",   LabelEn = "Ink change",                    LabelVi = "Thay mực",                    Kind = ReasonCodeKind.Pause, Sort = 20  },
            new ReasonCode { Code = "ML-PLATE", LabelEn = "Plate / cylinder change",       LabelVi = "Đổi bản / trục",              Kind = ReasonCodeKind.Pause, Sort = 30  },
            new ReasonCode { Code = "ML-QC",    LabelEn = "QC hold",                       LabelVi = "Giữ do QC",                   Kind = ReasonCodeKind.Pause, Sort = 40  },
            new ReasonCode { Code = "ML-BREAK", LabelEn = "Operator break",                LabelVi = "Nghỉ giải lao",               Kind = ReasonCodeKind.Pause, Sort = 50  },
            new ReasonCode { Code = "ML-MTC",   LabelEn = "Maintenance",                   LabelVi = "Bảo trì",                     Kind = ReasonCodeKind.Pause, Sort = 60  },
            new ReasonCode { Code = "ML-MEET",  LabelEn = "Shift handover / meeting",      LabelVi = "Giao ca / họp",               Kind = ReasonCodeKind.Pause, Sort = 70  },
            new ReasonCode { Code = "ML-UTIL",  LabelEn = "Power / air loss",              LabelVi = "Mất điện / khí",              Kind = ReasonCodeKind.Pause, Sort = 80  },
            // Scrap / NG causes (SC-* — used for SpecQcCapture FAIL result
            // AND PREPRESS material/plate/cutter NG per P10.7b contract §5.1).
            new ReasonCode { Code = "SC-COLOR", LabelEn = "Colour ΔE out of spec",         LabelVi = "Lệch màu ΔE quá ngưỡng",      Kind = ReasonCodeKind.Scrap, Sort = 10  },
            new ReasonCode { Code = "SC-REG",   LabelEn = "Registration / mis-print",      LabelVi = "Lệch định vị / in lệch",      Kind = ReasonCodeKind.Scrap, Sort = 20  },
            new ReasonCode { Code = "SC-DIE",   LabelEn = "Die-cut burr / break",          LabelVi = "Cắt bế xước / gãy",           Kind = ReasonCodeKind.Scrap, Sort = 30  },
            new ReasonCode { Code = "SC-BAR",   LabelEn = "Barcode grade below B",         LabelVi = "Barcode kém (dưới B)",        Kind = ReasonCodeKind.Scrap, Sort = 40  },
            // P10.7b-3 — PREPRESS-specific Scrap codes for material / plate /
            // cutter row checks. SC-MAT-* covers material defects surfacing
            // before press; SC-PLATE-* / SC-DIE-* surface tooling NG that the
            // SC-DIE existing code doesn't cleanly describe at the prepress
            // gate (vs. cut quality at FQC).
            new ReasonCode { Code = "SC-MAT-DAMAGE",  LabelEn = "Material damaged / contaminated", LabelVi = "Vật tư hỏng / nhiễm bẩn",      Kind = ReasonCodeKind.Scrap, Sort = 50 },
            new ReasonCode { Code = "SC-MAT-LOT",     LabelEn = "Wrong lot / expired material",    LabelVi = "Sai lot / vật tư hết hạn",     Kind = ReasonCodeKind.Scrap, Sort = 60 },
            new ReasonCode { Code = "SC-PLATE-WORN",  LabelEn = "Plate worn / unusable",           LabelVi = "Bản mòn / không dùng được",    Kind = ReasonCodeKind.Scrap, Sort = 70 },
            new ReasonCode { Code = "SC-CUTTER-WORN", LabelEn = "Cutter worn / blade nicked",      LabelVi = "Dao chặt mòn / sứt",           Kind = ReasonCodeKind.Scrap, Sort = 80 },
        };

        var toAdd = codes.Where(c => !existingSet.Contains(c.Code)).ToList();
        if (toAdd.Count == 0) return;

        db.ReasonCodes.AddRange(toAdd);
        await db.SaveChangesAsync();
    }

    // ── Phương án C — Bước 1: thư viện hạng mục kiểm ───────────────────

    /// <summary>Kết quả seed thư viện (cho log + test).</summary>
    public readonly record struct CheckLibrarySeedResult(int LibInserted, int LibUpdated, int ReasonAdded);

    /// <summary>
    /// Phương án C — seed/upsert thư viện hạng mục kiểm (CheckItemLibrary) +
    /// mở rộng ReasonCode (Kind=Scrap) theo các Defect code trong thư viện.
    /// <para><b>Idempotent</b>: upsert theo natural key <c>ItemId</c> — chạy 2 lần với
    /// cùng input ra cùng kết quả (lần 2: 0 insert, 0 reason mới; update chỉ khi field đổi).
    /// Sửa master KHÔNG hồi tố check đang chạy (freeze ProfileSnapshotJson — ranh giới Bước 4).</para>
    /// </summary>
    public static async Task<CheckLibrarySeedResult> SeedCheckItemLibraryAsync(
        MesDbContext db, IReadOnlyList<QcCheckLibraryRow> rows)
    {
        var existing = await db.CheckItemLibraries.ToListAsync();
        var byItemId = existing.ToDictionary(x => x.ItemId, StringComparer.Ordinal);

        int inserted = 0, updated = 0, sort = 0;
        foreach (var r in rows)
        {
            sort += 10;
            if (byItemId.TryGetValue(r.ItemId, out var cur))
            {
                if (ApplyRow(cur, r, sort)) { cur.UpdatedAt = DateTime.UtcNow; cur.UpdatedBy = "seed"; updated++; }
            }
            else
            {
                var e = new CheckItemLibrary { ItemId = r.ItemId, CreatedBy = "seed" };
                ApplyRow(e, r, sort);
                db.CheckItemLibraries.Add(e);
                byItemId[r.ItemId] = e;
                inserted++;
            }
        }

        // Mở rộng ReasonCode (Scrap) theo Defect code thư viện (quyết định #4).
        var existingScrap = new HashSet<string>(
            await db.ReasonCodes.Where(c => c.Kind == ReasonCodeKind.Scrap).Select(c => c.Code).ToListAsync(),
            StringComparer.Ordinal);
        var defectCodes = rows
            .Select(r => r.DefectCode?.Trim())
            .Where(c => !string.IsNullOrEmpty(c))
            .Select(c => c!)
            .Distinct(StringComparer.Ordinal)
            .Where(c => !existingScrap.Contains(c))
            .ToList();
        int reasonSort = 200;
        foreach (var code in defectCodes)
        {
            db.ReasonCodes.Add(new ReasonCode
            {
                Code = code, LabelEn = code, LabelVi = code,
                Kind = ReasonCodeKind.Scrap, Sort = reasonSort += 10, CreatedBy = "seed",
            });
        }

        await db.SaveChangesAsync();
        return new CheckLibrarySeedResult(inserted, updated, defectCodes.Count);
    }

    /// <summary>Đọc CSV thư viện từ <paramref name="path"/> rồi gọi
    /// <see cref="SeedCheckItemLibraryAsync"/>. Bỏ qua (NOOP) nếu file không tồn tại.</summary>
    public static async Task<CheckLibrarySeedResult> SeedCheckItemLibraryFromFileAsync(MesDbContext db, string path)
    {
        if (!File.Exists(path)) return new CheckLibrarySeedResult(0, 0, 0);
        // F4 (finding #8): parse strict — log hàng lỗi, KHÔNG seed im lặng.
        var parsed = QcCheckLibraryCsv.ParseDetailed(await File.ReadAllTextAsync(path));
        foreach (var s in parsed.Skipped) Console.WriteLine($"[csv] bad row — {s}");
        if (parsed.Skipped.Count > 0)
            Console.WriteLine($"[csv] {Path.GetFileName(path)}: parsed={parsed.Rows.Count} skipped={parsed.Skipped.Count}");
        return await SeedCheckItemLibraryAsync(db, parsed.Rows);
    }

    /// <summary>Resolve CSV thư viện rồi seed (idempotent). NOOP nếu không tìm thấy file.
    /// Dùng cho boot của Hybrid API (nó không gọi full <see cref="SeedAsync"/>) → tránh
    /// "seed quên chạy". Trả null khi không có CSV.</summary>
    public static async Task<CheckLibrarySeedResult?> SeedCheckItemLibraryAutoAsync(MesDbContext db)
    {
        var path = ResolveQcLibraryCsvPath();
        if (path is null) return null;
        return await SeedCheckItemLibraryFromFileAsync(db, path);
    }

    public readonly record struct ProcessLineMapSeedResult(int Inserted, int Updated, int Total);

    /// <summary>Phương án C — Bước 6: seed/upsert bảng map process→QC line
    /// (<see cref="ProcessLineMap"/>) từ <see cref="ProcessLineMapSeed.DefaultEntries"/>.
    /// Idempotent theo natural key (MatchType, MatchValue) — chạy 2 lần ra cùng số dòng.
    /// Chỉ cập nhật khi QcLine/Sort/Note đổi; KHÔNG đụng Active (admin có thể tắt thủ công).</summary>
    public static async Task<ProcessLineMapSeedResult> SeedProcessLineMapAsync(MesDbContext db)
    {
        var existing = await db.ProcessLineMaps.ToListAsync();
        var byKey = existing.ToDictionary(x => (x.MatchType, x.MatchValue));

        int inserted = 0, updated = 0;
        foreach (var e in ProcessLineMapSeed.DefaultEntries())
        {
            if (byKey.TryGetValue((e.MatchType, e.MatchValue), out var cur))
            {
                bool changed = false;
                if (cur.QcLine != e.QcLine) { cur.QcLine = e.QcLine; changed = true; }
                if (cur.Sort != e.Sort) { cur.Sort = e.Sort; changed = true; }
                if (!string.Equals(cur.Note, e.Note, StringComparison.Ordinal)) { cur.Note = e.Note; changed = true; }
                if (changed) { cur.UpdatedAt = DateTime.UtcNow; cur.UpdatedBy = "seed"; updated++; }
            }
            else
            {
                db.ProcessLineMaps.Add(new ProcessLineMap
                {
                    MatchType = e.MatchType, MatchValue = e.MatchValue, QcLine = e.QcLine,
                    Sort = e.Sort, Note = e.Note, Active = true, CreatedBy = "seed",
                });
                inserted++;
            }
        }
        await db.SaveChangesAsync();
        return new ProcessLineMapSeedResult(inserted, updated,
            await db.ProcessLineMaps.CountAsync());
    }

    /// <summary>Resolve CSV thư viện: env <c>MES_QC_LIBRARY_CSV</c> trước; nếu không,
    /// đi ngược từ thư mục build tìm <c>IPQC_Library_CMES_v3.csv</c>. Null nếu không thấy.</summary>
    private static string? ResolveQcLibraryCsvPath()
    {
        var env = Environment.GetEnvironmentVariable("MES_QC_LIBRARY_CSV");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;

        const string fileName = "IPQC_Library_CMES_v3.csv";
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, fileName);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>Cập nhật field từ row vào entity; trả true nếu CÓ thay đổi (cho upsert idempotent).</summary>
    private static bool ApplyRow(CheckItemLibrary e, QcCheckLibraryRow r, int sort)
    {
        bool changed = false;
        void Set(string cur, string val, Action<string> set) { if (!string.Equals(cur, val, StringComparison.Ordinal)) { set(val); changed = true; } }
        void SetN(string? cur, string? val, Action<string?> set) { if (!string.Equals(cur, val, StringComparison.Ordinal)) { set(val); changed = true; } }

        Set(e.ProcessLine, r.ProcessLine, v => e.ProcessLine = v);
        Set(e.GroupLabel, r.GroupLabel, v => e.GroupLabel = v);
        Set(e.Code, r.Code, v => e.Code = v);
        Set(e.ItemVi, r.ItemVi, v => e.ItemVi = v);
        Set(e.ItemEn, r.ItemEn, v => e.ItemEn = v);
        Set(e.AcceptanceVi, r.AcceptanceVi, v => e.AcceptanceVi = v);
        Set(e.AcceptanceEn, r.AcceptanceEn, v => e.AcceptanceEn = v);
        SetN(e.Method, r.Method, v => e.Method = v);
        SetN(e.Severity, r.Severity, v => e.Severity = v);
        SetN(e.Aql, r.Aql, v => e.Aql = v);
        SetN(e.Sampling, r.Sampling, v => e.Sampling = v);
        SetN(e.CheckType, r.CheckType, v => e.CheckType = v);
        SetN(e.DefectCode, r.DefectCode, v => e.DefectCode = v);
        SetN(e.ParetoPct, r.ParetoPct, v => e.ParetoPct = v);
        SetN(e.ShortForm, r.ShortForm, v => e.ShortForm = v);
        SetN(e.IsoRef, r.IsoRef, v => e.IsoRef = v);
        SetN(e.AppliesWhen, r.AppliesWhen, v => e.AppliesWhen = v);
        SetN(e.Note, r.Note, v => e.Note = v);
        if (e.Sort != sort) { e.Sort = sort; changed = true; }
        return changed;
    }

    /// <summary>
    /// P10.7a-2.1 — recovery surface bootstrap. Seeds the audit-only
    /// <c>sys-recovery</c> user + 6 Recovery-kind reason codes used by the
    /// admin force-phase endpoint to justify SYS_RECOVERY audit entries.
    /// Per-row idempotency: each insert is gated by a uniqueness check so
    /// re-running on a DB that already has either surface is a NOOP.
    /// Safe to call from app startup; safe to invoke standalone from
    /// migration-step scripts.
    /// </summary>
    public static async Task SeedRecoveryDataAsync(MesDbContext db)
    {
        await SeedRecoveryReasonCodesAsync(db);
        await SeedSysRecoveryUserAsync(db);
    }

    private static async Task SeedRecoveryReasonCodesAsync(MesDbContext db)
    {
        // Per-code idempotency. Pause/Scrap codes coexist with Recovery
        // codes (different Kind) so the global .Any() short-circuit used
        // by SeedReasonCodesAsync would skip these on any existing DB.
        var existing = await db.ReasonCodes
            .Where(r => r.Kind == ReasonCodeKind.Recovery)
            .Select(r => r.Code)
            .ToListAsync();
        var existingSet = new HashSet<string>(existing, StringComparer.Ordinal);

        var codes = new[]
        {
            new ReasonCode { Code = "REC-OP-WEDGE",     LabelEn = "Operator-reported wedge",        LabelVi = "Wedge do thao tác",            Kind = ReasonCodeKind.Recovery, Sort = 10 },
            new ReasonCode { Code = "REC-HW-FAULT",     LabelEn = "Hardware / sensor fault",        LabelVi = "Lỗi phần cứng / cảm biến",     Kind = ReasonCodeKind.Recovery, Sort = 20 },
            new ReasonCode { Code = "REC-MIG-LAG",      LabelEn = "Schema migration lag",           LabelVi = "DB chậm migration",            Kind = ReasonCodeKind.Recovery, Sort = 30 },
            new ReasonCode { Code = "REC-DATA-DRIFT",   LabelEn = "Data corruption / drift",        LabelVi = "Dữ liệu lỗi / sai lệch",       Kind = ReasonCodeKind.Recovery, Sort = 40 },
            new ReasonCode { Code = "REC-IPQC-OVR",     LabelEn = "IPQC reject override",           LabelVi = "Bỏ qua IPQC reject",           Kind = ReasonCodeKind.Recovery, Sort = 50 },
            new ReasonCode { Code = "REC-TEST-RESET",   LabelEn = "Test / dev reset",               LabelVi = "Reset dev/test",               Kind = ReasonCodeKind.Recovery, Sort = 60 },
        };

        var toAdd = codes.Where(c => !existingSet.Contains(c.Code)).ToList();
        if (toAdd.Count == 0) return;

        db.ReasonCodes.AddRange(toAdd);
        await db.SaveChangesAsync();
    }

    /// <summary>Reserved username for the audit-only system account.
    /// Lifted to a public const so tests + checkpoint scripts can refer
    /// to the same literal without re-typing.</summary>
    public const string SysRecoveryUsername = "sys-recovery";

    private static async Task SeedSysRecoveryUserAsync(MesDbContext db)
    {
        if (await db.Users.AnyAsync(u => u.Username == SysRecoveryUsername))
            return;

        // PasswordHash: deliberate non-PBKDF2 literal. Login flow short-
        // circuits on IsActive=false before reaching the hash check; this
        // string is here so any code path that bypasses the IsActive guard
        // still fails the hash verify with InvalidArgument / Failed.
        // Account is forensic-only — owns the actor.username on
        // SYS_RECOVERY audit rows + nothing else.
        db.Users.Add(new User
        {
            Username = SysRecoveryUsername,
            DisplayName = "System Recovery (audit-only)",
            Role = UserRole.Sys,
            IsActive = false,
            MustChangePassword = false,
            Department = "system",
            PasswordHash = "!SYS-RECOVERY-LOCKED-NEVER-LOGIN!",
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }
}
