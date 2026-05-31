using CCL.MES.Domain;
using CCL.MES.Domain.StateMachine;

namespace CCL.MES.Application;

public class CreateWoRequest
{
    public string WoNo { get; set; } = "";
    public long CustomerId { get; set; }
    public long ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public long? SpecVersionId { get; set; }
    public string? MachineCode { get; set; }
    public string? MachineName { get; set; }
    public int TargetQty { get; set; }
    public string? Uom { get; set; }
}

public class UpdateFlagsRequest
{
    public bool? MaterialsReady { get; set; }
    public bool? SetupConfirmed { get; set; }
    public bool? RohsOk { get; set; }
    public int? ProducedQty { get; set; }
}

public class CreateSpecRequest
{
    public long ProductId { get; set; }
    public string SpecCode { get; set; } = "";
    public string Title { get; set; } = "";
    public List<SpecParamDto> Parameters { get; set; } = new();
}

public class SpecParamDto
{
    public string ParamName { get; set; } = "";
    public string? Nominal { get; set; }
    public string? TolMin { get; set; }
    public string? TolMax { get; set; }
    public string? Uom { get; set; }
    public bool IsCritical { get; set; }
}

public class CreateQcRequest
{
    public long WorkOrderId { get; set; }
    public QcType Type { get; set; }
    public string? InspectorId { get; set; }
    public int SampleSize { get; set; }
    public List<QcDetailDto> Details { get; set; } = new();
}

public class QcDetailDto
{
    public string ItemName { get; set; } = "";
    public string? MeasuredValue { get; set; }
    public bool Pass { get; set; }
    public string? DefectCode { get; set; }
    public int Qty { get; set; }
}

// Phase 5 — ErrorCode replaces the prior free-form Error string. The Web
// layer maps the enum to a localized resource key via WoErrorKeys; the API
// serialises the enum as its name (e.g. "RequiresSpecAndMaterials") thanks
// to JsonStringEnumConverter registered in Program.cs.
public record AdvanceResult(bool Ok, WoErrorCode? ErrorCode, string CurrentStep);
