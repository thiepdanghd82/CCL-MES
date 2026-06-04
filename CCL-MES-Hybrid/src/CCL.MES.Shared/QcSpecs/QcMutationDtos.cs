namespace CCL.MES.Shared.QcSpecs;

/// <summary>
/// P10.5f — Per-stage QC plan upsert request. Operator edits one of
/// 4 stages (IpqcPrint / IpqcCut / Fqc / Oqc) at a time; the server
/// reads the current rows, computes a diff (delete / update / insert),
/// and writes atomically inside a single transaction. Mirrors the
/// legacy <c>SpecQcWindowService.UpsertStageAsync</c> contract.
/// </summary>
public sealed record QcPlanUpsertRequest
{
    /// <summary>Target stage — string form of <see cref="Domain.QcStage"/>
    /// so the wire stays human-readable. Case-insensitive.</summary>
    public string Stage { get; init; } = "";

    /// <summary>Full list of criteria for the stage AFTER save — rows
    /// with Id matching an existing criterion are updated in place,
    /// existing criteria not in this list are hard-deleted, rows with
    /// Id=null are inserted. Empty list = clear the stage.</summary>
    public List<QcCriterionRowRequest> Rows { get; init; } = new();
}

/// <summary>
/// One editable row in the QC Plans table. Matches the 5 free-form
/// cells (Criterion / Target / Tolerance / Method / Frequency). Per
/// the legacy mapping (PR-D-3):
///   - Name → QcCriterion.Name (required)
///   - Target → QcCriterion.PassCriteria
///   - Tolerance → QcCriterion.MeasureMethod
///   - Method → QcCriterion.Method (NEW PR-D-3)
///   - Frequency → QcCriterion.Frequency (NEW PR-D-3)
/// </summary>
public sealed record QcCriterionRowRequest
{
    public long? Id { get; init; }
    public string Name { get; init; } = "";
    public string? Target { get; init; }
    public string? Tolerance { get; init; }
    public string? Method { get; init; }
    public string? Frequency { get; init; }
}

/// <summary>
/// Response shape after a successful per-stage upsert. Server returns
/// the freshly-loaded window so the UI can re-bind the table without a
/// separate round-trip. Mirrors the existing <see cref="QcWindowItem"/>
/// read shape — we reuse it directly here.
/// </summary>
public sealed record QcPlanUpsertResponse
{
    public QcWindowItem Window { get; init; } = new();
    public int Created { get; init; }
    public int Updated { get; init; }
    public int Deleted { get; init; }
}

/// <summary>
/// P10.5f — QC capture write request. Operator fills the capture modal
/// with one of 3 results + measurement + reason (REQUIRED on FAIL) +
/// comment, then submits.
/// </summary>
public sealed record QcCaptureCreateRequest
{
    public long CriterionId { get; init; }

    /// <summary>One of <c>Pass</c> / <c>Fail</c> / <c>Na</c>
    /// (case-sensitive, matches <c>QcCaptureResult</c>).</summary>
    public string Result { get; init; } = "";

    public string? Measurement { get; init; }

    /// <summary>Required when <see cref="Result"/> = Fail. Must reference
    /// an active <c>ReasonCode</c> of kind <c>Scrap</c>.</summary>
    public string? NgReasonCode { get; init; }

    public string? Comment { get; init; }
}

/// <summary>
/// P10.5f — QC mutation error envelope. Wire-shape mirrors the
/// drawing + spec mutation patterns so the existing client error
/// pipeline (banners + Thử lại + SpecMutationErrorMapper) keeps
/// working uniformly.
/// </summary>
public sealed record QcMutationError
{
    public string Code { get; init; } = "";
    public string Error { get; init; } = "";
}
