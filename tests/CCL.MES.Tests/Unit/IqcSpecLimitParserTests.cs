using CCL.MES.Application.Services;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// P13 — bộ đọc chuỗi tiêu chuẩn IQC.
///
/// <para>MỌI chuỗi trong file này là chuỗi CÓ THẬT trong file master
/// "IQC report 2026", kèm số lần xuất hiện. Không có chuỗi nào do tôi bịa ra —
/// một bộ đọc test bằng dữ liệu tưởng tượng sẽ xanh rực rồi gãy ngay ngày đầu
/// gặp file thật.</para>
///
/// <para>Đo trên toàn bộ 937 dạng phân biệt / 12.999 lượt dùng: đọc được ngưỡng
/// số <b>49%</b> lượt · số danh nghĩa trần 43% · khai không có chuẩn 4% ·
/// còn lại <b>1%</b>. Con số 1% đó là CHỦ Ý — xem
/// <see cref="Dang_la_thi_TRA_NULL_chu_khong_doan"/>.</para>
/// </summary>
public sealed class IqcSpecLimitParserTests
{
    private static IqcSpecLimit P(string s)
    {
        var r = IqcSpecLimitParser.Parse(s);
        Assert.True(r.HasValue, $"đáng lẽ đọc được: {s}");
        return r!.Value;
    }

    // ── dung sai đối xứng — khuôn phổ biến nhất của độ dày ───────────────

    [Theory]
    [InlineData("0.16±0.016", 0.144, 0.176)]
    [InlineData("0.082 ±0.0082", 0.0738, 0.0902)]
    [InlineData("Face 0.05±0.005", 0.045, 0.055)]           // 72×
    [InlineData("Face 0.08±0.008", 0.072, 0.088)]           // 56×
    [InlineData("Face+Adhesive 0.075±0.01", 0.065, 0.085)]  // 10×
    [InlineData("0,3 ± 0,03", 0.27, 0.33)]                  // dấu phẩy thập phân VN
    public void Doc_duoc_dung_sai_doi_xung(string src, double lo, double up)
    {
        var l = P(src);
        Assert.Equal(lo, l.Low!.Value, 6);
        Assert.Equal(up, l.Up!.Value, 6);
    }

    [Fact]
    public void Nhan_dung_truoc_duoc_giu_lai_de_biet_do_cai_gi()
    {
        // "Face" là lớp mặt, "Adhesive" là lớp keo — hai thứ khác nhau trên
        // cùng một cuộn. Vứt nhãn đi là mất luôn thông tin đang đo lớp nào.
        Assert.Equal("Face", P("Face 0.05±0.005").Label);
        Assert.Equal("Adhesive", P("Adhesive 0.16±0.016").Label);
    }

    // ── dung sai lệch ────────────────────────────────────────────────────

    [Fact]
    public void Doc_duoc_dung_sai_lech_viet_lien()
    {
        var l = P("392+0.4-0.2");     // kích thước tấm PCS, 60× dạng này
        Assert.Equal(391.8, l.Low!.Value, 6);
        Assert.Equal(392.4, l.Up!.Value, 6);
        Assert.Equal(392, l.Nominal!.Value, 6);
    }

    [Fact]
    public void Doc_duoc_dung_sai_lech_viet_sau_don_vi()
    {
        var l = P("800 gf/in -160/+240");    // 65×
        Assert.Equal(640, l.Low!.Value, 6);
        Assert.Equal(1040, l.Up!.Value, 6);
        Assert.Equal("gf/in", l.Unit);
    }

    // ── lực bám dính: mặc định là CẬN DƯỚI ───────────────────────────────

    [Theory]
    [InlineData("270 N/m", 270)]          // 261×
    [InlineData("6.0 N/25mm", 6.0)]       // 162×
    [InlineData("6N/25mm", 6.0)]          // 114×
    [InlineData("13.0N/20mm", 13.0)]      // 79×
    [InlineData("122 N/100 mm", 122)]     // 60×
    [InlineData("6.6 N/cm", 6.6)]         // 58×
    public void Tri_kem_don_vi_LUC_hieu_ngam_la_toi_thieu(string src, double lo)
    {
        // Điểm dễ sai nhất cả bộ đọc. "270 N/m" hiểu thành "đúng bằng 270" thì
        // mọi lô keo TỐT HƠN tiêu chuẩn đều bị đánh trượt.
        var l = P(src);
        Assert.Equal(lo, l.Low!.Value, 6);
        Assert.Null(l.Up);
    }

    [Theory]
    [InlineData("≥7N/25mm", 7)]      // 135×
    [InlineData("≥8N/25mm", 8)]      // 105×
    [InlineData("> 750gf/in", 750)]  // 98×
    [InlineData(">800gf/in", 800)]   // 77×
    [InlineData("1000↑ gf/in", 1000)]// 165×
    [InlineData("300gf ↑", 300)]     // 8×
    public void Toan_tu_tuong_minh_va_mui_ten_len_deu_la_can_duoi(string src, double lo)
        => Assert.Equal(lo, P(src).Low!.Value, 6);

    [Theory]
    [InlineData("3-6g/25mm", 3, 6)]           // 160×
    [InlineData("0.4~1.0 kg/inch", 0.4, 1.0)] // 78×
    [InlineData("1.0-5.5 N/25mm", 1.0, 5.5)]  // 54×
    public void Doc_duoc_khoang(string src, double lo, double up)
    {
        var l = P(src);
        Assert.Equal(lo, l.Low!.Value, 6);
        Assert.Equal(up, l.Up!.Value, 6);
    }

    [Fact]
    public void Or_tear_duoc_ghi_nhan_rieng_chu_khong_lam_hong_viec_doc_so()
    {
        var l = P("420 N/m or tear");   // 226×
        Assert.Equal(420, l.Low!.Value, 6);
        Assert.True(l.TearIsPass);
        Assert.False(P("270 N/m").TearIsPass);
    }

    // ── ký tự toàn rộng lọt vào vì file gõ trên nhiều bàn phím ───────────

    [Fact]
    public void Ky_tu_toan_rong_CJK_van_doc_duoc()
    {
        Assert.Equal(32.2, P("32.2Ｎ/25㎜").Low!.Value, 6);
        Assert.Equal(750, P("＞750gf/in").Low!.Value, 6);
    }

    // ── ranh giới: KHÔNG đoán ────────────────────────────────────────────

    [Theory]
    [InlineData("N/A")]                    // 502×
    [InlineData("n/a")]
    [InlineData("Tham khảo báo cáo")]      // 24×
    [InlineData("Tham khảo File đo")]      // 18×
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-")]
    public void Khai_KHONG_co_tieu_chuan_thi_tra_null(string src)
        => Assert.Null(IqcSpecLimitParser.Parse(src));

    [Theory]
    [InlineData("220")]     // 247×
    [InlineData("270")]     // 203×
    [InlineData("140")]
    public void So_TRAN_khong_don_vi_KHONG_duoc_tu_suy_ra_nguong(string src)
    {
        // Đây là tiêu chuẩn ĐỘ RỘNG. Nó chỉ là trị danh nghĩa; dung sai nằm ở
        // cột Low/Up riêng của sheet Roll. Tự bịa ±0 sẽ đánh trượt mọi cuộn
        // lệch dù chỉ 0.1mm — trong khi thực tế cho phép ±1mm.
        Assert.Null(IqcSpecLimitParser.Parse(src));
        // Nhưng đọc được như TRỊ DANH NGHĨA khi chỗ gọi nói rõ ý định.
        Assert.NotNull(IqcSpecLimitParser.ParseBareNominal(src));
    }

    [Theory]
    [InlineData("3.0±0.3~0.5")]              // dung sai lại là một khoảng
    [InlineData("400/600/150/g/25mm")]       // nhiều trị, không rõ trị nào
    [InlineData("0.25＜T≤0.5mm±5%")]          // dung sai theo dải bề dày
    [InlineData("73(80) N/100mm)")]          // gõ lỗi, ngoặc thừa
    public void Dang_la_thi_TRA_NULL_chu_khong_doan(string src)
    {
        // 1% khối lượng file master rơi vào đây. Trả null là ĐÚNG: một ngưỡng
        // bịa ra sẽ âm thầm đánh trượt hàng tốt hoặc cho qua hàng hỏng, và
        // trông y hệt ngưỡng thật nên không ai phát hiện.
        Assert.Null(IqcSpecLimitParser.Parse(src));
    }

    [Fact]
    public void Chuoi_goc_LUON_duoc_giu_de_hien_cho_nguoi_kiem()
    {
        // Người đứng máy phải đọc được nguyên văn tiêu chuẩn, kể cả khi máy đã
        // chấm hộ — đó là thứ họ đối chiếu với tờ giấy của NCC.
        Assert.Equal("420 N/m or tear", P("420 N/m or tear").SourceText);
        Assert.Equal("Face 0.05±0.005", P("Face 0.05±0.005").SourceText);
    }

    [Fact]
    public void Khoang_nguoc_la_loi_go_KHONG_duoc_tu_dao_lai()
    {
        // "6-3" nhiều khả năng là gõ nhầm. Tự đảo thành 3–6 là thay người ta
        // quyết định trên một hồ sơ chất lượng.
        Assert.Null(IqcSpecLimitParser.Parse("6-3"));
    }
}
