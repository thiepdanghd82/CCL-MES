namespace CCL.MES.Domain.Entities;

/// <summary>Trung tâm sản xuất / máy (Work Center). Nguồn: distinct từ Routing.</summary>
public class WorkCenter : BaseEntity
{
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Area { get; set; }
}

/// <summary>Danh mục nguyên vật liệu (IFS Raw Material catalog).</summary>
public class RawMaterial : BaseEntity
{
    public string PartNo { get; set; } = "";
    public string? PartDescription { get; set; }
    public string? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public double Price { get; set; }
    public string? Currency { get; set; }
    public string? PriceUom { get; set; }
    public string? CatalogGroup { get; set; }
    public string? CatalogDesc { get; set; }
    public string? Grp { get; set; }
    public string? Type { get; set; }
    public string? TypeDesc { get; set; }
}

/// <summary>Định tuyến công đoạn (Engineer Routine / Routing Operations).</summary>
public class RoutingOperation : BaseEntity
{
    public string PartNo { get; set; } = "";
    public string? PartDescription { get; set; }
    public string? OpNo { get; set; }
    public string? Operation { get; set; }
    public string? WorkCenterNo { get; set; }
    public string? WorkCenterDescription { get; set; }
    public double MachineSetupTime { get; set; }
    public double LaborSetupTime { get; set; }
    public double MachineRunTime { get; set; }
    public double LaborRunTime { get; set; }
}

/// <summary>Cấu trúc sản phẩm / BOM (Engineer Structure / Manufacturing Structures).</summary>
public class ManufacturingStructure : BaseEntity
{
    public string ParentPart { get; set; } = "";
    public string? ParentDescription { get; set; }
    public string ComponentPart { get; set; } = "";
    public string? ComponentDescription { get; set; }
    public double QtyAssembly { get; set; }
    public string? Uom { get; set; }
    public double ScrapFactor { get; set; }
    public string? ScrapPct { get; set; }
    public string? Pitch { get; set; }
    public string? Cavity { get; set; }
    public string? Color { get; set; }
}
