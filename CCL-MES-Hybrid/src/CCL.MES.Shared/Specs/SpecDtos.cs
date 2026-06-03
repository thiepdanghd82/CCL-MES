namespace CCL.MES.Shared.Specs;

/// <summary>
/// P10.5b — Spec list row DTO. Mirrors
/// <c>CCL.MES.Application.ProductRevisionListItem</c> from Phase 8 PR #30
/// (SpecHub 14-col parity) so System.Text.Json deserializes 1:1 from the
/// existing API endpoint without server-side changes. Lives in
/// <c>CCL.MES.Shared</c> so the MAUI shell never pulls the EF entity.
///
/// Per Q2 (Henry's P10.5 parity decision): we keep the legacy 5-state
/// status enum (<see cref="SpecRevisionStatus"/>) — MAUI renders 5 pills
/// (Draft / InReview / Approved / Released / Superseded) directly, no
/// 5→3 collapse. SpecHub's 3-state UI was the legacy collapsed shape;
/// CCL.MES.Web has carried the full 5-state since Phase 8 and operators
/// rely on the InReview gate.
/// </summary>
public sealed record SpecListItem
{
    public long Id { get; init; }
    public string SpecCode { get; init; } = "";
    public string Title { get; init; } = "";
    public string RevisionCode { get; init; } = "";
    public SpecRevisionStatus Status { get; init; }
    public DateTime? EffectiveFrom { get; init; }
    public string? ApprovedBy { get; init; }
    public string ProductCode { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string? ProcessCode { get; init; }
    public string? CustomerName { get; init; }
    public string? RefNo { get; init; }
    public string? InspectionLevel { get; init; }
    public int NumColors { get; init; }
    public int? Cavity { get; init; }
    public double? PitchMm { get; init; }
    public string? Planner { get; init; }
    public DateTime? LastUpdatedAt { get; init; }
    public string? LastUpdatedBy { get; init; }
}

/// <summary>
/// 5-state status mirror of <c>CCL.MES.Domain.ProductRevisionStatus</c>.
/// Q2 decision: MAUI mirrors web/legacy (5 states), NOT SpecHub's 3-state
/// collapsed display. Wire-name matches the legacy enum exactly so JSON
/// deserialization is direct.
/// </summary>
public enum SpecRevisionStatus
{
    Draft,
    InReview,
    Approved,
    Released,
    Superseded,
}
