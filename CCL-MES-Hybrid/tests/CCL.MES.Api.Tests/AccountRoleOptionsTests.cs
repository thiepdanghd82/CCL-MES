using CCL.MES.Domain.Auth;
using CCL.MES.Shared.Accounts;
using Xunit;

namespace CCL.MES.Api.Tests;

/// <summary>
/// Danh sách vai cho GIAO DIỆN nằm ở Shared (Razor không tham chiếu Domain);
/// nguồn sự thật là <see cref="UserRole.All"/>. Hai bản sao là mầm lỗi — test
/// này khoá chúng không lệch nhau.
///
/// <para><b>Sự cố 2026-09-15:</b> mảng gõ tay trong trang Quản lý tài khoản ghi
/// <c>"Qc"</c> trong khi DB lưu <c>"QC"</c>. <c>&lt;select&gt;</c> không khớp
/// option nào nên hiện TRẮNG — người dùng `qc` trông như chưa gán vai, suốt
/// nhiều tháng không ai để ý. Sau A4 nó còn thiếu hai ngạch kỹ sư mới.</para>
/// </summary>
public sealed class AccountRoleOptionsTests
{
    [Fact]
    public void Danh_sach_vai_cho_UI_phai_KHOP_UserRole_All()
    {
        // ← đỏ nếu thêm/xoá vai ở Domain mà quên sửa danh sách UI, hoặc gõ sai
        //   hoa-thường như lần "Qc" vs "QC".
        Assert.Equal(UserRole.All, AccountRoleOptions.All);
    }

    [Fact]
    public void Moi_vai_deu_co_nhan_tieng_Viet_rieng()
    {
        foreach (var r in AccountRoleOptions.All)
        {
            var label = AccountRoleOptions.Label(r);
            Assert.False(string.IsNullOrWhiteSpace(label));
            // Nhãn phải KHÁC mã vai — nếu trùng nghĩa là quên dịch.
            if (r != "QC") Assert.NotEqual(r, label);
        }
    }

    [Fact]
    public void Vai_gop_cu_van_co_nhan_de_doc_du_khong_cap_moi()
    {
        // Tài khoản đang mang vai cũ vẫn phải hiện ra được, không để trắng.
        Assert.Contains("cũ", AccountRoleOptions.Label(UserRole.Engineer));
    }
}
