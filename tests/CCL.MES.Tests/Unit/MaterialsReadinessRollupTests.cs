using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using CCL.MES.Domain.StateMachine;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// P10.7b-1 — pure-helper coverage for the materials-readiness rollup.
/// Lessons applied:
///   * Legacy parity: "no snapshot" → HasSnapshot=false → caller leaves
///     WorkOrder.MaterialsReady bool untouched (zero-diff for pre-7b WOs).
///   * Forward state: every surface (materials + plate + cutter) must
///     be present AND OK for AllOk=true. Missing ANY surface → false.
/// </summary>
public sealed class MaterialsReadinessRollupTests
{
    private static WoMaterial Mat(PrepressCheckStatus s, int idx = 0) =>
        new() { BomLineIdx = idx, MaterialCode = "M" + idx, Status = s };

    private static WoPlateCheck Plate(PrepressCheckStatus s) =>
        new() { Status = s };

    private static WoCutterCheck Cutter(PrepressCheckStatus s) =>
        new() { Status = s };

    // ── No snapshot → legacy bool stays authoritative ───────────────

    [Fact]
    public void No_rows_at_all_returns_HasSnapshot_false()
    {
        var (hasSnap, allOk) = MaterialsReadinessRollup.Compute(null, null, null);
        Assert.False(hasSnap);
        Assert.False(allOk);
    }

    [Fact]
    public void Empty_materials_list_with_null_plate_and_cutter_returns_HasSnapshot_false()
    {
        var (hasSnap, allOk) = MaterialsReadinessRollup.Compute(Array.Empty<WoMaterial>(), null, null);
        Assert.False(hasSnap);
        Assert.False(allOk);
    }

    // ── Partial snapshots → HasSnapshot=true, AllOk=false ──────────

    [Fact]
    public void Only_plate_present_returns_HasSnapshot_true_AllOk_false()
    {
        var (hasSnap, allOk) = MaterialsReadinessRollup.Compute(null, Plate(PrepressCheckStatus.Ok), null);
        Assert.True(hasSnap);
        Assert.False(allOk);
    }

    [Fact]
    public void Materials_OK_but_no_plate_or_cutter_returns_AllOk_false()
    {
        var rows = new[] { Mat(PrepressCheckStatus.Ok, 0), Mat(PrepressCheckStatus.Ok, 1) };
        var (hasSnap, allOk) = MaterialsReadinessRollup.Compute(rows, null, null);
        Assert.True(hasSnap);
        Assert.False(allOk);
    }

    [Fact]
    public void All_three_surfaces_present_but_one_material_pending_returns_AllOk_false()
    {
        var rows = new[]
        {
            Mat(PrepressCheckStatus.Ok, 0),
            Mat(PrepressCheckStatus.Pending, 1),    // ← bites
        };
        var (hasSnap, allOk) = MaterialsReadinessRollup.Compute(
            rows, Plate(PrepressCheckStatus.Ok), Cutter(PrepressCheckStatus.Ok));
        Assert.True(hasSnap);
        Assert.False(allOk);
    }

    [Fact]
    public void All_three_surfaces_present_but_one_material_NG_returns_AllOk_false()
    {
        var rows = new[]
        {
            Mat(PrepressCheckStatus.Ok, 0),
            Mat(PrepressCheckStatus.Ng, 1),
        };
        var (hasSnap, allOk) = MaterialsReadinessRollup.Compute(
            rows, Plate(PrepressCheckStatus.Ok), Cutter(PrepressCheckStatus.Ok));
        Assert.True(hasSnap);
        Assert.False(allOk);
    }

    [Fact]
    public void Plate_NG_makes_rollup_false_even_if_materials_and_cutter_OK()
    {
        var rows = new[] { Mat(PrepressCheckStatus.Ok, 0) };
        var (hasSnap, allOk) = MaterialsReadinessRollup.Compute(
            rows, Plate(PrepressCheckStatus.Ng), Cutter(PrepressCheckStatus.Ok));
        Assert.True(hasSnap);
        Assert.False(allOk);
    }

    [Fact]
    public void Cutter_PENDING_makes_rollup_false_even_if_materials_and_plate_OK()
    {
        var rows = new[] { Mat(PrepressCheckStatus.Ok, 0) };
        var (hasSnap, allOk) = MaterialsReadinessRollup.Compute(
            rows, Plate(PrepressCheckStatus.Ok), Cutter(PrepressCheckStatus.Pending));
        Assert.True(hasSnap);
        Assert.False(allOk);
    }

    // ── All OK → AllOk=true ────────────────────────────────────────

    [Fact]
    public void All_surfaces_OK_returns_AllOk_true()
    {
        var rows = new[]
        {
            Mat(PrepressCheckStatus.Ok, 0),
            Mat(PrepressCheckStatus.Ok, 1),
            Mat(PrepressCheckStatus.Ok, 2),
        };
        var (hasSnap, allOk) = MaterialsReadinessRollup.Compute(
            rows, Plate(PrepressCheckStatus.Ok), Cutter(PrepressCheckStatus.Ok));
        Assert.True(hasSnap);
        Assert.True(allOk);
    }

    // ── Edge: 0 materials rows but plate + cutter OK → still false ──
    //    Operational case: legacy WO with empty BOM (no MS rows).
    //    Operator must explicitly add materials rows via 7b-2 endpoint
    //    OR confirm there genuinely are no materials (empty BOM
    //    products — rare). Default = require at least one materials row.

    [Fact]
    public void Empty_materials_with_plate_and_cutter_OK_returns_AllOk_false()
    {
        var (hasSnap, allOk) = MaterialsReadinessRollup.Compute(
            Array.Empty<WoMaterial>(), Plate(PrepressCheckStatus.Ok), Cutter(PrepressCheckStatus.Ok));
        Assert.True(hasSnap);
        Assert.False(allOk);
    }
}
