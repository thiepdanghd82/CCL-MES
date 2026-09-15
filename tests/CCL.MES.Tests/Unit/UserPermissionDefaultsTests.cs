using CCL.MES.Domain.Auth;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// Khoá bảng mặc định 8 quyền theo vai.
///
/// <para><b>Vì sao cần khoá.</b> Bảng này chạy ở HAI nơi cùng lúc: nó vẽ ra ô tick
/// trên tab Quản lý tài khoản, và nó phát ra claim <c>perm</c> trong JWT. Sửa một
/// dòng ở <see cref="UserPermission.DefaultForRole"/> là đổi quyền THẬT của cả một
/// vai trên toàn xưởng mà màn hình không kêu gì. Cho tới 2026-09-15 không có test
/// nào canh — số liệu chỉ từng được đếm tay một lần rồi thôi.</para>
///
/// <para>Test này cố tình viết dạng ma trận tường minh chứ không suy lại từ
/// <c>DefaultForRole</c>: suy lại thì nó chỉ chứng minh hàm bằng chính nó.</para>
/// </summary>
public sealed class UserPermissionDefaultsTests
{
    // Hàng = vai · cột = 8 quyền theo đúng thứ tự UserPermission.All:
    // ViewData · EditData · ApproveQc · ApproveProduction ·
    // SpecialAccept · ExportReport · ManageUsers · SystemConfig
    public static TheoryData<string, bool[]> Matrix() => new()
    {
        { UserRole.Admin,              new[] { true,  true,  true,  true,  true,  true,  true,  true  } },
        { UserRole.Supervisor,         new[] { true,  true,  true,  true,  true,  true,  false, false } },
        { UserRole.EngineerProduction, new[] { true,  true,  false, true,  true,  true,  false, false } },
        { UserRole.EngineerQuality,    new[] { true,  true,  true,  true,  true,  true,  false, false } },
        { UserRole.Engineer,           new[] { true,  true,  false, true,  true,  true,  false, false } },
        { UserRole.Qc,                 new[] { true,  true,  true,  true,  false, true,  false, false } },
        { UserRole.Operator,           new[] { true,  true,  false, true,  false, false, false, false } },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public void Mac_dinh_theo_vai_dung_nhu_ma_tran_da_chot(string role, bool[] expected)
    {
        Assert.Equal(UserPermission.All.Count, expected.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            var permission = UserPermission.All[i];
            Assert.Equal(expected[i], UserPermission.DefaultForRole(role, permission));
        }
    }

    [Fact]
    public void Tai_khoan_he_thong_chi_duoc_doc_va_khong_bao_gio_duyet_san_xuat()
    {
        // sys-recovery là tài khoản khôi phục, không phải người. Nó không được
        // đứng tên trên bất kỳ quyết định chất lượng hay sản xuất nào.
        Assert.True(UserPermission.DefaultForRole(UserRole.Sys, UserPermission.ViewData));
        foreach (var p in UserPermission.All)
        {
            if (p == UserPermission.ViewData) continue;
            Assert.False(UserPermission.DefaultForRole(UserRole.Sys, p));
        }
    }

    [Fact]
    public void Vai_la_khong_duoc_gi_ca()
    {
        // Vai mới rơi vào "không được gì", không phải "được mọi thứ" — luật vàng
        // của cmes-rbac-matrix. Chuỗi rỗng / null cũng vậy.
        foreach (var role in new string?[] { null, "", "   ", "KhongCoVaiNay" })
            foreach (var p in UserPermission.All)
                Assert.False(UserPermission.DefaultForRole(role, p));
    }

    [Fact]
    public void Co_rieng_tick_thi_de_len_mac_dinh_cua_vai()
    {
        Assert.False(UserPermission.Effective(UserRole.Admin, UserPermission.ManageUsers, false));
        Assert.True(UserPermission.Effective(UserRole.Operator, UserPermission.ManageUsers, true));
        // null = "theo vai trò" — đây là giá trị của MỌI dòng cũ sau migration.
        Assert.True(UserPermission.Effective(UserRole.Admin, UserPermission.ManageUsers, null));
        Assert.False(UserPermission.Effective(UserRole.Operator, UserPermission.ManageUsers, null));
    }

    [Fact]
    public void Ai_dung_cong_thi_duyet_duoc_san_xuat()
    {
        // Thiệp chốt 2026-09-15: người bấm cho hàng đi tiếp CHÍNH LÀ người đứng
        // cổng — QC ở IPQC, công nhân sản xuất ở prepress. Siết hai vai này ra
        // ngoài là làm chuyền đứng giữa ca.
        Assert.True(UserPermission.DefaultForRole(UserRole.Qc, UserPermission.ApproveProduction));
        Assert.True(UserPermission.DefaultForRole(UserRole.Operator, UserPermission.ApproveProduction));
    }
}
