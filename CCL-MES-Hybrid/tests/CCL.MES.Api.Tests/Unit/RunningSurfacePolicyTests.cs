using CCL.MES.Api.Policies;
using CCL.MES.Shared.RunningSurface;
using Xunit;

namespace CCL.MES.Api.Tests.Unit;

/// <summary>
/// Luật KIỂM GIÁ TRỊ body bề mặt RUNNING — tách khỏi <c>RunningSurfaceController</c>
/// theo mẫu <see cref="IpqcJudgmentPolicy"/>. Trước khi tách, muốn kiểm mọi tổ hợp
/// delta/độ-dài phải dựng <c>WebApplicationFactory</c> + DB + auth; ở dạng hàm
/// thuần cả ma trận chạy trong vài mili-giây. Các case dưới đây KHOÁ mã lỗi +
/// message byte-identical với bản inline cũ + thứ tự kiểm (reason trước note).
/// </summary>
public sealed class RunningSurfacePolicyTests
{
    // ── ValidateQtyAdd ───────────────────────────────────────────────────

    [Theory]
    [InlineData(5, 0)]
    [InlineData(0, 3)]
    [InlineData(2, 1)]
    public void ValidateQtyAdd_valid_deltas_return_null(int done, int ng)
    {
        var err = RunningSurfacePolicy.ValidateQtyAdd(
            new RunQtyAddRequest { QtyDoneDelta = done, QtyNgDelta = ng });
        Assert.Null(err);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -2)]
    [InlineData(-3, -4)]
    public void ValidateQtyAdd_negative_delta_yields_invalid_qty_delta(int done, int ng)
    {
        var err = RunningSurfacePolicy.ValidateQtyAdd(
            new RunQtyAddRequest { QtyDoneDelta = done, QtyNgDelta = ng });
        Assert.NotNull(err);
        Assert.Equal("running.invalid_qty_delta", err!.Value.ErrorCode);
        Assert.Equal("Add deltas must be >= 0; use /run/qty/correct for negative.",
            err.Value.Message);
    }

    [Fact]
    public void ValidateQtyAdd_both_zero_yields_invalid_qty_delta()
    {
        var err = RunningSurfacePolicy.ValidateQtyAdd(
            new RunQtyAddRequest { QtyDoneDelta = 0, QtyNgDelta = 0 });
        Assert.NotNull(err);
        Assert.Equal("running.invalid_qty_delta", err!.Value.ErrorCode);
        Assert.Equal("At least one of QtyDoneDelta or QtyNgDelta must be > 0.",
            err.Value.Message);
    }

    [Fact]
    public void ValidateQtyAdd_negative_takes_precedence_over_both_zero()
    {
        // -1 & 0: negative check fires first (its message), not the both-zero one.
        var err = RunningSurfacePolicy.ValidateQtyAdd(
            new RunQtyAddRequest { QtyDoneDelta = -1, QtyNgDelta = 0 });
        Assert.Equal("Add deltas must be >= 0; use /run/qty/correct for negative.",
            err!.Value.Message);
    }

    // ── ValidateNgFormat ─────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateNgFormat_blank_reason_yields_invalid_reason_code(string? reason)
    {
        var err = RunningSurfacePolicy.ValidateNgFormat(reason, "a valid note");
        Assert.NotNull(err);
        Assert.Equal("running.invalid_reason_code", err!.Value.ErrorCode);
        Assert.Equal("NgReasonCode is required when QtyNgDelta > 0.", err.Value.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void ValidateNgFormat_blank_note_yields_invalid_ng_note(string? note)
    {
        var err = RunningSurfacePolicy.ValidateNgFormat("SC-MAT-DAMAGE", note);
        Assert.NotNull(err);
        Assert.Equal("running.invalid_ng_note", err!.Value.ErrorCode);
        Assert.Equal("NgNote must be 1-500 chars when QtyNgDelta > 0.", err.Value.Message);
    }

    [Fact]
    public void ValidateNgFormat_note_over_500_yields_invalid_ng_note()
    {
        var err = RunningSurfacePolicy.ValidateNgFormat("SC-MAT-DAMAGE", new string('x', 501));
        Assert.Equal("running.invalid_ng_note", err!.Value.ErrorCode);
    }

    [Fact]
    public void ValidateNgFormat_valid_returns_null()
    {
        Assert.Null(RunningSurfacePolicy.ValidateNgFormat("SC-MAT-DAMAGE", "torn corner"));
        Assert.Null(RunningSurfacePolicy.ValidateNgFormat("SC-MAT-DAMAGE", new string('x', 500)));
    }

    [Fact]
    public void ValidateNgFormat_blank_reason_takes_precedence_over_blank_note()
    {
        // Both blank: the reason check fires first (order preserved from controller).
        var err = RunningSurfacePolicy.ValidateNgFormat("", "");
        Assert.Equal("running.invalid_reason_code", err!.Value.ErrorCode);
    }

    // ── ValidateQtyCorrect ───────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateQtyCorrect_blank_reason_yields_invalid_correction_reason(string reason)
    {
        var err = RunningSurfacePolicy.ValidateQtyCorrect(
            new RunQtyCorrectRequest { LinkedEntryId = 1, CorrectionReason = reason });
        Assert.NotNull(err);
        Assert.Equal("running.invalid_correction_reason", err!.Value.ErrorCode);
        Assert.Equal("CorrectionReason is required (1-500 chars).", err.Value.Message);
    }

    [Fact]
    public void ValidateQtyCorrect_reason_over_500_yields_invalid_correction_reason()
    {
        var err = RunningSurfacePolicy.ValidateQtyCorrect(
            new RunQtyCorrectRequest { LinkedEntryId = 1, CorrectionReason = new string('x', 501) });
        Assert.Equal("running.invalid_correction_reason", err!.Value.ErrorCode);
    }

    [Fact]
    public void ValidateQtyCorrect_valid_returns_null()
    {
        Assert.Null(RunningSurfacePolicy.ValidateQtyCorrect(
            new RunQtyCorrectRequest { LinkedEntryId = 1, CorrectionReason = "miscount fixed" }));
    }

    // ── ValidatePauseFormat ──────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidatePauseFormat_blank_reason_yields_invalid_reason_code(string reason)
    {
        var err = RunningSurfacePolicy.ValidatePauseFormat(
            new RunPauseRequest { ReasonCode = reason });
        Assert.NotNull(err);
        Assert.Equal("running.invalid_reason_code", err!.Value.ErrorCode);
        Assert.Equal("ReasonCode is required.", err.Value.Message);
    }

    [Fact]
    public void ValidatePauseFormat_note_over_500_yields_invalid_note()
    {
        var err = RunningSurfacePolicy.ValidatePauseFormat(
            new RunPauseRequest { ReasonCode = "PA-BREAK", Note = new string('x', 501) });
        Assert.NotNull(err);
        Assert.Equal("running.invalid_note", err!.Value.ErrorCode);
        Assert.Equal("Note must be 0-500 chars.", err.Value.Message);
    }

    [Fact]
    public void ValidatePauseFormat_valid_returns_null()
    {
        Assert.Null(RunningSurfacePolicy.ValidatePauseFormat(
            new RunPauseRequest { ReasonCode = "PA-BREAK" }));                       // null note OK
        Assert.Null(RunningSurfacePolicy.ValidatePauseFormat(
            new RunPauseRequest { ReasonCode = "PA-BREAK", Note = "shift change" }));
        Assert.Null(RunningSurfacePolicy.ValidatePauseFormat(
            new RunPauseRequest { ReasonCode = "PA-BREAK", Note = new string('x', 500) }));
    }
}
