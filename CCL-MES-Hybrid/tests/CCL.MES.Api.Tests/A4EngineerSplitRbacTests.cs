using System.Net;
using System.Net.Http.Json;
using CCL.MES.Api.Policies;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain.Auth;
using Xunit;

namespace CCL.MES.Api.Tests;

/// <summary>
/// A4 (Thiệp chốt 2026-09-14) — tách vai <c>Engineer</c> thành kỹ sư SẢN XUẤT
/// và kỹ sư CHẤT LƯỢNG.
///
/// <para>Theo skill <c>cmes-rbac-matrix</c>: mỗi endpoint phải có <b>một vai
/// được phép (2xx)</b> và <b>ít nhất một vai bị chặn (403)</b>. Test chỉ có
/// happy path là chưa test RBAC.</para>
/// </summary>
public sealed class A4EngineerSplitRbacTests : IClassFixture<MesApiFactory>
{
    private const string Pwd = "P@ss!1";
    private readonly MesApiFactory _fx;
    public A4EngineerSplitRbacTests(MesApiFactory fx) => _fx = fx;

    private async Task<HttpClient> ClientAsync(string user, string role)
    {
        await _fx.SeedUserAsync(user, Pwd, role);
        var c = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(c, user, Pwd);
        return c;
    }

    private static bool Forbidden(HttpStatusCode s) => s == HttpStatusCode.Forbidden;

    // ── Hai danh sách vai ở hai nơi phải KHỚP (đúng bệnh L83) ───────────────

    [Fact]
    public void Vai_ky_waiver_phai_khop_policy_EngineerWaive()
    {
        // `EngineerWaive` (Program.cs) gác endpoint; `WaiverSignerRoles` gác
        // CHỮ KÝ. Hai danh sách ở hai file — lệch nhau là có người ký được mà
        // không gọi được endpoint, hoặc ngược lại.
        Assert.Equal(
            new[]
            {
                UserRole.Admin, UserRole.Supervisor,
                UserRole.EngineerProduction, UserRole.EngineerQuality,
                UserRole.Engineer,
            },
            IpqcSignaturePolicy.WaiverSignerRoles);
    }

    [Fact]
    public void Vai_phan_dinh_IPQC_phai_khop_policy_IpqcSubmit()
    {
        Assert.Equal(
            new[] { UserRole.Admin, UserRole.Qc, UserRole.EngineerQuality, UserRole.Engineer },
            IpqcSignaturePolicy.JudgmentSignerRoles);
    }

    [Fact]
    public void Vai_gop_cu_KHONG_cap_moi_duoc_nhung_van_doc_duoc()
    {
        // Users.Role là chuỗi: xoá hẳn "Engineer" thì mọi tài khoản đang mang
        // vai ấy rơi vào "không hợp lệ" = mất quyền im lặng giữa ca.
        Assert.DoesNotContain(UserRole.Engineer, UserRole.All);
        Assert.False(UserRole.IsValid(UserRole.Engineer));
        Assert.True(UserRole.IsLegacyEngineer("Engineer"));
        Assert.True(UserRole.IsValid(UserRole.EngineerProduction));
        Assert.True(UserRole.IsValid(UserRole.EngineerQuality));
    }

    // ── GHI spec sản xuất: kỹ sư SX được, kỹ sư CL bị chặn ──────────────────

    [Fact]
    public async Task Ghi_spec_SX__ky_su_san_xuat_KHONG_bi_403()
    {
        var c = await ClientAsync("a4-eps-write", UserRole.EngineerProduction);
        var resp = await c.PostAsJsonAsync("/api/v2/specs", new { });
        Assert.False(Forbidden(resp.StatusCode));   // qua cổng vai; thân sai là 4xx khác
    }

    [Fact]
    public async Task Ghi_spec_SX__ky_su_chat_luong_BI_403()
    {
        var c = await ClientAsync("a4-eqs-write", UserRole.EngineerQuality);
        var resp = await c.PostAsJsonAsync("/api/v2/specs", new { });
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    // ── GHI tiêu chuẩn IQC: kỹ sư CL được, kỹ sư SX bị chặn ─────────────────

    [Fact]
    public async Task Ghi_tieu_chuan_IQC__ky_su_chat_luong_KHONG_bi_403()
    {
        var c = await ClientAsync("a4-eq-iqc", UserRole.EngineerQuality);
        var resp = await c.PostAsJsonAsync("/api/v2/iqc/specs/items", new { });
        Assert.False(Forbidden(resp.StatusCode));
    }

    [Fact]
    public async Task Ghi_tieu_chuan_IQC__ky_su_san_xuat_BI_403()
    {
        var c = await ClientAsync("a4-ep-iqc", UserRole.EngineerProduction);
        var resp = await c.PostAsJsonAsync("/api/v2/iqc/specs/items", new { });
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    // ── Kế hoạch QC: TRƯỚC A4 ai đăng nhập cũng ghi được ────────────────────

    [Fact]
    public async Task Soan_ke_hoach_QC__Operator_BI_403()
    {
        // Lỗ hổng có sẵn trước A4: hai endpoint này chỉ có [Authorize] trần.
        // Đây là tiêu chí nghiệm thu sau đó lái ngưỡng IPQC xuống hồ sơ WO.
        var c = await ClientAsync("a4-op-qcplan", UserRole.Operator);
        var resp = await c.PostAsJsonAsync("/api/v2/qc-specs/windows/upsert-stage/1", new { });
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Soan_ke_hoach_QC__ky_su_chat_luong_KHONG_bi_403()
    {
        var c = await ClientAsync("a4-eq-qcplan", UserRole.EngineerQuality);
        var resp = await c.PostAsJsonAsync("/api/v2/qc-specs/windows/upsert-stage/1", new { });
        Assert.False(Forbidden(resp.StatusCode));
    }

    // ── Phán định IPQC: kỹ sư CL được, kỹ sư SX bị chặn ─────────────────────

    [Fact]
    public async Task Phan_dinh_IPQC__ky_su_san_xuat_BI_403()
    {
        // Dùng endpoint GHI: `GET {id}/ipqc` cố ý không gác policy (đọc được).
        var c = await ClientAsync("a4-ep-ipqc", UserRole.EngineerProduction);
        var req = new HttpRequestMessage(HttpMethod.Put, "/api/v2/work-orders/1/ipqc/item/X")
        {
            Content = JsonContent.Create(new { status = "Ok" }),
            Headers = { { "If-Match", "\"x\"" }, { "Idempotency-Key", Guid.NewGuid().ToString() } },
        };
        var resp = await c.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
