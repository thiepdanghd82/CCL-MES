using CCL.MES.Domain.Routing;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// P11 per-leg Pre-press (Q-B) — LegKind → tool-check rule. PRINT needs a
/// plate; CUT/TAPE need a cutter; PRINT_CUT needs both; ASSEMBLY needs
/// neither. ⚠ Ops-confirm mapping — locked here so a silent drift trips CI.
/// </summary>
public sealed class LegPrepressToolsTests
{
    [Theory]
    [InlineData("PRINT", true, false)]
    [InlineData("PRINT_CUT", true, true)]
    [InlineData("CUT", false, true)]
    [InlineData("TAPE", false, true)]
    [InlineData("ASSEMBLY", false, false)]
    [InlineData("print", true, false)]   // case-insensitive
    [InlineData("", false, false)]
    [InlineData("UNKNOWN", false, false)]
    public void Tool_needs_match_leg_kind(string kind, bool plate, bool cutter)
    {
        Assert.Equal(plate, LegPrepressTools.NeedsPlate(kind));
        Assert.Equal(cutter, LegPrepressTools.NeedsCutter(kind));
    }

    [Fact]
    public void Null_kind_needs_nothing()
    {
        Assert.False(LegPrepressTools.NeedsPlate(null));
        Assert.False(LegPrepressTools.NeedsCutter(null));
    }
}
