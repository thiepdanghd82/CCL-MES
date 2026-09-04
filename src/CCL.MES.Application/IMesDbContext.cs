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
    // P11-1/P11-2 — Multi-Method Routing DAG (fork-join).
    DbSet<WoLeg> WoLegs { get; }
    DbSet<WoLegDependency> WoLegDependencies { get; }
    DbSet<ProcessLegMap> ProcessLegMaps { get; }
    // P11.5 — Semi-Stock decoupling (keep-stock).
    DbSet<SemiLot> SemiLots { get; }
    DbSet<SemiAllocation> SemiAllocations { get; }
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
    // P12 — thư viện tiêu chuẩn kiểm tra NVL (IQC).
    DbSet<IqcCheckItemLibrary> IqcCheckItemLibraries { get; }
    DbSet<IqcMaterialSpec> IqcMaterialSpecs { get; }
    DbSet<IqcSpecItem> IqcSpecItems { get; }
    DbSet<IqcMaterialDocument> IqcMaterialDocuments { get; }
    DbSet<WoIpqcCheckItem> WoIpqcCheckItems { get; }
    // IPQC first-article — MATERIAL (SYSTEM) LOT reconciliation per BOM line.
    DbSet<WoIpqcMaterialCheck> WoIpqcMaterialChecks { get; }
    // P10.7g — SETTING per-item persist + defect catalog per hạng mục.
    DbSet<WoSettingCheckItem> WoSettingCheckItems { get; }
    DbSet<CheckItemDefectOption> CheckItemDefectOptions { get; }
    // Phương án C — Bước 6: map process→QC line (data-driven, quyết định #5).
    DbSet<ProcessLineMap> ProcessLineMaps { get; }
    // P10.7e-1 Q3+Q6 — FQC + OQC data-driven surface + photo evidence.
    DbSet<WoQcCheck> WoQcChecks { get; }
    DbSet<WoQcCheckItem> WoQcCheckItems { get; }
    // Quality → Traceability frozen snapshots (append-only, immutable).
    DbSet<WoTraceSnapshot> WoTraceSnapshots { get; }
    // Real-time Traceability index — one mutable row per WO (drives the list).
    DbSet<WoTraceIndex> WoTraceIndexes { get; }
    DbSet<WoQcPhoto> WoQcPhotos { get; }
    // A1 — mạch lô nguyên vật liệu. MaterialLots = lô vật lý (khoá tự nhiên
    // LotNo NOCASE + TRIM ở schema); WoMaterialConsumptions = từng LẦN QUÉT
    // (append-only, Đ4). Truy xuất nguồn gốc đi bằng FK, chuỗi lô chỉ là nhãn.
    DbSet<MaterialLot> MaterialLots { get; }
    DbSet<WoMaterialConsumption> WoMaterialConsumptions { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
