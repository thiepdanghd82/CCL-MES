namespace CCL.MES.Shared.IpqcReview;

/// <summary>
/// IPQC first-article — read view for GET
/// <c>/work-orders/{id}/ipqc/material-system</c>. One round-trip carrying the
/// MATERIAL (SYSTEM) reconciliation grid: each BOM line + its resolved IQC lot
/// + the operator's confirm + the Engineer waiver state. ETag mirrors the WO
/// RowVersion so the caller can stage the next mutation without a second GET.
/// </summary>
public sealed record IpqcMaterialSystemView
{
    public long WoId { get; init; }
    public string WoNo { get; init; } = "";
    public string MesPhase { get; init; } = "";
    public string ETag { get; init; } = "";

    /// <summary>Every material is OK-and-matched or Engineer-waived — the GoRun
    /// material gate is satisfied.</summary>
    public bool AllResolved { get; init; }
    /// <summary>At least one divergent material awaits an Engineer decision.</summary>
    public bool AnyPendingWaiver { get; init; }
    /// <summary>At least one divergence was rejected by the Engineer.</summary>
    public bool AnyRejected { get; init; }

    public IReadOnlyList<IpqcMaterialRow> Rows { get; init; } = Array.Empty<IpqcMaterialRow>();
}

/// <summary>IPQC first-article — one MATERIAL (SYSTEM) row in the view.</summary>
public sealed record IpqcMaterialRow
{
    public int BomLineIdx { get; init; }
    public string MaterialCode { get; init; } = "";
    public string? MaterialDescription { get; init; }

    /// <summary>IQC receipt (ReceiptNo) the scanned lot resolved to — the
    /// "SOURCE IQC LOT" column. Null when unresolved.</summary>
    public string? SourceIqcReceiptNo { get; init; }
    /// <summary>What the operator scanned/entered at the machine in PREPRESS —
    /// the "ACTUAL AT MACHINE" column.</summary>
    public string? ActualAtMachine { get; init; }
    public string? ExpectedPartNo { get; init; }
    public string? MaterialLotStatus { get; init; }
    public string? IqcResult { get; init; }

    public string DivergenceKind { get; init; } = "None";
    public int DivergenceFlags { get; init; }
    public bool IsDivergent { get; init; }

    /// <summary>Operator confirm: "Pending" / "Ok" / "Ng".</summary>
    public string Status { get; init; } = "Pending";
    public string? NgReasonCode { get; init; }
    public string? NgNote { get; init; }

    /// <summary>Engineer waiver: "NotRequired" / "PendingEngineer" /
    /// "Approved" / "Rejected".</summary>
    public string DivergenceApprovalStatus { get; init; } = "NotRequired";
    public string? ApprovedBy { get; init; }
    public DateTime? ApprovedAt { get; init; }
    public string? ApprovalReason { get; init; }
}

/// <summary>IPQC first-article — request body for PUT
/// <c>/work-orders/{id}/ipqc/material-system/{bomLineIdx}</c> (confirm OK/NG).
/// On Ng, NgReasonCode (ReasonCodeKind.Scrap) + NgNote (1-500) required.</summary>
public sealed record SetIpqcMaterialRequest
{
    public string? Status { get; init; }
    public string? NgReasonCode { get; init; }
    public string? NgNote { get; init; }
}

/// <summary>IPQC first-article — request body for POST
/// <c>/work-orders/{id}/ipqc/material-system/{bomLineIdx}/approve-divergence</c>.
/// Engineer waiver decision. Outcome ∈ {Approve, Reject}; Reason required (1-500).
/// Q1 dual-sig: approver ≠ ConfirmedBy when the flag is ON (default).</summary>
public sealed record ApproveDivergenceRequest
{
    public string? Outcome { get; init; }
    public string? Reason { get; init; }
}

/// <summary>IPQC first-article — common reply for the material-system mutations.
/// Mirrors <see cref="IpqcSetResponse"/>; carries the post-write material rollup
/// so the client stages the next action without a fresh GET.</summary>
public sealed record IpqcMaterialSetResponse
{
    public bool Ok { get; init; }
    public string? ErrorCode { get; init; }
    public string ETag { get; init; } = "";
    public string MesPhase { get; init; } = "";

    public bool? AllResolved { get; init; }
    public bool? AnyPendingWaiver { get; init; }
    public bool? AnyRejected { get; init; }
    /// <summary>Post-write divergence approval state of the row just mutated.</summary>
    public string? RowApprovalStatus { get; init; }
}
