using CCL.MES.Domain.StateMachine;
using CCL.MES.Hybrid.Client.Status;

namespace CCL.MES.Hybrid.Client.Tests;

/// <summary>
/// D3/D4 drift guard — the WO-card phase palette MUST give every canonical
/// <see cref="MesPhase"/> a real colour, never falling to the generic
/// <c>wo-phase-other</c>. This caught the SPLIT gap (umbrella multi-leg phase
/// showed grey while every dashboard pill coloured it). Iterating the enum means
/// a NEW phase without a colour fails here — same protection PhaseVisual has (L46).
/// </summary>
public sealed class WoCardPhasePaletteTests
{
    public static IEnumerable<object[]> AllMesPhases() =>
        Enum.GetNames<MesPhase>().Select(n => new object[] { n });

    [Theory]
    [MemberData(nameof(AllMesPhases))]
    public void Every_MesPhase_has_a_dedicated_card_colour(string phase)
    {
        var css = WoCardPhasePalette.CssClass(phase);
        Assert.StartsWith("wo-phase-", css);
        Assert.NotEqual("wo-phase-other", css);
    }

    [Fact]
    public void SPLIT_shares_prepress_tone_not_generic_other()
    {
        // SPLIT projects to PrePressCheck in the domain → prepress tone, not grey.
        Assert.Equal("wo-phase-prepress", WoCardPhasePalette.CssClass("SPLIT"));
    }

    [Fact]
    public void Empty_phase_falls_back_to_server_badge_class()
    {
        Assert.Equal("legacy-badge", WoCardPhasePalette.CssClass("", "legacy-badge"));
        Assert.Equal("", WoCardPhasePalette.CssClass(null));
    }

    [Fact]
    public void Unknown_phase_is_the_only_path_to_other()
    {
        Assert.Equal("wo-phase-other", WoCardPhasePalette.CssClass("ZZ_UNKNOWN"));
    }
}
