namespace CCL.MES.Shared.Qms;

/// <summary>P10.9 — one work order waiting in a QC inspection queue.</summary>
public sealed record QmsQueueRow
{
    public long WoId { get; init; }
    public string WoNo { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string? MachineCode { get; init; }
    public int TargetQty { get; init; }
    public int QtyDone { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

/// <summary>
/// P10.9 — QMS Inspection Queue: the worklist of WOs due for each QC
/// stage, auto-built from MesPhase (IPQC = IPQC_WAIT, FQC = FQC_PENDING,
/// OQC = OQC_PENDING). Read-only; the actual capture happens on the
/// per-WO QC dashboards.
/// </summary>
public sealed record QmsQueueDto
{
    public int IpqcCount { get; init; }
    public int FqcCount { get; init; }
    public int OqcCount { get; init; }

    public IReadOnlyList<QmsQueueRow> Ipqc { get; init; } = Array.Empty<QmsQueueRow>();
    public IReadOnlyList<QmsQueueRow> Fqc { get; init; } = Array.Empty<QmsQueueRow>();
    public IReadOnlyList<QmsQueueRow> Oqc { get; init; } = Array.Empty<QmsQueueRow>();
}
