using CCL.MES.Application.Services;
using Xunit;
using Op = CCL.MES.Application.Services.QcLineResolver.RoutingOp;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// Phương án C — Bước 3. Khóa phân loại resolver bằng ROUTING THẬT của các mã
/// hàng 8064xxxx (đối soát <c>RoutingOperations 260525-52014.csv</c> trong DB live).
/// Mỗi fixture là routing y nguyên đã dump từ <c>sqlite3</c> — đổi rule mà lệch
/// kết quả mong đợi → fail CI.
/// </summary>
public sealed class QcLineResolverTests
{
    // ── Phân loại từng operation ────────────────────────────────────

    [Theory]
    [InlineData("(GALLUS) PRINT / In nhãn", "GFL01", QcLineResolver.ClsLabel)]
    [InlineData("(BROTECH) PRINT / In nhãn", "BFL01", QcLineResolver.ClsLabel)]
    [InlineData("(HP INDIGO) PRINT / In máy kts", "IDG01", QcLineResolver.ClsDigital)]
    [InlineData("SILK SEMI_AUTO SHEET/In dạng tờ-P1", "ASS08", QcLineResolver.ClsSilk)]
    [InlineData("(SILK)SEMI_AUTOSHEET/In dạng tờ-P1", "MSS01", QcLineResolver.ClsSilk)]
    [InlineData("(RDC) LAM.&Cut / Ép&Cắt dao tròn", "RDC12", QcLineResolver.ClsPressCnc)]
    [InlineData("(PRESS) CUT / Cắt", "PPSC1", QcLineResolver.ClsPressCnc)]
    [InlineData("(PRESS) LAM.&Cut / Ép &Cắt", "R2SC3", QcLineResolver.ClsPressCnc)] // LAM.&Cut → cut thắng
    [InlineData("DRILL HOLE / Khoan lỗ", "MDRH1", QcLineResolver.ClsPressCnc)]
    [InlineData("PUNCHING / Đục lỗ", "PUNC1", QcLineResolver.ClsPressCnc)]
    [InlineData("(SEAL) LAM. & CUT / Ép dán và cắt", "FBL02", QcLineResolver.ClsPressCnc)]
    [InlineData("(SEAL) LAM / Ép dán In nhãn", "LAML1", QcLineResolver.ClsAppearance)]
    [InlineData("(PRESS) LAM. ROLL/ Ép dán cuộn", "LAMR3", QcLineResolver.ClsAppearance)]
    [InlineData("PRE- PREPARE / Chuẩn bị trước SX", "FXPP1", QcLineResolver.ClsSkip)]
    [InlineData("BAKING (SHEET)", "OVS1", QcLineResolver.ClsSkip)]
    [InlineData("UV Tunnel furnace surface dry", "UVS1", QcLineResolver.ClsSkip)]
    [InlineData("TAPPING / Dán băng dính", "MAN3", QcLineResolver.ClsSkip)]
    [InlineData("FQC & PACKING/Kiểm tra và đóng gói", "MAN1", QcLineResolver.ClsFqc)]
    [InlineData("OQC Inspection", "MAN2", QcLineResolver.ClsOqc)]
    public void Classify_real_operations(string op, string wc, string expected)
    {
        Assert.Equal(expected, QcLineResolver.Classify(new Op(null, op, wc, null)));
    }

    // ── Resolve toàn routing (real 8064 parts) ──────────────────────

    [Fact]
    public void Resolve_80644935_label_flexo_plus_cut()
    {
        // Gallus + Brotech flexo PRINT, RDC LAM.&Cut, FQC, OQC.
        var ops = new[]
        {
            new Op("10", "PRE- PREPARE / Chuẩn bị trước SX", "FXPP1", null),
            new Op("20", "(GALLUS) PRINT / In nhãn", "GFL01", null),
            new Op("25", "(GALLUS) PRINT / In nhãn", "GFL01", null),
            new Op("27", "(BROTECH) PRINT / In nhãn", "BFL01", null),
            new Op("30", "(RDC) LAM.&Cut / Ép&Cắt dao tròn", "RDC12", null),
            new Op("50", "FQC & PACKING/Kiểm tra và đóng gói", "MAN1", null),
            new Op("60", "OQC Inspection", "MAN2", null),
        };
        var r = QcLineResolver.Resolve(ops);
        Assert.Equal(new[] { QcLineResolver.Label, QcLineResolver.PressCnc }, r.Lines);
        Assert.True(r.HasFqc);
        Assert.True(r.HasOqc);
        Assert.Empty(r.Unmapped);
    }

    [Fact]
    public void Resolve_80645392_digital_indigo_plus_cut()
    {
        var ops = new[]
        {
            new Op("10", "PRE- PREPARE / Chuẩn bị trước SX", "FXPP1", null),
            new Op("20", "(HP INDIGO) PRINT / In máy kts", "IDG01", null),
            new Op("31", "(PRESS) LAM.&Cut / Ép &Cắt", "R2SC3", null),
            new Op("50", "DRILL HOLE / Khoan lỗ", "MDRH1", null),
            new Op("60", "(PRESS) CUT / Cắt", "PPSC1", null),
            new Op("71", "TAPPING / Dán băng dính", "MAN3", null),
            new Op("80", "FQC & PACKING/Kiểm tra và đóng gói", "MAN1", null),
            new Op("90", "OQC Inspection", "MAN2", null),
        };
        var r = QcLineResolver.Resolve(ops);
        Assert.Equal(new[] { QcLineResolver.Digital, QcLineResolver.PressCnc }, r.Lines);
        Assert.True(r.HasFqc);
        Assert.True(r.HasOqc);
        Assert.Empty(r.Unmapped);
    }

    [Fact]
    public void Resolve_80640044_silk_plus_cut()
    {
        var ops = new[]
        {
            new Op("10", "PRE- PREPARE / Chuẩn bị trước SX", "FXPP1", null),
            new Op("20", "SILK SEMI_AUTO SHEET/In dạng tờ-P1", "ASS08", null),
            new Op("40", "BAKING (SHEET)", "OVS1", null),
            new Op("210", "UV Tunnel furnace surface dry", "UVS1", null),
            new Op("230", "SILK LAMINATION / Ép dán in lụa", "LAMR3", null),
            new Op("250", "PUNCHING / Đục lỗ", "PUNC1", null),
            new Op("260", "(PRESS) CUT / Cắt", "PPSC1", null),
            new Op("280", "FQC & PACKING/Kiểm tra và đóng gói", "MAN1", null),
            new Op("290", "OQC Inspection", "MAN2", null),
        };
        var r = QcLineResolver.Resolve(ops);
        // SILK in + PRESS_CNC cắt. (LABEL từ LAMINATION cũng vào do appearance.)
        Assert.Contains(QcLineResolver.Silk, r.Lines);
        Assert.Contains(QcLineResolver.PressCnc, r.Lines);
        Assert.True(r.HasFqc);
        Assert.True(r.HasOqc);
        Assert.Empty(r.Unmapped);
    }

    [Fact]
    public void Resolve_80640002_cut_only_label_appearance()
    {
        var ops = new[]
        {
            new Op("10", "PRE- PREPARE / Chuẩn bị trước SX", "FXPP1", null),
            new Op("20", "(SEAL) LAM / Ép dán In nhãn", "LAML1", null),
            new Op("30", "(SEAL) LAM. & CUT / Ép dán và cắt", "FBL02", null),
            new Op("70", "FQC & PACKING/Kiểm tra và đóng gói", "MAN1", null),
            new Op("80", "OQC Inspection", "MAN2", null),
        };
        var r = QcLineResolver.Resolve(ops);
        Assert.Equal(new[] { QcLineResolver.Label, QcLineResolver.PressCnc }, r.Lines);
        Assert.True(r.HasFqc);
        Assert.True(r.HasOqc);
        Assert.Empty(r.Unmapped);
    }

    // ── Hành vi biên ────────────────────────────────────────────────

    [Fact]
    public void Resolve_empty_routing_yields_no_lines()
    {
        var r = QcLineResolver.Resolve(Array.Empty<Op>());
        Assert.Empty(r.Lines);
        Assert.False(r.HasFqc);
        Assert.False(r.HasOqc);
    }

    [Fact]
    public void Unknown_operation_goes_to_unmapped_not_guessed()
    {
        var ops = new[] { new Op("99", "QUANTUM TELEPORT / Dịch chuyển", "ZZZ9", null) };
        var r = QcLineResolver.Resolve(ops);
        Assert.Empty(r.Lines);
        var u = Assert.Single(r.Unmapped);
        Assert.Contains("ZZZ9", u);
    }

    [Fact]
    public void Lines_order_is_stable_label_digital_silk_presscnc()
    {
        var ops = new[]
        {
            new Op("1", "(PRESS) CUT / Cắt", "PPSC1", null),
            new Op("2", "SILK SHEET", "ASS08", null),
            new Op("3", "(HP INDIGO) PRINT", "IDG01", null),
            new Op("4", "(GALLUS) PRINT", "GFL01", null),
        };
        var r = QcLineResolver.Resolve(ops);
        Assert.Equal(
            new[] { QcLineResolver.Label, QcLineResolver.Digital, QcLineResolver.Silk, QcLineResolver.PressCnc },
            r.Lines);
    }
}
