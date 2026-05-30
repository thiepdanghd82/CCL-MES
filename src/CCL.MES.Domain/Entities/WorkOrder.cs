namespace CCL.MES.Domain.Entities;

public class WorkOrder : BaseEntity
{
    public string WoNo { get; set; } = "";
    public long CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public long ProductId { get; set; }
    public Product? Product { get; set; }
    public string ProductName { get; set; } = "";

    public long? SpecVersionId { get; set; }
    public SpecVersion? SpecVersion { get; set; }

    public string? MachineCode { get; set; }
    public string? MachineName { get; set; }

    public int TargetQty { get; set; }
    public string Uom { get; set; } = "pcs";
    public int ProducedQty { get; set; }

    public ProcessStepCode CurrentStep { get; set; } = ProcessStepCode.PrePressCheck;
    public WoStatus Status { get; set; } = WoStatus.Draft;
    public int Priority { get; set; }

    // Điều kiện (guard) cho state machine
    public bool MaterialsReady { get; set; }
    public bool SetupConfirmed { get; set; }
    public bool RohsOk { get; set; }

    public DateTime? PlannedStart { get; set; }
    public DateTime? PlannedEnd { get; set; }

    public List<WoStatusHistory> History { get; set; } = new();
    public List<QcInspection> Inspections { get; set; } = new();

    /// <summary>Lần kiểm QC gần nhất theo loại (IPQC/FQC/OQC).</summary>
    public QcInspection? LastQc(QcType type) =>
        Inspections.Where(i => i.Type == type).OrderByDescending(i => i.Id).FirstOrDefault();
}

/// <summary>Log append-only mọi lần chuyển bước.</summary>
public class WoStatusHistory : BaseEntity
{
    public long WorkOrderId { get; set; }
    public ProcessStepCode FromStep { get; set; }
    public ProcessStepCode ToStep { get; set; }
    public string Action { get; set; } = "";
    public string? ByUser { get; set; }
    public string? Reason { get; set; }
}
