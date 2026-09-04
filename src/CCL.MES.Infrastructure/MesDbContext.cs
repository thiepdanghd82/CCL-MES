using CCL.MES.Application;
using CCL.MES.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Infrastructure;

public class MesDbContext : DbContext, IMesDbContext
{
    public MesDbContext(DbContextOptions<MesDbContext> options) : base(options) { }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Product> Products => Set<Product>();
    // Phase 8 PR #28 — Spec/SpecVersion/SpecParameter DbSets REMOVED.
    // New ProductRevision + 4 sibling spec tables + Drawings + QC plan +
    // ProcessCatalog. See OnModelCreating below for enum-as-string + index
    // configuration.
    public DbSet<ProductRevision> ProductRevisions => Set<ProductRevision>();
    public DbSet<SpecMaterial> SpecMaterials => Set<SpecMaterial>();
    public DbSet<SpecPrint> SpecPrints => Set<SpecPrint>();
    public DbSet<SpecPrintColor> SpecPrintColors => Set<SpecPrintColor>();
    public DbSet<SpecFlexoCuttingRow> SpecFlexoCuttingRows => Set<SpecFlexoCuttingRow>();
    public DbSet<SpecFlexoInkRow> SpecFlexoInkRows => Set<SpecFlexoInkRow>();
    public DbSet<SpecDiecut> SpecDiecuts => Set<SpecDiecut>();
    public DbSet<SpecFinishing> SpecFinishings => Set<SpecFinishing>();
    public DbSet<Drawing> Drawings => Set<Drawing>();
    public DbSet<DrawingVersion> DrawingVersions => Set<DrawingVersion>();
    public DbSet<DrawingApproval> DrawingApprovals => Set<DrawingApproval>();
    public DbSet<SpecQcWindow> SpecQcWindows => Set<SpecQcWindow>();
    public DbSet<QcCriterion> QcCriteria => Set<QcCriterion>();
    // Phase 8 PR-D-4 — QC Capture (NPI spec-level inspection result) + ReasonCode lookup.
    public DbSet<SpecQcCapture> SpecQcCaptures => Set<SpecQcCapture>();
    public DbSet<ReasonCode> ReasonCodes => Set<ReasonCode>();
    public DbSet<ProcessCatalog> ProcessCatalogs => Set<ProcessCatalog>();
    public DbSet<CheckItemLibrary> CheckItemLibraries => Set<CheckItemLibrary>();

    // P12 — thư viện tiêu chuẩn kiểm tra NVL (IQC). Ba bảng tách bạch, xem
    // chú thích ở Entities/IqcLibrary.cs.
    public DbSet<IqcCheckItemLibrary> IqcCheckItemLibraries => Set<IqcCheckItemLibrary>();
    public DbSet<IqcMaterialSpec> IqcMaterialSpecs => Set<IqcMaterialSpec>();
    public DbSet<IqcSpecItem> IqcSpecItems => Set<IqcSpecItem>();
    public DbSet<IqcMaterialDocument> IqcMaterialDocuments => Set<IqcMaterialDocument>();
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<WoStatusHistory> WoStatusHistories => Set<WoStatusHistory>();
    // P11-1 — Multi-Method Routing DAG (fork-join): leg nodes + dependency
    // edges + data-driven op→leg map. WO 1-leg cũ có 0 row WoLeg.
    public DbSet<WoLeg> WoLegs => Set<WoLeg>();
    public DbSet<WoLegDependency> WoLegDependencies => Set<WoLegDependency>();
    public DbSet<ProcessLegMap> ProcessLegMaps => Set<ProcessLegMap>();
    // P11.5 — Semi-Stock decoupling (keep-stock bán thành phẩm).
    public DbSet<SemiLot> SemiLots => Set<SemiLot>();
    public DbSet<SemiAllocation> SemiAllocations => Set<SemiAllocation>();
    public DbSet<QcInspection> QcInspections => Set<QcInspection>();
    public DbSet<QcResultDetail> QcResultDetails => Set<QcResultDetail>();
    public DbSet<Machine> Machines => Set<Machine>();
    public DbSet<DowntimeReason> DowntimeReasons => Set<DowntimeReason>();
    public DbSet<ProductionLog> ProductionLogs => Set<ProductionLog>();
    public DbSet<WorkInstruction> WorkInstructions => Set<WorkInstruction>();
    public DbSet<WiStepDetail> WiStepDetails => Set<WiStepDetail>();
    public DbSet<WorkCenter> WorkCenters => Set<WorkCenter>();
    public DbSet<RawMaterial> RawMaterials => Set<RawMaterial>();
    public DbSet<RoutingOperation> RoutingOperations => Set<RoutingOperation>();
    public DbSet<ManufacturingStructure> ManufacturingStructures => Set<ManufacturingStructure>();
    public DbSet<User> Users => Set<User>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    // Phase 6 Bước 7 — IQC entity + result detail (xem Iqc.cs).
    public DbSet<IqcInspection> IqcInspections => Set<IqcInspection>();
    public DbSet<IqcResultDetail> IqcResultDetails => Set<IqcResultDetail>();
    /// <summary>P13 — các phép đo lặp (độ rộng ×5, độ dày ×5) của một dòng kết quả.</summary>
    public DbSet<IqcResultMeasurement> IqcResultMeasurements => Set<IqcResultMeasurement>();
    // P10.7a-1.2 — Idempotency ledger (per contract §6.2).
    public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();
    // P10.7b-1 — PREPRESS row-level child tables (per contract §5.1).
    public DbSet<WoMaterial> WoMaterials => Set<WoMaterial>();
    public DbSet<WoPlateCheck> WoPlateChecks => Set<WoPlateCheck>();
    public DbSet<WoCutterCheck> WoCutterChecks => Set<WoCutterCheck>();
    // P10.7c-1 — RUNNING + PAUSED child tables (per contract §5.4).
    public DbSet<WoRunSession> WoRunSessions => Set<WoRunSession>();
    public DbSet<WoPauseEvent> WoPauseEvents => Set<WoPauseEvent>();
    public DbSet<WoQtyEntry> WoQtyEntries => Set<WoQtyEntry>();
    // P10.7d-1 — IPQC review surface (per contract §5.5).
    public DbSet<WoIpqcCheck> WoIpqcChecks => Set<WoIpqcCheck>();
    // Phương án C — Bước 2: data-driven IPQC items (shadow, additive).
    public DbSet<WoIpqcCheckItem> WoIpqcCheckItems => Set<WoIpqcCheckItem>();
    // IPQC first-article — MATERIAL (SYSTEM) LOT reconciliation per BOM line.
    public DbSet<WoIpqcMaterialCheck> WoIpqcMaterialChecks => Set<WoIpqcMaterialCheck>();
    // P10.7g — SETTING per-item persist + defect catalog per hạng mục.
    public DbSet<WoSettingCheckItem> WoSettingCheckItems => Set<WoSettingCheckItem>();
    public DbSet<CheckItemDefectOption> CheckItemDefectOptions => Set<CheckItemDefectOption>();
    // Phương án C — Bước 6: map process→QC line (data-driven, quyết định #5).
    public DbSet<ProcessLineMap> ProcessLineMaps => Set<ProcessLineMap>();
    // P10.7e-1 Q3+Q6 — DATA-DRIVEN FQC + OQC + photo evidence tables.
    public DbSet<WoQcCheck> WoQcChecks => Set<WoQcCheck>();
    public DbSet<WoTraceSnapshot> WoTraceSnapshots => Set<WoTraceSnapshot>();
    public DbSet<WoTraceIndex> WoTraceIndexes => Set<WoTraceIndex>();
    public DbSet<WoQcCheckItem> WoQcCheckItems => Set<WoQcCheckItem>();
    public DbSet<WoQcPhoto> WoQcPhotos => Set<WoQcPhoto>();
    // A1 — mạch lô nguyên vật liệu (xem MaterialLot.cs / WoMaterialConsumption.cs).
    public DbSet<MaterialLot> MaterialLots => Set<MaterialLot>();
    public DbSet<WoMaterialConsumption> WoMaterialConsumptions => Set<WoMaterialConsumption>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // Lưu enum dạng chuỗi cho dễ đọc trong DB
        b.Entity<WorkOrder>().Property(x => x.CurrentStep).HasConversion<string>();
        b.Entity<WorkOrder>().Property(x => x.Status).HasConversion<string>();
        // P10.7a-1 — canonical MesPhase column (string) + optimistic
        // RowVersion. Both ADDITIVE — legacy CurrentStep stays the
        // authoritative legacy-Razor read path. SQLite trigger that
        // bumps RowVersion on every UPDATE lives in the migration
        // (EF Core's IsRowVersion() handles SQL Server auto-bump but
        // not SQLite; the trigger fills the gap).
        b.Entity<WorkOrder>().Property(x => x.MesPhase).HasMaxLength(16).IsRequired();
        b.Entity<WorkOrder>().Property(x => x.RowVersion).IsRowVersion();
        // Phase 8 PR #28 — SpecVersion HasConversion removed (entity dropped);
        // ProductRevision + Drawing + QC plan + ProcessCatalog enum config below.
        b.Entity<ProductRevision>().Property(x => x.Status).HasConversion<string>();
        b.Entity<Drawing>().Property(x => x.Kind).HasConversion<string>();
        b.Entity<Drawing>().Property(x => x.Status).HasConversion<string>();
        b.Entity<DrawingVersion>().Property(x => x.Status).HasConversion<string>();
        b.Entity<DrawingApproval>().Property(x => x.Role).HasConversion<string>();
        b.Entity<DrawingApproval>().Property(x => x.Status).HasConversion<string>();
        b.Entity<SpecQcWindow>().Property(x => x.Stage).HasConversion<string>();
        b.Entity<SpecQcWindow>().Property(x => x.RejectAction).HasConversion<string>();
        b.Entity<SpecQcWindow>().Property(x => x.Status).HasConversion<string>();
        b.Entity<QcCriterion>().Property(x => x.CriterionType).HasConversion<string>();
        // Phase 8 PR-D-3 — Method (free-form ops text), Frequency (cadence) per-criterion.
        b.Entity<QcCriterion>().Property(x => x.Method).HasMaxLength(200);
        b.Entity<QcCriterion>().Property(x => x.Frequency).HasMaxLength(120);
        // Phase 8 PR-D-4 — SpecQcCapture (append-only result per criterion) + ReasonCode lookup.
        b.Entity<SpecQcCapture>().Property(x => x.Result).HasConversion<string>();
        b.Entity<SpecQcCapture>().Property(x => x.Measurement).HasMaxLength(200);
        b.Entity<SpecQcCapture>().Property(x => x.NgReasonCode).HasMaxLength(40);
        b.Entity<SpecQcCapture>().Property(x => x.Comment).HasMaxLength(500);
        b.Entity<SpecQcCapture>().Property(x => x.CapturedBy).HasMaxLength(80);
        b.Entity<ReasonCode>().Property(x => x.Code).HasMaxLength(40);
        b.Entity<ReasonCode>().Property(x => x.LabelEn).HasMaxLength(200);
        b.Entity<ReasonCode>().Property(x => x.LabelVi).HasMaxLength(200);
        b.Entity<ReasonCode>().Property(x => x.Kind).HasConversion<string>();
        b.Entity<ProcessCatalog>().Property(x => x.Category).HasConversion<string>();
        b.Entity<ProcessCatalog>().Property(x => x.Status).HasConversion<string>();
        b.Entity<QcInspection>().Property(x => x.Type).HasConversion<string>();
        b.Entity<QcInspection>().Property(x => x.Result).HasConversion<string>();
        b.Entity<WoStatusHistory>().Property(x => x.FromStep).HasConversion<string>();
        b.Entity<WoStatusHistory>().Property(x => x.ToStep).HasConversion<string>();
        // P10.7b-1 — PREPRESS row-level child tables. Status stored as string
        // (PENDING / OK / NG) per legacy convention; NgReasonCode references
        // ReasonCode.Code (Kind=Scrap) by natural key. Unique indexes enforce
        // contract §5.1 cardinality: wo_materials has (WO, BomLineIdx) unique;
        // plate + cutter are 1:1 per WO.
        b.Entity<WoMaterial>().Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.Entity<WoMaterial>().Property(x => x.MaterialCode).HasMaxLength(64).IsRequired();
        b.Entity<WoMaterial>().Property(x => x.MaterialDescription).HasMaxLength(200);
        b.Entity<WoMaterial>().Property(x => x.Uom).HasMaxLength(16);
        b.Entity<WoMaterial>().Property(x => x.LotNo).HasMaxLength(64);
        b.Entity<WoMaterial>().Property(x => x.PartScan).HasMaxLength(120);
        b.Entity<WoMaterial>().Property(x => x.PartScanDescription).HasMaxLength(200);
        b.Entity<WoMaterial>().Property(x => x.NgReasonCode).HasMaxLength(40);
        b.Entity<WoMaterial>().Property(x => x.NgNote).HasMaxLength(500);
        b.Entity<WoMaterial>().Property(x => x.CheckedBy).HasMaxLength(80);
        b.Entity<WoPlateCheck>().Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.Entity<WoPlateCheck>().Property(x => x.PlateNo).HasMaxLength(64);
        b.Entity<WoPlateCheck>().Property(x => x.NgReasonCode).HasMaxLength(40);
        b.Entity<WoPlateCheck>().Property(x => x.NgNote).HasMaxLength(500);
        b.Entity<WoPlateCheck>().Property(x => x.CheckedBy).HasMaxLength(80);
        b.Entity<WoCutterCheck>().Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.Entity<WoCutterCheck>().Property(x => x.CutterNo).HasMaxLength(64);
        b.Entity<WoCutterCheck>().Property(x => x.NgReasonCode).HasMaxLength(40);
        b.Entity<WoCutterCheck>().Property(x => x.NgNote).HasMaxLength(500);
        b.Entity<WoCutterCheck>().Property(x => x.CheckedBy).HasMaxLength(80);
        // P10.7d-1 — IPQC review surface (contract §5.5 + §5.5.1).
        b.Entity<WoIpqcCheck>().Property(x => x.MaterialStatus).HasConversion<string>().HasMaxLength(16);
        b.Entity<WoIpqcCheck>().Property(x => x.PrintAStatus).HasConversion<string>().HasMaxLength(16);
        b.Entity<WoIpqcCheck>().Property(x => x.PrintBStatus).HasConversion<string>().HasMaxLength(16);
        b.Entity<WoIpqcCheck>().Property(x => x.PrintCStatus).HasConversion<string>().HasMaxLength(16);
        b.Entity<WoIpqcCheck>().Property(x => x.Judgment).HasConversion<string>().HasMaxLength(20);
        b.Entity<WoIpqcCheck>().Property(x => x.QaOutcome).HasConversion<string>().HasMaxLength(16);
        b.Entity<Machine>().Property(x => x.CurrentState).HasConversion<string>();
        b.Entity<ProductionLog>().Property(x => x.EventType).HasConversion<string>();
        b.Entity<WorkInstruction>().Property(x => x.Status).HasConversion<string>();
        b.Entity<WorkInstruction>().Property(x => x.ProcessStep).HasConversion<string>();

        b.Entity<WorkOrder>().HasIndex(x => x.WoNo).IsUnique();
        b.Entity<Machine>().HasIndex(x => x.Code).IsUnique();

        // P10.7b-1 — PREPRESS row uniqueness.
        // P11 per-leg: WO-level uniqueness (1-leg / 81092000-style) kept as a
        // PARTIAL index filtered to WoLegId IS NULL so per-leg rows (WoLegId set)
        // coexist. The WoLegId-IS-NOT-NULL partial uniques are added after the
        // shadow-property loop below.
        b.Entity<WoMaterial>().HasIndex(x => new { x.WorkOrderId, x.BomLineIdx }).IsUnique().HasFilter("\"WoLegId\" IS NULL");
        b.Entity<WoMaterial>().HasIndex(x => x.WorkOrderId);
        b.Entity<WoPlateCheck>().HasIndex(x => x.WorkOrderId).IsUnique().HasFilter("\"WoLegId\" IS NULL");
        b.Entity<WoCutterCheck>().HasIndex(x => x.WorkOrderId).IsUnique().HasFilter("\"WoLegId\" IS NULL");

        // P10.7c-1 — RUNNING + PAUSED child tables (contract §5.4).
        b.Entity<WoRunSession>().HasIndex(x => x.WoId);
        b.Entity<WoRunSession>().HasIndex(x => new { x.WoId, x.EndedAt });
        b.Entity<WoPauseEvent>().HasIndex(x => x.WoId);
        b.Entity<WoPauseEvent>().HasIndex(x => x.RunSessionId);
        b.Entity<WoQtyEntry>().HasIndex(x => x.WoId);
        b.Entity<WoQtyEntry>().HasIndex(x => new { x.WoId, x.Ts });
        b.Entity<WoQtyEntry>().HasIndex(x => x.RunSessionId);
        b.Entity<WoQtyEntry>().HasIndex(x => x.LinkedEntryId);

        // P10.7d-1 — IPQC review surface 1:1 uniqueness (contract §5.5).
        // P11 per-leg: partial to WoLegId IS NULL (per-leg partial added below).
        b.Entity<WoIpqcCheck>().HasIndex(x => x.WorkOrderId).IsUnique().HasFilter("\"WoLegId\" IS NULL");

        // Phương án C — Bước 2: data-driven IPQC items (mirror WoQcCheckItem).
        b.Entity<WoIpqcCheckItem>().Property(x => x.ItemKey).HasMaxLength(64).IsRequired();
        b.Entity<WoIpqcCheckItem>().Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.Entity<WoIpqcCheckItem>().Property(x => x.ProcessLine).HasMaxLength(16);
        b.Entity<WoIpqcCheckItem>().Property(x => x.GroupLabel).HasMaxLength(128);
        b.Entity<WoIpqcCheckItem>().Property(x => x.Label).HasMaxLength(512);
        b.Entity<WoIpqcCheckItem>().Property(x => x.AcceptanceCriteria).HasMaxLength(512);
        b.Entity<WoIpqcCheckItem>().Property(x => x.Method).HasMaxLength(256);
        // Bản EN đóng băng — cùng MaxLength với bản VI tương ứng ngay trên.
        b.Entity<WoIpqcCheckItem>().Property(x => x.GroupLabelEn).HasMaxLength(128);
        b.Entity<WoIpqcCheckItem>().Property(x => x.LabelEn).HasMaxLength(512);
        b.Entity<WoIpqcCheckItem>().Property(x => x.AcceptanceCriteriaEn).HasMaxLength(512);
        b.Entity<WoIpqcCheckItem>().Property(x => x.MethodEn).HasMaxLength(256);
        b.Entity<WoIpqcCheckItem>().Property(x => x.Severity).HasMaxLength(64);
        b.Entity<WoIpqcCheckItem>().Property(x => x.DefectCode).HasMaxLength(64);
        b.Entity<WoIpqcCheckItem>().Property(x => x.NgReasonCode).HasMaxLength(64);
        b.Entity<WoIpqcCheckItem>().Property(x => x.NgNote).HasMaxLength(500);
        b.Entity<WoIpqcCheckItem>().HasIndex(x => new { x.WoIpqcCheckId, x.ItemKey }).IsUnique();

        // P10.7g — SETTING per-item persist. Concurrency qua WO.RowVersion (7c-2).
        b.Entity<WoSettingCheckItem>().Property(x => x.ProcessKind).HasMaxLength(8).IsRequired();
        b.Entity<WoSettingCheckItem>().Property(x => x.ItemKey).HasMaxLength(64).IsRequired();
        b.Entity<WoSettingCheckItem>().Property(x => x.Label).HasMaxLength(512);
        b.Entity<WoSettingCheckItem>().Property(x => x.Standard).HasMaxLength(512);
        b.Entity<WoSettingCheckItem>().Property(x => x.GroupLabel).HasMaxLength(128);
        b.Entity<WoSettingCheckItem>().Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.Entity<WoSettingCheckItem>().Property(x => x.DefectCode).HasMaxLength(64);
        b.Entity<WoSettingCheckItem>().Property(x => x.NgNote).HasMaxLength(500);
        b.Entity<WoSettingCheckItem>().Property(x => x.ConfirmedBy).HasMaxLength(128);
        b.Entity<WoSettingCheckItem>().HasIndex(x => new { x.WorkOrderId, x.ProcessKind, x.ItemKey }).IsUnique();
        b.Entity<WoSettingCheckItem>().HasOne(x => x.WorkOrder).WithMany()
            .HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Cascade);

        // P10.7g — defect option per hạng mục (base seed + user add-new per-product).
        b.Entity<CheckItemDefectOption>().Property(x => x.ItemId).HasMaxLength(64).IsRequired();
        b.Entity<CheckItemDefectOption>().Property(x => x.DefectCode).HasMaxLength(64).IsRequired();
        b.Entity<CheckItemDefectOption>().Property(x => x.LabelVi).HasMaxLength(256);
        b.Entity<CheckItemDefectOption>().Property(x => x.LabelEn).HasMaxLength(256);
        b.Entity<CheckItemDefectOption>().Property(x => x.ProductCode).HasMaxLength(64);
        b.Entity<CheckItemDefectOption>().HasIndex(x => new { x.ItemId, x.DefectCode, x.ProductCode }).IsUnique();
        b.Entity<CheckItemDefectOption>().HasIndex(x => x.ItemId);

        // Phương án C — Bước 6: map process→QC line (data-driven).
        b.Entity<ProcessLineMap>().Property(x => x.MatchType).HasMaxLength(32).IsRequired();
        b.Entity<ProcessLineMap>().Property(x => x.MatchValue).HasMaxLength(128).IsRequired();
        b.Entity<ProcessLineMap>().Property(x => x.QcLine).HasMaxLength(16).IsRequired();
        b.Entity<ProcessLineMap>().Property(x => x.Note).HasMaxLength(256);
        b.Entity<ProcessLineMap>().HasIndex(x => new { x.MatchType, x.MatchValue }).IsUnique();

        // ── P11-1 — Multi-Method Routing DAG (fork-join) ───────────────
        // WoLeg: enum-as-string + per-leg RowVersion (SQLite trigger sinh
        // randomblob(8) mỗi INSERT/UPDATE, cùng pattern WorkOrder — trigger
        // ở migration AddRoutingLegDag).
        b.Entity<WoLeg>().Property(x => x.LegKind).HasMaxLength(16).IsRequired();
        b.Entity<WoLeg>().Property(x => x.Method).HasMaxLength(32);
        b.Entity<WoLeg>().Property(x => x.ProcessLine).HasMaxLength(16);
        b.Entity<WoLeg>().Property(x => x.SurfaceProfile).HasMaxLength(8).IsRequired();
        b.Entity<WoLeg>().Property(x => x.InputSource).HasMaxLength(16).IsRequired();
        b.Entity<WoLeg>().Property(x => x.LegPhase).HasMaxLength(16).IsRequired();
        // P11-2 fix — per-leg concurrency token, nhưng KHÔNG store-generated:
        // EF GỬI giá trị (X'' rỗng) lúc INSERT nên cột BLOB NOT NULL không bị
        // NULL, rồi trigger SQLite randomblob(8) bump. IsConcurrencyToken vẫn
        // đưa RowVersion vào WHERE của UPDATE → optimistic-lock (soak 409).
        // Khác WorkOrders (IsRowVersion + DB default) nhưng tránh table-rebuild
        // AlterColumn (drop trigger). Không cần đổi schema đã áp trên live.
        b.Entity<WoLeg>().Property(x => x.RowVersion).IsConcurrencyToken().ValueGeneratedNever();
        b.Entity<WoLeg>().HasIndex(x => new { x.WorkOrderId, x.Sequence }).IsUnique();
        b.Entity<WoLeg>().HasIndex(x => x.WorkOrderId);
        b.Entity<WoLeg>().HasOne(x => x.WorkOrder)
            .WithMany(w => w.Legs)
            .HasForeignKey(x => x.WorkOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // WoLegDependency: cạnh DAG. (WO, Leg, DependsOn) unique. KHÔNG cấu
        // hình FK EF tới WoLeg (2 đường Leg/DependsOn → tránh multiple
        // cascade path SQLite); toàn vẹn tham chiếu enforce ở
        // RoutingDagValidator + service layer (loose-coupling, giống
        // SpecQcCapture.NgReasonCode).
        b.Entity<WoLegDependency>().Property(x => x.DependencyGate).HasMaxLength(8).IsRequired();
        b.Entity<WoLegDependency>().HasIndex(x => new { x.WorkOrderId, x.LegId, x.DependsOnLegId }).IsUnique();
        b.Entity<WoLegDependency>().HasIndex(x => x.WorkOrderId);
        b.Entity<WoLegDependency>().HasOne<WorkOrder>()
            .WithMany(w => w.LegEdges)
            .HasForeignKey(x => x.WorkOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // ProcessLegMap: data-driven op→leg (mirror ProcessLineMap).
        b.Entity<ProcessLegMap>().Property(x => x.MatchType).HasMaxLength(32).IsRequired();
        b.Entity<ProcessLegMap>().Property(x => x.MatchValue).HasMaxLength(128).IsRequired();
        b.Entity<ProcessLegMap>().Property(x => x.LegKind).HasMaxLength(16).IsRequired();
        b.Entity<ProcessLegMap>().Property(x => x.Method).HasMaxLength(32);
        b.Entity<ProcessLegMap>().Property(x => x.ProcessLine).HasMaxLength(16);
        b.Entity<ProcessLegMap>().Property(x => x.Note).HasMaxLength(256);
        b.Entity<ProcessLegMap>().HasIndex(x => new { x.MatchType, x.MatchValue }).IsUnique();

        // Leg-scoping column (shadow, nullable) trên 8 surface bảng P10.7.
        // null = WO 1-leg cũ (controllers hiện set null tới khi P11-2 wire).
        // Shadow → KHÔNG chạm 8 file entity, migration vẫn thêm cột.
        foreach (var surface in new[]
                 {
                     typeof(WoMaterial), typeof(WoPlateCheck), typeof(WoCutterCheck),
                     typeof(WoRunSession), typeof(WoPauseEvent), typeof(WoQtyEntry),
                     typeof(WoIpqcCheck), typeof(WoIpqcCheckItem),
                 })
        {
            b.Entity(surface).Property<long?>("WoLegId");
            b.Entity(surface).HasIndex("WoLegId");
        }

        // P11 per-leg (Henry-approved 2026-07-24): PARTIAL unique indexes scoped
        // by WoLegId so each leg materialises its own Pre-press / IPQC surface
        // without colliding with sibling legs or the WO-level (NULL) rows. Pairs
        // with the WoLegId-IS-NULL partial uniques above → 1-leg parity intact.
        b.Entity<WoMaterial>().HasIndex("WorkOrderId", "WoLegId", "BomLineIdx").IsUnique().HasFilter("\"WoLegId\" IS NOT NULL");
        b.Entity<WoPlateCheck>().HasIndex("WorkOrderId", "WoLegId").IsUnique().HasFilter("\"WoLegId\" IS NOT NULL");
        b.Entity<WoCutterCheck>().HasIndex("WorkOrderId", "WoLegId").IsUnique().HasFilter("\"WoLegId\" IS NOT NULL");
        b.Entity<WoIpqcCheck>().HasIndex("WorkOrderId", "WoLegId").IsUnique().HasFilter("\"WoLegId\" IS NOT NULL");

        // ── P11.5 — Semi-Stock decoupling (keep-stock) ─────────────────
        b.Entity<SemiLot>().Property(x => x.LotNo).HasMaxLength(64).IsRequired();
        b.Entity<SemiLot>().Property(x => x.SemiKind).HasMaxLength(16).IsRequired();
        b.Entity<SemiLot>().Property(x => x.Status).HasMaxLength(16).IsRequired();
        // L38 — RowVersion: EF gửi X'' lúc INSERT (không IsRowVersion), trigger
        // randomblob(8) bump; IsConcurrencyToken → 2 assembly reserve cùng lô
        // không over-sell (optimistic lock).
        b.Entity<SemiLot>().Property(x => x.RowVersion).IsConcurrencyToken().ValueGeneratedNever();
        b.Entity<SemiLot>().HasIndex(x => x.LotNo).IsUnique();          // barcode natural key
        b.Entity<SemiLot>().HasIndex(x => new { x.SemiKind, x.Status, x.SpecRevisionId }); // FEFO lookup
        b.Entity<SemiLot>().HasIndex(x => x.SourceWorkOrderId);          // genealogy

        b.Entity<SemiAllocation>().HasIndex(x => new { x.WorkOrderId, x.AssemblyLegId });
        b.Entity<SemiAllocation>().HasIndex(x => x.SemiLotId);

        // ── A1 — mạch lô nguyên vật liệu ──────────────────────────────
        // Khoá tự nhiên chuỗi giữa hai bảng = KHÔNG có khoá. Truy xuất nguồn
        // gốc phải là FK; chuỗi chỉ được tồn tại như nhãn hiển thị. Ba lớp
        // siết khoá chuỗi đặt Ở ĐÂY (schema), không rải trong C#:
        //   lớp 1 UseCollation("NOCASE") trên CỘT — cấm EF.Functions.Collate()
        //         trong query (thừa, và phá cổng sang SQL Server)
        //   lớp 2 CHECK TRIM + LENGTH>0 — NOCASE không lo khoảng trắng
        //   lớp 3 .Trim() ở MaterialLotScanService (tiện lợi, không phải bảo đảm)
        b.Entity<MaterialLot>().Property(x => x.LotNo).HasMaxLength(64).IsRequired().UseCollation("NOCASE");
        b.Entity<MaterialLot>().Property(x => x.PartNo).HasMaxLength(64).IsRequired().UseCollation("NOCASE");
        b.Entity<MaterialLot>().Property(x => x.SupplierLotNo).HasMaxLength(64).UseCollation("NOCASE");
        b.Entity<MaterialLot>().Property(x => x.SupplierName).HasMaxLength(120);
        b.Entity<MaterialLot>().Property(x => x.Uom).HasMaxLength(16);
        b.Entity<MaterialLot>().Property(x => x.Status).HasMaxLength(16).IsRequired();
        b.Entity<MaterialLot>().Property(x => x.StatusReason).HasMaxLength(500);
        b.Entity<MaterialLot>().Property(x => x.StatusChangedBy).HasMaxLength(80);
        // Bốn cột vòng đời Đ3 — bằng chứng của luật hai vai khi gia hạn.
        b.Entity<MaterialLot>().Property(x => x.RetestedBy).HasMaxLength(80);
        b.Entity<MaterialLot>().Property(x => x.ExpiryExtendedBy).HasMaxLength(80);
        // L38 Option-B: EF gửi X'' lúc INSERT, trigger SQLite bump; concurrency
        // token để hai operator quét cùng lô không tiêu thụ vượt tồn.
        b.Entity<MaterialLot>().Property(x => x.RowVersion).IsConcurrencyToken().ValueGeneratedNever();
        b.Entity<MaterialLot>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_MaterialLots_LotNo_Trimmed",
                "\"LotNo\" = TRIM(\"LotNo\") AND LENGTH(\"LotNo\") > 0");
            t.HasCheckConstraint("CK_MaterialLots_PartNo_Trimmed",
                "\"PartNo\" = TRIM(\"PartNo\") AND LENGTH(\"PartNo\") > 0");
        });
        // PHẢI CÓ CẢ HAI partial unique index. Trong SQLite NULL ≠ NULL trong
        // unique index — chỉ có UNIQUE(LotNo, RawMaterialId) thì MỌI lô chưa
        // resolve lọt hết, unique vô hiệu đúng ở chỗ dễ trùng nhất (lô về kho
        // khi catalog chưa có part).
        b.Entity<MaterialLot>().HasIndex(x => new { x.LotNo, x.RawMaterialId })
            .IsUnique().HasFilter("\"RawMaterialId\" IS NOT NULL")
            .HasDatabaseName("IX_MaterialLots_LotNo_RawMaterialId");
        b.Entity<MaterialLot>().HasIndex(x => new { x.LotNo, x.PartNo })
            .IsUnique().HasFilter("\"RawMaterialId\" IS NULL")
            .HasDatabaseName("IX_MaterialLots_LotNo_PartNo_Unresolved");
        b.Entity<MaterialLot>().HasIndex(x => new { x.Status, x.ExpiryAt });
        b.Entity<MaterialLot>().HasIndex(x => new { x.RawMaterialId, x.Status });
        b.Entity<MaterialLot>().HasIndex(x => x.IqcInspectionId);

        b.Entity<WoMaterialConsumption>().Property(x => x.Uom).HasMaxLength(16);
        b.Entity<WoMaterialConsumption>().Property(x => x.ScannedBy).HasMaxLength(80).IsRequired();
        b.Entity<WoMaterialConsumption>().Property(x => x.ReversedBy).HasMaxLength(80);
        b.Entity<WoMaterialConsumption>().Property(x => x.ReversedReason).HasMaxLength(500);
        b.Entity<WoMaterialConsumption>().HasOne<WorkOrder>().WithMany()
            .HasForeignKey(x => x.WoId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<WoMaterialConsumption>().HasOne<WoMaterial>().WithMany()
            .HasForeignKey(x => x.WoMaterialId).OnDelete(DeleteBehavior.Cascade);
        // RESTRICT trên lô: xoá một lô còn vết tiêu thụ = đứt mạch truy xuất
        // giữa chừng, đúng thứ A1 sinh ra để ngăn.
        b.Entity<WoMaterialConsumption>().HasOne<MaterialLot>().WithMany()
            .HasForeignKey(x => x.MaterialLotId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<WoMaterialConsumption>().HasOne<WoLeg>().WithMany()
            .HasForeignKey(x => x.LegId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<WoMaterialConsumption>().HasIndex(x => new { x.WoId, x.LegId });
        // Genealogy NGƯỢC: lô này đã vào những WO nào — câu hỏi ĐẦU TIÊN khi
        // phải thu hồi. Không có index này thì mỗi lần thu hồi là full scan.
        b.Entity<WoMaterialConsumption>().HasIndex(x => x.MaterialLotId);
        b.Entity<WoMaterialConsumption>().HasIndex(x => x.WoMaterialId);
        // KHÔNG có unique index chống quét lặp (Đ4). Chống bấm nhầm chuyển
        // hoàn toàn sang Idempotency-Key; backfill dựa vào dấu 'backfill-a1'.

        // WoMaterials — additive thuần: chỉ THÊM cột FK dạng shadow (đúng tiền
        // lệ WoLegId ở trên) để KHÔNG chạm file entity, và tuyệt đối KHÔNG
        // AlterColumn LotNo: rebuild bảng trên SQLite sẽ xoá mất 2 partial
        // unique index IX_WoMaterials_WorkOrderId_BomLineIdx / _WoLegId_BomLineIdx.
        b.Entity<WoMaterial>().Property<long?>("MaterialLotId");
        b.Entity<WoMaterial>().HasIndex("MaterialLotId");
        b.Entity<WoMaterial>().HasOne<MaterialLot>().WithMany()
            .HasForeignKey("MaterialLotId").OnDelete(DeleteBehavior.Restrict);
        b.Entity<WoIpqcCheckItem>().HasOne(x => x.WoIpqcCheck)
            .WithMany(c => c.Items)
            .HasForeignKey(x => x.WoIpqcCheckId)
            .OnDelete(DeleteBehavior.Cascade);
        // IPQC first-article — measured RESULT value (Q3) + CheckType tab (Q2).
        b.Entity<WoIpqcCheckItem>().Property(x => x.MeasuredValue).HasMaxLength(128);
        b.Entity<WoIpqcCheckItem>().Property(x => x.CheckType).HasMaxLength(24);
        // Applicability toggle — default true so existing rows backfill to "checked".
        b.Entity<WoIpqcCheckItem>().Property(x => x.Applicable).HasDefaultValue(true);

        // IPQC first-article — MATERIAL (SYSTEM) LOT reconciliation. 1 row per
        // BOM line; concurrency mediated by the parent WO row (NO RowVersion,
        // mirror WoIpqcCheck). Enum-as-string; (WorkOrderId, BomLineIdx) unique.
        b.Entity<WoIpqcMaterialCheck>().Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.Entity<WoIpqcMaterialCheck>().Property(x => x.DivergenceApprovalStatus).HasConversion<string>().HasMaxLength(20);
        b.Entity<WoIpqcMaterialCheck>().HasIndex(x => new { x.WorkOrderId, x.BomLineIdx }).IsUnique();
        b.Entity<WoIpqcMaterialCheck>().HasIndex(x => x.WorkOrderId);
        b.Entity<WoIpqcMaterialCheck>().HasOne(x => x.WorkOrder).WithMany()
            .HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Cascade);

        // P10.7e-1 Q3+Q6 — FQC + OQC + photo column shape + indices.
        b.Entity<WoQcCheck>().Property(x => x.QcKind).HasMaxLength(8).IsRequired();
        b.Entity<WoQcCheck>().Property(x => x.ProfileSnapshotJson).IsRequired();
        b.Entity<WoQcCheck>().Property(x => x.Judgment).HasConversion<string>().HasMaxLength(16);
        b.Entity<WoQcCheck>().Property(x => x.JudgmentReason).HasMaxLength(500);
        b.Entity<WoQcCheck>().Property(x => x.InspectedBy).HasMaxLength(64);
        b.Entity<WoQcCheck>().Property(x => x.ReviewedBy).HasMaxLength(64);
        b.Entity<WoQcCheck>().Property(x => x.ApprovedBy).HasMaxLength(64);
        // (WorkOrderId, QcKind) unique — 1 active row per kind per WO.
        b.Entity<WoQcCheck>().HasIndex(x => new { x.WorkOrderId, x.QcKind }).IsUnique();

        b.Entity<WoQcCheckItem>().Property(x => x.ItemKey).HasMaxLength(64).IsRequired();
        b.Entity<WoQcCheckItem>().Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.Entity<WoQcCheckItem>().Property(x => x.NgReasonCode).HasMaxLength(64);
        b.Entity<WoQcCheckItem>().Property(x => x.NgNote).HasMaxLength(500);
        // (WoQcCheckId, ItemKey) unique — one row per item per check.
        b.Entity<WoQcCheckItem>().HasIndex(x => new { x.WoQcCheckId, x.ItemKey }).IsUnique();
        b.Entity<WoQcCheckItem>().HasOne(x => x.WoQcCheck)
            .WithMany(c => c.Items)
            .HasForeignKey(x => x.WoQcCheckId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<WoQcPhoto>().Property(x => x.Sha256).HasMaxLength(64).IsRequired();
        b.Entity<WoQcPhoto>().Property(x => x.MimeType).HasMaxLength(32).IsRequired();
        b.Entity<WoQcPhoto>().Property(x => x.OriginalFileName).HasMaxLength(255);
        b.Entity<WoQcPhoto>().Property(x => x.RelativePath).HasMaxLength(512);
        b.Entity<WoQcPhoto>().Property(x => x.UploadedBy).HasMaxLength(64);
        b.Entity<WoQcPhoto>().HasIndex(x => x.WoQcCheckItemId);
        b.Entity<WoQcPhoto>().HasIndex(x => x.Sha256);

        // Phase 8 PR #28 — uniqueness + lookup indexes cho new schema.
        // (ProductId + RevisionCode) unique để enforce A/B/C duy nhất per product.
        b.Entity<ProductRevision>().HasIndex(x => new { x.ProductId, x.RevisionCode }).IsUnique();
        b.Entity<ProductRevision>().HasIndex(x => x.SpecCode);
        b.Entity<ProductRevision>().HasIndex(x => x.Status);
        // 1:1 sibling specs: index theo FK đủ; KHÔNG enforce unique (cho phép
        // soft-create empty siblings rồi populate sau).
        b.Entity<SpecMaterial>().HasIndex(x => x.ProductRevisionId).IsUnique();
        b.Entity<SpecPrint>().HasIndex(x => x.ProductRevisionId).IsUnique();
        b.Entity<SpecDiecut>().HasIndex(x => x.ProductRevisionId).IsUnique();
        b.Entity<SpecFinishing>().HasIndex(x => x.ProductRevisionId).IsUnique();
        // Drawing master per (revision, kind, title) — SpecHub pattern.
        b.Entity<Drawing>().HasIndex(x => new { x.ProductRevisionId, x.Kind, x.Title }).IsUnique();
        b.Entity<DrawingVersion>().HasIndex(x => new { x.DrawingId, x.VersionNo }).IsUnique();
        b.Entity<DrawingApproval>().HasIndex(x => new { x.DrawingVersionId, x.Role }).IsUnique();
        // QC plan lookups
        b.Entity<SpecQcWindow>().HasIndex(x => new { x.ProductRevisionId, x.Stage });
        b.Entity<QcCriterion>().HasIndex(x => new { x.SpecQcWindowId, x.Seq }).IsUnique();
        // Phase 8 PR-D-4 — QC Capture (append-only result rows). Lookup by
        // (window + criterion) for current pill render, by CapturedAt for
        // timeline view. ReasonCode.Code unique = natural-key lookup.
        b.Entity<SpecQcCapture>().HasIndex(x => new { x.SpecQcWindowId, x.QcCriterionId });
        b.Entity<SpecQcCapture>().HasIndex(x => x.CapturedAt);
        b.Entity<ReasonCode>().HasIndex(x => x.Code).IsUnique();
        b.Entity<ReasonCode>().HasIndex(x => new { x.Kind, x.Active, x.Sort });
        // ProcessCatalog Code = stable string code (unique lookup); EF still
        // uses Id as PK per BaseEntity convention.
        b.Entity<ProcessCatalog>().HasIndex(x => x.Code).IsUnique();
        b.Entity<ProcessCatalog>().HasIndex(x => new { x.Category, x.Status, x.DisplayOrder });

        // WorkOrder → ProductRevision FK (replaces WO → SpecVersion).
        b.Entity<WorkOrder>().HasOne(x => x.ProductRevision)
            .WithMany()
            .HasForeignKey(x => x.ProductRevisionId)
            .OnDelete(DeleteBehavior.Restrict);

        // Drawing has 2 relationships to DrawingVersion — disambiguate cho EF:
        //   (1) Drawing.Versions ↔ DrawingVersion.Drawing (1:N, FK DrawingId)
        //   (2) Drawing.CurrentVersion (1:0..1 optional pointer, FK CurrentVersionId,
        //       NO inverse navigation — different version có thể là current).
        // KHÔNG có inverse từ DrawingVersion → "owning current" để tránh chu trình ngầm.
        b.Entity<Drawing>()
            .HasMany(x => x.Versions)
            .WithOne(x => x.Drawing!)
            .HasForeignKey(x => x.DrawingId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<Drawing>()
            .HasOne(x => x.CurrentVersion)
            .WithMany()
            .HasForeignKey(x => x.CurrentVersionId)
            .OnDelete(DeleteBehavior.SetNull);

        // DrawingVersion 1:N DrawingApproval (3 role rows per version).
        b.Entity<DrawingVersion>()
            .HasMany(x => x.Approvals)
            .WithOne(x => x.DrawingVersion!)
            .HasForeignKey(x => x.DrawingVersionId)
            .OnDelete(DeleteBehavior.Cascade);

        // SpecQcWindow 1:N QcCriterion.
        b.Entity<SpecQcWindow>()
            .HasMany(x => x.Criteria)
            .WithOne(x => x.SpecQcWindow!)
            .HasForeignKey(x => x.SpecQcWindowId)
            .OnDelete(DeleteBehavior.Cascade);

        // Phase 8 PR-D-4 — SpecQcCapture FK both to window + criterion.
        // Cascade from window (delete window → all captures gone too);
        // RESTRICT from criterion so historical captures survive a criterion
        // delete (operator forensic trail). NgReasonCode is a string lookup,
        // NOT an EF FK (matches CMES pattern — loose coupling, validates in
        // service layer).
        b.Entity<SpecQcCapture>()
            .HasOne(x => x.SpecQcWindow)
            .WithMany()
            .HasForeignKey(x => x.SpecQcWindowId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<SpecQcCapture>()
            .HasOne(x => x.QcCriterion)
            .WithMany()
            .HasForeignKey(x => x.QcCriterionId)
            .OnDelete(DeleteBehavior.Restrict);

        // ProductRevision 1:1 sibling specs (Material/Print/Diecut/Finishing).
        // Delete cascade từ ProductRevision → siblings (orphan-safe).
        b.Entity<ProductRevision>()
            .HasOne(x => x.Material)
            .WithOne(x => x.ProductRevision!)
            .HasForeignKey<SpecMaterial>(x => x.ProductRevisionId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<ProductRevision>()
            .HasOne(x => x.Print)
            .WithOne(x => x.ProductRevision!)
            .HasForeignKey<SpecPrint>(x => x.ProductRevisionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Phase 8 PR #31a — SpecPrint 1:N SpecPrintColor (silkscreen print rows).
        // Cascade delete để xóa color rows khi xóa SpecPrint master.
        b.Entity<SpecPrint>()
            .HasMany(x => x.Colors)
            .WithOne(x => x.SpecPrint!)
            .HasForeignKey(x => x.SpecPrintId)
            .OnDelete(DeleteBehavior.Cascade);
        // Lookup index — (SpecPrintId + Seq) unique giữ thứ tự in canonical.
        b.Entity<SpecPrintColor>().HasIndex(x => new { x.SpecPrintId, x.Seq }).IsUnique();
        // PlateCode + InkCode search (PR #33 detail sheet + future cross-spec lookup).
        b.Entity<SpecPrintColor>().HasIndex(x => x.PlateCode);
        b.Entity<SpecPrintColor>().HasIndex(x => x.InkCode);

        // Phase 8 PR #31b — Flexo cutting + ink rows (1:N từ SpecPrint).
        // Cascade delete để xóa cả 3 dạng row con khi xóa SpecPrint master.
        b.Entity<SpecPrint>()
            .HasMany(x => x.FlexoCuttingRows)
            .WithOne(x => x.SpecPrint!)
            .HasForeignKey(x => x.SpecPrintId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<SpecPrint>()
            .HasMany(x => x.FlexoInkRows)
            .WithOne(x => x.SpecPrint!)
            .HasForeignKey(x => x.SpecPrintId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<SpecFlexoCuttingRow>().HasIndex(x => new { x.SpecPrintId, x.Seq }).IsUnique();
        b.Entity<SpecFlexoInkRow>().HasIndex(x => new { x.SpecPrintId, x.Seq }).IsUnique();
        b.Entity<SpecFlexoInkRow>().HasIndex(x => x.PlateCode);
        b.Entity<SpecFlexoInkRow>().HasIndex(x => x.InkCode);
        b.Entity<ProductRevision>()
            .HasOne(x => x.Diecut)
            .WithOne(x => x.ProductRevision!)
            .HasForeignKey<SpecDiecut>(x => x.ProductRevisionId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<ProductRevision>()
            .HasOne(x => x.Finishing)
            .WithOne(x => x.ProductRevision!)
            .HasForeignKey<SpecFinishing>(x => x.ProductRevisionId)
            .OnDelete(DeleteBehavior.Cascade);

        // ProductRevision 1:N SpecQcWindow + 1:N Drawing (master records).
        b.Entity<ProductRevision>()
            .HasMany(x => x.QcWindows)
            .WithOne(x => x.ProductRevision!)
            .HasForeignKey(x => x.ProductRevisionId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<ProductRevision>()
            .HasMany(x => x.Drawings)
            .WithOne(x => x.ProductRevision!)
            .HasForeignKey(x => x.ProductRevisionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Index cho tra cứu nhanh các bảng NPI dữ liệu lớn
        b.Entity<WorkCenter>().HasIndex(x => x.Code);
        b.Entity<RawMaterial>().HasIndex(x => x.PartNo);
        b.Entity<RoutingOperation>().HasIndex(x => x.PartNo);
        b.Entity<ManufacturingStructure>().HasIndex(x => x.ParentPart);

        // Thư viện hạng mục kiểm (re-model v5). ItemId là natural key (upsert
        // idempotent theo ItemId); resolver lookup theo ProcessLine + cờ stage
        // Ipqc/Fqc/Oqc (thay QcStage cũ).
        b.Entity<CheckItemLibrary>().HasIndex(x => x.ItemId).IsUnique();

        // ── P12 IQC ──────────────────────────────────────────────────────
        b.Entity<IqcCheckItemLibrary>().HasIndex(x => x.ItemId).IsUnique();
        b.Entity<IqcCheckItemLibrary>().HasIndex(x => x.GroupCode);

        b.Entity<IqcMaterialSpec>().HasIndex(x => x.SpecNo).IsUnique();
        // Hai đường tra nguyên liệu → spec. Mã IFS là khoá ưu tiên (44/459
        // spec có), tên là đường lùi. KHÔNG unique: cùng một nguyên liệu có
        // thể có nhiều spec qua các đời revision.
        b.Entity<IqcMaterialSpec>().HasIndex(x => x.MaterialCodeIfs);
        b.Entity<IqcMaterialSpec>().HasIndex(x => x.MaterialCode);

        // Khoá tự nhiên BA thành phần — chống nhân đôi khi import chạy lại,
        // NHƯNG vẫn cho một spec có nhiều tiêu chí cùng mã hạng mục (12 cặp
        // như vậy trong dữ liệu thật). Khoá hai thành phần sẽ nuốt mất 13
        // tiêu chí kiểm mà không báo gì.
        b.Entity<IqcSpecItem>().HasIndex(x => new { x.SpecNo, x.ItemId, x.Seq }).IsUnique();
        b.Entity<IqcSpecItem>().HasIndex(x => x.SpecNo);

        // ── P13 ──────────────────────────────────────────────────────────
        // Enum lưu dạng CHUỖI, đúng quy ước repo (WorkOrder.Status,
        // Drawing.Kind…): đọc thẳng được bằng sqlite3 lúc điều tra sự cố, và
        // thêm giá trị mới không phải lo thứ tự khai enum.
        // HasConversion<string> KHÔNG chỉ để dễ đọc bằng sqlite3: scanner của
        // gate enum-integrity đi theo model EF tìm property KIỂU ENUM có
        // conversion. Khai chúng là `string` thì gate quét qua mà không thấy —
        // vẫn báo PASS, và ba cột này lặng lẽ nằm ngoài lưới an toàn.
        b.Entity<IqcCheckItemLibrary>().Property(x => x.Category)
            .HasConversion<string>().HasMaxLength(16);
        b.Entity<IqcCheckItemLibrary>().Property(x => x.Kind)
            .HasConversion<string>().HasMaxLength(16);
        b.Entity<IqcCheckItemLibrary>().HasIndex(x => new { x.Category, x.Kind });

        b.Entity<IqcMaterialSpec>().Property(x => x.Approval)
            .HasConversion<string>().HasMaxLength(16);
        // Tra "còn bao nhiêu spec chờ duyệt" là câu hỏi hằng ngày của QC sau
        // khi import 672 mã mới — không index thì quét cả bảng mỗi lần mở.
        b.Entity<IqcMaterialSpec>().HasIndex(x => x.Approval);

        // Hai lần đo cùng số thứ tự trên một hạng mục là dữ liệu HỎNG, không
        // phải hai lần đo. Chặn ở tầng DB chứ đừng chỉ chặn ở service.
        b.Entity<IqcResultMeasurement>()
            .HasIndex(x => new { x.IqcResultDetailId, x.Seq }).IsUnique();

        // P12 bước 4 — hồ sơ HSF theo MÃ nguyên liệu. Một mã có tối đa MỘT
        // dòng cho mỗi loại hồ sơ; hai dòng "TDS" cùng mã thì không ai biết
        // bản nào đang hiệu lực.
        b.Entity<IqcMaterialDocument>().HasIndex(x => new { x.MaterialCode, x.DocType }).IsUnique();
        b.Entity<IqcMaterialDocument>().HasIndex(x => x.MaterialCode);
        b.Entity<CheckItemLibrary>().HasIndex(x => x.ProcessLine);

        // Auth — Username is unique and matched CASE-INSENSITIVELY. The
        // column carries a NOCASE collation so both the login lookup
        // (WHERE Username = @p) and the unique index treat "OQC" == "oqc":
        // an admin-reset user can sign in regardless of the case they type,
        // and "OQC"/"oqc" can never become two distinct rows. See Lesson
        // L26 (LESSONS-LEARNED.md) — case-sensitive lookup masked a correct
        // password behind a 401 after reset.
        b.Entity<User>().Property(x => x.Username).UseCollation("NOCASE");
        b.Entity<User>().HasIndex(x => x.Username).IsUnique();

        // Quality → Traceability frozen snapshots: unique per (WO, phase,
        // version) so a re-freeze must bump Version; WoNo/WoId indexed for
        // the list search + merged read. No FK to source entities by design.
        b.Entity<WoTraceSnapshot>().HasIndex(x => new { x.WoId, x.Phase, x.Version }).IsUnique();
        b.Entity<WoTraceSnapshot>().HasIndex(x => x.WoNo);
        b.Entity<WoTraceSnapshot>().HasIndex(x => x.WoId);

        // Real-time Traceability index — one MUTABLE row per WO (drives the
        // live list). Unique on WoId + WoNo so a scan/find upsert can't dup.
        b.Entity<WoTraceIndex>().HasIndex(x => x.WoId).IsUnique();
        b.Entity<WoTraceIndex>().HasIndex(x => x.WoNo).IsUnique();

        // Phase 6 Bước 5 — audit log indexes for Syslog filter UX.
        // Sort hiển thị thường theo Timestamp DESC; filter theo
        // ActorUsername / Action là pattern phổ biến nhất.
        b.Entity<AuditLog>().HasIndex(x => x.Timestamp);
        b.Entity<AuditLog>().HasIndex(x => x.ActorUsername);
        b.Entity<AuditLog>().HasIndex(x => x.Action);

        // Phase 6 Bước 7 — IqcInspection enum-as-string + lookup indexes.
        // Hybrid FK: RawMaterialId nullable; PartNo text snapshot bắt buộc.
        // Index theo PartNo + BatchNumber là pattern operator tra cứu phổ
        // biến nhất; ReceivedDate phục vụ sort DESC mặc định trên grid.
        b.Entity<IqcInspection>().Property(x => x.Result).HasConversion<string>();
        b.Entity<IqcInspection>().HasIndex(x => x.PartNo);
        b.Entity<IqcInspection>().HasIndex(x => x.BatchNumber);
        b.Entity<IqcInspection>().HasIndex(x => x.ReceivedDate);

        // feat/iqc-ticket — 6 field additive cho phiếu IQC.
        // ReceiptNo NOCASE + filtered unique WHERE IS NOT NULL: 3 dòng legacy
        // để null vẫn hợp lệ (index bỏ qua null), phiếu mới sinh không đụng độ
        // hoa/thường. Mirror pattern MaterialLot.LotNo (NOCASE + HasFilter).
        b.Entity<IqcInspection>().Property(x => x.ReceiptNo).HasMaxLength(32).UseCollation("NOCASE");
        b.Entity<IqcInspection>().Property(x => x.CodeIfs).HasMaxLength(64);
        b.Entity<IqcInspection>().Property(x => x.MakerName).HasMaxLength(120);
        b.Entity<IqcInspection>().Property(x => x.MaterialDescription).HasMaxLength(500);
        b.Entity<IqcInspection>().Property(x => x.IfsDescription).HasMaxLength(500);
        b.Entity<IqcInspection>().HasIndex(x => x.ReceiptNo)
            .IsUnique().HasFilter("\"ReceiptNo\" IS NOT NULL")
            .HasDatabaseName("IX_IqcInspections_ReceiptNo_Unique");

        // feat/iqc-module-tabs — Group ADDITIVE (default "Materials"). Cột NOT
        // NULL an toàn vì có default constant; backfill migration set legacy =
        // "Materials". Index phục vụ lọc IQC Data theo nhóm + KPI Dashboard đếm.
        b.Entity<IqcInspection>().Property(x => x.Group).HasMaxLength(20).HasDefaultValue("Materials");
        b.Entity<IqcInspection>().HasIndex(x => x.Group);

        // Tính toán read-only -> không map vào DB
        b.Entity<WorkOrder>().Ignore("LastQc");
        b.Entity<ProductionLog>().Ignore(p => p.DurationMinutes);

        // P10.7a-1.2 — IdempotencyKey mapping.
        // (KeyValue + ActorId) is the natural-key unique index — two
        // different actors can re-use the same UUID without collision.
        // EndpointPath + BodySha256 are length-capped to keep row size
        // predictable; ResponseBody has no max-length at the column
        // level (the middleware caps the buffered response at 256 KB
        // before insert).
        b.Entity<IdempotencyKey>().Property(x => x.KeyValue).HasMaxLength(64).IsRequired();
        b.Entity<IdempotencyKey>().Property(x => x.EndpointPath).HasMaxLength(256).IsRequired();
        b.Entity<IdempotencyKey>().Property(x => x.BodySha256).HasMaxLength(64).IsRequired();
        b.Entity<IdempotencyKey>().Property(x => x.ResponseContentType).HasMaxLength(128);
        b.Entity<IdempotencyKey>().HasIndex(x => new { x.KeyValue, x.ActorId }).IsUnique();
        b.Entity<IdempotencyKey>().HasIndex(x => x.ExpiresAtUtc);
        b.Entity<IdempotencyKey>().HasIndex(x => x.CompletedAtUtc);
    }
}
