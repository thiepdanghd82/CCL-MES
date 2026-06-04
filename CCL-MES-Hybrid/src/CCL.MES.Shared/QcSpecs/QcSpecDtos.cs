namespace CCL.MES.Shared.QcSpecs;

/// <summary>
/// P10.5b — QC Plans + Captures read mirror.
///
/// Window endpoint returns a Dictionary keyed by <see cref="QcStage"/>
/// (IpqcPrint / IpqcCut / Fqc / Oqc). The MAUI client receives a flat
/// list and groups by Stage on the page side — keeps the wire shape
/// simple and JSON-deserializable as a regular array. The server can
/// keep its Dictionary internal; the controller layer projects.
///
/// Captures endpoint returns the full list; the MAUI page filters
/// per Window on demand.
/// </summary>
public sealed record QcWindowItem
{
    public long Id { get; init; }
    public long ProductRevisionId { get; init; }
    public QcStage Stage { get; init; }
    public string? ProcessCode { get; init; }
    public string Title { get; init; } = "";
    public string? Description { get; init; }
    public string? SamplePlan { get; init; }
    public string? Frequency { get; init; }
    public QcRejectAction RejectAction { get; init; }
    public QcWindowStatus Status { get; init; }
    public string? ApprovedBy { get; init; }
    public DateTime? ApprovedAt { get; init; }
    public List<QcCriterionItem> Criteria { get; init; } = new();
}

public sealed record QcCriterionItem
{
    public long Id { get; init; }
    public long SpecQcWindowId { get; init; }
    public short Seq { get; init; }
    public string Name { get; init; } = "";
    public QcCriterionType CriterionType { get; init; }
    public string? MeasureMethod { get; init; }
    public double? TargetValue { get; init; }
    public double? ToleranceMin { get; init; }
    public double? ToleranceMax { get; init; }
    public string? Unit { get; init; }
    public string? PassCriteria { get; init; }
    public string? ReferenceImageKey { get; init; }
    public bool Required { get; init; }
    /// <summary>Per-row method override (Phase 8 PR-D-3 CMES parity).</summary>
    public string? MethodOverride { get; init; }
    /// <summary>Per-row frequency override.</summary>
    public string? FrequencyOverride { get; init; }
}

public sealed record QcCaptureItem
{
    public long Id { get; init; }
    public long SpecQcWindowId { get; init; }
    public long QcCriterionId { get; init; }
    public QcCaptureResult Result { get; init; }
    public string? Measurement { get; init; }
    public string? NgReasonCode { get; init; }
    public string? Comment { get; init; }
    public string CapturedBy { get; init; } = "";
    public DateTime CapturedAt { get; init; }
}

public sealed record QcReasonCode
{
    public long Id { get; init; }
    public string Code { get; init; } = "";
    public string LabelEn { get; init; } = "";
    public string LabelVi { get; init; } = "";

    /// <summary>P10.5f — exposed so the QC capture FAIL filter can
    /// keep only <c>"Scrap"</c>-kind codes (matches the server-side
    /// <c>SpecQcCaptureService</c> guard which only allows scrap
    /// reasons on a FAIL capture).</summary>
    public string Kind { get; init; } = "";

    /// <summary>Display ordering — matches the legacy
    /// <c>ReasonCode.Sort</c>.</summary>
    public int Sort { get; init; }
}

public enum QcStage
{
    IpqcPrint,
    IpqcCut,
    Fqc,
    Oqc,
}

public enum QcWindowStatus
{
    Draft,
    Approved,
    Superseded,
}

public enum QcRejectAction
{
    Rework,
    Scrap,
    Escalate,
    RecordOnly,
}

public enum QcCriterionType
{
    Visual,
    Dimensional,
    Colorimetric,
    Functional,
    Count,
}

public enum QcCaptureResult
{
    Pass,
    Fail,
    Na,
}
