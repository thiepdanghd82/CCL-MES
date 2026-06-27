using System.Net;
using System.Net.Http.Json;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain;
using CCL.MES.Domain.Auth;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Shared.IpqcReview;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Api.Tests;

/// <summary>
/// Phương án C — F1 (chặn slot-PUT khi đã có items) + F2 (self-heal materialize +
/// autoSyncStatus KHÔNG im lặng). Seed routing + library + ProcessLineMap để đi
/// đúng đường auto-sync qua HTTP.
/// </summary>
public sealed class IpqcAutoSyncControllerTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public IpqcAutoSyncControllerTests(MesApiFactory fx) => _fx = fx;

    private async Task<HttpClient> QcClientAsync(string user)
    {
        await _fx.SeedUserAsync(user, "P@ss!1", UserRole.Qc);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, user, "P@ss!1");
        return client;
    }

    private async Task SeedLibraryAndMapAsync()
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        if (!await db.CheckItemLibraries.AnyAsync(c => c.ProcessLine == "LABEL"))
        {
            db.CheckItemLibraries.Add(new CheckItemLibrary
            {
                ItemId = "TST-LBL-1", ProcessLine = "LABEL", QcStage = "IPQC", GroupLabel = "A",
                Code = "A1", ItemVi = "Nội dung", ItemEn = "Content", AcceptanceVi = "OK",
                AcceptanceEn = "OK", DefectCode = "TST_CONTENT", Active = true, Sort = 10,
            });
            await db.SaveChangesAsync();
        }
        await DbSeeder.SeedProcessLineMapAsync(db);
    }

    /// <summary>Tạo customer + product(code) + (tùy chọn) routing + WO ở IPQC_WAIT.</summary>
    private async Task<(long WoId, string Etag, string ProductCode)> SeedWoAsync(
        string productCode, (string op, string wc, string desc)[]? routing, string phase = "IPQC_WAIT")
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var cust = new Customer { Code = "C-" + Guid.NewGuid().ToString("N")[..6], Name = "Cust" };
        db.Customers.Add(cust); await db.SaveChangesAsync();
        var prod = new Product { ProductCode = productCode, Name = "Prod", CustomerId = cust.Id };
        db.Products.Add(prod); await db.SaveChangesAsync(); // prod.Id phải có trước khi WO tham chiếu
        if (routing is not null)
            foreach (var (op, wc, desc) in routing)
                db.RoutingOperations.Add(new RoutingOperation { PartNo = productCode, OpNo = op, Operation = "op", WorkCenterNo = wc, WorkCenterDescription = desc });
        var wo = new WorkOrder
        {
            WoNo = "WO-AS-" + Guid.NewGuid().ToString("N")[..6], CustomerId = cust.Id,
            ProductId = prod.Id, ProductName = prod.Name, TargetQty = 1000, Uom = "pcs",
            CurrentStep = ProcessStepCode.IpqcApproval, MesPhase = phase, Status = WoStatus.InProgress,
        };
        db.WorkOrders.Add(wo); await db.SaveChangesAsync();
        var rv = await db.WorkOrders.AsNoTracking().Where(w => w.Id == wo.Id).Select(w => w.RowVersion).SingleAsync();
        return (wo.Id, Convert.ToBase64String(rv), productCode);
    }

    private static HttpRequestMessage Put(string url, string body, string etag) =>
        new(HttpMethod.Put, url)
        {
            Content = JsonContent.Create(System.Text.Json.JsonSerializer.Deserialize<object>(body)),
            Headers = { { "If-Match", $"\"{etag}\"" }, { "Idempotency-Key", Guid.NewGuid().ToString() } },
        };

    // ── F1: slot-PUT bị chặn khi WO đã materialize items ────────────

    [Fact]
    public async Task F1_slot_put_rejected_when_items_materialized()
    {
        await SeedLibraryAndMapAsync();
        var (wo, _, _) = await SeedWoAsync("80644935", new[] { ("20", "GFL01", "Flexo (Gallus 4C)") });
        var client = await QcClientAsync("as-f1");

        // GET materialize items (LABEL).
        var view = await client.GetFromJsonAsync<IpqcView>($"/api/v2/work-orders/{wo}/ipqc");
        Assert.NotEmpty(view!.Items);
        Assert.Equal("Materialized", view.AutoSyncStatus);

        // PUT slot legacy → 422 slot_write_in_item_mode.
        var resp = await client.SendAsync(Put($"/api/v2/work-orders/{wo}/ipqc/material",
            "{\"status\":\"Ok\"}", view.ETag));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<CCL.MES.Shared.Envelopes.ApiError>();
        Assert.Equal("ipqc.slot_write_in_item_mode", err!.Code);
    }

    [Fact]
    public async Task F1_slot_put_allowed_on_legacy_wo_without_routing()
    {
        await SeedLibraryAndMapAsync();
        var (wo, etag, _) = await SeedWoAsync("NO-ROUTE-1", routing: null);
        var client = await QcClientAsync("as-f1b");
        var resp = await client.SendAsync(Put($"/api/v2/work-orders/{wo}/ipqc/material", "{\"status\":\"Ok\"}", etag));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    // ── F2: self-heal + autoSyncStatus ──────────────────────────────

    [Fact]
    public async Task F2a_self_heal_materializes_when_routing_added_after_first_get()
    {
        await SeedLibraryAndMapAsync();
        // Product KHÔNG routing lúc đầu.
        var (wo, _, code) = await SeedWoAsync("80644935", routing: null);
        var client = await QcClientAsync("as-f2a");

        var v1 = await client.GetFromJsonAsync<IpqcView>($"/api/v2/work-orders/{wo}/ipqc");
        Assert.Empty(v1!.Items);
        Assert.Equal("LegacyManual", v1.AutoSyncStatus);

        // Thêm routing SAU → GET lần 2 self-heal.
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
            db.RoutingOperations.Add(new RoutingOperation { PartNo = code, OpNo = "20", Operation = "op", WorkCenterNo = "GFL01", WorkCenterDescription = "Flexo (Gallus 4C)" });
            await db.SaveChangesAsync();
        }
        var v2 = await client.GetFromJsonAsync<IpqcView>($"/api/v2/work-orders/{wo}/ipqc");
        Assert.NotEmpty(v2!.Items);
        Assert.Equal("Materialized", v2.AutoSyncStatus);
    }

    [Fact]
    public async Task F2b_unmapped_workcenter_reports_skipped_unmapped_no_items()
    {
        await SeedLibraryAndMapAsync();
        var (wo, _, _) = await SeedWoAsync("UNMAP-1", new[] { ("10", "NGF1", "NextGen rig") });
        var client = await QcClientAsync("as-f2b");
        var v = await client.GetFromJsonAsync<IpqcView>($"/api/v2/work-orders/{wo}/ipqc");
        Assert.Empty(v!.Items);
        Assert.Equal("SkippedUnmapped", v.AutoSyncStatus);
    }

    [Fact]
    public async Task F2c_existing_legacy_slot_data_is_not_overwritten()
    {
        await SeedLibraryAndMapAsync();
        var (wo, _, _) = await SeedWoAsync("80644935", new[] { ("20", "GFL01", "Flexo (Gallus 4C)") });
        // Operator đã nhập 1 slot legacy TRƯỚC khi auto-sync (check không pristine).
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
            db.WoIpqcChecks.Add(new WoIpqcCheck
            {
                WorkOrderId = wo, MaterialStatus = IpqcCheckStatus.Ok,
                PrintAStatus = IpqcCheckStatus.Pending, PrintBStatus = IpqcCheckStatus.Pending,
                PrintCStatus = IpqcCheckStatus.Pending, Judgment = IpqcJudgment.Pending, QaOutcome = QaOutcome.Pending,
            });
            await db.SaveChangesAsync();
        }
        var client = await QcClientAsync("as-f2c");
        var v = await client.GetFromJsonAsync<IpqcView>($"/api/v2/work-orders/{wo}/ipqc");
        Assert.Empty(v!.Items);                       // KHÔNG tự materialize đè dữ liệu
        Assert.Equal("LegacyManual", v.AutoSyncStatus);
        Assert.Equal("Ok", v.MaterialStatus);          // dữ liệu operator còn nguyên
    }
}
