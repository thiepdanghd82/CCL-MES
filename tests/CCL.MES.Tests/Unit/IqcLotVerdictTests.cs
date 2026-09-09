using CCL.MES.Domain;
using CCL.MES.Domain.StateMachine;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// Khoá luật "hễ có Fail thì báo Fail" (phương án 2, Thiệp chốt 2026-09-09).
/// Mỗi ca dưới đây dựng từ một hình dạng CÓ THẬT trong DB — con số trích ở
/// doc-comment của <see cref="IqcLotVerdict"/>, không phải ca tưởng tượng.
/// </summary>
public class IqcLotVerdictTests
{
    private static long _seq;

    private static IqcLotVerdict.Ticket T(
        string code, string lot, QcResult r, string receipt, int day = 1, string? batch = null)
        => new(++_seq, code, code, lot, batch ?? lot, receipt, r, new DateTime(2026, 1, day));

    // ── Luật ưu tiên ────────────────────────────────────────────────────────

    [Fact]
    public void Fail_thang_Pass_du_Pass_moi_hon()
    {
        // Hình dạng thật: 30032127 / 1398244 — 5 phiếu trong 11 ngày, vừa Pass
        // vừa Fail. "Lấy phiếu gần nhất" sẽ ra Pass và THẢ lô đã Fail vào chuyền.
        var v = IqcLotVerdict.Resolve("30032127", "1398244", new[]
        {
            T("30032127", "1398244", QcResult.Fail, "R-01", day: 11),
            T("30032127", "1398244", QcResult.Pass, "R-02", day: 22),   // mới hơn
        });

        Assert.NotNull(v);
        Assert.Equal(QcResult.Fail, v!.Value.Result);
        Assert.True(v.Value.HasConflict);
        Assert.Equal(2, v.Value.TicketCount);
    }

    [Fact]
    public void Pending_thang_Pass_vi_chua_ket_luan_khong_phai_da_dat()
    {
        var v = IqcLotVerdict.Resolve("30030328", "1410182", new[]
        {
            T("30030328", "1410182", QcResult.Pass, "R-01", day: 1),
            T("30030328", "1410182", QcResult.Pending, "R-02", day: 2),
        });

        Assert.Equal(QcResult.Pending, v!.Value.Result);
    }

    [Fact]
    public void Fail_thang_ca_Pending()
    {
        var v = IqcLotVerdict.Resolve("A", "L1", new[]
        {
            T("A", "L1", QcResult.Pending, "R-01"),
            T("A", "L1", QcResult.Fail, "R-02"),
            T("A", "L1", QcResult.Pass, "R-03"),
        });

        Assert.Equal(QcResult.Fail, v!.Value.Result);
        Assert.True(v.Value.HasConflict);
    }

    [Fact]
    public void Toan_Pass_thi_Pass_va_khong_bao_mau_thuan()
    {
        var v = IqcLotVerdict.Resolve("30030328", "1416015", new[]
        {
            T("30030328", "1416015", QcResult.Pass, "R-01"),
            T("30030328", "1416015", QcResult.Pass, "R-02"),
        });

        Assert.Equal(QcResult.Pass, v!.Value.Result);
        Assert.False(v.Value.HasConflict);
    }

    // ── Phiếu đại diện ──────────────────────────────────────────────────────

    [Fact]
    public void Phieu_dai_dien_la_phieu_MOI_NHAT_MANG_KET_QUA_da_chot()
    {
        // Không phải phiếu mới nhất nói chung: phiếu Pass ngày 22 mới hơn, nhưng
        // kết quả chốt là Fail nên phải trỏ về phiếu FAIL mới nhất (R-03), để
        // người xem mở đúng phiếu giải thích vì sao lô bị chặn.
        var v = IqcLotVerdict.Resolve("A", "L1", new[]
        {
            T("A", "L1", QcResult.Fail, "R-01", day: 5),
            T("A", "L1", QcResult.Fail, "R-03", day: 9),
            T("A", "L1", QcResult.Pass, "R-02", day: 22),
        });

        Assert.Equal(QcResult.Fail, v!.Value.Result);
        Assert.Equal("R-03", v.Value.GoverningReceiptNo);
    }

    // ── Không rò rỉ giữa các cặp ────────────────────────────────────────────

    [Fact]
    public void Cung_so_lo_nhung_KHAC_ma_thi_khong_dinh_vao_nhau()
    {
        // Đo được: 822/1672 số lô dùng cho >1 mã, có lô nằm dưới 79 mã khác nhau.
        // Nếu resolver chỉ khớp theo lô thì mã B kéo cả Fail của mã A sang.
        var v = IqcLotVerdict.Resolve("B", "1410182", new[]
        {
            T("A", "1410182", QcResult.Fail, "R-A"),
            T("B", "1410182", QcResult.Pass, "R-B"),
        });

        Assert.Equal(QcResult.Pass, v!.Value.Result);
        Assert.Equal("R-B", v.Value.GoverningReceiptNo);
        Assert.Equal(1, v.Value.TicketCount);
    }

    [Fact]
    public void Cung_ma_nhung_KHAC_lo_thi_khong_dinh_vao_nhau()
    {
        // Đo được: 667/1600 mã có >1 lô, nhiều nhất 28 lô/mã. 30030328 có lô
        // 1377336 Fail nằm giữa 8 lô Pass — hỏi lô Pass không được ra Fail.
        var v = IqcLotVerdict.Resolve("30030328", "1416015", new[]
        {
            T("30030328", "1377336", QcResult.Fail, "R-FAIL"),
            T("30030328", "1416015", QcResult.Pass, "R-PASS"),
        });

        Assert.Equal(QcResult.Pass, v!.Value.Result);
        Assert.Equal("R-PASS", v.Value.GoverningReceiptNo);
    }

    // ── Chuẩn hoá đầu vào ───────────────────────────────────────────────────

    [Theory]
    [InlineData("  30030328 ", "1416015")]
    [InlineData("30030328", "  1416015  ")]
    [InlineData("30030328", "1416015")]
    public void Bo_khoang_trang_thua_hai_dau(string code, string lot)
    {
        var v = IqcLotVerdict.Resolve(code, lot, new[] { T("30030328", "1416015", QcResult.Pass, "R") });
        Assert.NotNull(v);
    }

    [Fact]
    public void Khong_phan_biet_hoa_thuong()
    {
        var v = IqcLotVerdict.Resolve("ab-01", "lot-x", new[] { T("AB-01", "LOT-X", QcResult.Fail, "R") });
        Assert.Equal(QcResult.Fail, v!.Value.Result);
    }

    [Fact]
    public void LotNumber_rong_thi_lui_ve_BatchNumber()
    {
        // 5320/5334 dòng có LotNumber, 5322 có BatchNumber — vài dòng chỉ có một.
        var t = new IqcLotVerdict.Ticket(1, "A", PartNo: "A", LotNumber: "  ", BatchNumber: "B-9",
            ReceiptNo: "R", Result: QcResult.Fail, ReceivedDate: new DateTime(2026, 1, 1));
        Assert.Equal("B-9", IqcLotVerdict.EffectiveLot(t));
        Assert.Equal(QcResult.Fail, IqcLotVerdict.Resolve("A", "B-9", new[] { t })!.Value.Result);
    }

    [Fact]
    public void LotNumber_co_thi_THANG_BatchNumber()
    {
        var t = new IqcLotVerdict.Ticket(1, "A", PartNo: "A", LotNumber: "L-1", BatchNumber: "B-9",
            ReceiptNo: "R", Result: QcResult.Fail, ReceivedDate: new DateTime(2026, 1, 1));
        Assert.Equal("L-1", IqcLotVerdict.EffectiveLot(t));
        Assert.Null(IqcLotVerdict.Resolve("A", "B-9", new[] { t }));
    }

    // ── Không có dữ liệu ────────────────────────────────────────────────────

    [Fact]
    public void Khong_co_phieu_khop_thi_tra_NULL_chu_khong_phai_Pending()
    {
        // Phân biệt "CHƯA CÓ dữ liệu IQC" với "có và đang chờ kết luận" — hai
        // thứ này hiện ra màn hình khác nhau, gộp lại là nói dối người dùng.
        Assert.Null(IqcLotVerdict.Resolve("A", "L1", Array.Empty<IqcLotVerdict.Ticket>()));
        Assert.Null(IqcLotVerdict.Resolve("A", "KHONG-CO", new[] { T("A", "L1", QcResult.Pass, "R") }));
    }

    [Theory]
    [InlineData(null, "L1")]
    [InlineData("A", null)]
    [InlineData("", "L1")]
    [InlineData("A", "   ")]
    public void Thieu_ma_hoac_lo_thi_tra_NULL(string? code, string? lot)
    {
        // 15/5334 phiếu thiếu mã hoặc lô — không được đoán bừa cho nhóm này.
        Assert.Null(IqcLotVerdict.Resolve(code, lot, new[] { T("A", "L1", QcResult.Pass, "R") }));
    }
}
