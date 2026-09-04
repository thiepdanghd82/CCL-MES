using CCL.MES.Application.Services;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// P13 — luật LẤY MẪU và luật CHẤP NHẬN của IQC.
///
/// <para>Mọi con số trong file này đo được từ file master "IQC report 2026"
/// (5.336 bản ghi), không lấy từ suy đoán. Đó là lý do chúng đáng được khoá:
/// đây là hành vi thật của xưởng, và nếu ai đó đổi luật thì phải đổi cùng bằng
/// chứng, không đổi lén.</para>
/// </summary>
public sealed class IqcSamplingAndAcceptanceTests
{
    // ── cỡ mẫu ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1, 1)]        // lô 1 → lấy 1, KHÔNG lấy 2 như bảng nói
    [InlineData(2, 2)]
    [InlineData(8, 2)]
    [InlineData(9, 3)]
    [InlineData(12, 3)]
    [InlineData(20, 5)]
    [InlineData(60, 13)]      // đo thật: Qty roll 60 → ghi 13
    [InlineData(66, 13)]      // đo thật: Qty roll 66 → ghi 13
    [InlineData(150, 20)]
    [InlineData(1_200, 80)]
    [InlineData(2_000, 125)]
    [InlineData(600_000, 1250)]
    public void Co_mau_de_xuat_la_min_cua_bang_AQL_va_co_lo(long lot, int expected)
        => Assert.Equal(expected, IqcSamplingTable.Suggest(lot));

    [Fact]
    public void Lo_nho_hon_co_mau_thi_CAT_NGON_chu_khong_doi_lay_nhieu_hon_so_hang_dang_co()
    {
        // 27% bản ghi Roll và 93% bản ghi Tool rơi vào nhánh này — không phải
        // ngoại lệ hiếm, mà là hành vi thường ngày của lô dao/dụng cụ.
        // Cắt ngọn CHỈ xảy ra khi lô nhỏ hơn cỡ mẫu của bảng. Với bậc 1
        // (lô 1–8, mẫu 2) thì đúng một cỡ lô bị cắt: lô = 1.
        Assert.Equal(1, IqcSamplingTable.Suggest(1));

        // Lô 3 KHÔNG bị cắt — bảng nói 2, lô có 3, lấy 2 là đủ. (File master
        // có vài dòng "lô 3 → lấy 3": đó là QC CỐ Ý kiểm 100% một lô bé, tức
        // ghi đè siết chặt, không phải quy tắc.)
        Assert.Equal(2, IqcSamplingTable.Suggest(3));

        // Lô 20: bảng nói 5, lô có 20 ⇒ vẫn 5.
        Assert.Equal(5, IqcSamplingTable.Suggest(20));
    }

    [Fact]
    public void Lo_rong_thi_KHONG_de_xuat_gi_chu_khong_de_xuat_bac_1()
    {
        // Trả 2 cho một lô 0 đơn vị là bịa ra con số cho việc không tồn tại.
        Assert.Equal(0, IqcSamplingTable.Suggest(0));
        Assert.Equal(0, IqcSamplingTable.Suggest(-5));
        Assert.Null(IqcSamplingTable.TierFor(0));
    }

    [Fact]
    public void Lay_it_hon_de_xuat_moi_la_noi_long_lay_nhieu_hon_thi_khong()
    {
        Assert.True(IqcSamplingTable.IsRelaxed(60, 5));    // đề xuất 13, lấy 5
        Assert.False(IqcSamplingTable.IsRelaxed(60, 13));
        Assert.False(IqcSamplingTable.IsRelaxed(60, 60));  // kiểm 100% — siết chặt
    }

    [Fact]
    public void Bang_AQL_lien_mach_khong_ho_va_khong_chong_lan()
    {
        // Một khe hở giữa hai bậc = một cỡ lô không tra được cỡ mẫu, và nó sẽ
        // im lặng rơi về 0 giữa ca sản xuất.
        var t = IqcSamplingTable.Tiers;
        Assert.Equal(15, t.Count);
        Assert.Equal(1, t[0].Lo);
        for (var i = 1; i < t.Count; i++)
            Assert.Equal(t[i - 1].Hi + 1, t[i].Lo);
        Assert.True(t[^1].Hi >= 1_000_000);
    }

    // ── chấp nhận: ngoại quan Ac = 0 ─────────────────────────────────────

    [Fact]
    public void Khong_loi_nao_thi_DAT()
    {
        var j = IqcAcceptance.JudgeDefectCounts([0, 0, 0, 0, 0]);
        Assert.Equal(IqcAutoVerdict.Pass, j.Verdict);
        Assert.Equal("iqc.judge.zero_defect", j.ReasonCode);
    }

    [Fact]
    public void MOT_loi_thoi_cung_TRUOT_va_chi_ro_o_nao()
    {
        // Đo trên 3.715 lô: KHÔNG lô nào có lỗi mà vẫn đạt. Ac = 0 tuyệt đối.
        var j = IqcAcceptance.JudgeDefectCounts([0, 0, 2, 0]);
        Assert.Equal(IqcAutoVerdict.Fail, j.Verdict);
        Assert.Equal("iqc.judge.defect_found", j.ReasonCode);
        Assert.Equal(3, j.OffendingIndex);
        Assert.Equal(2, j.OffendingValue);
    }

    [Fact]
    public void Con_o_TRONG_thi_CHUA_QUYET_DUOC_chu_khong_phai_dat()
    {
        // Bài học L67: thiếu một chiều thông tin thì bản ghi nói dối im lặng.
        var j = IqcAcceptance.JudgeDefectCounts([0, null, 0]);
        Assert.Equal(IqcAutoVerdict.Undecidable, j.Verdict);
        Assert.Equal(2, j.OffendingIndex);
    }

    // ── chấp nhận: đo lường ──────────────────────────────────────────────

    [Fact]
    public void Do_day_trong_dung_sai_thi_dat()
    {
        var lim = IqcSpecLimitParser.Parse("Adhesive 0.16±0.016");
        // 5 phép đo thật của lô NITTO 5000NS ngày 2026-01-02.
        var j = IqcAcceptance.JudgeMeasurements([0.155, 0.157, 0.158, 0.156, 0.159], lim);
        Assert.Equal(IqcAutoVerdict.Pass, j.Verdict);
    }

    [Fact]
    public void Mot_phep_do_ra_ngoai_la_TRUOT_ca_hang_muc()
    {
        var lim = IqcSpecLimitParser.Parse("0.16±0.016");
        var j = IqcAcceptance.JudgeMeasurements([0.155, 0.157, 0.199, 0.156, 0.159], lim);
        Assert.Equal(IqcAutoVerdict.Fail, j.Verdict);
        Assert.Equal("iqc.judge.above_up", j.ReasonCode);
        Assert.Equal(3, j.OffendingIndex);
        Assert.Equal(0.199, j.OffendingValue);
    }

    [Fact]
    public void Khong_co_nguong_so_thi_MAY_NHUONG_QUYEN_chu_khong_cho_qua()
    {
        // "Tham khảo báo cáo" / "Tham khảo File đo" — 4% khối lượng file master.
        var j = IqcAcceptance.JudgeMeasurements([1, 2, 3], IqcSpecLimitParser.Parse("Tham khảo báo cáo"));
        Assert.Equal(IqcAutoVerdict.Undecidable, j.Verdict);
        Assert.Equal("iqc.judge.no_numeric_limit", j.ReasonCode);
    }

    [Fact]
    public void Thieu_mot_phep_do_thi_chua_quyet_duoc()
    {
        var lim = IqcSpecLimitParser.Parse("0.16±0.016");
        var j = IqcAcceptance.JudgeMeasurements([0.16, null, 0.16], lim);
        Assert.Equal(IqcAutoVerdict.Undecidable, j.Verdict);
        Assert.Equal(2, j.OffendingIndex);
    }

    [Fact]
    public void Vat_lieu_RACH_la_dat_khi_tieu_chuan_ghi_or_tear()
    {
        // Keo bám chắc hơn độ bền của chính vật liệu ⇒ đạt. 226 lần xuất hiện
        // dạng "420 N/m or tear" trong file master.
        var lim = IqcSpecLimitParser.Parse("420 N/m or tear");
        Assert.True(lim!.Value.TearIsPass);
        var j = IqcAcceptance.JudgeMeasurements([300], lim, tearObserved: true);
        Assert.Equal(IqcAutoVerdict.Pass, j.Verdict);
        Assert.Equal("iqc.judge.tear_accepted", j.ReasonCode);

        // Không rách thì vẫn phải đạt ngưỡng.
        Assert.Equal(IqcAutoVerdict.Fail,
            IqcAcceptance.JudgeMeasurements([300], lim, tearObserved: false).Verdict);
    }

    // ── gộp kết luận phiếu ───────────────────────────────────────────────

    [Theory]
    [InlineData(new[] { 1, 1, 1 }, IqcAutoVerdict.Pass)]
    [InlineData(new[] { 1, 2, 1 }, IqcAutoVerdict.Fail)]
    [InlineData(new[] { 1, 0, 1 }, IqcAutoVerdict.Undecidable)]
    [InlineData(new[] { 2, 0, 1 }, IqcAutoVerdict.Fail)]   // trượt thắng chưa-quyết
    public void Ket_luan_phieu_gop_tu_cac_hang_muc(int[] raw, IqcAutoVerdict expected)
        => Assert.Equal(expected, IqcAcceptance.Combine(raw.Select(x => (IqcAutoVerdict)x)));

    [Fact]
    public void Phieu_KHONG_co_hang_muc_nao_thi_chua_quyet_duoc_chu_khong_phai_dat()
        => Assert.Equal(IqcAutoVerdict.Undecidable, IqcAcceptance.Combine([]));
}
