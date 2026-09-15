using System.Net;
using System.Net.Http.Json;
using System.Text;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain;
using CCL.MES.Domain.Auth;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Shared.Envelopes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Api.Tests;

/// <summary>
/// "Phê duyệt sản xuất" — Thiệp chốt 2026-09-15:
/// <i>"phê duyệt sản xuất là IPQC xác nhận nếu kiểm tra tất cả OK, không có
/// chấp nhận đặc biệt. Tương tự phê duyệt Prepress cũng là công nhân sản xuất
/// nếu không có chấp nhận đặc biệt có thể move sang bước tiếp theo."</i>
///
/// <para>Tức quyền này là <b>cho hàng qua một cổng khi mọi thứ ĐẠT</b> — khác hẳn
/// <c>SpecialAccept</c> là cho qua DÙ CÓ cái không đạt. Hai quyết định khác nhau ⇒
/// hai quyền khác nhau ⇒ không gác chung được bằng một attribute.</para>
///
/// <para><b>Rủi ro của đợt này vẫn là SIẾT NHẦM.</b> Nếu nối sai thì người đứng
/// máy bấm "Cho chạy" ra 403 và chuyền đứng giữa ca — nên nhóm test đầu tiên
/// chứng minh cái KHÔNG đổi, trước khi chứng minh cái đổi.</para>
/// </summary>
public sealed class ApproveProductionCapabilityTests : IClassFixture<MesApiFactory>
{
    private const string Pwd = "P@ss!1";
    private readonly MesApiFactory _fx;
    public ApproveProductionCapabilityTests(MesApiFactory fx) => _fx = fx;

    // ── hạ tầng ────────────────────────────────────────────────────────────

    private async Task<HttpClient> LoginAsync(string user)
    {
        var c = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(c, user, Pwd);
        return c;
    }

    private async Task<HttpClient> ClientAsync(string user, string role)
    {
        await _fx.SeedUserAsync(user, Pwd, role);
        return await LoginAsync(user);
    }

    /// <summary>Tick TẮT một quyền riêng ngay trên cột DB — đúng thứ mà bảng
    /// phân quyền ghi xuống. Đăng nhập lại sau đó để token mang claim mới.</summary>
    private async Task TurnOffAsync(string username, string permission)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var u = await db.Users.SingleAsync(x => x.Username == username);
        switch (permission)
        {
            case UserPermission.EditData:          u.PermEditData = false; break;
            case UserPermission.ApproveQc:         u.PermApproveQc = false; break;
            case UserPermission.ApproveProduction: u.PermApproveProduction = false; break;
            case UserPermission.SpecialAccept:     u.PermSpecialAccept = false; break;
            default: throw new ArgumentOutOfRangeException(nameof(permission), permission, null);
        }
        await db.SaveChangesAsync();
    }

    private async Task<long> SeedWoAsync(string woNo, string mesPhase)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();

        var customer = new Customer { Code = "C-" + woNo, Name = "Customer " + woNo };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var product = new Product { ProductCode = "P-" + woNo, Name = "Product " + woNo, CustomerId = customer.Id };
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var wo = new WorkOrder
        {
            WoNo = woNo,
            CustomerId = customer.Id,
            ProductId = product.Id,
            ProductName = product.Name,
            MachineCode = "M-1",
            MachineName = "Press 1",
            TargetQty = 1000,
            Uom = "pcs",
            CurrentStep = ProcessStepCode.PrePressCheck,
            MesPhase = mesPhase,
            Status = WoStatus.InProgress,
            MaterialsReady = true,
            SetupConfirmed = true,
        };
        db.WorkOrders.Add(wo);
        await db.SaveChangesAsync();
        return wo.Id;
    }

    private static HttpRequestMessage Advance(long woId) =>
        new(HttpMethod.Post, $"/api/v2/work-orders/{woId}/advance")
        {
            Headers = { { "If-Match", "\"x\"" }, { "Idempotency-Key", Guid.NewGuid().ToString() } },
        };

    private static HttpRequestMessage Judgment(string outcome) =>
        new(HttpMethod.Post, "/api/v2/work-orders/1/ipqc/judgment")
        {
            Content = new StringContent($"{{\"judgment\":\"{outcome}\"}}", Encoding.UTF8, "application/json"),
            Headers = { { "If-Match", "\"x\"" }, { "Idempotency-Key", Guid.NewGuid().ToString() } },
        };

    private static async Task<string?> CodeOf(HttpResponseMessage resp) =>
        (await resp.Content.ReadFromJsonAsync<ApiError>())?.Code;

    // ── ❶ CÁI KHÔNG ĐƯỢC ĐỔI ───────────────────────────────────────────────

    public static TheoryData<string> ShopFloorRoles() => new()
    {
        UserRole.Admin, UserRole.Supervisor,
        UserRole.EngineerProduction, UserRole.EngineerQuality,
        UserRole.Qc, UserRole.Operator,
    };

    [Theory]
    [MemberData(nameof(ShopFloorRoles))]
    public async Task Moi_vai_dung_may_van_cho_lenh_roi_Prepress_duoc(string role)
    {
        // ← đỏ nếu nối nhầm: người đứng máy bấm "đi tiếp" ra 403, chuyền đứng.
        var lower = role.ToLowerInvariant();
        var woId = await SeedWoAsync($"WO-AP-PRE-{lower}", "PREPRESS");
        var c = await ClientAsync($"ap-pre-{lower}", role);
        var resp = await c.SendAsync(Advance(woId));
        Assert.NotEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Theory]
    [MemberData(nameof(ShopFloorRoles))]
    public async Task Moi_vai_dung_may_van_chuyen_buoc_o_pha_khac_duoc(string role)
    {
        var lower = role.ToLowerInvariant();
        var woId = await SeedWoAsync($"WO-AP-SET-{lower}", "SETTING");
        var c = await ClientAsync($"ap-set-{lower}", role);
        var resp = await c.SendAsync(Advance(woId));
        Assert.NotEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Qc)]
    [InlineData(UserRole.EngineerQuality)]
    public async Task Vai_von_phan_dinh_IPQC_duoc_thi_ca_ba_nut_deu_qua_cong(string role)
    {
        var lower = role.ToLowerInvariant();
        var c = await ClientAsync($"ap-judge-{lower}", role);
        foreach (var outcome in new[] { "GoRun", "StopLine", "SpecialAccept" })
            Assert.NotEqual(HttpStatusCode.Forbidden, (await c.SendAsync(Judgment(outcome))).StatusCode);
    }

    // ── ❷ CÁI PHẢI ĐỔI — quyền theo KẾT QUẢ, không theo endpoint ───────────

    [Fact]
    public async Task Tat_Phe_duyet_san_xuat_thi_KHONG_bam_Cho_chay_duoc()
    {
        await _fx.SeedUserAsync("ap-norun", Pwd, UserRole.Qc);
        await TurnOffAsync("ap-norun", UserPermission.ApproveProduction);
        var c = await LoginAsync("ap-norun");

        var resp = await c.SendAsync(Judgment("GoRun"));

        // ← đỏ nếu cột "Phê duyệt sản xuất" chỉ để trang trí.
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.Equal("ipqc.go_run_forbidden", await CodeOf(resp));
    }

    [Fact]
    public async Task Tat_Phe_duyet_san_xuat_van_bam_Dung_chuyen_duoc()
    {
        // Quan trọng về an toàn: mất quyền CHO CHẠY không được làm mất quyền
        // DỪNG CHUYỀN. Chặn người ta báo hỏng là kiểu siết nguy hiểm nhất.
        await _fx.SeedUserAsync("ap-norun2", Pwd, UserRole.Qc);
        await TurnOffAsync("ap-norun2", UserPermission.ApproveProduction);
        var c = await LoginAsync("ap-norun2");

        var resp = await c.SendAsync(Judgment("StopLine"));
        Assert.NotEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task QC_khong_co_quyen_Chap_nhan_dac_biet_van_de_nghi_duoc()
    {
        // Ca này bắt được một lần siết nhầm THẬT trong lúc làm: bản đầu gác nút
        // "Chấp nhận đặc biệt" của IPQC bằng quyền `SpecialAccept`, mà vai QC
        // mặc định KHÔNG có quyền đó ⇒ người phát hiện hàng lỗi mất luôn đường
        // leo thang, chỉ còn Dừng chuyền. Nút ấy chỉ ĐỀ NGHỊ (đẩy sang QA_PENDING),
        // nhượng bộ thật nằm ở waiver vật tư và ở bước QA duyệt.
        var c = await ClientAsync("ap-sa-qc", UserRole.Qc);
        Assert.False(UserPermission.DefaultForRole(UserRole.Qc, UserPermission.SpecialAccept));

        Assert.NotEqual(HttpStatusCode.Forbidden, (await c.SendAsync(Judgment("SpecialAccept"))).StatusCode);
    }

    [Fact]
    public async Task Tat_Phe_duyet_QC_thi_mat_Dung_chuyen_nhung_con_Cho_chay()
    {
        await _fx.SeedUserAsync("ap-noqc", Pwd, UserRole.Qc);
        await TurnOffAsync("ap-noqc", UserPermission.ApproveQc);
        var c = await LoginAsync("ap-noqc");

        var blocked = await c.SendAsync(Judgment("StopLine"));
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Equal("ipqc.stop_line_forbidden", await CodeOf(blocked));

        // ← đỏ nếu ai đó gộp lại thành một quyền: hai nút, hai quyết định.
        Assert.NotEqual(HttpStatusCode.Forbidden, (await c.SendAsync(Judgment("GoRun"))).StatusCode);
    }

    // ── ❸ Prepress: rời cổng cần quyền duyệt sản xuất, pha khác thì không ──

    [Fact]
    public async Task Tat_Phe_duyet_san_xuat_thi_lenh_khong_roi_Prepress_duoc()
    {
        var woId = await SeedWoAsync("WO-AP-BLOCK", "PREPRESS");
        await _fx.SeedUserAsync("ap-preblock", Pwd, UserRole.Operator);
        await TurnOffAsync("ap-preblock", UserPermission.ApproveProduction);
        var c = await LoginAsync("ap-preblock");

        var resp = await c.SendAsync(Advance(woId));
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.Equal("wo.advance_approve_production_forbidden", await CodeOf(resp));
    }

    [Fact]
    public async Task Tat_Sua_du_lieu_KHONG_chan_duoc_cua_Prepress()
    {
        // Chứng minh ánh xạ thật sự theo PHA, không phải "cần cả hai quyền".
        // Nếu chỗ này đỏ nghĩa là đã âm thầm siết thành AND.
        var woId = await SeedWoAsync("WO-AP-EDITOFF", "PREPRESS");
        await _fx.SeedUserAsync("ap-editoff", Pwd, UserRole.Operator);
        await TurnOffAsync("ap-editoff", UserPermission.EditData);
        var c = await LoginAsync("ap-editoff");

        var resp = await c.SendAsync(Advance(woId));
        Assert.NotEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Tat_Sua_du_lieu_van_chan_chuyen_buoc_o_pha_khac()
    {
        var woId = await SeedWoAsync("WO-AP-EDITOFF2", "SETTING");
        await _fx.SeedUserAsync("ap-editoff2", Pwd, UserRole.Operator);
        await TurnOffAsync("ap-editoff2", UserPermission.EditData);
        var c = await LoginAsync("ap-editoff2");

        var resp = await c.SendAsync(Advance(woId));
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.Equal("wo.advance_forbidden", await CodeOf(resp));
    }
}
