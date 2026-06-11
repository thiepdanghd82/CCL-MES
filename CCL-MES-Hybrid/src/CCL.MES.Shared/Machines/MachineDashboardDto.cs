namespace CCL.MES.Shared.Machines;

/// <summary>
/// P10.8 — one row of the Machine Dashboard: a work-center plus the
/// live status derived from its active work order (matched by
/// <c>WorkOrder.MachineCode == WorkCenter.Code</c>).
///
/// Status (slice 1) is derived from the active WO's MesPhase:
///   Running = RUNNING|PAUSED · Setup = SETTING|PREPRESS|IPQC_WAIT|
///   QA_PENDING|IPQC_APPROVED · Idle = no active pre-FQC WO.
/// Down/Maintenance need the ProductionLog (OEE) feed and land in a
/// later slice.
/// </summary>
public sealed record MachineDashboardItem
{
    public long WorkCenterId { get; init; }
    public string Code { get; init; } = "";
    public string Description { get; init; } = "";
    public string? Area { get; init; }

    /// <summary>"Running" | "Setup" | "Idle".</summary>
    public string Status { get; init; } = "Idle";

    public string? ActiveWoNo { get; init; }
    public string? ActiveMesPhase { get; init; }
    public int? TargetQty { get; init; }
    public int? QtyDone { get; init; }
    public int? QtyNg { get; init; }
    public DateTime? LastUpdatedAt { get; init; }
}

/// <summary>
/// P10.8 — Machine Dashboard aggregate: plant KPI counts + per-machine
/// rows. Read-only, live-recomputed on each GET.
/// </summary>
public sealed record MachineDashboardDto
{
    public int Total { get; init; }
    public int Running { get; init; }
    public int Setup { get; init; }
    public int Idle { get; init; }

    public IReadOnlyList<MachineDashboardItem> Machines { get; init; }
        = Array.Empty<MachineDashboardItem>();
}
