namespace CCL.MES.Shared.WorkOrders;

/// <summary>
/// P10.3 W4 — lightweight WO summary returned by the scan-lookup endpoint.
/// Mirrors the subset of <c>WorkOrderDrawerView</c> the operator card UI
/// needs without dragging the full materials+QC history payload across
/// the wire for a quick "did we find it?" confirmation.
///
/// Wire shape stays DTO-only (no EF entity reference) so the MAUI client
/// never has to pull <c>CCL.MES.Domain</c> in. Server controller maps
/// from the Domain entity / drawer-view to this record.
/// </summary>
public sealed record WorkOrderSummary
{
    public long Id { get; init; }
    public string WoNo { get; init; } = "";
    public string? CustomerName { get; init; }
    public string? ProductCode { get; init; }
    public string? ProductName { get; init; }
    /// <summary>P10.10 — Part Description synced from the IFS BOM (Structure):
    /// <c>ManufacturingStructure.ParentDescription</c> resolved by the WO's
    /// product code. Falls back to null when the product has no Structure.</summary>
    public string? PartDescription { get; init; }
    public string? MachineCode { get; init; }
    public string? MachineName { get; init; }
    public int TargetQty { get; init; }
    public int ProducedQty { get; init; }
    public string Uom { get; init; } = "pcs";
    public DateTimeOffset? PlannedStart { get; init; }
    public DateTimeOffset? PlannedEnd { get; init; }

    /// <summary>Current step in the 8-step process flow (PrePressCheck → Closed).</summary>
    public string CurrentStep { get; init; } = "";

    /// <summary>P10.7c-3 — canonical MesPhase ("NEW" / "PREPRESS" / "SETTING" /
    /// "IPQC_WAIT" / "IPQC_APPROVED" / "RUNNING" / "PAUSED" / "FQC_PENDING" /
    /// "DONE" / "CANCELLED" etc.). MesPhase is the AUTHORITATIVE phase for
    /// dispatching 7b/7c dashboards; <see cref="CurrentStep"/> is a legacy
    /// projection that does NOT change in lock-step with MesPhase (e.g. after
    /// /setting/done the WO is in MesPhase=IPQC_WAIT but CurrentStep stays at
    /// "OpSetting"). Always dispatch routing decisions on MesPhase.</summary>
    public string MesPhase { get; init; } = "";

    /// <summary>Status badge i18n key e.g. "wo.status.in_progress".</summary>
    public string BadgeLabelKey { get; init; } = "";

    /// <summary>Pre-rendered tone class e.g. "wo-status-running" so the UI can
    /// stay copy-paste with the legacy color scheme.</summary>
    public string BadgeCssClass { get; init; } = "";

    /// <summary>P10.7a-1.3 — Base64-encoded current RowVersion. The client
    /// captures this from the GET response (also surfaced as HTTP
    /// <c>ETag</c> header) and sends it back as <c>If-Match</c> on the
    /// next mutation. Mismatch → 409 + <c>WO_STATE_CONFLICT</c> audit.</summary>
    public string ETag { get; init; } = "";
}

/// <summary>
/// Reply for POST work-orders/{id}/advance — mirrors
/// <c>AdvanceResult</c> from the legacy Application layer but stringified
/// for wire safety. Client surfaces <see cref="ErrorCode"/> verbatim when
/// <see cref="Ok"/> is false; the UI maps the well-known codes to
/// Vietnamese strings (auth.invalid_credentials pattern from P10.2).
/// </summary>
public sealed record AdvanceWorkOrderResponse
{
    public bool Ok { get; init; }

    /// <summary>Step the WO landed on AFTER attempted advance (unchanged on failure).</summary>
    public string CurrentStep { get; init; } = "";

    /// <summary>Stable error key e.g. "RequiresSpecAndMaterials" / "IpqcNotPassed" /
    /// "AlreadyAtFinalStep" / "WorkOrderNotFound". Empty when Ok=true.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>P10.7a-1.3 — Base64-encoded NEW RowVersion to be used as
    /// <c>If-Match</c> on the next mutation. On 200 success this is the
    /// post-advance RowVersion (bumped by the SQLite trigger). On 409
    /// conflict this is the CURRENT server RowVersion (so the client can
    /// reload + retry without re-fetching the summary). Empty for 404 /
    /// 428 / 400 responses where there's no valid version to surface.
    /// Also emitted as the HTTP <c>ETag</c> response header.</summary>
    public string ETag { get; init; } = "";
}

/// <summary>
/// P10.7a-2.2 — request body for <c>POST /admin/work-orders/{id}/force-phase</c>.
/// Per contract §8.1 + Henry-confirmed Q2 (structured): the caller MUST
/// supply <see cref="TargetStep"/> + a Recovery-kind
/// <see cref="ReasonCode"/> from the seeded vocabulary +
/// <see cref="ReasonNote"/> (1-500 chars) so SYS_RECOVERY audit rows
/// carry both a codified motive + free-text context. Allowed
/// (FROM, TO) edges are governed by
/// <c>WorkOrderStateMachine.IsForceablePhase</c> (11 of 144 cells —
/// see §3.1 "recovery-only" classification). Anything else returns
/// 409 force.unforceable_transition.
/// </summary>
public sealed record ForcePhaseRequest
{
    /// <summary>Legacy <c>ProcessStepCode</c> name (PrePressCheck /
    /// OpSetting / … / Closed). The server projects it to canonical
    /// <c>MesPhase</c> before consulting <c>IsForceablePhase</c>.</summary>
    public string? TargetStep { get; init; }

    /// <summary>One of the 6 Recovery-kind ReasonCodes seeded by
    /// 7a-2.1 (<c>REC-OP-WEDGE</c>, <c>REC-HW-FAULT</c>, …).</summary>
    public string? ReasonCode { get; init; }

    /// <summary>Free-text justification, 1-500 chars. Stamped into the
    /// SYS_RECOVERY audit row's <c>detail.reason.note</c> field for
    /// forensic replay.</summary>
    public string? ReasonNote { get; init; }
}

/// <summary>
/// P10.7a-2.2 — reply for <c>POST /admin/work-orders/{id}/force-phase</c>.
/// On success carries the new <see cref="CurrentStep"/> (post-force) +
/// bumped <see cref="ETag"/>. On guard failure (409 / 422) the
/// <see cref="ErrorCode"/> + the CURRENT server <see cref="ETag"/> let
/// the admin reload + decide without a second round-trip.
/// </summary>
public sealed record ForcePhaseResponse
{
    public bool Ok { get; init; }
    public string CurrentStep { get; init; } = "";
    public string? ErrorCode { get; init; }
    public string ETag { get; init; } = "";
}
