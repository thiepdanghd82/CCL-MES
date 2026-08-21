using CCL.MES.Api.Policies;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Entities;
using Xunit;

namespace CCL.MES.Api.Tests.Unit;

/// <summary>
/// Luật QUYẾT ĐỊNH FQC judgment — tách khỏi <c>WoQcReviewController</c> theo mẫu
/// L47. Trước khi tách, muốn kiểm phải dựng <c>WebApplicationFactory</c> + DB +
/// auth cho từng tổ hợp; ở dạng hàm thuần toàn bộ ma trận chạy trong vài
/// mili-giây. Các case dưới đây khoá: mã lỗi + message byte-identical, thứ tự
/// parse-trước-readiness / reason-sau-readiness (giữ bởi cấu trúc controller),
/// transition (pha + audit + freeze), và lý-do-chỉ-lưu-khi-Reject.
/// </summary>
public sealed class WoQcJudgmentPolicyTests
{
    // ── ExpectedPhaseForKind ─────────────────────────────────────────────

    [Fact]
    public void ExpectedPhaseForKind_FQC_maps_to_FQC_PENDING()
        => Assert.Equal("FQC_PENDING", WoQcJudgmentPolicy.ExpectedPhaseForKind("FQC"));

    [Fact]
    public void ExpectedPhaseForKind_OQC_maps_to_OQC_PENDING()
        => Assert.Equal("OQC_PENDING", WoQcJudgmentPolicy.ExpectedPhaseForKind("OQC"));

    [Fact]
    public void ExpectedPhaseForKind_any_other_maps_to_OQC_PENDING()
    {
        // normKind đã normalize về FQC/OQC nên chỉ có 2 giá trị thực; case này
        // khoá else-branch: bất kỳ giá trị nào ≠ "FQC" → OQC_PENDING.
        Assert.Equal("OQC_PENDING", WoQcJudgmentPolicy.ExpectedPhaseForKind("fqc")); // hoa-thường khác → else
        Assert.Equal("OQC_PENDING", WoQcJudgmentPolicy.ExpectedPhaseForKind("WHATEVER"));
    }

    // ── ParseJudgment ────────────────────────────────────────────────────

    [Fact]
    public void ParseJudgment_Pass_is_valid()
    {
        var p = WoQcJudgmentPolicy.ParseJudgment("Pass");
        Assert.True(p.IsValid);
        Assert.Equal(WoQcJudgment.Pass, p.Judgment);
        Assert.Null(p.ErrorCode);
    }

    [Fact]
    public void ParseJudgment_Reject_is_valid_case_insensitive()
    {
        var p = WoQcJudgmentPolicy.ParseJudgment("reject");
        Assert.True(p.IsValid);
        Assert.Equal(WoQcJudgment.Reject, p.Judgment);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseJudgment_null_or_blank_yields_required_message(string? raw)
    {
        var p = WoQcJudgmentPolicy.ParseJudgment(raw);
        Assert.False(p.IsValid);
        Assert.Equal("qc.invalid_judgment", p.ErrorCode);
        Assert.Equal("Judgment is required (\"Pass\" or \"Reject\").", p.Message);
    }

    [Fact]
    public void ParseJudgment_Pending_is_rejected_with_must_be_message()
    {
        var p = WoQcJudgmentPolicy.ParseJudgment("Pending");
        Assert.False(p.IsValid);
        Assert.Equal("qc.invalid_judgment", p.ErrorCode);
        Assert.Equal("Judgment must be Pass or Reject; got \"Pending\".", p.Message);
    }

    [Fact]
    public void ParseJudgment_unparseable_is_rejected_with_raw_echoed()
    {
        var p = WoQcJudgmentPolicy.ParseJudgment("Maybe");
        Assert.False(p.IsValid);
        Assert.Equal("qc.invalid_judgment", p.ErrorCode);
        Assert.Equal("Judgment must be Pass or Reject; got \"Maybe\".", p.Message);
    }

    // ── ValidateRejectReason ─────────────────────────────────────────────

    [Fact]
    public void ValidateRejectReason_Pass_never_requires_reason()
    {
        Assert.Null(WoQcJudgmentPolicy.ValidateRejectReason(WoQcJudgment.Pass, null));
        Assert.Null(WoQcJudgmentPolicy.ValidateRejectReason(WoQcJudgment.Pass, "anything"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateRejectReason_Reject_missing_reason_fails(string? reason)
    {
        var err = WoQcJudgmentPolicy.ValidateRejectReason(WoQcJudgment.Reject, reason);
        Assert.NotNull(err);
        Assert.Equal("qc.invalid_reason", err!.Value.ErrorCode);
        Assert.Equal("JudgmentReason is required (1-500 chars) for Reject.", err.Value.Message);
    }

    [Fact]
    public void ValidateRejectReason_Reject_over_500_chars_fails()
    {
        var err = WoQcJudgmentPolicy.ValidateRejectReason(WoQcJudgment.Reject, new string('x', 501));
        Assert.NotNull(err);
        Assert.Equal("qc.invalid_reason", err!.Value.ErrorCode);
    }

    [Fact]
    public void ValidateRejectReason_Reject_valid_reason_passes()
    {
        Assert.Null(WoQcJudgmentPolicy.ValidateRejectReason(WoQcJudgment.Reject, "misaligned print"));
        Assert.Null(WoQcJudgmentPolicy.ValidateRejectReason(WoQcJudgment.Reject, new string('x', 500))); // boundary
    }

    // ── Transition ───────────────────────────────────────────────────────

    [Fact]
    public void Transition_Pass_goes_to_OQC_PENDING_with_judgment_audit_and_freeze()
    {
        var t = WoQcJudgmentPolicy.Transition(WoQcJudgment.Pass);
        Assert.Equal("OQC_PENDING", t.NextPhase);
        Assert.Equal(AuditAction.WoFqcJudgment, t.AuditAction);
        Assert.True(t.FreezeOnPass);
    }

    [Fact]
    public void Transition_Reject_goes_to_PREPRESS_with_reject_audit_and_no_freeze()
    {
        var t = WoQcJudgmentPolicy.Transition(WoQcJudgment.Reject);
        Assert.Equal("PREPRESS", t.NextPhase);
        Assert.Equal(AuditAction.WoFqcRejectToPrepress, t.AuditAction);
        Assert.False(t.FreezeOnPass);
    }

    // ── PersistedReason ──────────────────────────────────────────────────

    [Fact]
    public void PersistedReason_Pass_is_null_even_if_reason_supplied()
        => Assert.Null(WoQcJudgmentPolicy.PersistedReason(WoQcJudgment.Pass, "ignored"));

    [Fact]
    public void PersistedReason_Reject_keeps_reason()
        => Assert.Equal("bad cut", WoQcJudgmentPolicy.PersistedReason(WoQcJudgment.Reject, "bad cut"));
}
