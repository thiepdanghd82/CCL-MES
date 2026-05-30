using CCL.MES.Domain.Entities;

namespace CCL.MES.Domain.StateMachine;

public record TransitionResult(bool Allowed, string? Reason = null);

/// <summary>
/// Máy trạng thái điều khiển luồng 7 bước của Work Order.
/// Mỗi bước chỉ được chuyển khi thỏa điều kiện (guard).
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
        if (next is null) return new TransitionResult(false, "Work Order da o buoc cuoi.");

        return (wo.CurrentStep, next.Value) switch
        {
            (ProcessStepCode.PrePressCheck, ProcessStepCode.OpSetting) =>
                wo.SpecVersionId is not null && wo.MaterialsReady
                    ? new TransitionResult(true)
                    : new TransitionResult(false, "Can Spec da duyet (SpecVersionId) va vat tu san sang (MaterialsReady)."),

            (ProcessStepCode.OpSetting, ProcessStepCode.IpqcApproval) =>
                wo.SetupConfirmed
                    ? new TransitionResult(true)
                    : new TransitionResult(false, "Can xac nhan can chinh may (SetupConfirmed)."),

            (ProcessStepCode.IpqcApproval, ProcessStepCode.ReadyToRun) =>
                wo.LastQc(QcType.IPQC)?.Result == QcResult.Pass
                    ? new TransitionResult(true)
                    : new TransitionResult(false, "IPQC chua Pass."),

            (ProcessStepCode.ReadyToRun, ProcessStepCode.Running) =>
                new TransitionResult(true),

            (ProcessStepCode.Running, ProcessStepCode.Fqc) =>
                wo.ProducedQty > 0
                    ? new TransitionResult(true)
                    : new TransitionResult(false, "Chua ghi nhan san luong (ProducedQty = 0)."),

            (ProcessStepCode.Fqc, ProcessStepCode.Oqc) =>
                wo.LastQc(QcType.FQC)?.Result == QcResult.Pass
                    ? new TransitionResult(true)
                    : new TransitionResult(false, "FQC chua Pass."),

            (ProcessStepCode.Oqc, ProcessStepCode.Closed) =>
                wo.LastQc(QcType.OQC)?.Result == QcResult.Pass && wo.RohsOk
                    ? new TransitionResult(true)
                    : new TransitionResult(false, "OQC chua Pass hoac RoHS chua dat."),

            _ => new TransitionResult(false, "Buoc chuyen khong hop le.")
        };
    }
}
