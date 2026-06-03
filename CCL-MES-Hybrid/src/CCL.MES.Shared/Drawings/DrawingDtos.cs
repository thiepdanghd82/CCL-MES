namespace CCL.MES.Shared.Drawings;

/// <summary>
/// P10.5b — Drawings tab (read). Mirrors
/// <c>DrawingsService.ListByRevisionAsync</c> return shape:
/// nine <see cref="DrawingKindSlot"/>s per revision (one per
/// <see cref="DrawingKindCode"/>), each carrying its current
/// version + history + per-role approval chain.
/// </summary>
public sealed record DrawingKindSlot
{
    public DrawingKindCode Kind { get; init; }
    public long? DrawingId { get; init; }
    public string Title { get; init; } = "";
    public DrawingSlotStatus DrawingStatus { get; init; }
    public long? CurrentVersionId { get; init; }
    public List<DrawingVersionView> Versions { get; init; } = new();
}

public sealed record DrawingVersionView(
    long Id,
    int VersionNo,
    string FileName,
    string Sha256Hex,
    long FileSize,
    DrawingVersionStatus Status,
    string? ChangeReason,
    DateTime UploadedAt,
    string? UploadedBy,
    List<DrawingApprovalView> Approvals);

public sealed record DrawingApprovalView(
    long Id,
    DrawingApprovalRole Role,
    DrawingApprovalStatus Status,
    string? ActedBy,
    DateTime? ActedAt,
    string? Comment);

public enum DrawingKindCode
{
    CustomerDrawing,
    NpiPrintLayout,
    NpiCutLayout,
    IpqcPrintReference,
    IpqcCutReference,
    FqcChecksheet,
    OqcChecksheet,
    CustomerApproval,
    InternalProof,
}

public enum DrawingSlotStatus
{
    Draft,
    PendingApproval,
    Approved,
    Superseded,
    Withdrawn,
}

public enum DrawingVersionStatus
{
    Draft,
    PendingApproval,
    Approved,
    Rejected,
    Superseded,
}

public enum DrawingApprovalRole
{
    Npi,
    Production,
    Qc,
}

public enum DrawingApprovalStatus
{
    Pending,
    Approved,
    Rejected,
}
