using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
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
/// Ký điện tử khi KỸ SƯ PHÊ DUYỆT vật tư lệch (Thiệp chốt 2026-09-14).
///
/// <para>Waiver là quyết định KỸ THUẬT chịu trách nhiệm cho một lô vật tư lệch
/// dữ liệu IQC. Hồ sơ phải đứng tên ĐÚNG kỹ sư đã duyệt, không phải tên tài
/// khoản đang mở trên máy chung của chuyền.</para>
///
/// <para><b>Và nó làm luật 4-mắt DÙNG ĐƯỢC.</b> Trước đây luật so người duyệt
/// với người xác nhận bằng tên PHIÊN, nên trên một máy đăng nhập bằng
/// <c>admin</c> thì mọi waiver đều bị chặn oan — đúng cảnh Thiệp gặp ngày
/// 14-09. So bằng tên NGƯỜI KÝ thì kỹ sư tới ký là qua.</para>
/// </summary>
public sealed class IpqcWaiverSignatureWireTests : IClassFixture<MesApiFactory>
{
    private const string Pwd = "P@ss!1";
    private const string EngPwd = "Eng!Secret9";
    private readonly MesApiFactory _fx;
    public IpqcWaiverSignatureWireTests(MesApiFactory fx) => _fx = fx;

    private async Task<HttpClient> ClientAsync(string user, string role)
    {
        await _fx.SeedUserAsync(user, Pwd, role);
        var c = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(c, user, Pwd);
        return c;
    }

    /// <summary>WO ở IPQC_WAIT + một dòng vật tư LỆCH đã được xác nhận ⇒ chờ kỹ sư.</summary>
    private async Task<(long WoId, string ConfirmedBy)> SeedPendingWaiverAsync(string tag, string confirmer)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();

        var cust = new Customer { Code = "C-" + Guid.NewGuid().ToString("N")[..6], Name = "C" };
        db.Customers.Add(cust); await db.SaveChangesAsync();
        var prod = new Product { ProductCode = "P-" + Guid.NewGuid().ToString("N")[..8], Name = "P", CustomerId = cust.Id };
        db.Products.Add(prod); await db.SaveChangesAsync();

        var wo = new WorkOrder
        {
            WoNo = $"WO-WSIG-{tag}-" + Guid.NewGuid().ToString("N")[..5],
            CustomerId = cust.Id, ProductId = prod.Id, ProductName = "P",
            TargetQty = 100, Uom = "pcs", MesPhase = "IPQC_WAIT",
            CurrentStep = ProcessStepCode.IpqcApproval, Status = WoStatus.InProgress,
        };
        db.WorkOrders.Add(wo); await db.SaveChangesAsync();

        db.WoIpqcMaterialChecks.Add(new WoIpqcMaterialCheck
        {
            WorkOrderId = wo.Id, BomLineIdx = 0, MaterialCode = "MAT-LECH",
            Status = IpqcCheckStatus.Ok,
            DivergenceKind = "IqcNotPass",
            DivergenceApprovalStatus = DivergenceApprovalStatus.PendingEngineer,
            ConfirmedBy = confirmer, ConfirmedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return (wo.Id, confirmer);
    }

    private async Task<string> EtagAsync(long woId)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var rv = await db.WorkOrders.AsNoTracking().Where(w => w.Id == woId)
            .Select(w => w.RowVersion).SingleAsync();
        return Convert.ToBase64String(rv);
    }

    private static HttpRequestMessage Approve(long woId, string etag, object body) =>
        new(HttpMethod.Post, $"/api/v2/work-orders/{woId}/ipqc/material-system/0/approve-divergence")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
            Headers = { { "If-Match", $"\"{etag}\"" }, { "Idempotency-Key", Guid.NewGuid().ToString() } },
        };

    private async Task<WoIpqcMaterialCheck> RowAsync(long woId)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        return await db.WoIpqcMaterialChecks.AsNoTracking()
            .FirstAsync(r => r.WorkOrderId == woId && r.BomLineIdx == 0);
    }

    private async Task<List<AuditLog>> AuditAsync(long woId)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        return await db.AuditLogs.AsNoTracking()
            .Where(a => a.TargetType == "WorkOrder" && a.TargetId == woId.ToString())
            .ToListAsync();
    }

    // ── Không ký thì không duyệt ────────────────────────────────────────────

    [Fact]
    public async Task Khong_ky_thi_KHONG_phe_duyet_duoc()
    {
        var (wo, _) = await SeedPendingWaiverAsync("nosig", "qc-w-confirmer");
        var client = await ClientAsync("eng-w-nosig", UserRole.Engineer);

        var resp = await client.SendAsync(Approve(wo, await EtagAsync(wo),
            new { outcome = "Approve", reason = "thiếu chữ ký" }));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("ipqc.signature_required", err!.Code);

        // Chốt chữ ký đứng TRƯỚC mọi phép ghi.
        var row = await RowAsync(wo);
        Assert.Equal(DivergenceApprovalStatus.PendingEngineer, row.DivergenceApprovalStatus);
    }

    [Fact]
    public async Task Sai_mat_khau_thi_bi_tu_choi_va_KHONG_ghi_mat_khau_vao_audit()
    {
        var (wo, _) = await SeedPendingWaiverAsync("badpwd", "qc-w-confirmer2");
        await _fx.SeedUserAsync("eng-w-victim", EngPwd, UserRole.Engineer);
        var client = await ClientAsync("eng-w-caller", UserRole.Engineer);

        var resp = await client.SendAsync(Approve(wo, await EtagAsync(wo), new
        {
            outcome = "Approve", reason = "thử sai",
            signerUsername = "eng-w-victim", signerPassword = "SAI-MAT-KHAU-HOAN-TOAN",
        }));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);

        var rows = await AuditAsync(wo);
        var denied = Assert.Single(rows, a => a.Action == "WO_IPQC_MATERIAL_SIGN_DENIED");
        Assert.Contains("eng-w-victim", denied.Detail);              // tên gõ vào: CÓ
        Assert.DoesNotContain("SAI-MAT-KHAU", denied.Detail);        // ← đỏ nếu lọt mật khẩu
        Assert.All(rows, a => Assert.DoesNotContain(EngPwd, a.Detail ?? ""));
    }

    [Fact]
    public async Task Vai_QC_khong_duoc_ky_waiver_du_mat_khau_dung()
    {
        // Mật khẩu đúng không có nghĩa là được ký duyệt KỸ THUẬT. Tập vai của
        // waiver (Admin·Engineer·Supervisor) khác tập vai phán định IPQC.
        var (wo, _) = await SeedPendingWaiverAsync("role", "qc-w-confirmer3");
        await _fx.SeedUserAsync("qc-w-signer", EngPwd, UserRole.Qc);
        var client = await ClientAsync("eng-w-role", UserRole.Engineer);

        var resp = await client.SendAsync(Approve(wo, await EtagAsync(wo), new
        {
            outcome = "Approve", reason = "QC ký thử",
            signerUsername = "qc-w-signer", signerPassword = EngPwd,
        }));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("ipqc.signer_not_allowed", err!.Code);
    }

    [Fact]
    public async Task Tai_khoan_con_dung_MAT_KHAU_SEED_thi_khong_duoc_ky()
    {
        // Seed đặt mật khẩu = chính tên tài khoản (CLAUDE.md §0). Đo 14-09:
        // engineer · supervisor · OQC đều còn ở trạng thái này, tức ai đứng ở
        // máy cũng ký thay họ được. Chữ ký sinh ra để chứng minh AI quyết định;
        // một mật khẩu đoán được làm nó vô nghĩa.
        var (wo, _) = await SeedPendingWaiverAsync("seedpwd", "qc-w-confirmer5");
        await _fx.SeedUserAsync("eng-w-seed", EngPwd, UserRole.Engineer);

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
            var u = await db.Users.FirstAsync(x => x.Username == "eng-w-seed");
            u.MustChangePassword = true;            // chưa từng tự đặt mật khẩu
            await db.SaveChangesAsync();
        }

        var client = await ClientAsync("admin-w-seed", UserRole.Admin);
        var resp = await client.SendAsync(Approve(wo, await EtagAsync(wo), new
        {
            outcome = "Approve", reason = "ký bằng tài khoản chưa đổi mật khẩu",
            signerUsername = "eng-w-seed", signerPassword = EngPwd,
        }));

        // Mật khẩu ĐÚNG, vai ĐÚNG — vẫn phải chặn.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("ipqc.signature_password_not_set", err!.Code);

        var row = await RowAsync(wo);
        Assert.Equal(DivergenceApprovalStatus.PendingEngineer, row.DivergenceApprovalStatus);
    }

    // ── Ký đúng ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Ky_dung_thi_ho_so_dung_ten_NGUOI_KY_chu_khong_phai_ten_phien()
    {
        // Máy đăng nhập bằng admin; kỹ sư đi tới ký bằng tài khoản của mình.
        var (wo, _) = await SeedPendingWaiverAsync("ok", "qc-w-confirmer4");
        await _fx.SeedUserAsync("eng-w-real", EngPwd, UserRole.Engineer);
        var client = await ClientAsync("admin-w-session", UserRole.Admin);

        var resp = await client.SendAsync(Approve(wo, await EtagAsync(wo), new
        {
            outcome = "Approve", reason = "Lô thay thế đã kiểm, cho dùng",
            signerUsername = "eng-w-real", signerPassword = EngPwd,
        }));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var row = await RowAsync(wo);
        Assert.Equal(DivergenceApprovalStatus.Approved, row.DivergenceApprovalStatus);
        Assert.Equal("eng-w-real", row.ApprovedBy);   // ← đỏ nếu đóng dấu tên phiên
    }

    [Fact]
    public async Task Luat_4_mat_so_NGUOI_KY_nen_may_dung_chung_khong_bi_chan_oan()
    {
        // Đây chính là ca đã chặn Thiệp ngày 14-09: máy đăng nhập bằng admin,
        // và admin cũng là người đã xác nhận dòng vật tư. So bằng tên PHIÊN thì
        // mọi kỹ sư đều bị chặn; so bằng tên NGƯỜI KÝ thì kỹ sư khác ký được.
        var (wo, _) = await SeedPendingWaiverAsync("dual", "admin-w-dual");
        await _fx.SeedUserAsync("eng-w-dual", EngPwd, UserRole.Engineer);
        var client = await ClientAsync("admin-w-dual", UserRole.Admin);

        var ok = await client.SendAsync(Approve(wo, await EtagAsync(wo), new
        {
            outcome = "Approve", reason = "kỹ sư khác ký",
            signerUsername = "eng-w-dual", signerPassword = EngPwd,
        }));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task Nguoi_ky_TRUNG_nguoi_xac_nhan_thi_van_bi_chan()
    {
        // Mặt còn lại: nới cho máy dùng chung KHÔNG được làm hỏng luật 4-mắt.
        var (wo, _) = await SeedPendingWaiverAsync("same", "eng-w-self");
        await _fx.SeedUserAsync("eng-w-self", EngPwd, UserRole.Engineer);
        var client = await ClientAsync("admin-w-same", UserRole.Admin);

        var resp = await client.SendAsync(Approve(wo, await EtagAsync(wo), new
        {
            outcome = "Approve", reason = "tự ký",
            signerUsername = "eng-w-self", signerPassword = EngPwd,
        }));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("material.same_user_as_confirmer", err!.Code);   // ← đỏ nếu nới luật 4-mắt
    }
}
