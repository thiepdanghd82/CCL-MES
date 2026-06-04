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
}
