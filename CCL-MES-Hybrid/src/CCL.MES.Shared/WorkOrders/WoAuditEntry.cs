namespace CCL.MES.Shared.WorkOrders;

/// <summary>
/// P10.7 — one audit-trail row for a WO, surfaced in the scan-surface
/// sidebar (SpecHub "Audit Trail" parity). Read-only.
/// </summary>
public sealed record WoAuditEntry
{
    public DateTime Timestamp { get; init; }
    public string Action { get; init; } = "";
    public string ActorUsername { get; init; } = "";
    public string? Detail { get; init; }
}
