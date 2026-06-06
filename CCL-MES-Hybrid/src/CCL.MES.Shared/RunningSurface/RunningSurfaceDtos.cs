namespace CCL.MES.Shared.RunningSurface;

/// <summary>
/// P10.7c-3 — read view returned by GET /work-orders/{id}/running-surface.
/// Surfaces every field the SETTING + RUNNING + PAUSED dashboards need in
/// a single round-trip so the operator UI never has to fan out across
/// multiple GETs to render the next state. <see cref="ETag"/> matches the
/// WO's current RowVersion (caller uses it as <c>If-Match</c> on
/// subsequent POST writes).
///
/// Phase fan-out:
///   SETTING        — <see cref="SettingStartAt"/> populated; client renders
///                    live timer + 6-item checklist + setting/done button.
///   RUNNING        — <see cref="ActiveSessionStartAt"/> populated;
///                    <see cref="QtyDoneCached"/>/<see cref="QtyNgCached"/>
///                    drive the big-counter UI; <see cref="RecentEntries"/>
///                    seeds the correction picker.
///   PAUSED         — <see cref="ActivePauseStartAt"/> + reason populated;
///                    client renders pause banner + resume button +
///                    correction (still allowed) + finish.
///   IPQC_WAIT      — read-only "đang chờ IPQC" banner; client returns
///                    operator to Work Orders.
///   IPQC_APPROVED  — run/start button enabled.
///   other          — invalid_phase guard.
/// </summary>
public sealed record RunningSurfaceView
{
    public long WoId { get; init; }
    public string WoNo { get; init; } = "";
    public string MesPhase { get; init; } = "";
    public string ETag { get; init; } = "";

    public int TargetQty { get; init; }
    public int QtyDoneCached { get; init; }
    public int QtyNgCached { get; init; }

    public DateTime? SettingStartAt { get; init; }
    public DateTime? SettingEndAt { get; init; }
    public int? SettingDurationSec { get; init; }

    public long? ActiveSessionId { get; init; }
    public DateTime? ActiveSessionStartAt { get; init; }

    public long? ActivePauseId { get; init; }
    public DateTime? ActivePauseStartAt { get; init; }
    public string? ActivePauseReasonCode { get; init; }
    public string? ActivePauseNote { get; init; }

    /// <summary>Newest-first list of qty entries on this WO. Drives the
    /// correction picker. Server clamps to most-recent N (default 20) so
    /// long-running WOs don't bloat the payload.</summary>
    public IReadOnlyList<RunningQtyEntryRow> RecentEntries { get; init; } = Array.Empty<RunningQtyEntryRow>();
}

/// <summary>
/// P10.7c-3 — qty-entry row for <see cref="RunningSurfaceView.RecentEntries"/>.
/// Linked corrections carry <see cref="LinkedEntryId"/> + <see cref="CorrectionReason"/>;
/// raw adds leave both null. Sign matches storage (corrections can be
/// negative on either delta).
/// </summary>
public sealed record RunningQtyEntryRow
{
    public long EntryId { get; init; }
    public DateTime CreatedAt { get; init; }
    public int QtyDoneDelta { get; init; }
    public int QtyNgDelta { get; init; }
    public string? NgReasonCode { get; init; }
    public string? NgNote { get; init; }
    public string? EnteredBy { get; init; }
    public long? LinkedEntryId { get; init; }
    public string? CorrectionReason { get; init; }
}

/// <summary>
/// P10.7c-3 — request body for POST <c>/work-orders/{id}/setting/enter</c>.
/// Idempotent stamp of <see cref="WorkOrder.SettingStartAt"/>. Required
/// because advance into SETTING happens via the existing /advance
/// orchestrator (legacy path: PrePressCheck → OpSetting) which doesn't
/// stamp the start timer. The dashboard fires this once on first load
/// when MesPhase = SETTING and SettingStartAt is null.
/// </summary>
public sealed record SettingEnterRequest;

/// <summary>
/// P10.7c-2 — request body for POST <c>/work-orders/{id}/setting/done</c>.
/// No body fields needed; server reads <see cref="WorkOrder.SettingStartAt"/>
/// + stamps EndAt = now + computes DurationSec. Empty body accepted.
/// </summary>
public sealed record SettingDoneRequest;

/// <summary>
/// P10.7c-2 — request body for POST <c>/work-orders/{id}/run/start</c>.
/// </summary>
public sealed record RunStartRequest;

/// <summary>
/// P10.7c-2 — request body for POST <c>/work-orders/{id}/run/qty</c>
/// (Q2 per-tap qty add). Server validates:
///   - <see cref="QtyDoneDelta"/> &gt;= 0 (corrections use the /correct endpoint)
///   - <see cref="QtyNgDelta"/> &gt;= 0
///   - If <see cref="QtyNgDelta"/> &gt; 0 then both
///     <see cref="NgReasonCode"/> (∈ ReasonCodes Kind=Scrap) and
///     <see cref="NgNote"/> (1-500 chars) are required.
/// </summary>
public sealed record RunQtyAddRequest
{
    public int QtyDoneDelta { get; init; }
    public int QtyNgDelta { get; init; }
    public string? NgReasonCode { get; init; }
    public string? NgNote { get; init; }
}

/// <summary>
/// P10.7c-2 — request body for POST <c>/work-orders/{id}/run/qty/correct</c>
/// (Q5 append-only negative-delta correction). Server validates:
///   - <see cref="LinkedEntryId"/> references a prior WoQtyEntry on same WO
///   - <see cref="CorrectionReason"/> is required (1-500 chars).
/// Deltas can be negative.
/// </summary>
public sealed record RunQtyCorrectRequest
{
    public long LinkedEntryId { get; init; }
    public int QtyDoneDelta { get; init; }
    public int QtyNgDelta { get; init; }
    public string CorrectionReason { get; init; } = "";
}

/// <summary>
/// P10.7c-2 — request body for POST <c>/work-orders/{id}/run/pause</c>
/// (Q4 catalog-only). Server validates <see cref="ReasonCode"/> against
/// ReasonCodes(Kind=Pause). <see cref="Note"/> optional 1-500 chars.
/// </summary>
public sealed record RunPauseRequest
{
    public string ReasonCode { get; init; } = "";
    public string? Note { get; init; }
}

/// <summary>
/// P10.7c-2 — request body for POST <c>/work-orders/{id}/run/resume</c>.
/// </summary>
public sealed record RunResumeRequest;

/// <summary>
/// P10.7c-2 — request body for POST <c>/work-orders/{id}/run/finish</c>.
/// Q6: server accepts from RUNNING OR PAUSED phases (controller stamps
/// active pause's EndedAt = now BEFORE transition so OEE math stays
/// consistent — no orphan open pause).
/// </summary>
public sealed record RunFinishRequest;

/// <summary>
/// P10.7c-2 — common reply shape for all 7 endpoints. Carries the
/// post-write state so the caller can stage the next mutation without
/// a second GET. On 409 <see cref="ErrorCode"/> = "wo.state_conflict"
/// and <see cref="ETag"/> = the server's current value.
/// </summary>
public sealed record RunningSurfaceSetResponse
{
    public bool Ok { get; init; }
    public string? ErrorCode { get; init; }
    public string ETag { get; init; } = "";

    /// <summary>Post-write MesPhase so the client can update the
    /// dashboard without a fresh GET. e.g. "RUNNING", "PAUSED",
    /// "IPQC_WAIT", "FQC_PENDING".</summary>
    public string MesPhase { get; init; } = "";

    /// <summary>Denormalised cache after the write. Useful for the
    /// big-counter UI to render without a fresh GET. Null on 409 /
    /// non-qty endpoints where the value didn't change.</summary>
    public int? QtyDoneCached { get; init; }
    public int? QtyNgCached { get; init; }
}
