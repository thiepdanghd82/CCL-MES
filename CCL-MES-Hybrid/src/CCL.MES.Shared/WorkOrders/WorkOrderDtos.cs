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
    public string? MachineCode { get; init; }
    public string? MachineName { get; init; }
    public int TargetQty { get; init; }
    public int ProducedQty { get; init; }
    public string Uom { get; init; } = "pcs";
    public DateTimeOffset? PlannedStart { get; init; }
    public DateTimeOffset? PlannedEnd { get; init; }

    /// <summary>Current step in the 8-step process flow (PrePressCheck → Closed).</summary>
    public string CurrentStep { get; init; } = "";

    /// <summary>Status badge i18n key e.g. "wo.status.in_progress".</summary>
    public string BadgeLabelKey { get; init; } = "";

    /// <summary>Pre-rendered tone class e.g. "wo-status-running" so the UI can
    /// stay copy-paste with the legacy color scheme.</summary>
    public string BadgeCssClass { get; init; } = "";
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
}
