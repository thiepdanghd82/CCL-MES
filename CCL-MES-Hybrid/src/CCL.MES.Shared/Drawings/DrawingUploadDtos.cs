namespace CCL.MES.Shared.Drawings;

/// <summary>
/// P10.5e-1 — Response shape after a successful drawing upload. Mirrors
/// the legacy <c>DrawingUploadResult</c> envelope from
/// <c>CCL.MES.Application.Services.DrawingsService</c>; client treats
/// as a typed projection of the same data so MAUI doesn't depend on
/// the Application namespace.
/// </summary>
public sealed record DrawingUploadResponse
{
    public long DrawingId { get; init; }
    public long VersionId { get; init; }
    public int VersionNo { get; init; }
    public string Kind { get; init; } = "";
    public string FileName { get; init; } = "";
    public string Sha256Hex { get; init; } = "";
    public long SizeBytes { get; init; }
}

/// <summary>
/// P10.5e-1 — Error envelope for drawings endpoints. Wire-shape mirrors
/// the spec mutation error pattern (PR #83) so the existing client
/// error pipeline (banners + Thử lại + SpecMutationErrorMapper) keeps
/// working uniformly.
/// </summary>
public sealed record DrawingMutationError
{
    public string Code { get; init; } = "";
    public string Error { get; init; } = "";
    public long? MaxBytes { get; init; }
    public string? AllowedExtensions { get; init; }
    public string? CurrentState { get; init; }
}

/// <summary>
/// P10.5e-2 — Decide request body. Operator picks the chip role they
/// want to act as + an Approve/Reject decision + optional comment
/// (server-side guard makes comment MANDATORY on Reject; client
/// mirrors the rule for UX).
/// </summary>
public sealed record DrawingDecideRequest
{
    /// <summary>One of <c>Npi</c> / <c>Production</c> / <c>Qc</c>
    /// (case-sensitive; matches <see cref="DrawingApprovalRole"/>).</summary>
    public string Role { get; init; } = "";

    /// <summary>One of <c>Approved</c> / <c>Rejected</c>; <c>Pending</c>
    /// is invalid at decide time and rejected by the server.</summary>
    public string Decision { get; init; } = "";

    /// <summary>Optional on Approve; REQUIRED on Reject.</summary>
    public string? Comment { get; init; }
}

/// <summary>
/// P10.5e-2 — Decide response. Mirrors
/// <c>DrawingDecideResult</c> from <c>DrawingsService.DecideAsync</c>.
/// Operator UI uses the post-decide statuses + supersede count to
/// refresh the version chain without re-fetching the parent.
/// </summary>
public sealed record DrawingDecideResponse
{
    public long VersionId { get; init; }
    public DrawingVersionStatus VersionStatus { get; init; }
    public DrawingSlotStatus DrawingStatus { get; init; }
    public int SupersededCount { get; init; }
}
