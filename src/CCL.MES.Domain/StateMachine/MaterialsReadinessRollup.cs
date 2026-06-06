using CCL.MES.Domain.Entities;

namespace CCL.MES.Domain.StateMachine;

/// <summary>
/// P10.7b-1 — pure helper computing the legacy <c>WorkOrder.MaterialsReady</c>
/// bool from the new row-level PREPRESS child tables (per breakdown
/// "Legacy parity decision — additive cached rollup").
///
/// The rollup is true only when:
///   1. At least one <see cref="WoMaterial"/> row exists for the WO
///      (no snapshot ⇒ legacy bool stays authoritative — preserves
///      zero-diff for pre-7b WOs that have no BOM snapshot yet).
///   2. ALL existing material rows have <see cref="PrepressCheckStatus.Ok"/>.
///   3. The plate check exists and is <see cref="PrepressCheckStatus.Ok"/>.
///   4. The cutter check exists and is <see cref="PrepressCheckStatus.Ok"/>.
///
/// When ALL three surfaces (materials + plate + cutter) are present and
/// all OK, the helper returns true → caller flips <c>WorkOrder.MaterialsReady</c>.
/// Otherwise returns false → caller leaves the bool as-is (legacy
/// pre-7b WOs with no rows keep their pre-existing bool value;
/// post-7b WOs with at least one row track the rollup state).
///
/// The fourth return value (<see cref="HasSnapshot"/>) lets the caller
/// distinguish "no snapshot yet → defer to legacy bool" from "snapshot
/// exists but not all rows OK → MaterialsReady = false".
/// </summary>
public static class MaterialsReadinessRollup
{
    /// <summary>
    /// Compute the rollup. Caller passes the loaded child rows (or null
    /// where the entity is missing).
    /// </summary>
    /// <param name="materials">All wo_materials rows for the WO (empty if no snapshot).</param>
    /// <param name="plate">wo_plate_check row for the WO (null if no snapshot).</param>
    /// <param name="cutter">wo_cutter_check row for the WO (null if no snapshot).</param>
    /// <returns>
    /// <c>HasSnapshot</c> = true iff at least one of (materials, plate,
    /// cutter) is present — caller should treat the rollup as authoritative.
    /// <c>AllOk</c> = the boolean to write to <c>WorkOrder.MaterialsReady</c>
    /// when <c>HasSnapshot</c>; meaningless when <c>HasSnapshot</c> is false.
    /// </returns>
    public static (bool HasSnapshot, bool AllOk) Compute(
        IReadOnlyCollection<WoMaterial>? materials,
        WoPlateCheck? plate,
        WoCutterCheck? cutter)
    {
        var hasMaterials = materials is { Count: > 0 };
        var hasPlate = plate is not null;
        var hasCutter = cutter is not null;
        var hasSnapshot = hasMaterials || hasPlate || hasCutter;

        if (!hasSnapshot) return (HasSnapshot: false, AllOk: false);

        var materialsOk = hasMaterials &&
            materials!.All(m => m.Status == PrepressCheckStatus.Ok);
        var plateOk = hasPlate && plate!.Status == PrepressCheckStatus.Ok;
        var cutterOk = hasCutter && cutter!.Status == PrepressCheckStatus.Ok;

        var allOk = materialsOk && plateOk && cutterOk;
        return (HasSnapshot: true, AllOk: allOk);
    }
}
