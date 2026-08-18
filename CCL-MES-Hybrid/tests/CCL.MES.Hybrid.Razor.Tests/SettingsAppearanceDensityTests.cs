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
/// CCL iX — công tắc mật độ hiển thị (office / shopfloor).
///
/// Vì sao có test này: <c>shopfloor</c> không phải sở thích thẩm mỹ mà là ràng
/// buộc VẬT LÝ — người đứng máy đeo găng cần vùng chạm ≥44px và chữ ≥16px
/// (ix.css §9). Nếu công tắc âm thầm hỏng, giao diện vẫn "trông ổn" trên màn
/// hình kỹ sư nhưng bấm không trúng ở xưởng — đúng loại lỗi không ai báo.
///
/// Trạng thái sống trong <c>window.cclMesDensity</c> (js/density.js) chứ không
/// trong C#, nên test khẳng định qua LỜI GỌI JS thật (bUnit JSInterop), không
/// qua field nội bộ.
/// </summary>
public sealed class SettingsAppearanceDensityTests : TestContext
{
    private InMemoryLanguageService Wire(string densityFromStorage = "office")
    {
        var lang = new InMemoryLanguageService();
        Services.AddSingleton<ILanguageService>(lang);
        Services.AddSingleton<ITranslationCatalog, TranslationCatalog>();
        Services.AddSingleton<ITranslator, Translator>();

        // Strict: mọi lời gọi JS phải được khai báo trước ⇒ nếu component gọi
        // một hàm không tồn tại, test đỏ thay vì im lặng.
        JSInterop.Mode = JSRuntimeMode.Strict;
        JSInterop.Setup<string>("cclMesDensity.get").SetResult(densityFromStorage);
        JSInterop.SetupVoid("cclMesDensity.set", _ => true);

        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("qc-user");
        auth.SetRoles("QC");
        return lang;
    }

    [Fact]
    public void Renders_both_density_cards_localised_vi()
    {
        Wire();
        var cut = RenderComponent<SettingsAppearance>();

        Assert.Contains("Mật độ hiển thị", cut.Markup);
        Assert.Contains("Văn phòng", cut.Markup);
        Assert.Contains("Xưởng", cut.Markup);
        // Lý do tồn tại của chế độ xưởng phải hiển thị cho người chọn.
        Assert.Contains("44px", cut.Markup);
    }

    [Fact]
    public void Density_labels_follow_language_switch()
    {
        var lang = Wire();
        var cut = RenderComponent<SettingsAppearance>();
        Assert.Contains("Văn phòng", cut.Markup);

        lang.Set(LanguageCode.English);

        cut.WaitForAssertion(() => Assert.Contains("Shop floor", cut.Markup));
        Assert.Contains("Display density", cut.Markup);
    }

    [Fact]
    public void Office_is_the_default_selection()
    {
        Wire(densityFromStorage: "office");
        var cut = RenderComponent<SettingsAppearance>();

        var office = cut.Find("[data-testid='density-office']");
        var shop = cut.Find("[data-testid='density-shopfloor']");
        Assert.Equal("true", office.GetAttribute("aria-checked"));
        Assert.Equal("false", shop.GetAttribute("aria-checked"));
    }

    [Fact]
    public void Stored_shopfloor_preference_is_reflected_after_first_render()
    {
        Wire(densityFromStorage: "shopfloor");
        var cut = RenderComponent<SettingsAppearance>();

        cut.WaitForAssertion(() =>
            Assert.Equal("true", cut.Find("[data-testid='density-shopfloor']").GetAttribute("aria-checked")));
    }

    [Fact]
    public void Choosing_shopfloor_persists_through_js()
    {
        Wire();
        var cut = RenderComponent<SettingsAppearance>();

        cut.Find("[data-testid='density-shopfloor']").Click();

        var call = Assert.Single(JSInterop.Invocations["cclMesDensity.set"]);
        Assert.Equal("shopfloor", Assert.Single(call.Arguments));
        cut.WaitForAssertion(() =>
            Assert.Equal("true", cut.Find("[data-testid='density-shopfloor']").GetAttribute("aria-checked")));
    }

    [Fact]
    public void Choosing_the_current_density_is_a_no_op()
    {
        Wire(densityFromStorage: "office");
        var cut = RenderComponent<SettingsAppearance>();

        cut.Find("[data-testid='density-office']").Click();

        // Idempotent: không ghi lại giá trị đang có (tránh nhiễu storage + render thừa).
        Assert.Empty(JSInterop.Invocations["cclMesDensity.set"]);
    }
}
