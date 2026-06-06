namespace CCL.MES.Shared.Prepress;

/// <summary>
/// P10.7b-2 — read view returned by GET /work-orders/{id}/prepress.
/// Surfaces the three child-table sets the operator works through during
/// PREPRESS + the current rollup flag so the Hybrid UI can render a single
/// dashboard without a second round-trip. <see cref="ETag"/> matches the
/// WO's current RowVersion (caller uses it as <c>If-Match</c> on subsequent
/// PUT writes).
/// </summary>
public sealed record PrepressView
{
    public long WoId { get; init; }
    public string WoNo { get; init; } = "";
    public string MesPhase { get; init; } = "";
    public bool MaterialsReady { get; init; }
    public string ETag { get; init; } = "";

    public IReadOnlyList<PrepressMaterialRow> Materials { get; init; } = Array.Empty<PrepressMaterialRow>();
    public PrepressPlateRow? PlateCheck { get; init; }
    public PrepressCutterRow? CutterCheck { get; init; }
}

public sealed record PrepressMaterialRow
{
    public long Id { get; init; }
    public int BomLineIdx { get; init; }
    public string MaterialCode { get; init; } = "";
    public string? MaterialDescription { get; init; }
    public double QtyRequired { get; init; }
    public string? Uom { get; init; }
    public double? QtyLoaded { get; init; }
    public string? LotNo { get; init; }
    public string Status { get; init; } = "Pending";
    public string? NgReasonCode { get; init; }
    public string? NgNote { get; init; }
    public string? CheckedBy { get; init; }
    public DateTimeOffset? CheckedAt { get; init; }
}

public sealed record PrepressPlateRow
{
    public long Id { get; init; }
    public string Status { get; init; } = "Pending";
    public string? PlateNo { get; init; }
    public string? NgReasonCode { get; init; }
    public string? NgNote { get; init; }
    public string? CheckedBy { get; init; }
    public DateTimeOffset? CheckedAt { get; init; }
}

public sealed record PrepressCutterRow
{
    public long Id { get; init; }
    public string Status { get; init; } = "Pending";
    public string? CutterNo { get; init; }
    public string? NgReasonCode { get; init; }
    public string? NgNote { get; init; }
    public string? CheckedBy { get; init; }
    public DateTimeOffset? CheckedAt { get; init; }
}

/// <summary>P10.7b-2 — request body for PUT
/// <c>/work-orders/{id}/materials/{bom_line_idx}</c>. Status must be "OK" or
/// "NG"; on NG, <see cref="NgReasonCode"/> (one of <see cref="CCL.MES.Domain.ReasonCodeKind.Scrap"/>
/// codes) and <see cref="NgNote"/> (1-500 chars) are required.</summary>
public sealed record SetPrepressMaterialRequest
{
    public string? Status { get; init; }
    public double? QtyLoaded { get; init; }
    public string? LotNo { get; init; }
    public string? NgReasonCode { get; init; }
    public string? NgNote { get; init; }
}

/// <summary>P10.7b-2 — request body for PUT
/// <c>/work-orders/{id}/plate-check</c>. Same NG rules.</summary>
public sealed record SetPrepressPlateRequest
{
    public string? Status { get; init; }
    public string? PlateNo { get; init; }
    public string? NgReasonCode { get; init; }
    public string? NgNote { get; init; }
}

/// <summary>P10.7b-2 — request body for PUT
/// <c>/work-orders/{id}/cutter-check</c>. Same NG rules.</summary>
public sealed record SetPrepressCutterRequest
{
    public string? Status { get; init; }
    public string? CutterNo { get; init; }
    public string? NgReasonCode { get; init; }
    public string? NgNote { get; init; }
}

/// <summary>P10.7b-2 — reply for the 3 PUT endpoints. Carries the updated
/// row + the (post-write) <see cref="MaterialsReady"/> rollup + the bumped
/// <see cref="ETag"/> so the caller can stage the next mutation without
/// a second GET.</summary>
public sealed record PrepressSetResponse
{
    public bool Ok { get; init; }
    public bool MaterialsReady { get; init; }
    public string? ErrorCode { get; init; }
    public string ETag { get; init; } = "";
}
