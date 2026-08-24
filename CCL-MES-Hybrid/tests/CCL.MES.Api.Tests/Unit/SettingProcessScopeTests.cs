using CCL.MES.Shared.RunningSurface;
using Xunit;

namespace CCL.MES.Api.Tests.Unit;

/// <summary>
/// P10.7f — pure derivation of the SETTING Print/Cut tab scope from a routing
/// plan's leg kinds. Fallback-to-both is the safety invariant: an operator must
/// never be left with zero applicable checklists.
/// </summary>
public sealed class SettingProcessScopeTests
{
    [Fact]
    public void PrintCut_inline_leg_shows_both_tabs()
    {
        var (print, cut) = SettingProcessScope.FromLegKinds(new[] { "PRINT_CUT" });
        Assert.True(print);
        Assert.True(cut);
    }

    [Fact]
    public void Print_only_leg_shows_print_tab_only()
    {
        var (print, cut) = SettingProcessScope.FromLegKinds(new[] { "PRINT" });
        Assert.True(print);
        Assert.False(cut);
    }

    [Fact]
    public void Cut_only_leg_shows_cut_tab_only()
    {
        var (print, cut) = SettingProcessScope.FromLegKinds(new[] { "CUT" });
        Assert.False(print);
        Assert.True(cut);
    }

    [Fact]
    public void Separate_print_and_cut_legs_show_both()
    {
        var (print, cut) = SettingProcessScope.FromLegKinds(new[] { "PRINT", "CUT" });
        Assert.True(print);
        Assert.True(cut);
    }

    [Fact]
    public void Empty_plan_falls_back_to_both()
    {
        var (print, cut) = SettingProcessScope.FromLegKinds(System.Array.Empty<string>());
        Assert.True(print);
        Assert.True(cut);
    }

    [Fact]
    public void Only_tape_or_assembly_semis_fall_back_to_both()
    {
        // TAPE / ASSEMBLY don't participate in the print/cut split — don't hide
        // a tab we can't classify.
        var (print, cut) = SettingProcessScope.FromLegKinds(new[] { "TAPE", "ASSEMBLY" });
        Assert.True(print);
        Assert.True(cut);
    }

    [Fact]
    public void Kind_matching_is_case_insensitive()
    {
        var (print, cut) = SettingProcessScope.FromLegKinds(new[] { "print_cut" });
        Assert.True(print);
        Assert.True(cut);
    }
}
