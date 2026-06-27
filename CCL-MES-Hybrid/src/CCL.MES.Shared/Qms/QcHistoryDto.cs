namespace CCL.MES.Shared.Qms;

/// <summary>P10.9 — one completed QC check (FQC/OQC) in the QC History.</summary>
public sealed record QcHistoryRow
{
    public long CheckId { get; init; }
    public long WoId { get; init; }
    public string WoNo { get; init; } = "";
    public string QcKind { get; init; } = "";       // FQC | OQC
    public string Judgment { get; init; } = "";      // Pass | Reject
    public string? JudgmentReason { get; init; }
    public string? InspectedBy { get; init; }
    public string? ReviewedBy { get; init; }
    public string? ApprovedBy { get; init; }
    public DateTime? CompletedAt { get; init; }
}

/// <summary>
/// P10.9 — QC History: forensic record of completed FQC/OQC checks
/// (Judgment != Pending) with KPI roll-ups + kind/judgment/search
/// filters. Read-only. Source: WoQcChecks joined to WorkOrders.
/// </summary>
public sealed record QcHistoryDto
{
    public int Total { get; init; }
    public int Pass { get; init; }
    public int Reject { get; init; }
    public int PassRatePct { get; init; }   // pass / total, 0-100

    public IReadOnlyList<QcHistoryRow> Rows { get; init; } = Array.Empty<QcHistoryRow>();
}
