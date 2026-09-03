using CCL.MES.Application.Services;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// P12 bước 3 — hạng mục kiểm nào rơi vào MỤC nào của stepper phiếu IQC.
///
/// <para>Khoá bảng đã chốt ở scope proposal §4.3. Đây là con số nghiệm thu mà
/// người dùng đếm được trên màn hình, nên nó phải nằm trong test chứ không nằm
/// trong đầu ai.</para>
/// </summary>
public sealed class IqcTicketSectionTests
{
    /// <summary>21 hạng mục thật của thư viện IQC (đo trên live 2026-08-28).</summary>
    private static readonly (string Item, string Group)[] Library =
    [
        ("NL-01", "NL"),
        ("NQ-01", "NQ"), ("NQ-02", "NQ"), ("NQ-03", "NQ"),
        ("NQ-04", "NQ"), ("NQ-05", "NQ"), ("NQ-06", "NQ"),
        ("KT-01", "KT"), ("KT-02", "KT"), ("KT-03", "KT"), ("KT-04", "KT"),
        ("MT-01", "MT"), ("MT-02", "MT"), ("MT-03", "MT"),
        ("BD-01", "BD"), ("BD-02", "BD"),
        ("CU-01", "CU"), ("XS-01", "XS"), ("TL-01", "TL"), ("BO-01", "BO"),
        ("KH-01", "KH"),
    ];

    private static int Count(int section) =>
        Library.Count(x => IqcTicketSection.Of(x.Item, x.Group) == section);

    // ── con số nghiệm thu ────────────────────────────────────────────────

    [Fact]
    public void Muc_2_co_dung_7_hang_muc_NL_va_NQ()
        => Assert.Equal(7, Count(IqcTicketSection.Visual));

    [Fact]
    public void Muc_1_chi_co_ho_so_giay_MT_02()
    {
        Assert.Equal(1, Count(IqcTicketSection.Documents));
        Assert.Equal(IqcTicketSection.Documents, IqcTicketSection.Of("MT-02", "MT"));
    }

    [Fact]
    public void Muc_3_om_13_hang_muc_con_lai()
    {
        // Bảng §4.3 ghi "12" vì liệt kê KT·BD·CU·XS·TL·BO·MT-01·MT-03; dòng ngay
        // dưới bảng bổ sung KH-01 ⇒ tổng thật là 13. Chốt con số THẬT ở đây để
        // lần sau không ai phải đọc hai chỗ mới biết đáp án.
        Assert.Equal(13, Count(IqcTicketSection.Functional));
        Assert.Equal(21, Library.Length);
    }

    [Fact]
    public void Moi_hang_muc_thu_vien_deu_co_MOT_muc_khong_ai_bi_bo_roi()
    {
        // Hạng mục không thuộc mục nào = biến mất khỏi phiếu mà không báo lỗi.
        var total = Count(IqcTicketSection.Documents)
                  + Count(IqcTicketSection.Visual)
                  + Count(IqcTicketSection.Functional);
        Assert.Equal(Library.Length, total);
    }

    // ── nhóm MT bị CHẺ — bẫy chính của luật này ──────────────────────────

    [Fact]
    public void Nhom_MT_bi_che_ba_duong_theo_MA_chu_khong_theo_nhom()
    {
        // Tra theo nhóm không thôi sẽ đẩy cả ba MT-* về cùng một mục: MT-02 là
        // hồ sơ giấy (mục 1), MT-01/MT-03 là phép đo thật (mục 3).
        Assert.Equal(IqcTicketSection.Documents,  IqcTicketSection.Of("MT-02", "MT"));
        Assert.Equal(IqcTicketSection.Functional, IqcTicketSection.Of("MT-01", "MT"));
        Assert.Equal(IqcTicketSection.Functional, IqcTicketSection.Of("MT-03", "MT"));
    }

    [Theory]
    [InlineData("mt-02")]
    [InlineData("  MT-02  ")]
    public void Ma_ho_so_khop_khong_phan_biet_hoa_thuong_va_da_trim(string key)
        => Assert.Equal(IqcTicketSection.Documents, IqcTicketSection.Of(key, "MT"));

    // ── biên ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("NL-01", "nl")]
    [InlineData("NQ-01", "  NQ ")]
    public void Nhom_ngoai_quan_khop_khong_phan_biet_hoa_thuong(string item, string group)
        => Assert.Equal(IqcTicketSection.Visual, IqcTicketSection.Of(item, group));

    [Theory]
    [InlineData(null, null)]
    [InlineData("ZZ-99", "ZZ")]
    [InlineData("KT-01", "")]
    public void Hang_muc_la_roi_ve_muc_3_chu_KHONG_bien_mat(string? item, string? group)
    {
        // Thư viện mở rộng về sau sẽ có mã chưa ai khai báo. Hiện ra chỗ nào đó
        // còn hơn rơi khỏi phiếu im lặng.
        Assert.Equal(IqcTicketSection.Functional, IqcTicketSection.Of(item, group));
    }
}
