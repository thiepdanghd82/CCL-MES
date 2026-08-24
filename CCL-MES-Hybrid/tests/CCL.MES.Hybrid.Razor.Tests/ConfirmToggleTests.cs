using Bunit;
using CCL.MES.Hybrid.Client.Localization;
using CCL.MES.Hybrid.Razor.Shared;
using CCL.MES.Shared.Localization;
using CCL.MES.Shared.Prepress;
using CCL.MES.Shared.ReasonCodes;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// L52 — bUnit render tests for the shared <see cref="ConfirmToggle"/>
/// segmented OK/NG control, plus one Prepress-surface fixture proving the
/// refactor wires it in (WoPlateCheck). Rule 4: plain &lt;button&gt; only.
/// </summary>
public sealed class ConfirmToggleTests : TestContext
{
    private void Wire()
    {
        Services.AddSingleton<ILanguageService, InMemoryLanguageService>();
        Services.AddSingleton<ITranslationCatalog, TranslationCatalog>();
        Services.AddSingleton<ITranslator, Translator>();
    }

    // ── state → class + aria-pressed ────────────────────────────────

    [Theory]
    [InlineData("Pending", "is-pending", "false", "false")]
    [InlineData("Ok",      "is-ok",      "true",  "false")]
    [InlineData("Ng",      "is-ng",      "false", "true")]
    public void Status_drives_state_class_and_aria_pressed(
        string status, string stateClass, string okPressed, string ngPressed)
    {
        Wire();
        var cut = RenderComponent<ConfirmToggle>(p => p
            .Add(x => x.Status, status)
            .Add(x => x.TestIdPrefix, "t"));

        var wrap = cut.Find("[data-testid='t-confirm']");
        Assert.Contains(stateClass, wrap.GetAttribute("class"));
        Assert.Equal(okPressed, cut.Find("[data-testid='t-ok']").GetAttribute("aria-pressed"));
        Assert.Equal(ngPressed, cut.Find("[data-testid='t-ng']").GetAttribute("aria-pressed"));
    }

    [Fact]
    public void Default_labels_are_OK_and_NG()
    {
        Wire();
        var cut = RenderComponent<ConfirmToggle>(p => p
            .Add(x => x.Status, "Pending")
            .Add(x => x.TestIdPrefix, "t"));

        Assert.Equal("OK", cut.Find("[data-testid='t-ok']").TextContent.Trim());
        Assert.Equal("NG", cut.Find("[data-testid='t-ng']").TextContent.Trim());
    }

    // ── callbacks ───────────────────────────────────────────────────

    [Fact]
    public void OnOk_and_OnNg_fire_on_their_cell()
    {
        Wire();
        var okFired = false;
        var ngFired = false;
        var cut = RenderComponent<ConfirmToggle>(p => p
            .Add(x => x.Status, "Pending")
            .Add(x => x.TestIdPrefix, "t")
            .Add(x => x.OnOk, EventCallback.Factory.Create(this, () => okFired = true))
            .Add(x => x.OnNg, EventCallback.Factory.Create(this, () => ngFired = true)));

        cut.Find("[data-testid='t-ok']").Click();
        Assert.True(okFired);
        Assert.False(ngFired);

        cut.Find("[data-testid='t-ng']").Click();
        Assert.True(ngFired);
    }

    // ── disable matrix ──────────────────────────────────────────────

    [Fact]
    public void Disabled_disables_both_cells()
    {
        Wire();
        var cut = RenderComponent<ConfirmToggle>(p => p
            .Add(x => x.Status, "Pending")
            .Add(x => x.Disabled, true)
            .Add(x => x.TestIdPrefix, "t"));

        Assert.True(cut.Find("[data-testid='t-ok']").HasAttribute("disabled"));
        Assert.True(cut.Find("[data-testid='t-ng']").HasAttribute("disabled"));
    }

    [Fact]
    public void OkDisabled_and_NgDisabled_are_independent()
    {
        Wire();
        var cut = RenderComponent<ConfirmToggle>(p => p
            .Add(x => x.Status, "Ok")
            .Add(x => x.OkDisabled, true)
            .Add(x => x.NgDisabled, false)
            .Add(x => x.TestIdPrefix, "t"));

        Assert.True(cut.Find("[data-testid='t-ok']").HasAttribute("disabled"));
        Assert.False(cut.Find("[data-testid='t-ng']").HasAttribute("disabled"));
    }

    [Fact]
    public void NgTitle_renders_on_the_ng_cell()
    {
        Wire();
        var cut = RenderComponent<ConfirmToggle>(p => p
            .Add(x => x.Status, "Pending")
            .Add(x => x.NgDisabled, true)
            .Add(x => x.NgTitle, "catalog empty")
            .Add(x => x.TestIdPrefix, "t"));

        Assert.Equal("catalog empty", cut.Find("[data-testid='t-ng']").GetAttribute("title"));
    }

    // ── Prepress surface fixture — WoPlateCheck now uses ConfirmToggle ─

    [Fact]
    public void WoPlateCheck_renders_ConfirmToggle_in_ok_state()
    {
        Wire();
        var cut = RenderComponent<WoPlateCheck>(p => p
            .Add(x => x.Row, new PrepressPlateRow { Id = 1, Status = "Ok" })
            .Add(x => x.ScrapReasons, new List<ReasonCodeOption>
            {
                new() { Code = "SC-PLATE-WORN", LabelVi = "Bản mòn" },
            }));

        var wrap = cut.Find("[data-testid='plate-btn-confirm']");
        Assert.Contains("is-ok", wrap.GetAttribute("class"));
        Assert.Equal("true", cut.Find("[data-testid='plate-btn-ok']").GetAttribute("aria-pressed"));
    }

    [Fact]
    public void WoPlateCheck_disables_NG_cell_when_scrap_catalog_empty()
    {
        Wire();
        var cut = RenderComponent<WoPlateCheck>(p => p
            .Add(x => x.Row, new PrepressPlateRow { Id = 1, Status = "Pending" })
            .Add(x => x.ScrapReasons, new List<ReasonCodeOption>()));

        var ng = cut.Find("[data-testid='plate-btn-ng']");
        Assert.True(ng.HasAttribute("disabled"),
            "Empty scrap catalog → operator must not be able to arm NG (L17).");
    }
}
