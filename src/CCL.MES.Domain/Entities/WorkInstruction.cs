namespace CCL.MES.Domain.Entities;

public class WorkInstruction : BaseEntity
{
    public string Title { get; set; } = "";
    public long ProductId { get; set; }
    public Product? Product { get; set; }
    public ProcessStepCode ProcessStep { get; set; }
    public string? MachineCode { get; set; }
    public int VersionNo { get; set; } = 1;
    public WiStatus Status { get; set; } = WiStatus.Draft;
    public DateTime? EffectiveDate { get; set; }
    public List<WiStepDetail> Steps { get; set; } = new();
}

public class WiStepDetail : BaseEntity
{
    public long WorkInstructionId { get; set; }
    public int Sequence { get; set; }
    public string Description { get; set; } = "";
    public string? ImageUrl { get; set; }
    public string? WarningNote { get; set; }
}
