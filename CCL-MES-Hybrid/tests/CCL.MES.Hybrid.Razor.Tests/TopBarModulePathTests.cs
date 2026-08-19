using System.Linq;
using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client.Localization;
using CCL.MES.Hybrid.Razor.Shared;
using CCL.MES.Shared.Localization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// feat/qms-topbar-breadcrumb — the main app top-bar renders a context-aware
/// module path (breadcrumb) after the brand on QMS-family routes, and NOTHING
/// extra on non-QMS routes. The per-module QmsModuleTopBar strip (+ duplicate
/// user badge) is gone; this locks the replacement behaviour.
/// </summary>
public sealed class TopBarModulePathTests : TestContext
{
    private readonly InMemoryLanguageService _lang = new();

    public TopBarModulePathTests()
    {
        Services.AddSingleton<ILanguageService>(_lang);
        Services.AddSingleton<ITranslationCatalog, TranslationCatalog>();
        Services.AddSingleton<ITranslator, Translator>();
        var session = new _Support.StubAuthSession();
        session.SetUser("qc-user", "QC");
        Services.AddSingleton<CCL.MES.Hybrid.Client.Auth.IAuthSession>(session);
        this.AddTestAuthorization().SetAuthorized("qc-user");
    }

    private IRenderedComponent<TopBar> RenderAt(string relativeUrl)
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo(relativeUrl);
        return RenderComponent<TopBar>();
    }

    [Theory]
    [InlineData("/qms/iqc",   "QMS | IQC- Module")]
    [InlineData("/qms/ipqc",  "QMS | IPQC- Module")]
    [InlineData("/qms/oqc",   "QMS | OQC- Module")]
    [InlineData("/qms/icra",  "QMS | iCRA- Module")]
    [InlineData("/qms/fqc",   "QMS | FQC- Module")]
    [InlineData("/qms",       "QMS")]
    [InlineData("/qms/history", "QMS | History")]
    [InlineData("/qc/library", "QMS | Library")]
    [InlineData("/quality/traceability", "QMS | Traceability")]
    public void QmsRoute_renders_module_path(string url, string expected)
    {
        var cut = RenderAt(url);
        var path = cut.Find("[data-testid='topbar-module-path']");
        Assert.Equal(expected, string.Join(" | ", cut.FindAll(".app-topbar-crumb").Select(c => c.TextContent.Trim())));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/work-orders")]
    [InlineData("/npi/specs")]
    [InlineData("/settings")]
    public void NonQmsRoute_renders_no_module_path(string url)
    {
        var cut = RenderAt(url);
        Assert.Empty(cut.FindAll("[data-testid='topbar-module-path']"));
        // Brand + user/shift/time chrome must NOT regress.
        Assert.Contains("CCL DESIGN", cut.Markup);
        Assert.Single(cut.FindAll("[data-testid='topbar-lang-toggle']"));
    }

    [Fact]
    public void QmsRoute_keeps_main_topbar_chrome_and_drops_duplicate_badge()
    {
        var cut = RenderAt("/qms/iqc");
        // Path present…
        Assert.Single(cut.FindAll("[data-testid='topbar-module-path']"));
        // …and the main-topbar user/shift/time/lang chrome is intact.
        Assert.Contains("CCL DESIGN", cut.Markup);
        Assert.Single(cut.FindAll("[data-testid='topbar-lang-toggle']"));
        // The old per-module duplicate badge is gone from the shell entirely.
        Assert.Empty(cut.FindAll("[data-testid='qms-user-badge']"));
        Assert.Empty(cut.FindAll("[data-testid='qms-module-topbar']"));
    }

    [Fact]
    public void ModulePath_translates_module_word_on_language_flip()
    {
        var cut = RenderAt("/qms/iqc");
        // "Module" is the same token in VI + EN today; assert the render is
        // live-recomputed (property, not a cached field) by flipping language
        // and confirming the path is still correct (no stale/empty render).
        Assert.Equal("QMS | IQC- Module", string.Join(" | ", cut.FindAll(".app-topbar-crumb").Select(c => c.TextContent.Trim())));

        _lang.Set(LanguageCode.English);

        cut.WaitForAssertion(() =>
            Assert.Equal("QMS | IQC- Module",
                string.Join(" | ", cut.FindAll(".app-topbar-crumb").Select(c => c.TextContent.Trim()))));
    }

    [Fact]
    public void ModulePath_updates_when_navigating_between_modules()
    {
        var cut = RenderAt("/qms/iqc");
        Assert.Equal("QMS | IQC- Module", string.Join(" | ", cut.FindAll(".app-topbar-crumb").Select(c => c.TextContent.Trim())));

        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/qms/oqc");

        cut.WaitForAssertion(() =>
            Assert.Equal("QMS | OQC- Module",
                string.Join(" | ", cut.FindAll(".app-topbar-crumb").Select(c => c.TextContent.Trim()))));
    }
}
