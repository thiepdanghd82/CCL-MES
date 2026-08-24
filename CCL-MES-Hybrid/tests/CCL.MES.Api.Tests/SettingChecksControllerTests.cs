using System.Net;
using System.Net.Http.Json;
using System.Text;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain;
using CCL.MES.Domain.Auth;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Shared.SettingChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Api.Tests;

/// <summary>
/// P10.7g — SettingChecksController coverage.
///
/// Status contract mirrors 7c-2 RunningSurfaceController:
///   200 success + bumped ETag + post-write Ready rollup
///   400 missing Idempotency-Key
///   403 role-gated (Operator on add-defect; SettingItemAdd policy)
///   404 WO not found
///   409 stale If-Match + WO_STATE_CONFLICT audit
///   422 wo.invalid_phase / setting.invalid_status / setting.invalid_defect /
///       setting.invalid_ng_note / setting.incomplete
///   428 missing If-Match
///
/// RBAC (QD): set-item = Admin|QC|Supervisor|Engineer|Operator;
/// add-item (F4) Engineer+ writes master + ad-hoc, Operator writes ad-hoc only;
/// add-defect (QC-add-new) Engineer+ (Operator 403).
///
/// Rule 7.3 wire-mirror: the audit-visibility test calls the same
/// /api/v2/audit/log URL the checkpoint script uses.
/// </summary>
public sealed class SettingChecksControllerTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public SettingChecksControllerTests(MesApiFactory fx) => _fx = fx;

    // ── Seed helpers ───────────────────────────────────────────────

    private async Task EnsureSettingLibraryAsync()
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        await DbSeeder.SeedSettingLibraryAsync(db);
    }

    private async Task<(long WoId, string Etag, string ProductCode)> SeedWoAsync(string mesPhase = "SETTING")
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var customer = new Customer { Code = "C-" + Guid.NewGuid().ToString("N")[..6], Name = "Cust" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var productCode = "P-" + Guid.NewGuid().ToString("N")[..6];
        var product = new Product { ProductCode = productCode, Name = "Prod", CustomerId = customer.Id };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        var wo = new WorkOrder
        {
            WoNo = "WO-7G-" + Guid.NewGuid().ToString("N")[..6],
            CustomerId = customer.Id,
            ProductId = product.Id,
            ProductName = product.Name,
            TargetQty = 1000,
            Uom = "pcs",
            CurrentStep = ProcessStepCode.PrePressCheck,
            MesPhase = mesPhase,
            Status = WoStatus.InProgress,
            SettingStartAt = DateTime.UtcNow.AddMinutes(-5),
        };
        db.WorkOrders.Add(wo);
        await db.SaveChangesAsync();

        var freshRv = await db.WorkOrders.AsNoTracking()
            .Where(w => w.Id == wo.Id).Select(w => w.RowVersion).SingleAsync();
        return (wo.Id, Convert.ToBase64String(freshRv), productCode);
    }

    private async Task<HttpClient> RoleClientAsync(string user, string role)
    {
        await _fx.SeedUserAsync(user, "P@ss!1", role);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, user, "P@ss!1");
        return client;
    }

    private Task<HttpClient> OperatorClientAsync(string u) => RoleClientAsync(u, UserRole.Operator);
    private Task<HttpClient> EngineerClientAsync(string u) => RoleClientAsync(u, UserRole.Engineer);

    private static HttpRequestMessage Mk(HttpMethod method, string path, string body, string? ifMatch, string? idem)
    {
        var req = new HttpRequestMessage(method, path)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (ifMatch is not null) req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        if (idem is not null) req.Headers.TryAddWithoutValidation("Idempotency-Key", idem);
        return req;
    }

    private async Task<string> CurrentEtagAsync(long woId)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var rv = await db.WorkOrders.AsNoTracking().Where(w => w.Id == woId)
            .Select(w => w.RowVersion).SingleAsync();
        return Convert.ToBase64String(rv);
    }

    // ── GET — lazy-materialise + view ──────────────────────────────

    [Fact]
    public async Task Get_lazy_materialises_20_items_and_returns_view()
    {
        await EnsureSettingLibraryAsync();
        var (wo, _, _) = await SeedWoAsync();
        var client = await OperatorClientAsync("op-7g-get");

        var resp = await client.GetAsync($"/api/v2/work-orders/{wo}/setting-checks");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var view = await resp.Content.ReadFromJsonAsync<SettingChecksView>();
        Assert.NotNull(view);
        // Both processes apply (no routing seeded → SettingProcessScope both-true).
        Assert.True(view!.HasPrint);
        Assert.True(view.HasCut);
        Assert.Equal(20, view.Items.Count);
        Assert.False(view.Ready); // all Pending
        // Each Print/Cut item carries its defect drop-list (base options).
        var printItem = view.Items.First(i => i.ProcessKind == "Print");
        Assert.NotEmpty(printItem.DefectOptions);

        // Second GET does not double-materialise.
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var count = await db.WoSettingCheckItems.CountAsync(i => i.WorkOrderId == wo);
        Assert.Equal(20, count);
    }

    [Fact]
    public async Task Get_returns_404_for_unknown_wo()
    {
        var client = await OperatorClientAsync("op-7g-404");
        var resp = await client.GetAsync($"/api/v2/work-orders/{long.MaxValue}/setting-checks");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ── Prelude — 428 / 400 / 409 ─────────────────────────────────

    [Fact]
    public async Task PutItem_missing_IfMatch_returns_428()
    {
        await EnsureSettingLibraryAsync();
        var (wo, _, _) = await SeedWoAsync();
        var client = await OperatorClientAsync("op-7g-428");
        await client.GetAsync($"/api/v2/work-orders/{wo}/setting-checks"); // materialise
        var resp = await client.SendAsync(Mk(HttpMethod.Put,
            $"/api/v2/work-orders/{wo}/setting-checks/SET-PR-00",
            "{\"status\":\"Ok\"}", ifMatch: null, idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.PreconditionRequired, resp.StatusCode);
    }

    [Fact]
    public async Task PutItem_missing_Idem_returns_400()
    {
        await EnsureSettingLibraryAsync();
        var (wo, etag, _) = await SeedWoAsync();
        var client = await OperatorClientAsync("op-7g-400");
        var resp = await client.SendAsync(Mk(HttpMethod.Put,
            $"/api/v2/work-orders/{wo}/setting-checks/SET-PR-00",
            "{\"status\":\"Ok\"}", ifMatch: $"\"{etag}\"", idem: null));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task PutItem_stale_IfMatch_returns_409_and_emits_conflict_audit()
    {
        await EnsureSettingLibraryAsync();
        var (wo, _, _) = await SeedWoAsync();
        var client = await OperatorClientAsync("op-7g-409");
        var resp = await client.SendAsync(Mk(HttpMethod.Put,
            $"/api/v2/work-orders/{wo}/setting-checks/SET-PR-00",
            "{\"status\":\"Ok\"}", ifMatch: "\"AAAA\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<SettingChecksSetResponse>();
        Assert.Equal("wo.state_conflict", body!.ErrorCode);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var conflict = await db.AuditLogs.FirstOrDefaultAsync(a =>
            a.Action == "WO_STATE_CONFLICT" && a.TargetId == wo.ToString());
        Assert.NotNull(conflict);
    }

    // ── PUT — OK happy path + Ready rollup ────────────────────────

    [Fact]
    public async Task PutItem_status_OK_persists_and_bumps_ETag()
    {
        await EnsureSettingLibraryAsync();
        var (wo, etag, _) = await SeedWoAsync();
        var client = await OperatorClientAsync("op-7g-ok");
        await client.GetAsync($"/api/v2/work-orders/{wo}/setting-checks");
        var fresh = await CurrentEtagAsync(wo);

        var resp = await client.SendAsync(Mk(HttpMethod.Put,
            $"/api/v2/work-orders/{wo}/setting-checks/SET-PR-00",
            "{\"status\":\"Ok\",\"applicable\":true}",
            ifMatch: $"\"{fresh}\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<SettingChecksSetResponse>();
        Assert.True(body!.Ok);
        Assert.NotEqual(fresh, body.ETag); // bumped

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var item = await db.WoSettingCheckItems.FirstAsync(i => i.WorkOrderId == wo && i.ItemKey == "SET-PR-00");
        Assert.Equal(PrepressCheckStatus.Ok, item.Status);
    }

    // ── PUT — NG requires a catalog defect + note ─────────────────

    [Fact]
    public async Task PutItem_NG_without_defect_returns_422()
    {
        await EnsureSettingLibraryAsync();
        var (wo, _, _) = await SeedWoAsync();
        var client = await OperatorClientAsync("op-7g-ng1");
        await client.GetAsync($"/api/v2/work-orders/{wo}/setting-checks");
        var fresh = await CurrentEtagAsync(wo);

        var resp = await client.SendAsync(Mk(HttpMethod.Put,
            $"/api/v2/work-orders/{wo}/setting-checks/SET-PR-00",
            "{\"status\":\"Ng\",\"ngNote\":\"lem\"}",
            ifMatch: $"\"{fresh}\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [Fact]
    public async Task PutItem_NG_with_catalog_defect_persists()
    {
        await EnsureSettingLibraryAsync();
        var (wo, _, _) = await SeedWoAsync();
        var client = await OperatorClientAsync("op-7g-ng2");
        await client.GetAsync($"/api/v2/work-orders/{wo}/setting-checks");
        var fresh = await CurrentEtagAsync(wo);

        // pl_ver is a base defect of SET-PR-00.
        var resp = await client.SendAsync(Mk(HttpMethod.Put,
            $"/api/v2/work-orders/{wo}/setting-checks/SET-PR-00",
            "{\"status\":\"Ng\",\"defectCode\":\"pl_ver\",\"ngNote\":\"sai revision\"}",
            ifMatch: $"\"{fresh}\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var item = await db.WoSettingCheckItems.FirstAsync(i => i.WorkOrderId == wo && i.ItemKey == "SET-PR-00");
        Assert.Equal(PrepressCheckStatus.Ng, item.Status);
        Assert.Equal("pl_ver", item.DefectCode);
    }

    // ── Applicable=false excluded from the guard ──────────────────

    [Fact]
    public async Task Setting_done_422_when_incomplete_then_200_when_all_applicable_OK()
    {
        await EnsureSettingLibraryAsync();
        var (wo, _, _) = await SeedWoAsync("SETTING");
        var admin = await RoleClientAsync("admin-7g-done", UserRole.Admin);
        await admin.GetAsync($"/api/v2/work-orders/{wo}/setting-checks"); // materialise 20 items

        // done while everything is Pending → 422 setting.incomplete
        var etag0 = await CurrentEtagAsync(wo);
        var early = await admin.SendAsync(Mk(HttpMethod.Post,
            $"/api/v2/work-orders/{wo}/setting/done", "{}",
            ifMatch: $"\"{etag0}\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, early.StatusCode);
        var earlyBody = await early.Content.ReadAsStringAsync();
        Assert.Contains("setting.incomplete", earlyBody);

        // Mark 19 items OK; mark 1 item N/A (Applicable=false) → still complete.
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
            var items = await db.WoSettingCheckItems.Where(i => i.WorkOrderId == wo)
                .OrderBy(i => i.Sort).ToListAsync();
            for (var i = 0; i < items.Count; i++)
            {
                if (i == 0) { items[i].Applicable = false; items[i].Status = PrepressCheckStatus.Pending; }
                else { items[i].Status = PrepressCheckStatus.Ok; }
            }
            await db.SaveChangesAsync();
        }

        var etag1 = await CurrentEtagAsync(wo);
        var ok = await admin.SendAsync(Mk(HttpMethod.Post,
            $"/api/v2/work-orders/{wo}/setting/done", "{}",
            ifMatch: $"\"{etag1}\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        using var scope2 = _fx.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<MesDbContext>();
        var phase = await db2.WorkOrders.AsNoTracking().Where(w => w.Id == wo)
            .Select(w => w.MesPhase).SingleAsync();
        Assert.Equal("IPQC_WAIT", phase);
    }

    // ── F4 add-item RBAC ───────────────────────────────────────────

    [Fact]
    public async Task AddItem_operator_creates_adhoc_only_no_master()
    {
        await EnsureSettingLibraryAsync();
        var (wo, _, productCode) = await SeedWoAsync();
        var client = await OperatorClientAsync("op-7g-add");
        await client.GetAsync($"/api/v2/work-orders/{wo}/setting-checks");
        var fresh = await CurrentEtagAsync(wo);

        var resp = await client.SendAsync(Mk(HttpMethod.Post,
            $"/api/v2/work-orders/{wo}/setting-checks/item",
            "{\"processKind\":\"Print\",\"label\":\"Kiểm tra thêm\",\"standard\":\"OK\"}",
            ifMatch: $"\"{fresh}\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<SettingChecksSetResponse>();
        Assert.True(body!.Ok);
        Assert.NotNull(body.AddedKey);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var adhoc = await db.WoSettingCheckItems.FirstAsync(i => i.WorkOrderId == wo && i.AdHoc);
        Assert.True(adhoc.AdHoc);
        // Operator must NOT have written a per-product master library row.
        var master = await db.CheckItemLibraries.CountAsync(c => c.ProductCode == productCode);
        Assert.Equal(0, master);
    }

    [Fact]
    public async Task AddItem_engineer_also_writes_per_product_master()
    {
        await EnsureSettingLibraryAsync();
        var (wo, _, productCode) = await SeedWoAsync();
        var client = await EngineerClientAsync("eng-7g-add");
        await client.GetAsync($"/api/v2/work-orders/{wo}/setting-checks");
        var fresh = await CurrentEtagAsync(wo);

        var resp = await client.SendAsync(Mk(HttpMethod.Post,
            $"/api/v2/work-orders/{wo}/setting-checks/item",
            "{\"processKind\":\"Cut\",\"label\":\"Hạng mục Cut mới\",\"standard\":\"OK\"}",
            ifMatch: $"\"{fresh}\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var master = await db.CheckItemLibraries.CountAsync(c => c.ProductCode == productCode && c.Setting);
        Assert.Equal(1, master);
    }

    // ── QC-add-new defect RBAC ─────────────────────────────────────

    [Fact]
    public async Task AddDefect_operator_forbidden_403()
    {
        await EnsureSettingLibraryAsync();
        var (wo, _, _) = await SeedWoAsync();
        var client = await OperatorClientAsync("op-7g-defect");
        var fresh = await CurrentEtagAsync(wo);

        var resp = await client.SendAsync(Mk(HttpMethod.Post,
            $"/api/v2/work-orders/{wo}/setting-checks/defect",
            "{\"itemId\":\"SET-PR-00\",\"defectCode\":\"custom1\",\"labelVi\":\"Lỗi mới\",\"labelEn\":\"New defect\"}",
            ifMatch: $"\"{fresh}\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task AddDefect_engineer_registers_per_product_option()
    {
        await EnsureSettingLibraryAsync();
        var (wo, _, productCode) = await SeedWoAsync();
        var client = await EngineerClientAsync("eng-7g-defect");
        var fresh = await CurrentEtagAsync(wo);

        var resp = await client.SendAsync(Mk(HttpMethod.Post,
            $"/api/v2/work-orders/{wo}/setting-checks/defect",
            "{\"itemId\":\"SET-PR-00\",\"defectCode\":\"custom1\",\"labelVi\":\"Lỗi mới\",\"labelEn\":\"New defect\"}",
            ifMatch: $"\"{fresh}\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var opt = await db.CheckItemDefectOptions.FirstAsync(o =>
            o.ItemId == "SET-PR-00" && o.DefectCode == "custom1" && o.ProductCode == productCode);
        Assert.True(opt.PerProductGuard());
    }

    // ── Rule 7.3 wire-mirror ───────────────────────────────────────

    [Fact]
    public async Task Audit_visibility_via_wire_audit_log_endpoint()
    {
        await EnsureSettingLibraryAsync();
        var (wo, _, _) = await SeedWoAsync();
        var admin = await RoleClientAsync("admin-7g-audit", UserRole.Admin);
        await admin.GetAsync($"/api/v2/work-orders/{wo}/setting-checks");
        var fresh = await CurrentEtagAsync(wo);

        var put = await admin.SendAsync(Mk(HttpMethod.Put,
            $"/api/v2/work-orders/{wo}/setting-checks/SET-PR-00",
            "{\"status\":\"Ok\"}", ifMatch: $"\"{fresh}\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var log = await admin.GetAsync(
            "/api/v2/audit/log?action=WO_SETTING_ITEM_SET&page=1&pageSize=50");
        Assert.Equal(HttpStatusCode.OK, log.StatusCode);
        var text = await log.Content.ReadAsStringAsync();
        Assert.Contains("WO_SETTING_ITEM_SET", text);
        Assert.Contains(wo.ToString(), text);
    }
}

// Tiny helper so the test asserts intent clearly without leaking EF types.
file static class DefectOptionTestExtensions
{
    public static bool PerProductGuard(this CheckItemDefectOption o) => o.ProductCode != null;
}
