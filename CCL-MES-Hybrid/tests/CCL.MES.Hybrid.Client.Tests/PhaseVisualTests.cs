using CCL.MES.Hybrid.Client.Status;
using Xunit;

namespace CCL.MES.Hybrid.Client.Tests;

/// <summary>
/// Khoá bảng màu trạng thái. Trước khi có <see cref="PhaseVisual"/>, map phase →
/// màu nằm rải rác ≥4 kiểu (class cứng trong 6 dashboard, 2 switch cục bộ, 1
/// inline style hex thô), nên cùng một PAUSED có thể ra hai màu ở hai màn hình
/// và người đứng máy phải học lại bảng màu mỗi lần đổi trang.
///
/// Ý nghĩa vận hành của từng bậc — đây mới là thứ test này bảo vệ, không phải
/// mã màu:
///   Warn  = ĐANG CHỜ một người cụ thể ra tay
///   Alarm = cần QUYẾT ĐỊNH / đã hỏng, không tự trôi
///   Ok    = đang chạy / đã duyệt / đã xong
/// </summary>
public sealed class PhaseVisualTests
{
    [Theory]
    // WO — chưa khởi động
    [InlineData("NEW", PhaseTone.Neutral)]
    // WO — đang trôi, chưa cần ai
    [InlineData("PREPRESS", PhaseTone.Info)]
    [InlineData("SPLIT", PhaseTone.Info)]
    [InlineData("SETTING", PhaseTone.Info)]
    // đang CHỜ người
    [InlineData("IPQC_WAIT", PhaseTone.Warn)]
    [InlineData("FQC_PENDING", PhaseTone.Warn)]
    [InlineData("OQC_PENDING", PhaseTone.Warn)]
    [InlineData("PAUSED", PhaseTone.Warn)]
    // cần QUYẾT ĐỊNH
    [InlineData("QA_PENDING", PhaseTone.Alarm)]
    [InlineData("CANCELLED", PhaseTone.Alarm)]
    // tốt
    [InlineData("IPQC_APPROVED", PhaseTone.Ok)]
    [InlineData("RUNNING", PhaseTone.Ok)]
    [InlineData("DONE", PhaseTone.Ok)]
    [InlineData("SHIPPED", PhaseTone.Ok)]
    // leg-only
    [InlineData("LEG_DONE", PhaseTone.Ok)]
    public void Every_declared_phase_has_an_explicit_tone(string phase, PhaseTone expected)
        => Assert.Equal(expected, PhaseVisual.Tone(phase));

    [Theory]
    [InlineData("running")]
    [InlineData("  RUNNING  ")]
    [InlineData("Running")]
    public void Token_matching_is_case_and_whitespace_tolerant(string phase)
        => Assert.Equal(PhaseTone.Ok, PhaseVisual.Tone(phase));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("PHASE_KHONG_TON_TAI")]
    public void Unknown_phase_falls_back_to_neutral_not_a_guessed_colour(string? phase)
    {
        // Hiện SAI màu tệ hơn hiện KHÔNG màu: một phase mới chưa khai báo mà tự
        // đoán thành Ok có thể khiến người vận hành tưởng WO đang chạy bình thường.
        Assert.Equal(PhaseTone.Neutral, PhaseVisual.Tone(phase));
        Assert.Equal("ix-pill", PhaseVisual.CssClass(phase));
    }

    [Theory]
    [InlineData("NEW")] [InlineData("PREPRESS")] [InlineData("SPLIT")] [InlineData("SETTING")]
    [InlineData("IPQC_WAIT")] [InlineData("QA_PENDING")] [InlineData("IPQC_APPROVED")]
    [InlineData("RUNNING")] [InlineData("PAUSED")] [InlineData("FQC_PENDING")]
    [InlineData("OQC_PENDING")] [InlineData("DONE")] [InlineData("CANCELLED")]
    [InlineData("SHIPPED")] [InlineData("LEG_DONE")] [InlineData("gì đó lạ")]
    public void Css_class_always_goes_through_the_ix_pill_set(string phase)
    {
        var css = PhaseVisual.CssClass(phase);
        Assert.StartsWith("ix-pill", css);
        // Không được sinh class ngoài bộ 5 bậc — đó là cách bảng màu phình ra.
        Assert.All(css.Split(' '), c => Assert.Contains(c,
            new[] { "ix-pill", "ix-pill-info", "ix-pill-ok", "ix-pill-warn", "ix-pill-alarm" }));
    }

    [Theory]
    [InlineData("RUNNING", "legs.phase.running")]
    [InlineData("LEG_DONE", "legs.phase.leg_done")]
    [InlineData("PAUSED", "legs.phase.paused")]
    public void Leg_phases_resolve_to_the_existing_i18n_keys(string phase, string key)
        => Assert.Equal(key, PhaseVisual.LabelKey(phase));

    [Theory]
    [InlineData("SHIPPED")]
    [InlineData("QA_PENDING")]
    [InlineData("SPLIT")]
    public void Wo_only_phases_have_no_label_key_yet_so_the_raw_token_shows(string phase)
    {
        // CỐ Ý: bịa nhãn tiếng Việt cho 14 phase WO là quyết định về từ ngữ vận
        // hành, phải do người vận hành chốt. Tới lúc đó StatusPill hiện token thô
        // — đúng hành vi đang có, không đổi thầm chữ trên màn hình của ai cả.
        Assert.Null(PhaseVisual.LabelKey(phase));
    }
}
