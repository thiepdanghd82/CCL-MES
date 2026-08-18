using Bunit;
using CCL.MES.Hybrid.Client.Localization;
using CCL.MES.Hybrid.Razor.Shared;
using CCL.MES.Hybrid.Razor.Tests._Support;
using CCL.MES.Shared.Localization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// StatusPill là NƠI DUY NHẤT quyết định trạng thái hiện màu gì. Test này khoá
/// đúng điều đó: trang gọi chỉ đưa phase vào, không được tự chọn màu.
/// </summary>
public sealed class StatusPillTests : TestContext
{
    private InMemoryLanguageService Wire()
    {
        var lang = new InMemoryLanguageService();
        Services.AddSingleton<ILanguageService>(lang);
        Services.AddSingleton<ITranslationCatalog, TranslationCatalog>();
        Services.AddSingleton<ITranslator, Translator>();
        return lang;
    }

    [Theory]
    [InlineData("RUNNING", "ix-pill-ok")]
    [InlineData("PAUSED", "ix-pill-warn")]
    [InlineData("QA_PENDING", "ix-pill-alarm")]
    [InlineData("SETTING", "ix-pill-info")]
    public void Tone_class_comes_from_the_phase_not_from_the_caller(string phase, string expected)
    {
        Wire();
        var cut = RenderComponent<StatusPill>(p => p.Add(x => x.Phase, phase));
        Assert.Contains(expected, cut.Markup);
        Assert.Contains($"data-phase=\"{phase}\"", cut.Markup);
    }

    [Fact]
    public void Neutral_phase_gets_the_bare_pill_with_no_tone_modifier()
    {
        Wire();
        var cut = RenderComponent<StatusPill>(p => p.Add(x => x.Phase, "NEW"));
        Assert.Contains("ix-pill", cut.Markup);
        Assert.DoesNotContain("ix-pill-ok", cut.Markup);
        Assert.DoesNotContain("ix-pill-warn", cut.Markup);
        Assert.DoesNotContain("ix-pill-alarm", cut.Markup);
        Assert.DoesNotContain("ix-pill-info", cut.Markup);
    }

    [Fact]
    public void Leg_phase_label_is_localised_and_follows_a_language_switch()
    {
        var lang = Wire();
        var cut = RenderComponent<StatusPill>(p => p.Add(x => x.Phase, "RUNNING"));
        var vi = cut.Markup;

        lang.Set(LanguageCode.English);
        cut.Render();

        Assert.NotEqual(vi, cut.Markup);   // nhãn đổi theo ngôn ngữ
        Assert.Contains("data-phase=\"RUNNING\"", cut.Markup);  // token thì không
    }

    [Fact]
    public void Wo_only_phase_shows_the_raw_token_unchanged()
    {
        // Chưa có bản dịch cho phase WO ⇒ hiện token thô. Đây là hành vi ĐANG CÓ
        // trên màn hình; component không được tự bịa chữ mới.
        Wire();
        var cut = RenderComponent<StatusPill>(p => p.Add(x => x.Phase, "SHIPPED"));
        Assert.Contains(">SHIPPED<", cut.Markup);
    }

    [Fact]
    public void Explicit_label_wins_over_the_i18n_lookup()
    {
        Wire();
        var cut = RenderComponent<StatusPill>(p => p
            .Add(x => x.Phase, "CANCELLED")
            .Add(x => x.Label, "Đã dừng"));
        Assert.Contains("Đã dừng", cut.Markup);
        Assert.Contains("ix-pill-alarm", cut.Markup);   // màu vẫn do phase quyết
    }

    [Fact]
    public void Test_id_is_forwarded_so_existing_selectors_keep_working()
    {
        Wire();
        var cut = RenderComponent<StatusPill>(p => p
            .Add(x => x.Phase, "RUNNING").Add(x => x.TestId, "leg-phase-2"));
        Assert.Contains("data-testid=\"leg-phase-2\"", cut.Markup);
    }
}
