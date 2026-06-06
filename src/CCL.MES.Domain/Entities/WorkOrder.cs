namespace CCL.MES.Domain.Entities;

public class WorkOrder : BaseEntity
{
    public string WoNo { get; set; } = "";
    public long CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public long ProductId { get; set; }
    public Product? Product { get; set; }
    public string ProductName { get; set; } = "";

    // Phase 8 PR #28 — Spec → ProductRevision clean rewrite.
    //   Was: SpecVersionId / SpecVersion (Phase 6 baseline)
    //   Now: ProductRevisionId / ProductRevision (SpecHub revision-is-truth model)
    // Migration `AddProductRevisionSchema` UPDATE WorkOrders SET ProductRevisionId =
    // SpecVersionId WHERE SpecVersionId IS NOT NULL (PK preserved 1:1 remap),
    // then DROP column SpecVersionId.
    public long? ProductRevisionId { get; set; }
    public ProductRevision? ProductRevision { get; set; }

    public string? MachineCode { get; set; }
    public string? MachineName { get; set; }

    public int TargetQty { get; set; }
    public string Uom { get; set; } = "pcs";
    public int ProducedQty { get; set; }

    public ProcessStepCode CurrentStep { get; set; } = ProcessStepCode.PrePressCheck;
    public WoStatus Status { get; set; } = WoStatus.Draft;
    public int Priority { get; set; }

    // Điều kiện (guard) cho state machine
    public bool MaterialsReady { get; set; }
    public bool SetupConfirmed { get; set; }
    public bool RohsOk { get; set; }

    public DateTime? PlannedStart { get; set; }
    public DateTime? PlannedEnd { get; set; }

    // P10.7a-1 — canonical 12-state model per docs/P10.7-WO-STATE-CONTRACT.md.
    // Stored as string so legacy Web reads see a readable column; the
    // <see cref="CCL.MES.Domain.StateMachine.MesPhase"/> enum is the
    // intended type. Server-side write path projects MesPhase →
    // CurrentStep deterministically in
    // WorkOrderStateMachine.ProjectToLegacy so legacy Razor pages keep
    // rendering the 8-step badge unchanged.
    public string MesPhase { get; set; } = "NEW";

    // P10.7a-1 — EF Core optimistic-concurrency token. SQL Server bumps
    // automatically; SQLite uses the trigger created in migration
    // AddWorkOrderRowVersionAndMesPhase to generate a fresh randomblob(8)
    // on every UPDATE where the token wasn't explicitly set by the
    // application. Mutation endpoints carry the token as base64 in the
    // If-Match HTTP header (PR 7a-1.3).
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    // P10.7c-1 — SETTING phase timer + RUNNING counter denormalisation.
    // Per contract §5.4 amendment. SettingStartAt is set ONCE on the
    // first PREPRESS → SETTING transition (null-guarded so race writes
    // don't reset it); SettingEndAt is set on SETTING → IPQC_WAIT;
    // SettingDurationSec computed at controller commit time. The
    // QtyDoneCached/QtyNgCached columns are denormalised snapshots of
    // SUM(WoQtyEntry.QtyDoneDelta)/SUM(WoQtyEntry.QtyNgDelta) updated
    // on every WO_RUN_QTY_ADD / WO_RUN_QTY_CORRECT write; the ledger
    // remains authoritative.
    public DateTime? SettingStartAt { get; set; }
    public DateTime? SettingEndAt { get; set; }
    public int? SettingDurationSec { get; set; }
    public int QtyDoneCached { get; set; }
    public int QtyNgCached { get; set; }

    public List<WoStatusHistory> History { get; set; } = new();
    public List<QcInspection> Inspections { get; set; } = new();

    /// <summary>Lần kiểm QC gần nhất theo loại (IPQC/FQC/OQC).</summary>
    public QcInspection? LastQc(QcType type) =>
        Inspections.Where(i => i.Type == type).OrderByDescending(i => i.Id).FirstOrDefault();
}

/// <summary>Log append-only mọi lần chuyển bước.</summary>
public class WoStatusHistory : BaseEntity
{
    public long WorkOrderId { get; set; }
    public ProcessStepCode FromStep { get; set; }
    public ProcessStepCode ToStep { get; set; }
    public string Action { get; set; } = "";
    public string? ByUser { get; set; }
    public string? Reason { get; set; }
}
