using CCL.MES.Api.Policies;
using CCL.MES.Domain;
using Xunit;

namespace CCL.MES.Api.Tests.Unit;

/// <summary>
/// Luật KIỂM GIÁ TRỊ body PREPRESS — tách khỏi <c>PrepressController</c> theo mẫu
/// <see cref="IpqcJudgmentPolicy"/>. Trước khi tách, muốn kiểm status-parse +
/// NG-format phải dựng <c>WebApplicationFactory</c> + DB + auth cho 3 endpoint
/// (materials/plate/cutter); ở dạng hàm thuần cả ma trận chạy vài mili-giây. Các
/// case dưới đây KHOÁ mã lỗi + message byte-identical + thứ tự (status→reason→note).
/// </summary>
public sealed class PrepressPolicyTests
{
    // ── ParseStatus ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("Pending", PrepressCheckStatus.Pending)]
    [InlineData("Ok", PrepressCheckStatus.Ok)]
    [InlineData("Ng", PrepressCheckStatus.Ng)]
    [InlineData("ok", PrepressCheckStatus.Ok)]           // case-insensitive
    [InlineData("NG", PrepressCheckStatus.Ng)]
    public void ParseStatus_valid_returns_status(string raw, PrepressCheckStatus expected)
    {
        var p = PrepressPolicy.ParseStatus(raw);
        Assert.True(p.IsValid);
        Assert.Equal(expected, p.Status);
        Assert.Null(p.ErrorCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseStatus_null_or_blank_yields_required_message(string? raw)
    {
        var p = PrepressPolicy.ParseStatus(raw);
        Assert.False(p.IsValid);
        Assert.Equal("prepress.invalid_status", p.ErrorCode);
        Assert.Equal("Status is required (one of: Pending / Ok / Ng).", p.ErrorMessage);
    }

    [Fact]
    public void ParseStatus_unknown_uses_raw_in_message()
    {
        var p = PrepressPolicy.ParseStatus("Bogus");
        Assert.False(p.IsValid);
        Assert.Equal("prepress.invalid_status", p.ErrorCode);
        Assert.Equal("Status 'Bogus' is not one of Pending / Ok / Ng.", p.ErrorMessage);
    }

    // ── ValidateNgFormat ─────────────────────────────────────────────────

    [Theory]
    [InlineData(PrepressCheckStatus.Ok)]
    [InlineData(PrepressCheckStatus.Pending)]
    public void ValidateNgFormat_non_ng_is_always_null(PrepressCheckStatus status)
    {
        // Even with blank reason + note, a non-Ng status short-circuits to null
        // (controller then skips the catalog lookup entirely).
        Assert.Null(PrepressPolicy.ValidateNgFormat(status, null, null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateNgFormat_ng_blank_reason_yields_invalid_reason_code(string? reason)
    {
        var err = PrepressPolicy.ValidateNgFormat(PrepressCheckStatus.Ng, reason, "torn corner");
        Assert.NotNull(err);
        Assert.Equal("prepress.invalid_reason_code", err!.Value.ErrorCode);
        Assert.Equal("NgReasonCode is required when status=NG.", err.Value.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void ValidateNgFormat_ng_blank_note_yields_invalid_ng_note(string? note)
    {
        var err = PrepressPolicy.ValidateNgFormat(PrepressCheckStatus.Ng, "SC-MAT-DAMAGE", note);
        Assert.NotNull(err);
        Assert.Equal("prepress.invalid_ng_note", err!.Value.ErrorCode);
        Assert.Equal("NgNote must be 1-500 chars when status=NG.", err.Value.Message);
    }

    [Fact]
    public void ValidateNgFormat_ng_note_over_500_yields_invalid_ng_note()
    {
        var err = PrepressPolicy.ValidateNgFormat(
            PrepressCheckStatus.Ng, "SC-MAT-DAMAGE", new string('x', 501));
        Assert.Equal("prepress.invalid_ng_note", err!.Value.ErrorCode);
    }

    [Fact]
    public void ValidateNgFormat_ng_valid_returns_null()
    {
        Assert.Null(PrepressPolicy.ValidateNgFormat(
            PrepressCheckStatus.Ng, "SC-MAT-DAMAGE", "torn corner"));
        Assert.Null(PrepressPolicy.ValidateNgFormat(
            PrepressCheckStatus.Ng, "SC-MAT-DAMAGE", new string('x', 500)));
    }

    [Fact]
    public void ValidateNgFormat_blank_reason_takes_precedence_over_blank_note()
    {
        // Both blank on an Ng: reason check fires first (order preserved).
        var err = PrepressPolicy.ValidateNgFormat(PrepressCheckStatus.Ng, "", "");
        Assert.Equal("prepress.invalid_reason_code", err!.Value.ErrorCode);
    }
}
