namespace CCL.MES.Shared.Specs;

/// <summary>
/// P10.5c-1 — Spec mutation request DTOs sent FROM MAUI TO the API.
/// Wire-shape mirrors the legacy <c>CCL.MES.Application.Create/Copy/
/// Update/Revise/SupersedeSpecRequest</c> classes so the API controllers
/// can do a 1:1 forward to <see cref="CCL.MES.Application.Services.SpecService"/>.
/// </summary>
public sealed record CreateSpecMutation
{
    public long ProductId { get; init; }
    /// <summary>P10.10 — IFS / product code typed by the engineer (replaces
    /// the product dropdown). When ProductId is 0, the server resolves the
    /// product by this code (find-or-create).</summary>
    public string? IfsCode { get; init; }
    public string SpecCode { get; init; } = "";
    public string Title { get; init; } = "";
    /// <summary>Customer name typed on the Create modal. Find-or-created on the
    /// server and assigned to the product so it syncs into the spec sheet's
    /// Customer cell. Blank falls back to the "UNASSIGNED" customer.</summary>
    public string? Customer { get; init; }
    /// <summary>Planner / process code (SILKSCREEN / FLEXO / INDIGO …).</summary>
    public string? ProcessCode { get; init; }
    public List<SpecParam> Parameters { get; init; } = new();
}

public sealed record SpecParam(
    string ParamName,
    string? Nominal,
    string? TolMin,
    string? TolMax,
    string? Uom,
    bool IsCritical);

public sealed record CopySpecMutation
{
    public long ProductId { get; init; }
    public string SpecCode { get; init; } = "";
    public string Title { get; init; } = "";
}

public sealed record ReviseSpecMutation
{
    /// <summary>Mandatory ≥5 chars per legacy validator. Surface in modal
    /// as required field with VN helper text.</summary>
    public string Reason { get; init; } = "";
}

public sealed record SupersedeSpecMutation
{
    /// <summary>Operator types this to confirm — must match the target
    /// rev's SpecCode exactly. Server enforces; client UI gives a
    /// before-submit nudge for usability.</summary>
    public string ConfirmSpecCode { get; init; } = "";
}

public sealed record UpdateSpecMutation
{
    public string? Title { get; init; }
    public string? RefNo { get; init; }
    public string? InspectionLevel { get; init; }
    public string? ProcessCode { get; init; }
    public string? ColorSpecJson { get; init; }

    // P10.10 — inline-edit additions (all existing entity columns, no
    // migration). Null = leave unchanged. Material scalars:
    public string? SubstrateType { get; init; }
    public string? AdhesiveType { get; init; }
    public int? ThicknessUm { get; init; }
    // Print-parameter scalars (SpecPrint):
    public int? PrintingCavity { get; init; }
    public double? LengthPitchMm { get; init; }
    public double? ProductSizeWmm { get; init; }
    public double? ProductSizeHmm { get; init; }
    public string? RemarksText { get; init; }
    public string? RemarksCutText { get; init; }
    /// <summary>Full replacement of the structured Print colour rows.
    /// Null = leave unchanged; non-null = replace the whole set (rows are
    /// re-sequenced 1..N server-side).</summary>
    public IReadOnlyList<SpecColorRowMutation>? Colors { get; init; }
}

/// <summary>P10.10 — one structured Print-process colour row for inline
/// editing. Field names match the wire contract the API binds to.</summary>
public sealed record SpecColorRowMutation
{
    public string? Surface { get; init; }
    public string? Color { get; init; }
    public string? InkName { get; init; }
    public string? InkCode { get; init; }
    public string? Maker { get; init; }
    public string? Retarder { get; init; }
    public double? Viscosity { get; init; }
    public double? Speed { get; init; }
    public string? Squeegee { get; init; }
    public string? Dry { get; init; }
    public double? TemperatureC { get; init; }
    public int? TimeMin { get; init; }
    public string? Uv { get; init; }
    public double? EmulsionUm { get; init; }
    public string? PlateSize { get; init; }
    public string? Mesh { get; init; }
    public double? AngleDeg { get; init; }
    public string? PlateCode { get; init; }
    public int? ControlNo { get; init; }
    public string? Remark { get; init; }
}

/// <summary>
/// Lightweight response shape mirroring what the controller projects
/// after a successful mutation. Wire-name matches the legacy controller's
/// anonymous-object projection so deserialization is direct.
/// </summary>
public sealed record SpecMutationResponse
{
    public long Id { get; init; }
    public string SpecCode { get; init; } = "";
    public string Revision { get; init; } = "";
    public string? Status { get; init; }
    public string? Title { get; init; }
    public long? ProductId { get; init; }
    public long? ParentId { get; init; }
    public bool? IsTrashed { get; init; }
    public bool? NoChanges { get; init; }
}

/// <summary>
/// Error envelope shared across all 8 lifecycle endpoints. Wire shape
/// mirrors what the controllers project on UnprocessableEntity (422):
/// <c>{ "code": "duplicate_spec_code", "error": "...", ... }</c>.
/// MAUI page maps <see cref="Code"/> to a VN operator-readable message;
/// <see cref="Error"/> is the English fallback.
/// </summary>
public sealed record SpecMutationError
{
    public string Code { get; init; } = "";
    public string Error { get; init; } = "";
    public string? CurrentStatus { get; init; }
    public int? ActiveWoCount { get; init; }
}

/// <summary>Product dropdown row for the Create Spec modal product
/// picker. Mirrors <c>ProductDropdownItem</c> from the legacy DTOs.</summary>
public sealed record SpecProductDropdownItem(long Id, string ProductCode, string Name);
