namespace CCL.MES.Domain;

/// <summary>7 bước của process flow đúng theo màn hình Work Order (mockup).</summary>
public enum ProcessStepCode
{
    PrePressCheck = 1,
    OpSetting     = 2,
    IpqcApproval  = 3,
    ReadyToRun    = 4,
    Running       = 5,
    Fqc           = 6,
    Oqc           = 7,
    Closed        = 8
}

public enum WoStatus { Draft, InProgress, OnHold, Finished, Closed, Cancelled }
public enum SpecStatus { Draft, InReview, Approved, Obsolete }
public enum QcType { IPQC, FQC, OQC }
public enum QcResult { Pending, Pass, Fail }

/// <summary>Loại sự kiện sản xuất ghi nhận theo máy (cho OEE).</summary>
public enum ProductionEventType { Run, Stop, Setup, Idle }
public enum WiStatus { Draft, Approved, Obsolete }
