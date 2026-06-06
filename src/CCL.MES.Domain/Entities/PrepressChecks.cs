namespace CCL.MES.Domain.Entities;

/// <summary>
/// P10.7b-1 — row-level material check per BOM line. Created at WO
/// creation time by <c>PrepressBomSnapshotService</c> from a snapshot
/// of <see cref="ManufacturingStructure"/> rows where
/// <c>ParentPart == product.ProductCode</c>. Per contract §5.3 the
/// snapshot is frozen — BOM revisions post WO-create do NOT update
/// existing wo_materials rows.
///
/// PK is the synthetic <see cref="BaseEntity.Id"/>; the operational
/// uniqueness constraint is <c>(WorkOrderId, BomLineIdx)</c> enforced
/// by the EF model. <see cref="BomLineIdx"/> is the zero-based ordinal
/// of the BOM line at snapshot time (ManufacturingStructure rows
/// ordered by <c>Id</c>); it lets operators reference rows by a stable
/// index across UI re-renders.
///
/// Per §5.1 the row is OVERWRITEABLE (operator sets status / qty
/// loaded / lot / NG reason) until the WO exits PREPRESS — after which
/// the API rejects further writes with 422 invalid_phase.
/// </summary>
public class WoMaterial : BaseEntity
{
    public long WorkOrderId { get; set; }

    /// <summary>Zero-based ordinal of the BOM line at snapshot time.
    /// (WorkOrderId, BomLineIdx) is unique per contract §5.1 PK clause.</summary>
    public int BomLineIdx { get; set; }

    /// <summary>From <c>ManufacturingStructure.ComponentPart</c>.</summary>
    public string MaterialCode { get; set; } = "";

    /// <summary>From <c>ManufacturingStructure.ComponentDescription</c>;
    /// nullable because legacy IFS-imported BOM rows may omit it.</summary>
    public string? MaterialDescription { get; set; }

    /// <summary>Computed at snapshot time as
    /// <c>ManufacturingStructure.QtyAssembly × WorkOrder.TargetQty</c>
    /// (baseline requirement; operator records actual via
    /// <see cref="QtyLoaded"/>). Scrap factor NOT applied at this
    /// layer; scrap policy is per-WO config and lives downstream.</summary>
    public double QtyRequired { get; set; }

    public string? Uom { get; set; }

    public double? QtyLoaded { get; set; }
    public string? LotNo { get; set; }

    public PrepressCheckStatus Status { get; set; } = PrepressCheckStatus.Pending;

    /// <summary>Required when <see cref="Status"/> = NG — references
    /// <see cref="ReasonCode.Code"/> by natural key, kind
    /// <see cref="ReasonCodeKind.Scrap"/>. API enforces.</summary>
    public string? NgReasonCode { get; set; }
    public string? NgNote { get; set; }

    public string? CheckedBy { get; set; }
    public DateTime? CheckedAt { get; set; }
}

/// <summary>
/// P10.7b-1 — single-row plate check per WO (1:1, enforced by unique
/// index on <see cref="WorkOrderId"/>). Mirrors the legacy
/// <c>WorkOrder.SetupConfirmed</c> bool but at row-level granularity
/// (plate-no + NG reason + photo evidence pending).
///
/// Per §5.1 overwriteable until PREPRESS exit.
/// </summary>
public class WoPlateCheck : BaseEntity
{
    public long WorkOrderId { get; set; }

    public PrepressCheckStatus Status { get; set; } = PrepressCheckStatus.Pending;

    /// <summary>Operator-recorded plate identifier (printed on the
    /// flexo plate sleeve). Optional; only required when Status=OK.</summary>
    public string? PlateNo { get; set; }

    public string? NgReasonCode { get; set; }
    public string? NgNote { get; set; }

    public string? CheckedBy { get; set; }
    public DateTime? CheckedAt { get; set; }
}

/// <summary>
/// P10.7b-1 — single-row cutter check per WO (1:1). Same shape +
/// invariants as <see cref="WoPlateCheck"/>; the two are kept as
/// distinct entities per contract §5.1 row definition so future
/// per-check fields (cutter blade wear count, plate ink density
/// reading) can evolve independently without overloading one row.
/// </summary>
public class WoCutterCheck : BaseEntity
{
    public long WorkOrderId { get; set; }

    public PrepressCheckStatus Status { get; set; } = PrepressCheckStatus.Pending;

    /// <summary>Operator-recorded cutter / die identifier.</summary>
    public string? CutterNo { get; set; }

    public string? NgReasonCode { get; set; }
    public string? NgNote { get; set; }

    public string? CheckedBy { get; set; }
    public DateTime? CheckedAt { get; set; }
}
