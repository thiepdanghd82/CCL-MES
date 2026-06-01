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
    public DbSet<ProcessCatalog> ProcessCatalogs => Set<ProcessCatalog>();
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<WoStatusHistory> WoStatusHistories => Set<WoStatusHistory>();
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

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // Lưu enum dạng chuỗi cho dễ đọc trong DB
        b.Entity<WorkOrder>().Property(x => x.CurrentStep).HasConversion<string>();
        b.Entity<WorkOrder>().Property(x => x.Status).HasConversion<string>();
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
        b.Entity<ProcessCatalog>().Property(x => x.Category).HasConversion<string>();
        b.Entity<ProcessCatalog>().Property(x => x.Status).HasConversion<string>();
        b.Entity<QcInspection>().Property(x => x.Type).HasConversion<string>();
        b.Entity<QcInspection>().Property(x => x.Result).HasConversion<string>();
        b.Entity<WoStatusHistory>().Property(x => x.FromStep).HasConversion<string>();
        b.Entity<WoStatusHistory>().Property(x => x.ToStep).HasConversion<string>();
        b.Entity<Machine>().Property(x => x.CurrentState).HasConversion<string>();
        b.Entity<ProductionLog>().Property(x => x.EventType).HasConversion<string>();
        b.Entity<WorkInstruction>().Property(x => x.Status).HasConversion<string>();
        b.Entity<WorkInstruction>().Property(x => x.ProcessStep).HasConversion<string>();

        b.Entity<WorkOrder>().HasIndex(x => x.WoNo).IsUnique();
        b.Entity<Machine>().HasIndex(x => x.Code).IsUnique();

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

        // Auth — Username must be unique; login lookup hits this index every sign-in.
        b.Entity<User>().HasIndex(x => x.Username).IsUnique();

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

        // Tính toán read-only -> không map vào DB
        b.Entity<WorkOrder>().Ignore("LastQc");
        b.Entity<ProductionLog>().Ignore(p => p.DurationMinutes);
    }
}
