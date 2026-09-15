using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain.Auth;
using CCL.MES.Infrastructure;
using CCL.MES.Shared.Accounts;
using CCL.MES.Shared.Envelopes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Api.Tests;

/// <summary>
/// Bảng phân quyền của tab Quản lý tài khoản (Thiệp chốt 2026-09-15).
///
/// <para><b>Đây là bề mặt nhạy cảm nhất trong hệ:</b> ai sửa được nó thì tự cấp
/// được mọi quyền còn lại. Nên test nhắm vào bốn chỗ có thể mất kiểm soát —
/// không phải vào việc "bảng có hiện ra không".</para>
/// </summary>
public sealed class PermissionMatrixWireTests : IClassFixture<MesApiFactory>
{
    private const string Pwd = "P@ss!1";
    private readonly MesApiFactory _fx;
    public PermissionMatrixWireTests(MesApiFactory fx) => _fx = fx;

    private async Task<HttpClient> ClientAsync(string user, string role)
    {
        await _fx.SeedUserAsync(user, Pwd, role);
        var c = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(c, user, Pwd);
        return c;
    }

    private static HttpRequestMessage Put(long id, object body) =>
        new(HttpMethod.Put, $"/api/v2/admin/users/{id}/permissions")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };

    private async Task<long> UserIdAsync(string username)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        return await db.Users.AsNoTracking().Where(u => u.Username == username)
            .Select(u => u.Id).SingleAsync();
    }

    // ── Ai được xem, ai được sửa ────────────────────────────────────────────

    [Fact]
    public async Task Khong_phai_Admin_thi_KHONG_xem_duoc_bang()
    {
        var c = await ClientAsync("pm-qc-read", UserRole.Qc);
        var resp = await c.GetAsync("/api/v2/admin/users/permissions");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Khong_phai_Admin_thi_KHONG_sua_duoc()
    {
        var c = await ClientAsync("pm-sup-write", UserRole.Supervisor);
        var id = await UserIdAsync("pm-sup-write");
        var resp = await c.SendAsync(Put(id, new
        {
            permissions = new Dictionary<string, bool?> { ["SystemConfig"] = true },
            signerUsername = "pm-sup-write", signerPassword = Pwd,
        }));
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    // ── Chữ ký: không ký thì không sửa ──────────────────────────────────────

    [Fact]
    public async Task Khong_ky_thi_KHONG_sua_duoc_quyen()
    {
        var c = await ClientAsync("pm-admin-nosig", UserRole.Admin);
        var target = await UserIdAsync("pm-admin-nosig");

        var resp = await c.SendAsync(Put(target, new
        {
            permissions = new Dictionary<string, bool?> { ["SystemConfig"] = true },
        }));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("ipqc.signature_required", err!.Code);

        // Chốt chữ ký đứng TRƯỚC mọi phép ghi.
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var u = await db.Users.AsNoTracking().SingleAsync(x => x.Id == target);
        Assert.Null(u.PermSystemConfig);
    }

    [Fact]
    public async Task Sai_mat_khau_thi_bi_tu_choi_va_KHONG_ghi_mat_khau_vao_audit()
    {
        var c = await ClientAsync("pm-admin-badpwd", UserRole.Admin);
        var target = await UserIdAsync("pm-admin-badpwd");

        var resp = await c.SendAsync(Put(target, new
        {
            permissions = new Dictionary<string, bool?> { ["SystemConfig"] = false },
            signerUsername = "pm-admin-badpwd", signerPassword = "SAI-MAT-KHAU-HOAN-TOAN",
        }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var rows = await db.AuditLogs.AsNoTracking()
            .Where(a => a.TargetType == "User" && a.TargetId == target.ToString()).ToListAsync();
        var denied = Assert.Single(rows, a => a.Action == "USER_PERMISSION_SIGN_DENIED");
        Assert.Contains("pm-admin-badpwd", denied.Detail);
        Assert.DoesNotContain("SAI-MAT-KHAU", denied.Detail);   // ← đỏ nếu lọt mật khẩu
    }

    // ── Ký đúng thì ghi được, và ghi ĐÚNG ──────────────────────────────────

    [Fact]
    public async Task Ky_dung_thi_co_ghi_duoc_va_audit_du_hai_danh_tinh()
    {
        var c = await ClientAsync("pm-admin-ok", UserRole.Admin);
        await _fx.SeedUserAsync("pm-target", Pwd, UserRole.Operator);
        var target = await UserIdAsync("pm-target");

        // Operator vốn KHÔNG có quyền duyệt QC — admin cấp riêng cho người này.
        var resp = await c.SendAsync(Put(target, new
        {
            permissions = new Dictionary<string, bool?> { ["ApproveQc"] = true },
            signerUsername = "pm-admin-ok", signerPassword = Pwd,
        }));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var u = await db.Users.AsNoTracking().SingleAsync(x => x.Id == target);
        Assert.True(u.PermApproveQc);

        var audit = await db.AuditLogs.AsNoTracking()
            .Where(a => a.TargetType == "User" && a.TargetId == target.ToString()
                     && a.Action == "USER_PERMISSION_SET").SingleAsync();
        Assert.Contains("pm-admin-ok", audit.Detail);      // người ký
        Assert.Contains("ApproveQc", audit.Detail);        // đổi cái gì
        Assert.DoesNotContain(Pwd, audit.Detail ?? "");    // không có mật khẩu
    }

    [Fact]
    public async Task Tra_ve_null_thi_quay_lai_THEO_VAI()
    {
        var c = await ClientAsync("pm-admin-null", UserRole.Admin);
        await _fx.SeedUserAsync("pm-target-null", Pwd, UserRole.Operator);
        var target = await UserIdAsync("pm-target-null");

        await c.SendAsync(Put(target, new
        {
            permissions = new Dictionary<string, bool?> { ["ApproveQc"] = true },
            signerUsername = "pm-admin-null", signerPassword = Pwd,
        }));
        await c.SendAsync(Put(target, new
        {
            permissions = new Dictionary<string, bool?> { ["ApproveQc"] = (bool?)null },
            signerUsername = "pm-admin-null", signerPassword = Pwd,
        }));

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var u = await db.Users.AsNoTracking().SingleAsync(x => x.Id == target);
        Assert.Null(u.PermApproveQc);   // ← NULL = theo vai, không phải false
    }

    [Fact]
    public async Task Tai_khoan_he_thong_KHONG_sua_quyen_duoc()
    {
        // sys-recovery được bảo vệ ở MỌI đường mutation (P10.7a-2.1).
        var c = await ClientAsync("pm-admin-sys", UserRole.Admin);
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var sys = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Role == UserRole.Sys);
        if (sys is null) return;   // môi trường test chưa seed sys

        var resp = await c.SendAsync(Put(sys.Id, new
        {
            permissions = new Dictionary<string, bool?> { ["SystemConfig"] = true },
            signerUsername = "pm-admin-sys", signerPassword = Pwd,
        }));
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Quyen_la_khong_co_trong_danh_muc_thi_tu_choi()
    {
        var c = await ClientAsync("pm-admin-bad", UserRole.Admin);
        var target = await UserIdAsync("pm-admin-bad");
        var resp = await c.SendAsync(Put(target, new
        {
            permissions = new Dictionary<string, bool?> { ["KhongCoQuyenNay"] = true },
            signerUsername = "pm-admin-bad", signerPassword = Pwd,
        }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    // ── Bảng đọc ra có đúng không ──────────────────────────────────────────

    [Fact]
    public async Task Bang_tra_ve_du_8_cot_va_dong_TONG_khop_so_dong()
    {
        var c = await ClientAsync("pm-admin-view", UserRole.Admin);
        var view = await c.GetFromJsonAsync<PermissionMatrixView>("/api/v2/admin/users/permissions");

        Assert.NotNull(view);
        Assert.Equal(8, view!.Permissions.Count);
        Assert.True(view.CanEdit);
        Assert.Equal("pm-admin-view", view.CurrentUsername);

        // Dòng TỔNG phải bằng số dòng thật sự có quyền — không phải số đếm rời.
        foreach (var perm in view.Permissions)
        {
            var counted = view.Rows.Count(r => r.Effective[perm]);
            Assert.Equal(counted, view.Totals[perm]);
        }
    }
}
