using System.Net;
using System.Net.Http.Json;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Auth;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Shared.Envelopes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Api.Tests;

/// <summary>
/// Ký điện tử khi duyệt IPQC (Thiệp chốt 2026-09-11): người đánh giá tại chuyền
/// phải gõ lại tài khoản + mật khẩu của chính họ thì mới phán định và chuyển
/// bước được.
///
/// <para>Người ký CÓ THỂ khác người đang đăng nhập — cả chuyền dùng chung một
/// máy. Hồ sơ phải ghi cả hai danh tính, và <b>tuyệt đối không ghi mật khẩu</b>.</para>
/// </summary>
public sealed class IpqcSignatureWireTests : IClassFixture<MesApiFactory>
{
    private const string Pwd = "P@ss!1";
    private const string SecretPwd = "Sup3rSecret!Signer";
    private readonly MesApiFactory _fx;
    public IpqcSignatureWireTests(MesApiFactory fx) => _fx = fx;

    private async Task<HttpClient> ClientAsync(string user, string role)
    {
        await _fx.SeedUserAsync(user, Pwd, role);
        var c = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(c, user, Pwd);
        return c;
    }

    /// <summary>WO ở IPQC_WAIT với đúng một hạng mục đã xác nhận OK ⇒ sẵn sàng phán định.</summary>
    private async Task<(long WoId, string Etag)> SeedReadyWoAsync(string tag)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();

        var cust = new Customer { Code = "C-" + Guid.NewGuid().ToString("N")[..6], Name = "C" };
        db.Customers.Add(cust); await db.SaveChangesAsync();
        var prod = new Product { ProductCode = "P-" + Guid.NewGuid().ToString("N")[..8], Name = "P", CustomerId = cust.Id };
        db.Products.Add(prod); await db.SaveChangesAsync();

        var wo = new WorkOrder
        {
            WoNo = $"WO-SIG-{tag}-" + Guid.NewGuid().ToString("N")[..5],
            CustomerId = cust.Id, ProductId = prod.Id, ProductName = "P",
            TargetQty = 100, Uom = "pcs", MesPhase = "IPQC_WAIT",
            CurrentStep = ProcessStepCode.IpqcApproval, Status = WoStatus.InProgress,
        };
        db.WorkOrders.Add(wo); await db.SaveChangesAsync();

        var check = new WoIpqcCheck { WorkOrderId = wo.Id };
        db.WoIpqcChecks.Add(check); await db.SaveChangesAsync();
        db.WoIpqcCheckItems.Add(new WoIpqcCheckItem
        {
            WoIpqcCheckId = check.Id, ItemKey = "SIG-1", ProcessLine = "LABEL",
            Label = "Hạng mục thử", Status = IpqcCheckStatus.Ok, Applicable = true, Sort = 10,
        });
        await db.SaveChangesAsync();

        var rv = await db.WorkOrders.AsNoTracking().Where(w => w.Id == wo.Id)
            .Select(w => w.RowVersion).SingleAsync();
        return (wo.Id, Convert.ToBase64String(rv));
    }

    private static HttpRequestMessage Judgment(long woId, string etag, object body) =>
        new(HttpMethod.Post, $"/api/v2/work-orders/{woId}/ipqc/judgment")
        {
            Content = JsonContent.Create(body),
            Headers = { { "If-Match", $"\"{etag}\"" }, { "Idempotency-Key", Guid.NewGuid().ToString() } },
        };

    private async Task<List<AuditLog>> AuditAsync(long woId)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        return await db.AuditLogs.AsNoTracking()
            .Where(a => a.TargetType == "WorkOrder" && a.TargetId == woId.ToString())
            .ToListAsync();
    }

    // ── Không ký thì không qua ──────────────────────────────────────────────

    [Fact]
    public async Task Khong_go_chu_ky_thi_KHONG_phan_dinh_duoc()
    {
        var (woId, etag) = await SeedReadyWoAsync("nosig");
        var client = await ClientAsync("sig-nosig", UserRole.Qc);

        var resp = await client.SendAsync(Judgment(woId, etag, new { judgment = "GoRun" }));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("ipqc.signature_required", err!.Code);

        // Không được ghi gì: chốt chữ ký đứng TRƯỚC mọi phép mutate.
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var check = await db.WoIpqcChecks.AsNoTracking().FirstAsync(c => c.WorkOrderId == woId);
        Assert.Equal(IpqcJudgment.Pending, check.Judgment);
    }

    [Fact]
    public async Task Sai_mat_khau_thi_bi_tu_choi_va_KHONG_ghi_mat_khau_vao_audit()
    {
        // Đây là phép kiểm quan trọng nhất của cả tính năng.
        var (woId, etag) = await SeedReadyWoAsync("wrongpwd");
        await _fx.SeedUserAsync("sig-signer-a", SecretPwd, UserRole.Qc);
        var client = await ClientAsync("sig-wrong", UserRole.Qc);

        var resp = await client.SendAsync(Judgment(woId, etag, new
        {
            judgment = "GoRun",
            signerUsername = "sig-signer-a",
            signerPassword = "SAI-MAT-KHAU-HOAN-TOAN",
        }));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);

        var rows = await AuditAsync(woId);
        var denied = Assert.Single(rows, a => a.Action == AuditAction.WoIpqcSignDenied);
        Assert.Contains("sig-signer-a", denied.Detail);                 // tên gõ vào: CÓ
        Assert.DoesNotContain("SAI-MAT-KHAU", denied.Detail);           // ← đỏ nếu lọt mật khẩu
        Assert.All(rows, a => Assert.DoesNotContain(SecretPwd, a.Detail ?? ""));
    }

    [Fact]
    public async Task Tai_khoan_khong_ton_tai_va_sai_mat_khau_tra_CUNG_mot_loi()
    {
        // Tách hai ca ra là giúp người dò biết tài khoản nào có thật.
        var (wo1, e1) = await SeedReadyWoAsync("ghost");
        var (wo2, e2) = await SeedReadyWoAsync("real");
        await _fx.SeedUserAsync("sig-signer-b", SecretPwd, UserRole.Qc);
        var client = await ClientAsync("sig-probe", UserRole.Qc);

        var r1 = await client.SendAsync(Judgment(wo1, e1, new
        { judgment = "GoRun", signerUsername = "khong-ton-tai-bao-gio", signerPassword = "x" }));
        var r2 = await client.SendAsync(Judgment(wo2, e2, new
        { judgment = "GoRun", signerUsername = "sig-signer-b", signerPassword = "sai" }));

        Assert.Equal(r1.StatusCode, r2.StatusCode);
        Assert.Equal(
            (await r1.Content.ReadFromJsonAsync<ApiError>())!.Code,
            (await r2.Content.ReadFromJsonAsync<ApiError>())!.Code);
    }

    // ── Ký đúng ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Ky_dung_thi_phan_dinh_duoc_va_ho_so_dung_ten_NGUOI_KY()
    {
        // Máy đăng nhập bằng tài khoản A, người đánh giá IPQC ký bằng tài khoản
        // B. Hồ sơ phải đứng tên B — luật chữ ký kép sau đó so người duyệt QA
        // với chính trường này.
        var (woId, etag) = await SeedReadyWoAsync("ok");
        await _fx.SeedUserAsync("sig-signer-c", SecretPwd, UserRole.Qc);
        var client = await ClientAsync("sig-session-user", UserRole.Qc);

        var resp = await client.SendAsync(Judgment(woId, etag, new
        {
            judgment = "GoRun",
            signerUsername = "sig-signer-c",
            signerPassword = SecretPwd,
        }));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var check = await db.WoIpqcChecks.AsNoTracking().FirstAsync(c => c.WorkOrderId == woId);
        Assert.Equal(IpqcJudgment.GoRun, check.Judgment);
        Assert.Equal("sig-signer-c", check.IpqcSubmittedBy);            // ← đỏ nếu ghi tên phiên

        // Audit ghi CẢ HAI danh tính, không có mật khẩu.
        var judged = Assert.Single(await AuditAsync(woId), a => a.Action == AuditAction.WoIpqcJudgment);
        Assert.Contains("sig-signer-c", judged.Detail);
        Assert.Contains("sig-session-user", judged.Detail);
        Assert.DoesNotContain(SecretPwd, judged.Detail ?? "");
    }

    [Fact]
    public async Task Nguoi_ky_KHONG_du_vai_thi_bi_chan_du_mat_khau_dung()
    {
        // Mật khẩu đúng không có nghĩa là được phán định IPQC.
        var (woId, etag) = await SeedReadyWoAsync("role");
        await _fx.SeedUserAsync("sig-operator", SecretPwd, UserRole.Operator);
        var client = await ClientAsync("sig-role", UserRole.Qc);

        var resp = await client.SendAsync(Judgment(woId, etag, new
        {
            judgment = "GoRun", signerUsername = "sig-operator", signerPassword = SecretPwd,
        }));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("ipqc.signer_not_allowed", err!.Code);
    }

    // ── Khoá thử-sai ────────────────────────────────────────────────────────

    [Fact]
    public async Task Go_sai_qua_nhieu_lan_thi_tai_khoan_bi_KHOA()
    {
        // Không có khoá thì ô mật khẩu tại chuyền thành chỗ dò mật khẩu đồng
        // nghiệp — và động cơ có sẵn: ký duyệt chính lô mình vừa làm hỏng.
        await _fx.SeedUserAsync("sig-victim", SecretPwd, UserRole.Qc);
        var client = await ClientAsync("sig-attacker", UserRole.Qc);

        HttpResponseMessage? last = null;
        for (var i = 0; i <= CCL.MES.Api.Auth.ReauthThrottle.MaxFailures; i++)
        {
            var (woId, etag) = await SeedReadyWoAsync($"lock{i}");
            last = await client.SendAsync(Judgment(woId, etag, new
            { judgment = "GoRun", signerUsername = "sig-victim", signerPassword = $"doan-{i}" }));
        }

        var err = await last!.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("ipqc.signature_locked", err!.Code);               // ← đỏ nếu bỏ khoá
    }
}
