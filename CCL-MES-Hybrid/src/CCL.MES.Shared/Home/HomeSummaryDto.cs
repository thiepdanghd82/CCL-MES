namespace CCL.MES.Shared.Home;

/// <summary>
/// P10.10 — read-only aggregate powering the Home dashboard KPI tiles
/// (SpecHub parity: Specs in library / Pending approvals / Drafts /
/// Today's activity). Live-recomputed on each GET; no mutation surface.
/// </summary>
public sealed record HomeSummaryDto
{
    /// <summary>Non-trashed product revisions in the spec library.</summary>
    public int SpecsTotal { get; init; }

    /// <summary>Revisions awaiting approval (Status = InReview).</summary>
    public int PendingApprovals { get; init; }

    /// <summary>Draft revisions (Status = Draft).</summary>
    public int Drafts { get; init; }

    /// <summary>Work orders touched today (UpdatedAt = today, UTC).</summary>
    public int TodayActivity { get; init; }
}
