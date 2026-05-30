using CCL.MES.Domain;

namespace CCL.MES.Application;

public class CreateWiRequest
{
    public string Title { get; set; } = "";
    public long ProductId { get; set; }
    public ProcessStepCode ProcessStep { get; set; }
    public string? MachineCode { get; set; }
    public List<WiStepDto> Steps { get; set; } = new();
}

public class WiStepDto
{
    public string Description { get; set; } = "";
    public string? ImageUrl { get; set; }
    public string? WarningNote { get; set; }
}
