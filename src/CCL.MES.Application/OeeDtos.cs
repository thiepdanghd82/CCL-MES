namespace CCL.MES.Application;

public class FinishRunRequest
{
    public int GoodQty { get; set; }
    public int RejectQty { get; set; }
    public string? OperatorId { get; set; }
}

public class PauseRequest
{
    public long? DowntimeReasonId { get; set; }
    public string? OperatorId { get; set; }
}

/// <summary>Kết quả tính OEE cho 1 máy trong khoảng thời gian.</summary>
public record OeeResult(
    long MachineId,
    string MachineCode,
    string MachineName,
    string CurrentState,
    double PlannedMinutes,
    double RunMinutes,
    double DownMinutes,
    int GoodQty,
    int RejectQty,
    double Availability,
    double Performance,
    double Quality,
    double Oee);
