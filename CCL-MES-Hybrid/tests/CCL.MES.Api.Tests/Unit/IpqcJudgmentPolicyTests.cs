using CCL.MES.Api.Policies;
using CCL.MES.Domain;
using Xunit;

namespace CCL.MES.Api.Tests.Unit;

/// <summary>
/// Luật QUYẾT ĐỊNH IPQC judgment — tách khỏi <c>IpqcReviewController</c> theo mẫu
/// <see cref="WoQcJudgmentPolicy"/>. Trước khi tách, muốn kiểm phải dựng
/// <c>WebApplicationFactory</c> + DB + auth cho từng tổ hợp; ở dạng hàm thuần
/// toàn bộ ma trận chạy trong vài mili-giây. Các case dưới đây khoá: mã lỗi +
/// message byte-identical, thứ tự parse-trước-readiness / special-reason-sau-
/// consistency (giữ bởi cấu trúc controller), transition (pha + freeze-on-GoRun),
/// và lý-do-chỉ-bắt-buộc-khi-SpecialAccept.
/// </summary>
public sealed class IpqcJudgmentPolicyTests
{
    // ── ParseJudgment ────────────────────────────────────────────────────

    [Fact]
    public void ParseJudgment_GoRun_is_valid()
    {
        var p = IpqcJudgmentPolicy.ParseJudgment("GoRun");
        Assert.True(p.IsValid);
        Assert.Equal(IpqcJudgment.GoRun, p.Judgment);
        Assert.Null(p.ErrorCode);
    }

    [Fact]
    public void ParseJudgment_StopLine_is_valid_case_insensitive()
    {
        var p = IpqcJudgmentPolicy.ParseJudgment("stopline");
        Assert.True(p.IsValid);
        Assert.Equal(IpqcJudgment.StopLine, p.Judgment);
    }

    [Fact]
    public void ParseJudgment_SpecialAccept_is_valid_case_insensitive()
    {
        var p = IpqcJudgmentPolicy.ParseJudgment("SPECIALACCEPT");
        Assert.True(p.IsValid);
        Assert.Equal(IpqcJudgment.SpecialAccept, p.Judgment);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseJudgment_null_or_blank_yields_required_message(string? raw)
    {
        var p = IpqcJudgmentPolicy.ParseJudgment(raw);
        Assert.False(p.IsValid);
        Assert.Equal("ipqc.invalid_judgment", p.ErrorCode);
        Assert.Equal("Judgment is required (\"GoRun\", \"StopLine\", or \"SpecialAccept\").",
            p.ErrorMessage);
    }

    [Fact]
    public void ParseJudgment_Pending_is_rejected_with_must_be_message()
    {
        var p = IpqcJudgmentPolicy.ParseJudgment("Pending");
        Assert.False(p.IsValid);
        Assert.Equal("ipqc.invalid_judgment", p.ErrorCode);
        Assert.Equal("Judgment must be GoRun / StopLine / SpecialAccept; got \"Pending\".",
            p.ErrorMessage);
    }

    [Fact]
    public void ParseJudgment_unparseable_is_rejected_with_raw_echoed()
    {
        var p = IpqcJudgmentPolicy.ParseJudgment("Maybe");
        Assert.False(p.IsValid);
        Assert.Equal("ipqc.invalid_judgment", p.ErrorCode);
        Assert.Equal("Judgment must be GoRun / StopLine / SpecialAccept; got \"Maybe\".",
            p.ErrorMessage);
    }

    // ── ValidateSpecialAcceptReason ──────────────────────────────────────

    [Fact]
    public void ValidateSpecialAcceptReason_GoRun_never_requires_reason()
    {
        Assert.Null(IpqcJudgmentPolicy.ValidateSpecialAcceptReason(IpqcJudgment.GoRun, null));
        Assert.Null(IpqcJudgmentPolicy.ValidateSpecialAcceptReason(IpqcJudgment.GoRun, "anything"));
    }

    [Fact]
    public void ValidateSpecialAcceptReason_StopLine_never_requires_reason()
    {
        Assert.Null(IpqcJudgmentPolicy.ValidateSpecialAcceptReason(IpqcJudgment.StopLine, null));
        Assert.Null(IpqcJudgmentPolicy.ValidateSpecialAcceptReason(IpqcJudgment.StopLine, "anything"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateSpecialAcceptReason_SpecialAccept_missing_reason_fails(string? reason)
    {
        var err = IpqcJudgmentPolicy.ValidateSpecialAcceptReason(IpqcJudgment.SpecialAccept, reason);
        Assert.NotNull(err);
        Assert.Equal("ipqc.invalid_special_accept_reason", err!.Value.ErrorCode);
        Assert.Equal("SpecialAcceptReason is required (1-500 chars) for SpecialAccept judgment.",
            err.Value.Message);
    }

    [Fact]
    public void ValidateSpecialAcceptReason_SpecialAccept_over_500_chars_fails()
    {
        var err = IpqcJudgmentPolicy.ValidateSpecialAcceptReason(
            IpqcJudgment.SpecialAccept, new string('x', 501));
        Assert.NotNull(err);
        Assert.Equal("ipqc.invalid_special_accept_reason", err!.Value.ErrorCode);
    }

    [Fact]
    public void ValidateSpecialAcceptReason_SpecialAccept_valid_reason_passes()
    {
        Assert.Null(IpqcJudgmentPolicy.ValidateSpecialAcceptReason(
            IpqcJudgment.SpecialAccept, "customer waived defect"));
        Assert.Null(IpqcJudgmentPolicy.ValidateSpecialAcceptReason(
            IpqcJudgment.SpecialAccept, new string('x', 500))); // boundary
    }

    // ── Transition ───────────────────────────────────────────────────────

    [Fact]
    public void Transition_GoRun_goes_to_IPQC_APPROVED_with_freeze()
    {
        var t = IpqcJudgmentPolicy.Transition(IpqcJudgment.GoRun);
        Assert.Equal("IPQC_APPROVED", t.NextPhase);
        Assert.True(t.FreezeOnGoRun);
    }

    [Fact]
    public void Transition_StopLine_goes_to_PREPRESS_without_freeze()
    {
        var t = IpqcJudgmentPolicy.Transition(IpqcJudgment.StopLine);
        Assert.Equal("PREPRESS", t.NextPhase);
        Assert.False(t.FreezeOnGoRun);
    }

    [Fact]
    public void Transition_SpecialAccept_goes_to_QA_PENDING_without_freeze()
    {
        var t = IpqcJudgmentPolicy.Transition(IpqcJudgment.SpecialAccept);
        Assert.Equal("QA_PENDING", t.NextPhase);
        Assert.False(t.FreezeOnGoRun);
    }

    [Fact]
    public void Transition_only_GoRun_freezes()
    {
        // Freeze-on-GoRun invariant: đúng 1 nhánh freeze, khoá tương đương
        // predicate cũ `judgment == GoRun` trong controller.
        Assert.True(IpqcJudgmentPolicy.Transition(IpqcJudgment.GoRun).FreezeOnGoRun);
        Assert.False(IpqcJudgmentPolicy.Transition(IpqcJudgment.StopLine).FreezeOnGoRun);
        Assert.False(IpqcJudgmentPolicy.Transition(IpqcJudgment.SpecialAccept).FreezeOnGoRun);
    }

    [Fact]
    public void Transition_Pending_fallback_keeps_phase_unchanged_and_no_freeze()
    {
        // ParseJudgment loại Pending trước khi tới Transition; nhánh mặc định trả
        // NextPhase rỗng để controller GIỮ NGUYÊN wo.MesPhase (semantics `_` cũ).
        var t = IpqcJudgmentPolicy.Transition(IpqcJudgment.Pending);
        Assert.Equal("", t.NextPhase);
        Assert.False(t.FreezeOnGoRun);
    }
}
