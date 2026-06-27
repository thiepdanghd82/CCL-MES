namespace CCL.MES.Hybrid.Client.Npi;

/// <summary>
/// P10.5a — NPI grid DTOs. The server returns the legacy
/// <c>CCL.MES.Domain.Entities.{WorkCenter, RawMaterial, RoutingOperation,
/// ManufacturingStructure}</c> directly; we mirror only the properties
/// the MAUI grid actually displays. System.Text.Json ignores extra
/// JSON fields by default so future server-side additions don't break
/// deserialization.
///
/// Each DTO is a record with init-only properties (immutable snapshots
/// safe to pass between components). Audit stamps (CreatedAt/Updated*)
/// are included so future spec-style "By/At" columns can render without
/// schema churn.
/// </summary>
public sealed record NpiAuditStamp
{
    public DateTime CreatedAt { get; init; }
    public string? CreatedBy { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public string? UpdatedBy { get; init; }
}

/// <summary>WorkCenter — full 6 fields (Phase 7 hạng mục 5).</summary>
public sealed record NpiWorkCenter
{
    public long Id { get; init; }
    public string Code { get; init; } = "";
    public string? Description { get; init; }
    public string? Area { get; init; }
    public double? IdealSpeedPcsH { get; init; }
    public string? ShiftPattern { get; init; }
    public bool? Active { get; init; }
    public DateTime CreatedAt { get; init; }
    public string? CreatedBy { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public string? UpdatedBy { get; init; }
}

/// <summary>RawMaterial — full 28 IFS fields (Phase 7 hạng mục 3).
/// Grid renders 12 default visible; remaining 16 hidden behind columns
/// toggle to keep first paint fast on Mac Catalyst trackpad scroll.</summary>
public sealed record NpiRawMaterial
{
    public long Id { get; init; }
    public string PartNo { get; init; } = "";
    public string? PartDescription { get; init; }
    public string? SupplierId { get; init; }
    public string? SupplierName { get; init; }
    public double? Price { get; init; }
    public double? PriceInclTax { get; init; }
    public string? Currency { get; init; }
    public string? PriceUom { get; init; }
    public double? SupplierLeadtimeDays { get; init; }
    public string? PurchUom { get; init; }
    public string? InventoryUom { get; init; }
    public string? Site { get; init; }
    public string? SiteDescription { get; init; }
    public string? StatusCode { get; init; }
    public double? MinimumQuantity { get; init; }
    public double? StdMultipleQty { get; init; }
    public double? StandardPackSize { get; init; }
    public double? ConversionFactor { get; init; }
    public string? TaxCode { get; init; }
    public string? TaxCodeDescription { get; init; }
    public string? CountryOfOrigin { get; init; }
    public string? AcquisitionType { get; init; }
    public string? SupplierPartNo { get; init; }
    public string? SupplierPartDescription { get; init; }
    public double? NetWeight { get; init; }
    public string? NetWeightUom { get; init; }
    public string? NextOrderDate { get; init; }
    public string? Notes { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

/// <summary>RoutingOperation — full 20 fields (Phase 7 hạng mục 2).</summary>
public sealed record NpiRoutingOperation
{
    public long Id { get; init; }
    public string PartNo { get; init; } = "";
    public string? PartDescription { get; init; }
    public string? OpNo { get; init; }
    public string? Operation { get; init; }
    public string? WorkCenterNo { get; init; }
    public string? WorkCenterDescription { get; init; }
    public double? MachineSetupTime { get; init; }
    public double? LaborSetupTime { get; init; }
    public double? MachineRunTime { get; init; }
    public double? LaborRunTime { get; init; }
    public string? Unit { get; init; }
    public double? Crew { get; init; }
    public double? SetupCrew { get; init; }
    public string? LaborClass { get; init; }
    public string? Alt { get; init; }
    public string? Effectivity { get; init; }
    public double? Efficiency { get; init; }
    public string? Site { get; init; }
    public string? RoutingType { get; init; }
    public string? Planner { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

/// <summary>ManufacturingStructure / BOM — full 16 fields (Phase 7 hạng mục 1).
/// Wire-name matches the Domain entity property names so deserialization is
/// 1:1 without DTO transforms.</summary>
public sealed record NpiStructure
{
    public long Id { get; init; }
    public string ParentPart { get; init; } = "";
    public string? ParentDescription { get; init; }
    public string ComponentPart { get; init; } = "";
    public string? ComponentDescription { get; init; }
    public double QtyAssembly { get; init; }
    public string? Uom { get; init; }
    public double ScrapFactor { get; init; }
    public double? ScrapPct { get; init; }
    public double? Pitch { get; init; }
    public double? Cavity { get; init; }
    public string? Color { get; init; }
    public string? StructureType { get; init; }
    public string? Alt { get; init; }
    public string? Effectivity { get; init; }
    public string? EffectivityDate { get; init; }
    public string? Planner { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

/// <summary>P10.5 follow-up — result of an NPI CSV import.</summary>
public sealed record NpiImportResultDto
{
    public string Kind { get; init; } = "";
    public int Inserted { get; init; }
    public int Skipped { get; init; }
}
