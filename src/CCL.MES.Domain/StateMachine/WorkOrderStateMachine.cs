using CCL.MES.Domain.Entities;

namespace CCL.MES.Domain.StateMachine;

public record TransitionResult(bool Allowed, string? Reason = null);

/// <summary>
/// State machine governing the 7-step Work Order flow. Each transition
/// fires only when its guard is satisfied. Reason strings are kept in
/// English because they bubble through the Razor page as the dynamic
/// portion of a localized message ("Cannot advance: <Reason>"). Phase 4+
/// should swap to an error-code → resource-key map so the dynamic portion
/// also localises.
/// </summary>
public static class WorkOrderStateMachine
{
    public static readonly ProcessStepCode[] Flow =
    {
        ProcessStepCode.PrePressCheck,
        ProcessStepCode.OpSetting,
        ProcessStepCode.IpqcApproval,
        ProcessStepCode.ReadyToRun,
        ProcessStepCode.Running,
        ProcessStepCode.Fqc,
        ProcessStepCode.Oqc,
        ProcessStepCode.Closed
    };

    public static ProcessStepCode? Next(ProcessStepCode current)
    {
        var idx = Array.IndexOf(Flow, current);
        if (idx < 0 || idx >= Flow.Length - 1) return null;
        return Flow[idx + 1];
    }

    public static TransitionResult CanAdvance(WorkOrder wo)
    {
        var next = Next(wo.CurrentStep);
        if (next is null) return new TransitionResult(false, "Work Order is already at the final step.");

        return (wo.CurrentStep, next.Value) switch
        {
            (ProcessStepCode.PrePressCheck, ProcessStepCode.OpSetting) =>
                wo.SpecVersionId is not null && wo.MaterialsReady
                    ? new TransitionResult(true)
                    : new TransitionResult(false, "Requires an approved Spec (SpecVersionId) and ready materials (MaterialsReady)."),

            (ProcessStepCode.OpSetting, ProcessStepCode.IpqcApproval) =>
                wo.SetupConfirmed
                    ? new TransitionResult(true)
                    : new TransitionResult(false, "Requires machine setup confirmation (SetupConfirmed)."),

            (ProcessStepCode.IpqcApproval, ProcessStepCode.ReadyToRun) =>
                wo.LastQc(QcType.IPQC)?.Result == QcResult.Pass
                    ? new TransitionResult(true)
                    : new TransitionResult(false, "IPQC has not yet Passed."),

            (ProcessStepCode.ReadyToRun, ProcessStepCode.Running) =>
                new TransitionResult(true),

            (ProcessStepCode.Running, ProcessStepCode.Fqc) =>
                wo.ProducedQty > 0
                    ? new TransitionResult(true)
                    : new TransitionResult(false, "No production recorded yet (ProducedQty = 0)."),

            (ProcessStepCode.Fqc, ProcessStepCode.Oqc) =>
                wo.LastQc(QcType.FQC)?.Result == QcResult.Pass
                    ? new TransitionResult(true)
                    : new TransitionResult(false, "FQC has not yet Passed."),

            (ProcessStepCode.Oqc, ProcessStepCode.Closed) =>
                wo.LastQc(QcType.OQC)?.Result == QcResult.Pass && wo.RohsOk
                    ? new TransitionResult(true)
                    : new TransitionResult(false, "OQC has not yet Passed or RoHS not met."),

            _ => new TransitionResult(false, "Invalid step transition.")
        };
    }
}
