namespace CCL.MES.Shared.WoQcReview;

/// <summary>
/// P10.7e-2 — read view returned by GET /work-orders/{id}/qc/{kind}
/// where {kind} ∈ "fqc" | "oqc". Single round-trip carries every
/// field the FqcDashboard / OqcDashboard needs to render the
/// data-driven check items + judgment + (OQC only) 3-signature
/// state.
///
/// Per Q3 schema is data-driven — items come from the frozen
/// ProfileSnapshotJson rather than 4 hardcoded slots like 7d's
/// IPQC. Items returned in profile-declared order (operators see
/// the same sequence the inspector walks the form).
///
/// MesPhase field projected per L19 amendment (every WO-returning
/// DTO MUST surface canonical phase).
/// </summary>
public sealed record WoQcView
{
    public long WoId { get; init; }
    public string WoNo { get; init; } = "";
    public string MesPhase { get; init; } = "";
    public string ETag { get; init; } = "";

    /// <summary>"FQC" or "OQC" — discriminator the dashboard renders.</summary>
    public string QcKind { get; init; } = "";

    /// <summary>Frozen profile snapshot (Q3 freeze-at-creation).
    /// Shape: { "sections": [...], "items": [...] } — opaque to the
    /// client which iterates Items[] keyed by ItemKey.</summary>
    public string ProfileSnapshotJson { get; init; } = "{}";

    /// <summary>Per-item state, indexed by item key. Order matches
    /// the profile snapshot's declared sequence.</summary>
    public IReadOnlyList<WoQcViewItem> Items { get; init; } = Array.Empty<WoQcViewItem>();

    // ── Judgment ───────────────────────────────────────────────────
    public string Judgment { get; init; } = "Pending";
    public string? JudgmentReason { get; init; }

    // ── Inspector signature (FQC + OQC) ───────────────────────────
    public string? InspectedBy { get; init; }
    public DateTime? InspectedAt { get; init; }

    // ── OQC-only 3-sig fields (null on FQC rows) ──────────────────
    public string? ReviewedBy { get; init; }
    public DateTime? ReviewedAt { get; init; }
    public string? ApprovedBy { get; init; }
    public DateTime? ApprovedAt { get; init; }

    // ── Server-computed rollup ─────────────────────────────────────
    public bool IsReadyForJudgment { get; init; }
    public bool AllOk { get; init; }
    public bool AnyNg { get; init; }
}

/// <summary>Single item within a <see cref="WoQcView"/>. Status
/// mirrors the 7d 3-state pattern (Pending / Ok / Ng). PhotoIds
/// surfaces the per-item photo blob IDs so the client can render
/// thumbnail strips.</summary>
public sealed record WoQcViewItem
{
    public string ItemKey { get; init; } = "";
    public string Status { get; init; } = "Pending";

    // ── Nhãn hiển thị, LẤY TỪ SNAPSHOT ĐÃ ĐÓNG BĂNG của chính check này.
    //    Trước đây UI render thẳng ItemKey ("color_match", "no_tear") vì DTO
    //    không mang nhãn ra — profile CÓ nhãn, nó chỉ nằm trong
    //    ProfileSnapshotJson mà không ai đọc.
    //    Đừng đọc thẳng: dùng *For(bool) để luôn có đường rơi về VI rồi về key.
    public string? Label { get; init; }
    public string? LabelEn { get; init; }
    public string? Spec { get; init; }
    public string? SpecEn { get; init; }
    public string? Method { get; init; }
    public string? MethodEn { get; init; }

    /// <summary>Nhóm hạng mục (A·Ngoại quan … E·RoHS). Là KHOÁ TAB — giữ chuỗi
    /// VI làm định danh, chỉ nhãn mới dịch, để đổi ngôn ngữ không văng tab.</summary>
    public string? GroupLabel { get; init; }
    public string? GroupLabelEn { get; init; }

    /// <summary>Nhãn theo ngôn ngữ đang bật. Thiếu EN → bản VI; thiếu cả hai →
    /// <see cref="ItemKey"/>, để ô không bao giờ trống.</summary>
    public string LabelFor(bool english) => Pick(LabelEn, Label, english) ?? ItemKey;

    /// <inheritdoc cref="LabelFor"/>
    public string? SpecFor(bool english) => Pick(SpecEn, Spec, english);

    /// <inheritdoc cref="LabelFor"/>
    public string? MethodFor(bool english) => Pick(MethodEn, Method, english);

    /// <inheritdoc cref="LabelFor"/>
    public string? GroupLabelFor(bool english) => Pick(GroupLabelEn, GroupLabel, english);

    private static string? Pick(string? en, string? vi, bool english)
        => english && !string.IsNullOrWhiteSpace(en) ? en : (string.IsNullOrWhiteSpace(vi) ? null : vi);
    public string? NgReasonCode { get; init; }
    public string? NgNote { get; init; }
    public IReadOnlyList<long> PhotoIds { get; init; } = Array.Empty<long>();
}

/// <summary>PUT body for /work-orders/{id}/qc/{kind}/items/{itemKey}
/// — set a single item Status. On NG, both NgReasonCode (validates
/// against ReasonCodeKind.Scrap) and NgNote (1-500 chars) required.</summary>
public sealed record SetWoQcItemRequest
{
    public string? Status { get; init; }
    public string? NgReasonCode { get; init; }
    public string? NgNote { get; init; }
}

/// <summary>POST body for /work-orders/{id}/qc/fqc/judgment.
/// Server validates: rollup ready + judgment ∈ {Pass, Reject} +
/// JudgmentReason 1-500 chars when Reject.
/// Transition: Pass → OQC_PENDING; Reject → PREPRESS (per Q2
/// transient — audit row WO_FQC_REJECT_TO_PREPRESS captures the
/// event).</summary>
public sealed record SubmitFqcJudgmentRequest
{
    public string? Judgment { get; init; }
    public string? JudgmentReason { get; init; }
}

/// <summary>POST body for /work-orders/{id}/qc/oqc/inspect. The
/// Inspector completes the items + commits the rollup. The
/// signature step is implicit — InspectedBy = caller username +
/// InspectedAt = now. No judgment yet; that arrives via Approve
/// after the 3-sig Q5 chain.</summary>
public sealed record OqcInspectRequest
{
    public string? Note { get; init; }
}

/// <summary>POST body for /work-orders/{id}/qc/oqc/review.
/// Reviewer signs after Inspector. Server enforces Q5 invariant
/// Reviewer ≠ Inspector when OqcRequireDistinctReviewer=on
/// (default). On violation: 422 oqc.same_user_as_inspector +
/// WO_OQC_REVIEW_DENIED audit.</summary>
public sealed record OqcReviewRequest
{
    public string? Note { get; init; }
}

/// <summary>POST body for /work-orders/{id}/qc/oqc/approve.
/// Approver signs last + carries the final judgment.
/// Server enforces TWO Q5 invariants:
///   - Approver ≠ Reviewer (when OqcRequireDistinctApprover=on)
///   - Approver ≠ Inspector (when
///     OqcRequireApproverDistinctFromInspector=on)
/// Both default ON. On either violation: 422 +
/// oqc.same_user_as_reviewer OR oqc.same_user_as_inspector +
/// WO_OQC_APPROVE_DENIED audit.
/// On Approve happy path: WO advances OQC_PENDING → SHIPPED +
/// WO_OQC_APPROVE + WO_SHIPPED audits emit in the same SaveChanges.
/// On Reject: WO advances OQC_PENDING → FQC_PENDING +
/// WO_OQC_REJECT_TO_FQC_PENDING audit.</summary>
public sealed record OqcApproveRequest
{
    public string? Outcome { get; init; }
    public string? JudgmentReason { get; init; }
}

/// <summary>Common reply for the QC mutation endpoints. Mirrors the
/// 7d IpqcSetResponse shape so the client UI distinguishes
/// wire-vs-domain failures by ErrorCode rather than HTTP status alone.</summary>
public sealed record WoQcSetResponse
{
    public bool Ok { get; init; }
    public string? ErrorCode { get; init; }
    public string ETag { get; init; } = "";
    public string MesPhase { get; init; } = "";

    public bool? IsReadyForJudgment { get; init; }
    public bool? AllOk { get; init; }
    public bool? AnyNg { get; init; }
}

/// <summary>Photo metadata returned by upload + list endpoints.</summary>
public sealed record WoQcPhotoDto
{
    public long Id { get; init; }
    public long WoQcCheckItemId { get; init; }
    public string Sha256 { get; init; } = "";
    public string MimeType { get; init; } = "";
    public long SizeBytes { get; init; }
    public string? OriginalFileName { get; init; }
    public string? UploadedBy { get; init; }
    public DateTime UploadedAt { get; init; }
}

/// <summary>P10.7e-3 — POST /qc/{kind}/items/{itemKey}/photos response.
/// Carries the trigger-bumped ETag + canonical MesPhase (L19) so the
/// client refreshes the WoQcView without an extra GET.</summary>
public sealed record WoQcPhotoUploadResponse
{
    public bool Ok { get; init; }
    public string? ErrorCode { get; init; }
    public string ETag { get; init; } = "";
    public string MesPhase { get; init; } = "";
    public WoQcPhotoDto? Photo { get; init; }
}

/// <summary>P10.7e-1 Q8 — JSON /summary endpoint. Live-recomputed
/// each GET (NOT frozen at SHIPPED time) so late corrections via
/// /run/qty/correct flow through. Read-only; no mutation surface.
/// MesPhase projected per L19 amendment.</summary>
public sealed record WoSummaryReport
{
    public long WoId { get; init; }
    public string WoNo { get; init; } = "";
    public string MesPhase { get; init; } = "";
    public DateTime? ShippedAt { get; init; }

    public WoSummaryTotals Totals { get; init; } = new();
    public WoSummaryRuntime Runtime { get; init; } = new();
    public WoSummaryOee Oee { get; init; } = new();
    public IReadOnlyList<WoSummaryParetoRow> PausePareto { get; init; } = Array.Empty<WoSummaryParetoRow>();
    public WoSummaryQc QcSummary { get; init; } = new();
}

public sealed record WoSummaryTotals
{
    public int QtyTarget { get; init; }
    public int QtyDone { get; init; }
    public int QtyNg { get; init; }
}

public sealed record WoSummaryRuntime
{
    public long RunSeconds { get; init; }
    public long PauseSeconds { get; init; }
    public int SessionCount { get; init; }
}

public sealed record WoSummaryOee
{
    /// <summary>(run_seconds) / (run_seconds + pause_seconds). Null
    /// when run_seconds == 0 (operator never started).</summary>
    public double? Availability { get; init; }
    /// <summary>(qty_done) / (run_seconds ÷ idealCycleSec), where
    /// idealCycleSec = 3600 / WorkCenter.IdealSpeedPcsH. Null when the
    /// speed is unknown — and when it is null,
    /// <see cref="PerformanceUnavailableReason"/> says why.</summary>
    public double? Performance { get; init; }
    /// <summary>Đợt 1 C3 (additive) — machine-readable reason
    /// <see cref="Performance"/> is null: <c>workcenter_missing</c> ·
    /// <c>workcenter_speed_missing</c> · <c>no_runtime</c>. Null exactly
    /// when Performance is non-null. Only 5 of 43 work centers carry a
    /// speed today, so a silent null here hid the gap on 19 of 27 WOs;
    /// the client renders this code through TranslationCatalog instead of
    /// showing a bare dash.</summary>
    public string? PerformanceUnavailableReason { get; init; }
    /// <summary>(qty_done - qty_ng) / qty_done. Null when qty_done == 0.</summary>
    public double? Quality { get; init; }
    /// <summary>availability × performance × quality. Null when ANY
    /// factor is null.</summary>
    public double? Oee { get; init; }
}

public sealed record WoSummaryParetoRow
{
    public string ReasonCode { get; init; } = "";
    public int Count { get; init; }
    public long TotalSeconds { get; init; }
}

public sealed record WoSummaryQc
{
    public WoSummaryQcLeg Ipqc { get; init; } = new();
    public WoSummaryQcLeg Fqc { get; init; } = new();
    public WoSummaryQcLeg Oqc { get; init; } = new();
}

public sealed record WoSummaryQcLeg
{
    public string Judgment { get; init; } = "Pending";
    public string? SubmittedBy { get; init; }
    public string? Reviewer { get; init; }
    public string? Approver { get; init; }
    public string? Reason { get; init; }
}
