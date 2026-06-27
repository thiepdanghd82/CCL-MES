namespace CCL.MES.Shared.IpqcReview;

/// <summary>
/// P10.7d-2 — read view returned by GET /work-orders/{id}/ipqc.
/// Single round-trip carries every field the IPQC dashboard needs to
/// render the 4-check + 3-button flow without a second hop.
/// ETag mirrors the WO's current RowVersion so the caller can stage
/// the next mutation without a second GET.
///
/// Per Q1 (Henry-confirmed), the 3 print check slots are hardcoded
/// (Color / Registration / Content). Per-product threshold override
/// (e.g. ΔE ≤ 2 vs ≤ 3) defers to 7e.
/// </summary>
public sealed record IpqcView
{
    public long WoId { get; init; }
    public string WoNo { get; init; } = "";
    public string MesPhase { get; init; } = "";
    public string ETag { get; init; } = "";

    // ── ① Material recheck ──────────────────────────────────────────
    public string MaterialStatus { get; init; } = "Pending";
    public string? MaterialNgReasonCode { get; init; }
    public string? MaterialNgNote { get; init; }

    // ── ② Print check A — Color (default ΔE ≤ 2) ─────────────────
    public string PrintAStatus { get; init; } = "Pending";
    public string? PrintANgReasonCode { get; init; }
    public string? PrintANgNote { get; init; }

    // ── ② Print check B — Registration (chồng màu) ───────────────
    public string PrintBStatus { get; init; } = "Pending";
    public string? PrintBNgReasonCode { get; init; }
    public string? PrintBNgNote { get; init; }

    // ── ② Print check C — Content (text + barcode) ───────────────
    public string PrintCStatus { get; init; } = "Pending";
    public string? PrintCNgReasonCode { get; init; }
    public string? PrintCNgNote { get; init; }

    // ── ③ Judgment ─────────────────────────────────────────────────
    public string Judgment { get; init; } = "Pending";
    public string? SpecialAcceptReason { get; init; }
    public string? IpqcSubmittedBy { get; init; }
    public DateTime? IpqcSubmittedAt { get; init; }

    // ── QA approval (only when Judgment = SpecialAccept) ──────────
    public string QaOutcome { get; init; } = "Pending";
    public string? QaReason { get; init; }
    public string? QaApprovedBy { get; init; }
    public DateTime? QaApprovedAt { get; init; }

    // ── Rollup (server-computed) ───────────────────────────────────

    /// <summary>True iff all 4 slots are non-Pending (operator has
    /// resolved each). Drives the judgment button on the dashboard.</summary>
    public bool IsReadyForJudgment { get; init; }

    /// <summary>True iff all 4 slots = Ok. When true, judgment
    /// defaults to GoRun on the dashboard.</summary>
    public bool AllOk { get; init; }

    /// <summary>True iff at least one slot = Ng. When true, judgment
    /// must be StopLine or SpecialAccept; GoRun is server-rejected.</summary>
    public bool AnyNg { get; init; }

    // ── Phương án C — Bước 2/4: data-driven items (auto-sync) ────────

    /// <summary>QC line đã resolve từ routing (vd "LABEL,PRESS_CNC"). Rỗng
    /// = WO chưa/không auto-sync được → dùng 4 slot legacy ở trên.</summary>
    public string? ResolvedLines { get; init; }

    /// <summary>Phương án C — F2 (finding #2): trạng thái auto-sync để UI cảnh báo,
    /// KHÔNG im lặng. DERIVE (không cột DB). "Materialized" · "SkippedUnmapped"
    /// (routing có op không map được) · "SkippedNoLibrary" (ra line nhưng thư viện trống)
    /// · "LegacyManual" (không routing / đã nhập tay 4-slot → giữ thủ công).</summary>
    public string AutoSyncStatus { get; init; } = "LegacyManual";

    /// <summary>Bộ hạng mục IPQC materialize từ thư viện theo process line.
    /// Rỗng = mode legacy 4 slot (UI render 4 slot cũ). Có phần tử = mode
    /// data-driven (UI render danh sách này; rollup tính trên đây).</summary>
    public IReadOnlyList<IpqcViewItem> Items { get; init; } = Array.Empty<IpqcViewItem>();
}

/// <summary>Phương án C — 1 hạng mục IPQC data-driven trong view.</summary>
public sealed record IpqcViewItem
{
    public string ItemKey { get; init; } = "";
    public string? ProcessLine { get; init; }
    public string? GroupLabel { get; init; }
    public string? Label { get; init; }
    public string? AcceptanceCriteria { get; init; }
    public string? Method { get; init; }
    public string? Severity { get; init; }
    public string? DefectCode { get; init; }
    public string Status { get; init; } = "Pending";
    public string? NgReasonCode { get; init; }
    public string? NgNote { get; init; }
}

/// <summary>Phương án C — request body cho PUT
/// <c>/work-orders/{id}/ipqc/item/{itemKey}</c> (đánh OK/NG 1 hạng mục
/// data-driven). Cùng luật NG như slot legacy.</summary>
public sealed record SetIpqcItemRequest
{
    public string? Status { get; init; }
    public string? NgReasonCode { get; init; }
    public string? NgNote { get; init; }
}

/// <summary>
/// P10.7d-2 — request body for PUT <c>/work-orders/{id}/ipqc/{slot}</c>.
/// Status must be "Ok" or "Ng". On Ng, both <see cref="NgReasonCode"/>
/// (validates against ReasonCodeKind.Scrap) and <see cref="NgNote"/>
/// (1-500 chars) are required.
/// </summary>
public sealed record SetIpqcSlotRequest
{
    public string? Status { get; init; }
    public string? NgReasonCode { get; init; }
    public string? NgNote { get; init; }
}

/// <summary>
/// P10.7d-2 — request body for POST <c>/work-orders/{id}/ipqc/judgment</c>.
/// Server validates:
///   - All 4 slots non-Pending (IsReadyForJudgment).
///   - Judgment ∈ {GoRun, StopLine, SpecialAccept}.
///   - GoRun rejected when any slot = Ng (IsJudgmentConsistent).
///   - SpecialAccept requires <see cref="SpecialAcceptReason"/> 1-500 chars.
/// Transition map:
///   GoRun         → MesPhase IPQC_APPROVED
///   StopLine      → MesPhase PREPRESS (reset 7b checklists)
///   SpecialAccept → MesPhase QA_PENDING
/// </summary>
public sealed record SubmitIpqcJudgmentRequest
{
    public string? Judgment { get; init; }
    public string? SpecialAcceptReason { get; init; }
}

/// <summary>
/// P10.7d-2 — request body for POST <c>/work-orders/{id}/qa/approve</c>.
/// QA approver acts on a SPECIAL_ACCEPT escalation. Server validates:
///   - Outcome ∈ {Approve, Reject}.
///   - Reject requires <see cref="QaReason"/> 1-500 chars; Approve
///     accepts optional reason.
///   - Q3 dual-sig: when OPS_IPQC_REQUIRE_DISTINCT_QA_APPROVER=on
///     (default), QA approver username MUST NOT equal IpqcSubmittedBy.
///     Server emits 422 qa.same_user_as_ipqc_submitter +
///     WO_QA_APPROVE_DENIED audit on violation.
/// Transition map:
///   Approve → MesPhase IPQC_APPROVED
///   Reject  → MesPhase PREPRESS (mirrors IPQC StopLine)
/// </summary>
public sealed record QaApproveRequest
{
    public string? Outcome { get; init; }
    public string? QaReason { get; init; }
}

/// <summary>
/// P10.7d-2 — common reply for all 6 IPQC + QA endpoints. Carries the
/// post-write state so caller stages the next mutation without a
/// second GET. On 409 <see cref="ErrorCode"/> = "wo.state_conflict"
/// and <see cref="ETag"/> = the server's current value.
/// </summary>
public sealed record IpqcSetResponse
{
    public bool Ok { get; init; }
    public string? ErrorCode { get; init; }
    public string ETag { get; init; } = "";

    /// <summary>Post-write MesPhase so client can update the dashboard
    /// without a fresh GET. e.g. "IPQC_WAIT" (after slot write), "IPQC_APPROVED"
    /// (after GoRun), "QA_PENDING" (after SpecialAccept), "PREPRESS"
    /// (after StopLine / QA Reject).</summary>
    public string MesPhase { get; init; } = "";

    /// <summary>Post-write rollup flags. Null on non-slot endpoints.</summary>
    public bool? IsReadyForJudgment { get; init; }
    public bool? AllOk { get; init; }
    public bool? AnyNg { get; init; }
}
