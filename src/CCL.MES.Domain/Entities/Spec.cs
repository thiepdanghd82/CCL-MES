namespace CCL.MES.Domain.Entities;

public class Spec : BaseEntity
{
    public string SpecCode { get; set; } = "";
    public string Title { get; set; } = "";
    public long ProductId { get; set; }
    public Product? Product { get; set; }
    public List<SpecVersion> Versions { get; set; } = new();
}

public class SpecVersion : BaseEntity
{
    public long SpecId { get; set; }
    public Spec? Spec { get; set; }
    public int VersionNo { get; set; } = 1;
    public SpecStatus Status { get; set; } = SpecStatus.Draft;
    public DateTime? EffectiveDate { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public List<SpecParameter> Parameters { get; set; } = new();
}

public class SpecParameter : BaseEntity
{
    public long SpecVersionId { get; set; }
    public string ParamName { get; set; } = "";
    public string? Nominal { get; set; }
    public string? TolMin { get; set; }
    public string? TolMax { get; set; }
    public string? Uom { get; set; }
    public bool IsCritical { get; set; }
}
