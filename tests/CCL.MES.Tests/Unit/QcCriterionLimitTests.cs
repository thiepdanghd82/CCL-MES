using CCL.MES.Application.Services;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// P4 — đọc cặp Target + Tolerance của tab QC Plans thành cặn số.
///
/// <para>Các chuỗi dưới đây là cách kỹ sư THỰC SỰ gõ, không phải ca tưởng
/// tượng: đo bằng cách chạy <see cref="IqcSpecLimitParser"/> qua 17 dạng gõ
/// thường gặp ngày 2026-09-11 — nó kham 7, và ba dạng bỏ sót
/// (<c>±t</c> một mình · <c>+/-</c> kiểu bàn phím · <c>min/max</c> bằng chữ)
/// chính là lý do lớp này tồn tại.</para>
/// </summary>
public class QcCriterionLimitTests
{
    // ── Dạng đầy đủ — đã kham sẵn, không được làm hỏng ──────────────────────

    [Theory]
    [InlineData("20 ± 0.5")]
    [InlineData("20 ± 0,5 mm")]
    public void Tolerance_day_du_thi_dung_luon_khong_can_Target(string tol)
    {
        var r = QcCriterionLimit.Resolve(target: "", tolerance: tol);
        Assert.True(r.Parsed);
        Assert.Equal(19.5, r.Low!.Value, 3);
        Assert.Equal(20.5, r.Up!.Value, 3);
        Assert.Equal(20, r.Nominal!.Value, 3);
    }

    [Fact]
    public void Doc_duoc_don_vi_khi_chuoi_co_ghi()
    {
        Assert.Equal("mm", QcCriterionLimit.Resolve("", "20 ± 0,5 mm").Unit);
    }

    // ── Ba dạng bộ đọc cũ BỎ SÓT ────────────────────────────────────────────

    [Theory]
    [InlineData("20", "±0,5")]
    [InlineData("20 mm", "± 0.5")]
    [InlineData("20", "+/-0,5")]
    [InlineData("20", "+-0.5")]
    public void Chi_co_dung_sai_thi_lay_GOC_tu_o_Target(string tgt, string tol)
    {
        // "±0,5" một mình không nói lên khoảng nào — gốc nằm ở ô bên cạnh.
        var r = QcCriterionLimit.Resolve(tgt, tol);
        Assert.True(r.Parsed);                                   // ← đỏ nếu bỏ nhánh này
        Assert.Equal(19.5, r.Low!.Value, 3);
        Assert.Equal(20.5, r.Up!.Value, 3);
    }

    [Theory]
    [InlineData("20.0 +/- 0.5")]
    [InlineData("20,0 +- 0,5")]
    public void Dang_bang_phim_plus_slash_minus(string tol)
    {
        var r = QcCriterionLimit.Resolve("", tol);
        Assert.True(r.Parsed);
        Assert.Equal(19.5, r.Low!.Value, 3);
        Assert.Equal(20.5, r.Up!.Value, 3);
    }

    [Theory]
    [InlineData("min 18", 18d, null)]
    [InlineData("MAX 22", null, 22d)]
    [InlineData("tối thiểu 18", 18d, null)]
    [InlineData("tối đa 22", null, 22d)]
    public void Mot_phia_bang_CHU(string tol, double? low, double? up)
    {
        var r = QcCriterionLimit.Resolve("", tol);
        Assert.True(r.Parsed);
        Assert.Equal(low, r.Low);
        Assert.Equal(up, r.Up);
    }

    [Fact]
    public void Ky_su_go_het_vao_o_Target_van_doc_duoc()
    {
        var r = QcCriterionLimit.Resolve(target: "20 ± 0,5 mm", tolerance: "");
        Assert.True(r.Parsed);
        Assert.Equal(19.5, r.Low!.Value, 3);
    }

    // ── KHÔNG ĐOÁN — quan trọng ngang phần đọc được ─────────────────────────

    [Theory]
    [InlineData("", "")]
    [InlineData("   ", "  ")]
    public void Ca_hai_o_trong_thi_bao_EMPTY(string tgt, string tol)
    {
        var r = QcCriterionLimit.Resolve(tgt, tol);
        Assert.False(r.Parsed);
        Assert.Equal(QcCriterionLimit.ReasonEmpty, r.Reason);
    }

    [Theory]
    [InlineData("Theo bản vẽ")]
    [InlineData("trong dung sai spec")]
    [InlineData("Đúng kích thước & tỉ lệ spec")]
    public void Cau_CHU_tro_sang_ban_ve_thi_KHONG_doc_duoc_va_noi_ra(string tol)
    {
        // 11/11 hạng mục Measure của IPQC hiện đang ghi kiểu này — chúng trỏ
        // sang bản vẽ chứ không tự mang dung sai. Bịa ra một cặn số ở đây là
        // bịa tiêu chuẩn chất lượng.
        var r = QcCriterionLimit.Resolve("", tol);
        Assert.False(r.Parsed);                                  // ← đỏ nếu ai đó "cố đọc cho bằng được"
        Assert.Equal(QcCriterionLimit.ReasonNoNumber, r.Reason);
    }

    [Fact]
    public void Chi_co_dung_sai_ma_Target_KHONG_co_so_thi_chiu()
    {
        var r = QcCriterionLimit.Resolve(target: "Theo bản vẽ", tolerance: "±0,5");
        Assert.False(r.Parsed);
        Assert.Equal(QcCriterionLimit.ReasonNoNumber, r.Reason);
    }

    [Fact]
    public void Target_co_NHIEU_so_thi_khong_doan_goc_nao()
    {
        // "20x10" — ràng buộc chiều rộng hay chiều dài? Chọn bừa một chiều là
        // dựng một tiêu chuẩn không ai viết ra.
        var r = QcCriterionLimit.Resolve(target: "20x10", tolerance: "±0,5");
        Assert.False(r.Parsed);
        Assert.Equal(QcCriterionLimit.ReasonNoNumber, r.Reason);
    }

    [Fact]
    public void Dau_PHAY_la_dau_thap_phan_theo_vi_VN()
    {
        // Bẫy đã trả giá trong dự án: "0,5" dưới vi-VN là 0.5, không phải 5.
        var r = QcCriterionLimit.Resolve("20", "±0,5");
        Assert.Equal(19.5, r.Low!.Value, 3);
        Assert.NotEqual(15, r.Low!.Value);
    }

    [Fact]
    public void Dung_sai_am_van_hieu_la_khoang_hai_phia()
    {
        var r = QcCriterionLimit.Resolve("20", "±-0,5");
        Assert.True(r.Parsed);
        Assert.Equal(19.5, r.Low!.Value, 3);
        Assert.Equal(20.5, r.Up!.Value, 3);
    }
}
