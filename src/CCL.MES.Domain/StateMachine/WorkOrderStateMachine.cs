using CCL.MES.Domain.Entities;

namespace CCL.MES.Domain.StateMachine;

public record TransitionResult(bool Allowed, WoErrorCode? Error = null);

/// <summary>
/// State machine governing the 7-step Work Order flow. Each transition
/// fires only when its guard is satisfied. Phase 5 — guards return a
/// language-free <see cref="WoErrorCode"/>; the Web layer maps each code
/// to a resource key via <c>WoErrorKeys</c> so the dynamic portion of the
/// UI message localises with the surrounding text. See
/// docs/PHASE5-STEP3-PLAN.md.
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
        if (next is null) return new TransitionResult(false, WoErrorCode.AlreadyAtFinalStep);

        return (wo.CurrentStep, next.Value) switch
        {
            (ProcessStepCode.PrePressCheck, ProcessStepCode.OpSetting) =>
                wo.ProductRevisionId is not null && wo.MaterialsReady
                    ? new TransitionResult(true)
                    : new TransitionResult(false, WoErrorCode.RequiresSpecAndMaterials),

            (ProcessStepCode.OpSetting, ProcessStepCode.IpqcApproval) =>
                wo.SetupConfirmed
                    ? new TransitionResult(true)
                    : new TransitionResult(false, WoErrorCode.RequiresSetupConfirmed),

            (ProcessStepCode.IpqcApproval, ProcessStepCode.ReadyToRun) =>
                wo.LastQc(QcType.IPQC)?.Result == QcResult.Pass
                    ? new TransitionResult(true)
                    : new TransitionResult(false, WoErrorCode.IpqcNotPassed),

            (ProcessStepCode.ReadyToRun, ProcessStepCode.Running) =>
                new TransitionResult(true),

            (ProcessStepCode.Running, ProcessStepCode.Fqc) =>
                wo.ProducedQty > 0
                    ? new TransitionResult(true)
                    : new TransitionResult(false, WoErrorCode.NoProductionYet),

            (ProcessStepCode.Fqc, ProcessStepCode.Oqc) =>
                wo.LastQc(QcType.FQC)?.Result == QcResult.Pass
                    ? new TransitionResult(true)
                    : new TransitionResult(false, WoErrorCode.FqcNotPassed),

            (ProcessStepCode.Oqc, ProcessStepCode.Closed) =>
                wo.LastQc(QcType.OQC)?.Result == QcResult.Pass && wo.RohsOk
                    ? new TransitionResult(true)
                    : new TransitionResult(false, WoErrorCode.OqcOrRohsNotMet),

            _ => new TransitionResult(false, WoErrorCode.InvalidStepTransition)
        };
    }
}
