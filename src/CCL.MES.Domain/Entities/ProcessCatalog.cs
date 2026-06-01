namespace CCL.MES.Domain.Entities;

/// <summary>
/// Phase 8 PR #28 — Lookup table cho mọi method in / cắt / finishing.
/// Extensible by Library admin without DB migration (admin UI defer Phase 9).
/// SpecPrint.ProcessCode / SpecDiecut.CutProcessCode / SpecFinishing.FinishingProcessesJson
/// reference qua VARCHAR code thay vì FK strict — validate ở app layer (mirror
/// Library schema validation pattern của Ops Control).
///
/// PR #28 seed 17 codes mặc định trong DbSeeder (Phase 8 PROCESS_CATALOG_SEED).
/// </summary>
public class ProcessCatalog : BaseEntity
{
    /// <summary>Stable code as PRIMARY KEY semantic — but EF still uses Id. Unique index on Code.</summary>
    public string Code { get; set; } = "";

    public ProcessCategory Category { get; set; }

    public string DisplayNameVi { get; set; } = "";
    public string DisplayNameEn { get; set; } = "";

    public string? Description { get; set; }

    public ProcessCatalogStatus Status { get; set; } = ProcessCatalogStatus.Active;
    public short DisplayOrder { get; set; } = 100;
}
