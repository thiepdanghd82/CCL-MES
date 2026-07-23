using System.Net;
using System.Net.Http.Json;
using System.Text;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain;
using CCL.MES.Domain.Auth;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Shared.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Api.Tests;

/// <summary>
/// P11.5-2 — SemiStockController (keep-stock decoupling) + RoutingController
/// FROM_STOCK gate. post/list · reserve FEFO/insufficient · consume · gate ·
/// soak concurrency per-lot.
/// </summary>
public sealed class SemiStockControllerTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public SemiStockControllerTests(MesApiFactory fx) => _fx = fx;

    private async Task<HttpClient> ClientAsync(string user)
    {
        await _fx.SeedUserAsync(user, "P@ss!1", UserRole.Operator);
        var c = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(c, user, "P@ss!1");
        return c;
    }

    private static HttpRequestMessage Post(string path, string body, bool idem = true, string? ifMatch = null)
    {
        var r = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (idem) r.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());
        if (ifMatch is not null) r.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return r;
    }

    private async Task<string> PostLotAsync(HttpClient c, string kind, int qty, DateTime? expiry, long? spec = null, string? lotNo = null)
    {
        lotNo ??= "LOT-" + Guid.NewGuid().ToString("N")[..8];
        var body = System.Text.Json.JsonSerializer.Serialize(new PostSemiLotRequest
        { LotNo = lotNo, SemiKind = kind, Qty = qty, ExpiryAt = expiry, SpecRevisionId = spec, SourceWorkOrderId = 1 });
        var resp = await c.SendAsync(Post("/api/v2/semi-lots", body));
        resp.EnsureSuccessStatusCode();
        return lotNo;
    }

    // WO có 1 leg ASSEMBLY (InputSource) ở IPQC_APPROVED, target qty.
    private async Task<(long WoId, long LegId)> SeedAssemblyWoAsync(string inputSource, int targetQty, long? spec = null)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var cust = new Customer { Code = "C-" + Guid.NewGuid().ToString("N")[..6], Name = "c" };
        db.Customers.Add(cust); await db.SaveChangesAsync();
        var prod = new Product { ProductCode = "P-" + Guid.NewGuid().ToString("N")[..6], Name = "p", CustomerId = cust.Id };
        db.Products.Add(prod); await db.SaveChangesAsync();
        var wo = new WorkOrder
        {
            WoNo = "WO-SEMI-" + Guid.NewGuid().ToString("N")[..6], CustomerId = cust.Id, ProductId = prod.Id,
            ProductName = "p", TargetQty = targetQty, Uom = "pcs", CurrentStep = ProcessStepCode.PrePressCheck,
            MesPhase = "SPLIT", Status = WoStatus.InProgress,
        };
        db.WorkOrders.Add(wo); await db.SaveChangesAsync();
        var leg = new WoLeg
        {
            WorkOrderId = wo.Id, Sequence = 0, LegKind = "ASSEMBLY", Method = "Assembly",
            ProcessLine = "FINISHING", SurfaceProfile = "LITE", InputSource = inputSource,
            LegPhase = "IPQC_APPROVED", SpecRevisionId = spec,
        };
        db.WoLegs.Add(leg); await db.SaveChangesAsync();
        return (wo.Id, leg.Id);
    }

    private async Task<string> LegEtagAsync(long legId)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var rv = await db.WoLegs.AsNoTracking().Where(l => l.Id == legId).Select(l => l.RowVersion).SingleAsync();
        return Convert.ToBase64String(rv);
    }

    private static readonly DateTime T0 = new(2026, 7, 1);

    // ── Post + list ────────────────────────────────────────────────

    [Fact]
    public async Task Post_lot_then_visible_in_stock_list()
    {
        var c = await ClientAsync("op-semi-post");
        var lotNo = await PostLotAsync(c, "PRINTED_SEMI", 500, T0.AddDays(30));
        var view = await c.GetFromJsonAsync<SemiStockView>("/api/v2/semi-lots?kind=PRINTED_SEMI");
        Assert.Contains(view!.Lots, l => l.LotNo == lotNo && l.QtyAvailable == 500 && l.Status == "AVAILABLE");
    }

    [Fact]
    public async Task Post_duplicate_lot_no_returns_409()
    {
        var c = await ClientAsync("op-semi-dup");
        var lotNo = await PostLotAsync(c, "PRINTED_SEMI", 100, null);
        var body = System.Text.Json.JsonSerializer.Serialize(new PostSemiLotRequest
        { LotNo = lotNo, SemiKind = "PRINTED_SEMI", Qty = 100, SourceWorkOrderId = 1 });
        var resp = await c.SendAsync(Post("/api/v2/semi-lots", body));
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task Post_invalid_kind_and_qty_return_422()
    {
        var c = await ClientAsync("op-semi-inv");
        var r1 = await c.SendAsync(Post("/api/v2/semi-lots", "{\"lotNo\":\"L1\",\"semiKind\":\"BOGUS\",\"qty\":10}"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r1.StatusCode);
        var r2 = await c.SendAsync(Post("/api/v2/semi-lots", "{\"lotNo\":\"L2\",\"semiKind\":\"PRINTED_SEMI\",\"qty\":0}"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r2.StatusCode);
    }

    [Fact]
    public async Task Post_missing_idempotency_key_returns_400()
    {
        var c = await ClientAsync("op-semi-noidem");
        var resp = await c.SendAsync(Post("/api/v2/semi-lots", "{\"lotNo\":\"L3\",\"semiKind\":\"PRINTED_SEMI\",\"qty\":10}", idem: false));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ── Reserve ────────────────────────────────────────────────────

    [Fact]
    public async Task Reserve_fefo_picks_earliest_expiry_and_updates_qty()
    {
        var c = await ClientAsync("op-semi-fefo");
        // spec riêng (444) — DB dùng chung IClassFixture nên phải cô lập lô
        // của test này khỏi FEFO của test khác.
        var late = await PostLotAsync(c, "PRINTED_SEMI", 400, T0.AddDays(60), spec: 444);
        var early = await PostLotAsync(c, "PRINTED_SEMI", 400, T0.AddDays(5), spec: 444);
        var (wo, leg) = await SeedAssemblyWoAsync("FROM_STOCK", 500, spec: 444);

        var body = System.Text.Json.JsonSerializer.Serialize(new ReserveSemiRequest { Qty = 500, SemiKind = "PRINTED_SEMI" });
        var resp = await c.SendAsync(Post($"/api/v2/work-orders/{wo}/legs/{leg}/semi/reserve", body));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var r = await resp.Content.ReadFromJsonAsync<SemiSetResponse>();
        Assert.Equal(500, r!.Allocated);
        Assert.Equal(0, r.Shortfall);

        var view = await c.GetFromJsonAsync<SemiStockView>("/api/v2/semi-lots?kind=PRINTED_SEMI");
        var earlyLot = view!.Lots.Single(l => l.LotNo == early);
        Assert.Equal(0, earlyLot.QtyAvailable);     // lô hết hạn sớm bị rút hết trước
        Assert.Equal(400, earlyLot.QtyReserved);
        var lateLot = view.Lots.Single(l => l.LotNo == late);
        Assert.Equal(300, lateLot.QtyAvailable);     // 100 rút từ lô hạn muộn
    }

    [Fact]
    public async Task Reserve_insufficient_stock_returns_422_no_partial()
    {
        var c = await ClientAsync("op-semi-short");
        await PostLotAsync(c, "PRINTED_SEMI", 100, null, spec: 555);
        var (wo, leg) = await SeedAssemblyWoAsync("FROM_STOCK", 500, spec: 555);
        var body = System.Text.Json.JsonSerializer.Serialize(new ReserveSemiRequest { Qty = 500, SemiKind = "PRINTED_SEMI" });
        var resp = await c.SendAsync(Post($"/api/v2/work-orders/{wo}/legs/{leg}/semi/reserve", body));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        // Không partial-commit: lô vẫn 100 available.
        var view = await c.GetFromJsonAsync<SemiStockView>("/api/v2/semi-lots?spec=555");
        Assert.All(view!.Lots, l => Assert.Equal(0, l.QtyReserved));
    }

    [Fact]
    public async Task Reserve_on_in_line_leg_rejected()
    {
        var c = await ClientAsync("op-semi-inline");
        await PostLotAsync(c, "PRINTED_SEMI", 500, null);
        var (wo, leg) = await SeedAssemblyWoAsync("IN_LINE", 500);
        var body = System.Text.Json.JsonSerializer.Serialize(new ReserveSemiRequest { Qty = 100, SemiKind = "PRINTED_SEMI" });
        var resp = await c.SendAsync(Post($"/api/v2/work-orders/{wo}/legs/{leg}/semi/reserve", body));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    // ── Consume ────────────────────────────────────────────────────

    [Fact]
    public async Task Consume_moves_reserved_to_consumed_and_depletes_lot()
    {
        var c = await ClientAsync("op-semi-consume");
        await PostLotAsync(c, "TAPE_SEMI", 200, null);
        var (wo, leg) = await SeedAssemblyWoAsync("FROM_STOCK", 200);
        await c.SendAsync(Post($"/api/v2/work-orders/{wo}/legs/{leg}/semi/reserve",
            System.Text.Json.JsonSerializer.Serialize(new ReserveSemiRequest { Qty = 200, SemiKind = "TAPE_SEMI" })));
        var resp = await c.SendAsync(Post($"/api/v2/work-orders/{wo}/legs/{leg}/semi/consume", "{}"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var view = await c.GetFromJsonAsync<SemiStockView>("/api/v2/semi-lots?kind=TAPE_SEMI");
        var lot = view!.Lots.Single(l => l.QtyProduced == 200 && l.SourceWorkOrderId == 1 && l.Status == "DEPLETED");
        Assert.Equal(0, lot.QtyReserved);
        Assert.Equal(0, lot.QtyAvailable);
    }

    // ── FROM_STOCK gate integration (RoutingController.Advance) ─────

    [Fact]
    public async Task Assembly_from_stock_advance_running_blocked_until_reserved()
    {
        var c = await ClientAsync("op-semi-gate");
        await PostLotAsync(c, "PRINTED_SEMI", 300, null, spec: 777);
        var (wo, leg) = await SeedAssemblyWoAsync("FROM_STOCK", 300, spec: 777);

        // Chưa reserve → advance RUNNING bị chặn.
        var etag = await LegEtagAsync(leg);
        var blocked = await c.SendAsync(Post($"/api/v2/work-orders/{wo}/legs/{leg}/advance",
            "{\"toPhase\":\"RUNNING\"}", ifMatch: $"\"{etag}\""));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, blocked.StatusCode);

        // Reserve đủ → advance RUNNING được.
        await c.SendAsync(Post($"/api/v2/work-orders/{wo}/legs/{leg}/semi/reserve",
            System.Text.Json.JsonSerializer.Serialize(new ReserveSemiRequest { Qty = 300, SemiKind = "PRINTED_SEMI" })));
        etag = await LegEtagAsync(leg);
        var ok = await c.SendAsync(Post($"/api/v2/work-orders/{wo}/legs/{leg}/advance",
            "{\"toPhase\":\"RUNNING\"}", ifMatch: $"\"{etag}\""));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    // ── Soak — concurrency per-lot ─────────────────────────────────

    [Trait("Category", "Soak")]
    [Fact]
    public async Task Concurrent_reserve_same_lot_does_not_oversell()
    {
        var c = await ClientAsync("op-semi-soak");
        // 1 lô 100; 10 assembly cùng reserve 100 → chỉ 1 thành công, tổng
        // reserved không vượt 100 (RowVersion optimistic-lock).
        await PostLotAsync(c, "PRINTED_SEMI", 100, null, spec: 888);
        var legs = new List<(long wo, long leg)>();
        for (int i = 0; i < 10; i++) legs.Add(await SeedAssemblyWoAsync("FROM_STOCK", 100, spec: 888));

        var body = System.Text.Json.JsonSerializer.Serialize(new ReserveSemiRequest { Qty = 100, SemiKind = "PRINTED_SEMI" });
        var tasks = legs.Select(x => c.SendAsync(Post($"/api/v2/work-orders/{x.wo}/legs/{x.leg}/semi/reserve", body)));
        var results = await Task.WhenAll(tasks);

        var ok = results.Count(r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(1, ok);   // chỉ 1 reserve thành công
        var view = await c.GetFromJsonAsync<SemiStockView>("/api/v2/semi-lots?spec=888");
        Assert.Equal(100, view!.Lots.Sum(l => l.QtyReserved));  // không over-sell
        Assert.Equal(0, view.Lots.Sum(l => l.QtyAvailable));
    }
}
