using CCL.MES.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Application;

/// <summary>Trừu tượng hóa DbContext để tầng Application không phụ thuộc Infrastructure.</summary>
public interface IMesDbContext
{
    DbSet<Customer> Customers { get; }
    DbSet<Product> Products { get; }
    // Phase 8 PR #28 — Spec/SpecVersion/SpecParameter REMOVED via clean
    // rewrite. New ProductRevision + 4 sibling specs + Drawings + QC plan +
    // ProcessCatalog DbSets below.
    DbSet<ProductRevision> ProductRevisions { get; }
    DbSet<SpecMaterial> SpecMaterials { get; }
    DbSet<SpecPrint> SpecPrints { get; }
    DbSet<SpecPrintColor> SpecPrintColors { get; }
    DbSet<SpecFlexoCuttingRow> SpecFlexoCuttingRows { get; }
    DbSet<SpecFlexoInkRow> SpecFlexoInkRows { get; }
    DbSet<SpecDiecut> SpecDiecuts { get; }
    DbSet<SpecFinishing> SpecFinishings { get; }
    DbSet<Drawing> Drawings { get; }
    DbSet<DrawingVersion> DrawingVersions { get; }
    DbSet<DrawingApproval> DrawingApprovals { get; }
    DbSet<SpecQcWindow> SpecQcWindows { get; }
    DbSet<QcCriterion> QcCriteria { get; }
    // Phase 8 PR-D-4 — QC Capture entity + ReasonCode lookup.
    DbSet<SpecQcCapture> SpecQcCaptures { get; }
    DbSet<ReasonCode> ReasonCodes { get; }
    DbSet<ProcessCatalog> ProcessCatalogs { get; }
    DbSet<WorkOrder> WorkOrders { get; }
    DbSet<WoStatusHistory> WoStatusHistories { get; }
    DbSet<QcInspection> QcInspections { get; }
    DbSet<QcResultDetail> QcResultDetails { get; }
    DbSet<Machine> Machines { get; }
    DbSet<DowntimeReason> DowntimeReasons { get; }
    DbSet<ProductionLog> ProductionLogs { get; }
    DbSet<WorkInstruction> WorkInstructions { get; }
    DbSet<WiStepDetail> WiStepDetails { get; }
    DbSet<WorkCenter> WorkCenters { get; }
    DbSet<RawMaterial> RawMaterials { get; }
    DbSet<RoutingOperation> RoutingOperations { get; }
    DbSet<ManufacturingStructure> ManufacturingStructures { get; }
    DbSet<User> Users { get; }
    DbSet<AuditLog> AuditLogs { get; }
    // Phase 6 Bước 7 — IQC = pre-WO raw-material inspection, tách
    // khỏi QcInspections vì semantically khác (xem Iqc.cs).
    DbSet<IqcInspection> IqcInspections { get; }
    DbSet<IqcResultDetail> IqcResultDetails { get; }
    // P10.7a-1.2 — Idempotency ledger (per contract §6.2).
    DbSet<IdempotencyKey> IdempotencyKeys { get; }
    // P10.7b-1 — PREPRESS row-level child tables (per contract §5.1).
    DbSet<WoMaterial> WoMaterials { get; }
    DbSet<WoPlateCheck> WoPlateChecks { get; }
    DbSet<WoCutterCheck> WoCutterChecks { get; }
    // P10.7c-1 — RUNNING + PAUSED child tables (per contract §5.4).
    DbSet<WoRunSession> WoRunSessions { get; }
    DbSet<WoPauseEvent> WoPauseEvents { get; }
    DbSet<WoQtyEntry> WoQtyEntries { get; }
    // P10.7d-1 — IPQC review surface (per contract §5.5).
    DbSet<WoIpqcCheck> WoIpqcChecks { get; }
    // Phương án C — Bước 1/2: thư viện hạng mục + IPQC items data-driven.
    DbSet<CheckItemLibrary> CheckItemLibraries { get; }
    DbSet<WoIpqcCheckItem> WoIpqcCheckItems { get; }
    // P10.7e-1 Q3+Q6 — FQC + OQC data-driven surface + photo evidence.
    DbSet<WoQcCheck> WoQcChecks { get; }
    DbSet<WoQcCheckItem> WoQcCheckItems { get; }
    DbSet<WoQcPhoto> WoQcPhotos { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
