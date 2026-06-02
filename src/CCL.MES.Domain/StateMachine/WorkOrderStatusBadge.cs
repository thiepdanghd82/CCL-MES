using CCL.MES.Domain.Entities;

namespace CCL.MES.Domain.StateMachine;

/// <summary>
/// Phase 8 PR #32a — Shop Order status badge derivation.
///
/// Pure helper that maps the CCL-MES dual-enum (<see cref="WoStatus"/> +
/// <see cref="ProcessStepCode"/>) + the latest IPQC outcome onto the
/// SpecHub Shop Order 9-state badge palette. Runtime derive only — NO
/// new enum, NO new field, NO migration. Stable callable from anywhere
/// in the Application + Web layers; output record is i18n-key + token
/// for the UI to render (CSS class + label key).
///
/// SpecHub badges referenced (read-only port):
///   NEW / PRE-PRESS / SETTING / IPQC-WAIT / IPQC-OK / IPQC-FAIL /
///   READY / RUNNING / PAUSED / QA-PENDING / DONE / CANCELLED
/// </summary>
public static class WorkOrderStatusBadge
{
    public sealed record Badge(string Token, string LabelKey, string CssClass, string Icon);

    public static Badge From(WorkOrder wo)
    {
        // Terminal states first — they override step semantics.
        if (wo.Status == WoStatus.Cancelled)
            return new("cancelled", "shop_order.status.cancelled", "shop-pill-cancelled", "✕");
        if (wo.Status == WoStatus.Closed || wo.Status == WoStatus.Finished)
            return new("done",      "shop_order.status.done",      "shop-pill-done",      "✔");

        // Operator pause (no enum — derive from Status == OnHold).
        if (wo.Status == WoStatus.OnHold)
            return new("paused",    "shop_order.status.paused",    "shop-pill-paused",    "⏸");

        // Draft never picked up by operator — NEW badge.
        if (wo.Status == WoStatus.Draft && wo.CurrentStep == ProcessStepCode.PrePressCheck)
            return new("new",       "shop_order.status.new",       "shop-pill-new",       "○");

        // Step-driven progression. Branches on IPQC outcome where relevant.
        return wo.CurrentStep switch
        {
            ProcessStepCode.PrePressCheck => new("pre_press", "shop_order.status.pre_press", "shop-pill-pre-press", "①"),
            ProcessStepCode.OpSetting     => new("setting",   "shop_order.status.setting",   "shop-pill-setting",   "②"),
            ProcessStepCode.IpqcApproval  => IpqcBadge(wo),
            ProcessStepCode.ReadyToRun    => new("ready",     "shop_order.status.ready",     "shop-pill-ready",     "④"),
            ProcessStepCode.Running       => new("running",   "shop_order.status.running",   "shop-pill-running",   "▶"),
            ProcessStepCode.Fqc           => new("qa_pending","shop_order.status.qa_pending","shop-pill-qa",        "⏳"),
            ProcessStepCode.Oqc           => new("qa_pending","shop_order.status.qa_pending","shop-pill-qa",        "⏳"),
            ProcessStepCode.Closed        => new("done",      "shop_order.status.done",      "shop-pill-done",      "✔"),
            _                             => new("new",       "shop_order.status.new",       "shop-pill-new",       "○"),
        };
    }

    private static Badge IpqcBadge(WorkOrder wo)
    {
        var ipqc = wo.LastQc(QcType.IPQC);
        if (ipqc is null || ipqc.Result == QcResult.Pending)
            return new("ipqc_wait",     "shop_order.status.ipqc_wait",     "shop-pill-ipqc-wait", "③");
        if (ipqc.Result == QcResult.Pass)
            return new("ipqc_approved", "shop_order.status.ipqc_approved", "shop-pill-ipqc-ok",   "✓");
        return new("ipqc_rejected",     "shop_order.status.ipqc_rejected", "shop-pill-ipqc-fail", "✗");
    }
}
