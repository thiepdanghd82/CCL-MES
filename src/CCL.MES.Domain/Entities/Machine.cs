namespace CCL.MES.Domain.Entities;

public class Machine : BaseEntity
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Type { get; set; }
    /// <summary>Trạng thái hiện thời để hiển thị dashboard.</summary>
    public ProductionEventType CurrentState { get; set; } = ProductionEventType.Idle;
    /// <summary>Thời gian chu kỳ lý tưởng cho 1 sản phẩm (giây) - dùng tính Performance.</summary>
    public double IdealCycleTimeSec { get; set; } = 1.0;
}

public class DowntimeReason : BaseEntity
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Category { get; set; } // Planned / Unplanned
}

/// <summary>Một khoảng sự kiện sản xuất của máy gắn với WO (append-only).</summary>
public class ProductionLog : BaseEntity
{
    public long WorkOrderId { get; set; }
    public WorkOrder? WorkOrder { get; set; }
    public long MachineId { get; set; }
    public Machine? Machine { get; set; }

    public ProductionEventType EventType { get; set; }
    public DateTime StartAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndAt { get; set; }

    public int GoodQty { get; set; }
    public int RejectQty { get; set; }
    public string? OperatorId { get; set; }

    public long? DowntimeReasonId { get; set; }
    public DowntimeReason? DowntimeReason { get; set; }

    public double DurationMinutes =>
        ((EndAt ?? DateTime.UtcNow) - StartAt).TotalMinutes;
}
