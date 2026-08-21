using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client.Localization;
using CCL.MES.Hybrid.Razor.Pages;
using CCL.MES.Hybrid.Razor.Tests._Support;
using CCL.MES.Shared.Localization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// L42 — công cụ chỉnh cỡ chữ / UI (display scaling, giống Win/macOS).
///
/// Vì sao có test này: người dùng thấy chữ nhỏ trên màn hình lớn ⇒ cần phóng
/// nội dung mà KHÔNG vỡ khung. Hệ số sống trong <c>window.cclMesDensity</c>
/// (js/density.js) qua scaleGet/scaleSet chứ không trong C#, nên test khẳng
/// định qua LỜI GỌI JS thật (bUnit JSInterop), không qua field nội bộ. Giá trị
/// là hệ số thập phân dạng chuỗi ('0.9'…'1.5') áp thẳng vào --ui-scale.
/// </summary>
public sealed class SettingsAppearanceScaleTests : TestContext
{
    private static readonly string[] AllScaleValues = { "0.9", "1", "1.1", "1.25", "1.5" };

    private InMemoryLanguageService Wire(
        string densityFromStorage = "office",
        string scaleFromStorage = "1")
    {
        var lang = new InMemoryLanguageService();
        Services.AddSingleton<ILanguageService>(lang);
        Services.AddSingleton<ITranslationCatalog, TranslationCatalog>();
        Services.AddSingleton<ITranslator, Translator>();

        JSInterop.Mode = JSRuntimeMode.Strict;
        JSInterop.Setup<string>("cclMesDensity.get").SetResult(densityFromStorage);
        JSInterop.SetupVoid("cclMesDensity.set", _ => true);
        JSInterop.Setup<string>("cclMesDensity.scaleGet").SetResult(scaleFromStorage);
        JSInterop.SetupVoid("cclMesDensity.scaleSet", _ => true);

        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("qc-user");
        auth.SetRoles("QC");
        return lang;
    }

    [Fact]
    public void Renders_all_scale_steps()
    {
        Wire();
        var cut = RenderComponent<SettingsAppearance>();

        foreach (var v in AllScaleValues)
        {
            Assert.NotNull(cut.Find($"[data-testid='uiscale-{v}']"));
        }
        // Có nút Reset về 100%.
        Assert.NotNull(cut.Find("[data-testid='uiscale-reset']"));
    }

    [Fact]
    public void Scale_section_localised_vi()
    {
        Wire();
        var cut = RenderComponent<SettingsAppearance>();

        Assert.Contains("Cỡ chữ / UI", cut.Markup);
        Assert.Contains("Đặt lại 100%", cut.Markup);
        Assert.Contains("100%", cut.Markup);
        Assert.Contains("150%", cut.Markup);
    }

    [Fact]
    public void Scale_section_follows_language_switch()
    {
        var lang = Wire();
        var cut = RenderComponent<SettingsAppearance>();
        Assert.Contains("Cỡ chữ / UI", cut.Markup);

        lang.Set(LanguageCode.English);

        cut.WaitForAssertion(() => Assert.Contains("Text / UI size", cut.Markup));
        Assert.Contains("Reset to 100%", cut.Markup);
    }

    [Fact]
    public void Default_scale_is_100_percent()
    {
        Wire(scaleFromStorage: "1");
        var cut = RenderComponent<SettingsAppearance>();

        Assert.Equal("true", cut.Find("[data-testid='uiscale-1']").GetAttribute("aria-checked"));
        Assert.Equal("false", cut.Find("[data-testid='uiscale-1.25']").GetAttribute("aria-checked"));
    }

    [Fact]
    public void Stored_scale_preference_is_reflected_after_first_render()
    {
        Wire(scaleFromStorage: "1.25");
        var cut = RenderComponent<SettingsAppearance>();

        cut.WaitForAssertion(() =>
            Assert.Equal("true", cut.Find("[data-testid='uiscale-1.25']").GetAttribute("aria-checked")));
    }

    [Fact]
    public void Choosing_a_scale_step_calls_scaleSet_with_that_value()
    {
        Wire(scaleFromStorage: "1");
        var cut = RenderComponent<SettingsAppearance>();

        cut.Find("[data-testid='uiscale-1.5']").Click();

        var call = Assert.Single(JSInterop.Invocations["cclMesDensity.scaleSet"]);
        Assert.Equal("1.5", Assert.Single(call.Arguments));
        cut.WaitForAssertion(() =>
            Assert.Equal("true", cut.Find("[data-testid='uiscale-1.5']").GetAttribute("aria-checked")));
    }

    [Fact]
    public void Choosing_the_current_scale_is_a_no_op()
    {
        Wire(scaleFromStorage: "1");
        var cut = RenderComponent<SettingsAppearance>();

        cut.Find("[data-testid='uiscale-1']").Click();

        Assert.Empty(JSInterop.Invocations["cclMesDensity.scaleSet"]);
    }

    [Fact]
    public void Reset_button_returns_scale_to_100_percent()
    {
        Wire(scaleFromStorage: "1.5");
        var cut = RenderComponent<SettingsAppearance>();

        // Đợi trạng thái đồng bộ về 1.5 (nút reset bật) rồi bấm reset.
        cut.WaitForAssertion(() =>
            Assert.Equal("true", cut.Find("[data-testid='uiscale-1.5']").GetAttribute("aria-checked")));

        cut.Find("[data-testid='uiscale-reset']").Click();

        var call = Assert.Single(JSInterop.Invocations["cclMesDensity.scaleSet"]);
        Assert.Equal("1", Assert.Single(call.Arguments));
    }
}
