using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// P10.7b-1 — entity-defaults coverage. Locks the documented invariants
/// from PrepressChecks.cs so a future PR can't silently change a default
/// status (e.g. PENDING → OK) without tripping these fixtures.
/// </summary>
public sealed class PrepressEntitiesTests
{
    [Fact]
    public void WoMaterial_defaults_status_is_pending_and_material_code_empty_string()
    {
        var m = new WoMaterial();
        Assert.Equal(PrepressCheckStatus.Pending, m.Status);
        Assert.Equal("", m.MaterialCode);
        Assert.Null(m.MaterialDescription);
        Assert.Null(m.QtyLoaded);
        Assert.Null(m.LotNo);
        Assert.Null(m.NgReasonCode);
        Assert.Null(m.NgNote);
        Assert.Null(m.CheckedBy);
        Assert.Null(m.CheckedAt);
    }

    [Fact]
    public void WoPlateCheck_defaults_status_is_pending_and_plate_no_null()
    {
        var p = new WoPlateCheck();
        Assert.Equal(PrepressCheckStatus.Pending, p.Status);
        Assert.Null(p.PlateNo);
        Assert.Null(p.NgReasonCode);
        Assert.Null(p.NgNote);
        Assert.Null(p.CheckedBy);
        Assert.Null(p.CheckedAt);
    }

    [Fact]
    public void WoCutterCheck_defaults_status_is_pending_and_cutter_no_null()
    {
        var c = new WoCutterCheck();
        Assert.Equal(PrepressCheckStatus.Pending, c.Status);
        Assert.Null(c.CutterNo);
        Assert.Null(c.NgReasonCode);
        Assert.Null(c.NgNote);
        Assert.Null(c.CheckedBy);
        Assert.Null(c.CheckedAt);
    }

    [Fact]
    public void PrepressCheckStatus_enum_has_exactly_three_values()
    {
        var values = Enum.GetValues<PrepressCheckStatus>();
        Assert.Equal(3, values.Length);
        Assert.Contains(PrepressCheckStatus.Pending, values);
        Assert.Contains(PrepressCheckStatus.Ok, values);
        Assert.Contains(PrepressCheckStatus.Ng, values);
    }

    [Fact]
    public void WoMaterial_bom_line_idx_round_trips_zero_based()
    {
        var m = new WoMaterial { BomLineIdx = 0, MaterialCode = "X" };
        Assert.Equal(0, m.BomLineIdx);
        m.BomLineIdx = 42;
        Assert.Equal(42, m.BomLineIdx);
    }
}
