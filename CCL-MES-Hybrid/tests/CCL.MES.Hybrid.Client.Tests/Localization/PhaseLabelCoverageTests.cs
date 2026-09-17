using CCL.MES.Domain.StateMachine;
using CCL.MES.Hybrid.Client.Localization;
using CCL.MES.Hybrid.Client.Status;
using CCL.MES.Shared.Localization;
using Xunit;

namespace CCL.MES.Hybrid.Client.Tests.Localization;

/// <summary>
/// Khoá lời hứa: người đứng máy KHÔNG BAO GIỜ phải đọc token thô.
///
/// <para><b>Vì sao cần, trong khi <c>PhaseVisualTests</c> đã kiểm map token →
/// key.</b> Test kia liệt kê 15 token bằng tay trong <c>[InlineData]</c>. Thêm
/// một phase thứ 16 vào <see cref="MesPhase"/> mà quên nhãn thì test kia vẫn
/// XANH — nó chỉ kiểm những gì đã được viết ra, không kiểm những gì tồn tại.
/// Test dưới đây duyệt thẳng <c>Enum.GetValues</c> nên không trốn được.</para>
///
/// <para>Và nó nối tiếp một mắt xích nữa mà <c>PhaseVisualTests</c> bỏ trống:
/// có key là một chuyện, key ấy CÓ CHỮ trong catalog lại là chuyện khác.
/// <c>Translator</c> trả về chính key khi tra trượt, nên một key thiếu chữ sẽ
/// hiện <c>legs.phase.ipqc_wait</c> lên màn hình — tệ hơn cả token thô.</para>
/// </summary>
public sealed class PhaseLabelCoverageTests
{
    /// <summary>LEG_DONE là token tầng leg, không nằm trong MesPhase, nhưng dùng
    /// chung bảng nhãn nên phải phủ cùng một luật.</summary>
    private const string LegDone = "LEG_DONE";

    private static IEnumerable<string> AllPhaseTokens() =>
        Enum.GetNames<MesPhase>().Append(LegDone);

    [Fact]
    public void Every_MesPhase_value_has_a_label_key()
    {
        var missing = AllPhaseTokens()
            .Where(t => PhaseVisual.LabelKey(t) is null)
            .ToList();

        Assert.True(missing.Count == 0,
            "Phase không có key nhãn — người đứng máy sẽ đọc token thô:\n  " +
            string.Join("\n  ", missing) +
            "\nThêm vào PhaseVisual.LabelKey + TranslationCatalog.Legs (đủ VI và EN).");
    }

    [Fact]
    public void Every_phase_label_key_has_both_languages_in_the_catalog()
    {
        var catalog = new TranslationCatalog();
        var offenders = new List<string>();

        foreach (var token in AllPhaseTokens())
        {
            var key = PhaseVisual.LabelKey(token);
            if (key is null) continue;   // đã có test riêng ở trên

            foreach (var lang in new[] { LanguageCode.Vietnamese, LanguageCode.English })
            {
                var s = catalog.Lookup(key, lang);
                if (string.IsNullOrWhiteSpace(s)) offenders.Add($"{token} → {key} [{lang}]");
            }
        }

        Assert.True(offenders.Count == 0,
            "Key nhãn phase thiếu chữ trong catalog (Translator sẽ hiện nguyên key ra màn hình):\n  " +
            string.Join("\n  ", offenders));
    }

    [Fact]
    public void Vietnamese_label_is_never_just_the_raw_token()
    {
        // Đây là chính cái bệnh đang chữa: dán "IPQC_WAIT" vào ô VI thì mọi test
        // đếm-số-key vẫn xanh mà người đứng máy vẫn không đọc được gì.
        var catalog = new TranslationCatalog();
        var offenders = new List<string>();

        foreach (var token in AllPhaseTokens())
        {
            var key = PhaseVisual.LabelKey(token);
            if (key is null) continue;

            var vi = catalog.Lookup(key, LanguageCode.Vietnamese);
            if (string.Equals(vi?.Trim(), token, StringComparison.OrdinalIgnoreCase))
                offenders.Add($"{token} → VI \"{vi}\"");
        }

        Assert.True(offenders.Count == 0,
            "Nhãn tiếng Việt vẫn là token thô:\n  " + string.Join("\n  ", offenders));
    }

    // ── từ vựng LEGACY ProcessStepCode (8 giá trị) ────────────────────────────
    // Băng-rôn "đã chuyển bước" in cột `CurrentStep`, nên tới khi cutover A1 xong
    // thì người đứng máy vẫn đọc nó mỗi lần chuyển bước. Duyệt thẳng enum, KHÔNG
    // liệt kê tay — thêm giá trị thứ 9 mà quên nhãn thì phải ĐỎ.

    [Fact]
    public void Every_legacy_ProcessStepCode_has_a_label_key()
    {
        var missing = Enum.GetNames<CCL.MES.Domain.ProcessStepCode>()
            .Where(n => PhaseVisual.LegacyStepLabelKey(n) is null)
            .ToList();

        Assert.True(missing.Count == 0,
            "ProcessStepCode không có key nhãn — băng-rôn chuyển bước sẽ in token thô:\n  " +
            string.Join("\n  ", missing) +
            "\nThêm vào PhaseVisual.LegacyStepLabelKey + TranslationCatalog.WorkOrders (đủ VI và EN).");
    }

    [Fact]
    public void Every_legacy_step_key_has_both_languages_and_is_not_the_raw_token()
    {
        var catalog = new TranslationCatalog();
        var offenders = new List<string>();

        foreach (var name in Enum.GetNames<CCL.MES.Domain.ProcessStepCode>())
        {
            var key = PhaseVisual.LegacyStepLabelKey(name);
            if (key is null) continue;   // đã có test riêng ở trên

            var vi = catalog.Lookup(key, LanguageCode.Vietnamese);
            var en = catalog.Lookup(key, LanguageCode.English);
            if (string.IsNullOrWhiteSpace(vi)) offenders.Add($"{name} → {key} [VI thiếu]");
            if (string.IsNullOrWhiteSpace(en)) offenders.Add($"{name} → {key} [EN thiếu]");
            if (string.Equals(vi?.Trim(), name, StringComparison.OrdinalIgnoreCase))
                offenders.Add($"{name} → VI vẫn là token thô \"{vi}\"");
        }

        Assert.True(offenders.Count == 0,
            "Nhãn bước legacy chưa dùng được:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void Running_resolves_through_the_CANONICAL_vocabulary_not_the_legacy_one()
    {
        // `Running` có ở CẢ HAI tập token và mang đúng cùng nghĩa. Thứ tự tra
        // phải để MesPhase thắng, để một trạng thái chỉ có MỘT chữ trên màn hình
        // bất kể server trả cột nào. Test này khoá đúng thứ tự đó.
        Assert.Equal("legs.phase.running", PhaseVisual.LabelKey("Running"));
        Assert.Equal("wo.legacystep.running", PhaseVisual.LegacyStepLabelKey("Running"));
    }

    [Theory]
    [InlineData("wo.step.prepress")]
    [InlineData("wo.step.setting")]
    [InlineData("wo.step.ipqc")]
    [InlineData("wo.step.readytorun")]
    [InlineData("wo.step.running")]
    [InlineData("wo.step.fqc")]
    [InlineData("wo.step.oqc")]
    public void Seven_step_stepper_labels_exist_in_both_languages(string key)
    {
        // Thanh 7 bước ở thẻ WO từng là string[] tiếng Anh khai cứng, nằm ngay
        // cạnh chip phase đã song ngữ. WorkOrders.razor nay giữ KEY chứ không
        // giữ chuỗi, nên nếu ai xoá key mà quên sửa mảng thì hỏng ở đây.
        var catalog = new TranslationCatalog();
        Assert.False(string.IsNullOrWhiteSpace(catalog.Lookup(key, LanguageCode.Vietnamese)));
        Assert.False(string.IsNullOrWhiteSpace(catalog.Lookup(key, LanguageCode.English)));
    }
}
