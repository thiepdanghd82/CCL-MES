namespace CCL.MES.Domain.Entities;

public class QcInspection : BaseEntity
{
    public long WorkOrderId { get; set; }
    public WorkOrder? WorkOrder { get; set; }
    public QcType Type { get; set; }
    public QcResult Result { get; set; } = QcResult.Pending;
    public string? InspectorId { get; set; }
    public int SampleSize { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public List<QcResultDetail> Details { get; set; } = new();
}

public class QcResultDetail : BaseEntity
{
    public long QcInspectionId { get; set; }
    public string ItemName { get; set; } = "";
    public string? MeasuredValue { get; set; }
    public bool Pass { get; set; }
    public string? DefectCode { get; set; }
    public int Qty { get; set; }
}
