using System.Net;
using System.Net.Http.Json;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain.Auth;
using Xunit;

namespace CCL.MES.Api.Tests;

/// <summary>
/// A5 (2026-09-15) — 18 endpoint vận hành chuyển từ "dựa vào FallbackPolicy"
/// sang policy <c>ShopFloorWrite</c> tường minh.
///
/// <para><b>Rủi ro thật của thay đổi này là SIẾT NHẦM, không phải nới lỏng.</b>
/// Danh sách vai cố ý rộng — mọi vai đứng máy đều làm những việc này. Nếu một
/// vai bị bỏ sót thì chuyền đứng giữa ca, và người vận hành không có cách nào
/// tự gỡ. Nên test ở đây nhắm đúng chỗ đó: từng vai phải KHÔNG bị 403.</para>
///
/// <para>Nói thẳng giới hạn: <c>ShopFloorWrite</c> gồm cả 6 vai đăng nhập
/// được, nên nó là <b>tuyên bố ý đồ</b> chứ chưa phải ranh giới bảo mật. Giá
/// trị nằm ở chỗ ý đồ ấy giờ nằm trong mã và có gate canh — thu hẹp về sau là
/// một thay đổi CÓ Ý THỨC, không phải một hôm nào đó ai đó sửa lặng lẽ.</para>
/// </summary>
public sealed class A5ShopFloorPolicyTests : IClassFixture<MesApiFactory>
{
    private const string Pwd = "P@ss!1";
    private readonly MesApiFactory _fx;
    public A5ShopFloorPolicyTests(MesApiFactory fx) => _fx = fx;

    private async Task<HttpClient> ClientAsync(string user, string role)
    {
        await _fx.SeedUserAsync(user, Pwd, role);
        var c = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(c, user, Pwd);
        return c;
    }

    public static TheoryData<string> ShopFloorRoles() => new()
    {
        UserRole.Admin, UserRole.Supervisor,
        UserRole.EngineerProduction, UserRole.EngineerQuality,
        UserRole.Qc, UserRole.Operator,
    };

    [Theory]
    [MemberData(nameof(ShopFloorRoles))]
    public async Task Moi_vai_dung_may_deu_dem_duoc_san_luong(string role)
    {
        // ← đỏ nếu A5 siết nhầm và bỏ sót một vai: chuyền đứng giữa ca.
        var c = await ClientAsync($"a5-qty-{role.ToLowerInvariant()}", role);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v2/work-orders/1/run/qty")
        {
            Content = JsonContent.Create(new { qtyDoneDelta = 1 }),
            Headers = { { "If-Match", "\"x\"" }, { "Idempotency-Key", Guid.NewGuid().ToString() } },
        };
        var resp = await c.SendAsync(req);
        Assert.NotEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Theory]
    [MemberData(nameof(ShopFloorRoles))]
    public async Task Moi_vai_dung_may_deu_xac_nhan_duoc_vat_tu_prepress(string role)
    {
        var c = await ClientAsync($"a5-prep-{role.ToLowerInvariant()}", role);
        var req = new HttpRequestMessage(HttpMethod.Put, "/api/v2/work-orders/1/materials/0")
        {
            Content = JsonContent.Create(new { status = "Ok" }),
            Headers = { { "If-Match", "\"x\"" }, { "Idempotency-Key", Guid.NewGuid().ToString() } },
        };
        var resp = await c.SendAsync(req);
        Assert.NotEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Khong_dang_nhap_thi_van_401()
    {
        // Policy rộng KHÔNG có nghĩa là mở cho người lạ.
        var c = _fx.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v2/work-orders/1/run/qty")
        {
            Content = JsonContent.Create(new { qtyDoneDelta = 1 }),
        };
        var resp = await c.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
