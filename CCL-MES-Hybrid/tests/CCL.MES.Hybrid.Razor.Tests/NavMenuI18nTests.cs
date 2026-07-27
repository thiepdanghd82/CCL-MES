using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client.Auth;
using CCL.MES.Hybrid.Client.Hardware;
using CCL.MES.Hybrid.Client.Localization;
using CCL.MES.Hybrid.Client.RecentScans;
using CCL.MES.Hybrid.Razor.Shared;
using CCL.MES.Hybrid.Razor.Tests._Support;
using CCL.MES.Shared.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// i18n Phase-2 (2A) — proves the picker takes effect LIVE: flipping
/// ILanguageService re-renders LocalizedComponentBase-derived NavMenu into
/// the new language WITHOUT a page reload. This is the mechanism the whole
/// migration rides on.
/// </summary>
public sealed class NavMenuI18nTests : TestContext
{
    private readonly InMemoryLanguageService _lang = new();

    private void Wire()
    {
        var session = new StubAuthSession();
        session.SetUser("admin-user", "Admin");
        Services.AddSingleton<IAuthSession>(session);
        Services.AddSingleton<ILanguageService>(_lang);
        Services.AddSingleton<ITranslationCatalog, TranslationCatalog>();
        Services.AddSingleton<ITranslator, Translator>();
        Services.AddSingleton<IRecentScansService, InMemoryRecentScansService>();
        Services.AddSingleton<IOptions<HardwareOptions>>(
            Options.Create(new HardwareOptions { ScanEnabled = true }));
        JSInterop.Mode = JSRuntimeMode.Loose;
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("admin-user");
        auth.SetRoles("Admin");
    }

    [Fact]
    public void Defaults_to_Vietnamese()
    {
        Wire();
        var cut = RenderComponent<NavMenu>();
        Assert.Contains("Trang chủ", cut.Markup);          // nav.home VI
        Assert.Contains("Kho bán thành phẩm", cut.Markup);  // nav.semiproducts VI
        Assert.DoesNotContain(">Home<", cut.Markup);
    }

    [Fact]
    public void Switching_to_English_re_renders_live()
    {
        Wire();
        var cut = RenderComponent<NavMenu>();
        Assert.Contains("Trang chủ", cut.Markup);

        _lang.Set(LanguageCode.English);                    // flip language

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(">Home<", cut.Markup);          // nav.home EN
            Assert.Contains("Semi-Finished Store", cut.Markup);
            Assert.DoesNotContain("Trang chủ", cut.Markup); // VI gone
        });
    }
}
