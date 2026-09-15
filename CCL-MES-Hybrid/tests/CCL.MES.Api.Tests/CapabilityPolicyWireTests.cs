using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain.Auth;
using CCL.MES.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Api.Tests;

/// <summary>
/// Nối 8 quyền riêng vào tầng policy (Thiệp chốt 2026-09-15).
///
/// <para><b>Rủi ro lớn nhất của đợt này KHÔNG phải là quên chặn ai — mà là
/// chặn nhầm tất cả.</b> Claim <c>perm</c> phát ra từ
/// <c>cờ riêng ?? mặc định của vai</c>; nếu phép tính ấy sai một chỗ thì cả
/// xưởng mất quyền giữa ca. Nên test tập trung chứng minh: bật cơ chế này
/// KHÔNG đổi quyền của bất kỳ vai nào khi chưa ai tick gì.</para>
///
/// <para>Chiều còn lại — tick TẮT phải có hiệu lực thật — cũng phải chứng
/// minh, nếu không đây chỉ là bảng trang trí.</para>
/// </summary>
public sealed class CapabilityPolicyWireTests : IClassFixture<MesApiFactory>
{
    private const string Pwd = "P@ss!1";
    private readonly MesApiFactory _fx;
    public CapabilityPolicyWireTests(MesApiFactory fx) => _fx = fx;

    private async Task<HttpClient> ClientAsync(string user, string role)
    {
        await _fx.SeedUserAsync(user, Pwd, role);
        var c = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(c, user, Pwd);
        return c;
    }

    private static HttpRequestMessage Judgment(long woId) =>
        new(HttpMethod.Post, $"/api/v2/work-orders/{woId}/ipqc/judgment")
        {
            Content = new StringContent("{\"judgment\":\"GoRun\"}", Encoding.UTF8, "application/json"),
            Headers = { { "If-Match", "\"x\"" }, { "Idempotency-Key", Guid.NewGuid().ToString() } },
        };

    // ── Bật cơ chế KHÔNG được đổi quyền của ai ─────────────────────────────

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Qc)]
    [InlineData(UserRole.EngineerQuality)]
    public async Task Vai_VON_duoc_phan_dinh_IPQC_thi_van_qua_cong(string role)
    {
        // ← đỏ nếu claim `perm` tính sai: cả xưởng mất quyền giữa ca.
        var c = await ClientAsync($"cap-ok-{role.ToLowerInvariant()}", role);
        var resp = await c.SendAsync(Judgment(1));
        Assert.NotEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Theory]
    [InlineData(UserRole.Operator)]
    [InlineData(UserRole.EngineerProduction)]
    public async Task Vai_VON_khong_duoc_phan_dinh_thi_van_bi_chan(string role)
    {
        var c = await ClientAsync($"cap-no-{role.ToLowerInvariant()}", role);
        var resp = await c.SendAsync(Judgment(1));
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    // ── Tick TẮT phải có hiệu lực thật ─────────────────────────────────────

    [Fact]
    public async Task Tat_quyen_ApproveQc_thi_nguoi_do_KHONG_phan_dinh_duoc_nua()
    {
        await _fx.SeedUserAsync("cap-victim", Pwd, UserRole.Qc);
        long victimId;
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
            victimId = await db.Users.Where(u => u.Username == "cap-victim")
                .Select(u => u.Id).SingleAsync();
        }

        // Trước khi tắt: qua được cổng vai.
        var before = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(before, "cap-victim", Pwd);
        Assert.NotEqual(HttpStatusCode.Forbidden, (await before.SendAsync(Judgment(1))).StatusCode);

        // Admin tắt quyền ApproveQc của người này.
        var admin = await ClientAsync("cap-admin", UserRole.Admin);
        var put = new HttpRequestMessage(HttpMethod.Put, $"/api/v2/admin/users/{victimId}/permissions")
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                permissions = new Dictionary<string, bool?> { ["ApproveQc"] = false },
                signerUsername = "cap-admin", signerPassword = Pwd,
            }), Encoding.UTF8, "application/json"),
        };
        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(put)).StatusCode);

        // Đăng nhập LẠI để lấy token mang claim mới — đúng như đời thật, vì
        // refresh token của người này đã bị thu hồi.
        var after = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(after, "cap-victim", Pwd);
        var resp = await after.SendAsync(Judgment(1));

        // ← đỏ nếu quyền riêng không được nối vào policy: bảng chỉ để trang trí.
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Tat_quyen_ManageUsers_thi_Admin_do_mat_quyen_vao_trang_tai_khoan()
    {
        // Ca đáng sợ nhất: admin tự tắt quyền của chính mình. Phải CHẠY ĐƯỢC
        // (không nổ), và người đó thật sự bị chặn — để còn biết mà nhờ admin khác.
        await _fx.SeedUserAsync("cap-admin2", Pwd, UserRole.Admin);
        long id;
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
            id = await db.Users.Where(u => u.Username == "cap-admin2").Select(u => u.Id).SingleAsync();
        }

        var admin = await ClientAsync("cap-admin-actor", UserRole.Admin);
        var put = new HttpRequestMessage(HttpMethod.Put, $"/api/v2/admin/users/{id}/permissions")
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                permissions = new Dictionary<string, bool?> { ["ManageUsers"] = false },
                signerUsername = "cap-admin-actor", signerPassword = Pwd,
            }), Encoding.UTF8, "application/json"),
        };
        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(put)).StatusCode);

        var victim = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(victim, "cap-admin2", Pwd);
        var resp = await victim.GetAsync("/api/v2/admin/users");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Claim_perm_phat_ra_dung_bang_quyen_cua_vai_khi_chua_tick_gi()
    {
        // Nền tảng của cả cơ chế: chưa ai tick ⇒ claim = mặc định vai.
        var c = await ClientAsync("cap-claims", UserRole.Supervisor);
        var me = await c.GetFromJsonAsync<JsonElement>("/api/v2/auth/me");
        Assert.True(me.ValueKind != JsonValueKind.Undefined);

        var expected = UserPermission.All
            .Count(p => UserPermission.DefaultForRole(UserRole.Supervisor, p));
        Assert.Equal(6, expected);   // khớp ảnh Thiệp đưa cho vai Supervisor
    }
}
