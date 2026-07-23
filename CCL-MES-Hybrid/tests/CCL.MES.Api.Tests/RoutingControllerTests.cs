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
/// P11-2 — RoutingController (Multi-Method Routing DAG fork-join).
/// materialize (fork) · GET legs · advance (+ join cascade) · rework.
/// Contract: 428 (no If-Match) · 400 (no Idem) · 404 · 409 (stale) ·
/// 422 (gate/phase) · 200. Concurrency PER-LEG (soak). Audit wire-mirror.
/// </summary>
public sealed class RoutingControllerTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public RoutingControllerTests(MesApiFactory fx) => _fx = fx;

    // ── Seed helpers ───────────────────────────────────────────────

    private async Task SeedLegMapAsync()
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        await DbSeeder.SeedProcessLegMapAsync(db); // idempotent
    }

    // T3 silkscreen: in lụa ∥ cắt tape → assembly → cắt outline (4 op).
    private async Task<(long WoId, string WoEtag)> SeedT3Async(string tag)
    {
        await SeedLegMapAsync();
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var code = "P3-" + Guid.NewGuid().ToString("N")[..6];
        var cust = new Customer { Code = "C-" + Guid.NewGuid().ToString("N")[..6], Name = "C" };
        db.Customers.Add(cust); await db.SaveChangesAsync();
        var prod = new Product { ProductCode = code, Name = "T3", CustomerId = cust.Id };
        db.Products.Add(prod); await db.SaveChangesAsync();
        db.RoutingOperations.AddRange(
            new RoutingOperation { PartNo = code, OpNo = "10", Operation = "Silkscreen print", WorkCenterNo = "MSS01" },
            new RoutingOperation { PartNo = code, OpNo = "20", Operation = "CẮT TAPE" },
            new RoutingOperation { PartNo = code, OpNo = "30", Operation = "DÁN TAPE với semi-in" },
            new RoutingOperation { PartNo = code, OpNo = "40", Operation = "CẮT OUTLINE" });
        await db.SaveChangesAsync();
        var wo = new WorkOrder
        {
            WoNo = "WO-P11-" + tag + "-" + Guid.NewGuid().ToString("N")[..5],
            CustomerId = cust.Id, ProductId = prod.Id, ProductName = "T3",
            TargetQty = 1000, Uom = "pcs", CurrentStep = ProcessStepCode.PrePressCheck,
            MesPhase = "PREPRESS", Status = WoStatus.InProgress,
        };
        db.WorkOrders.Add(wo); await db.SaveChangesAsync();
        var rv = await db.WorkOrders.AsNoTracking().Where(w => w.Id == wo.Id).Select(w => w.RowVersion).SingleAsync();
        return (wo.Id, Convert.ToBase64String(rv));
    }

    // T1 combined-inline: 1 op in+bế.
    private async Task<(long WoId, string WoEtag)> SeedT1Async(string tag)
    {
        await SeedLegMapAsync();
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var code = "P1-" + Guid.NewGuid().ToString("N")[..6];
        var cust = new Customer { Code = "C-" + Guid.NewGuid().ToString("N")[..6], Name = "C" };
        db.Customers.Add(cust); await db.SaveChangesAsync();
        var prod = new Product { ProductCode = code, Name = "T1", CustomerId = cust.Id };
        db.Products.Add(prod); await db.SaveChangesAsync();
        db.RoutingOperations.Add(new RoutingOperation { PartNo = code, OpNo = "10", Operation = "IN BẾ inline 1 lượt" });
        await db.SaveChangesAsync();
        var wo = new WorkOrder
        {
            WoNo = "WO-P11-" + tag + "-" + Guid.NewGuid().ToString("N")[..5],
            CustomerId = cust.Id, ProductId = prod.Id, ProductName = "T1",
            TargetQty = 500, Uom = "pcs", CurrentStep = ProcessStepCode.PrePressCheck,
            MesPhase = "PREPRESS", Status = WoStatus.InProgress,
        };
        db.WorkOrders.Add(wo); await db.SaveChangesAsync();
        var rv = await db.WorkOrders.AsNoTracking().Where(w => w.Id == wo.Id).Select(w => w.RowVersion).SingleAsync();
        return (wo.Id, Convert.ToBase64String(rv));
    }

    // Đặt phase 1 leg trực tiếp qua DB (bypass, trả leg etag mới).
    private async Task<string> SetLegPhaseAsync(long woId, int seq, string phase, int qtyDone = 0)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var leg = await db.WoLegs.FirstAsync(l => l.WorkOrderId == woId && l.Sequence == seq);
        leg.LegPhase = phase; leg.QtyDoneCached = qtyDone; leg.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var rv = await db.WoLegs.AsNoTracking().Where(l => l.Id == leg.Id).Select(l => l.RowVersion).SingleAsync();
        return Convert.ToBase64String(rv);
    }

    private async Task<(long LegId, string Etag)> LegBySeqAsync(long woId, int seq)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var leg = await db.WoLegs.AsNoTracking().FirstAsync(l => l.WorkOrderId == woId && l.Sequence == seq);
        return (leg.Id, Convert.ToBase64String(leg.RowVersion));
    }

    private async Task<HttpClient> ClientAsync(string user, string role = UserRole.Operator)
    {
        await _fx.SeedUserAsync(user, "P@ss!1", role);
        var c = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(c, user, "P@ss!1");
        return c;
    }

    private static HttpRequestMessage Post(string path, string body, string? ifMatch, string? idem)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (ifMatch is not null) req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        if (idem is not null) req.Headers.TryAddWithoutValidation("Idempotency-Key", idem);
        return req;
    }

    private async Task<string> MaterializeAsync(HttpClient c, long wo, string etag)
    {
        var resp = await c.SendAsync(Post($"/api/v2/work-orders/{wo}/legs/materialize", "{}", $"\"{etag}\"", Guid.NewGuid().ToString()));
        resp.EnsureSuccessStatusCode();
        return etag;
    }

    // ── Materialize ────────────────────────────────────────────────

    [Fact]
    public async Task Materialize_T3_forks_into_4_legs_and_3_edges()
    {
        var (wo, etag) = await SeedT3Async("fork");
        var c = await ClientAsync("op-p11-fork");
        var resp = await c.SendAsync(Post($"/api/v2/work-orders/{wo}/legs/materialize", "{}", $"\"{etag}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<LegMaterializeResponse>();
        Assert.True(body!.Ok);
        Assert.True(body.Forked);
        Assert.Equal(4, body.LegCount);
        Assert.Equal("SPLIT", body.MesPhase);

        var view = await c.GetFromJsonAsync<LegsView>($"/api/v2/work-orders/{wo}/legs");
        Assert.Equal(4, view!.Legs.Count);
        Assert.Equal(3, view.Edges.Count);
        Assert.Equal(new[] { "PRINT", "TAPE", "ASSEMBLY", "CUT" }, view.Legs.OrderBy(l => l.Sequence).Select(l => l.LegKind));
        Assert.Single(view.Legs.Where(l => l.IsTerminal)); // chỉ CUT terminal
        Assert.Equal("CUT", view.Legs.Single(l => l.IsTerminal).LegKind);
    }

    [Fact]
    public async Task Materialize_is_idempotent()
    {
        var (wo, etag) = await SeedT3Async("idem");
        var c = await ClientAsync("op-p11-idem");
        await c.SendAsync(Post($"/api/v2/work-orders/{wo}/legs/materialize", "{}", $"\"{etag}\"", Guid.NewGuid().ToString()));
        var fresh = (await c.GetFromJsonAsync<LegsView>($"/api/v2/work-orders/{wo}/legs"))!.WoETag;
        var resp2 = await c.SendAsync(Post($"/api/v2/work-orders/{wo}/legs/materialize", "{}", $"\"{fresh}\"", Guid.NewGuid().ToString()));
        var body = await resp2.Content.ReadFromJsonAsync<LegMaterializeResponse>();
        Assert.True(body!.Ok);
        Assert.False(body.Forked);      // đã fork rồi
        Assert.Equal(4, body.LegCount);
    }

    [Fact]
    public async Task Materialize_single_op_does_not_fork()
    {
        var (wo, etag) = await SeedT1Async("t1");
        var c = await ClientAsync("op-p11-t1");
        var resp = await c.SendAsync(Post($"/api/v2/work-orders/{wo}/legs/materialize", "{}", $"\"{etag}\"", Guid.NewGuid().ToString()));
        var body = await resp.Content.ReadFromJsonAsync<LegMaterializeResponse>();
        Assert.True(body!.Ok);
        Assert.False(body.Forked);
        Assert.Equal(1, body.LegCount);
        Assert.NotEqual("SPLIT", body.MesPhase);
    }

    [Fact]
    public async Task Materialize_missing_ifmatch_428_and_missing_idem_400()
    {
        var (wo, etag) = await SeedT3Async("hdr");
        var c = await ClientAsync("op-p11-hdr");
        var r428 = await c.SendAsync(Post($"/api/v2/work-orders/{wo}/legs/materialize", "{}", null, Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.PreconditionRequired, r428.StatusCode);
        var r400 = await c.SendAsync(Post($"/api/v2/work-orders/{wo}/legs/materialize", "{}", $"\"{etag}\"", null));
        Assert.Equal(HttpStatusCode.BadRequest, r400.StatusCode);
    }

    // ── Advance + gate + join ──────────────────────────────────────

    [Fact]
    public async Task Advance_prepress_to_setting_ok_bumps_leg_etag()
    {
        var (wo, etag) = await SeedT3Async("adv");
        var c = await ClientAsync("op-p11-adv");
        await MaterializeAsync(c, wo, etag);
        var (legId, legEtag) = await LegBySeqAsync(wo, 0); // PRINT
        var resp = await c.SendAsync(Post($"/api/v2/work-orders/{wo}/legs/{legId}/advance",
            "{\"toPhase\":\"SETTING\"}", $"\"{legEtag}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<LegSetResponse>();
        Assert.True(body!.Ok);
        Assert.Equal("SETTING", body.LegPhase);
        Assert.NotEqual(legEtag, body.LegETag); // per-leg RowVersion bumped
    }

    [Fact]
    public async Task Advance_assembly_to_running_blocked_by_hard_gate_422()
    {
        var (wo, etag) = await SeedT3Async("hard");
        var c = await ClientAsync("op-p11-hard");
        await MaterializeAsync(c, wo, etag);
        // ASSEMBLY (seq2) tới IPQC_APPROVED; PRINT/TAPE vẫn PREPRESS.
        await SetLegPhaseAsync(wo, 2, "IPQC_APPROVED");
        var (legId, legEtag) = await LegBySeqAsync(wo, 2);
        var resp = await c.SendAsync(Post($"/api/v2/work-orders/{wo}/legs/{legId}/advance",
            "{\"toPhase\":\"RUNNING\"}", $"\"{legEtag}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [Fact]
    public async Task Advance_assembly_to_running_allowed_when_inputs_done()
    {
        var (wo, etag) = await SeedT3Async("hardok");
        var c = await ClientAsync("op-p11-hardok");
        await MaterializeAsync(c, wo, etag);
        await SetLegPhaseAsync(wo, 0, "LEG_DONE", qtyDone: 1000); // PRINT done
        await SetLegPhaseAsync(wo, 1, "LEG_DONE", qtyDone: 1000); // TAPE done
        await SetLegPhaseAsync(wo, 2, "IPQC_APPROVED");
        var (legId, legEtag) = await LegBySeqAsync(wo, 2);
        var resp = await c.SendAsync(Post($"/api/v2/work-orders/{wo}/legs/{legId}/advance",
            "{\"toPhase\":\"RUNNING\"}", $"\"{legEtag}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Advance_terminal_leg_done_joins_split_to_fqc_pending()
    {
        var (wo, etag) = await SeedT3Async("join");
        var c = await ClientAsync("op-p11-join");
        await MaterializeAsync(c, wo, etag);
        // Đưa PRINT/TAPE/ASSEMBLY về LEG_DONE; CUT (terminal) tới RUNNING.
        await SetLegPhaseAsync(wo, 0, "LEG_DONE", 1000);
        await SetLegPhaseAsync(wo, 1, "LEG_DONE", 1000);
        await SetLegPhaseAsync(wo, 2, "LEG_DONE", 1000);
        await SetLegPhaseAsync(wo, 3, "RUNNING", 1000);
        var (legId, legEtag) = await LegBySeqAsync(wo, 3); // CUT
        var resp = await c.SendAsync(Post($"/api/v2/work-orders/{wo}/legs/{legId}/advance",
            "{\"toPhase\":\"LEG_DONE\"}", $"\"{legEtag}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<LegSetResponse>();
        Assert.True(body!.Joined);
        Assert.Equal("FQC_PENDING", body.WoMesPhase);
    }

    [Fact]
    public async Task Advance_stale_ifmatch_returns_409()
    {
        var (wo, etag) = await SeedT3Async("stale");
        var c = await ClientAsync("op-p11-stale");
        await MaterializeAsync(c, wo, etag);
        var (legId, _) = await LegBySeqAsync(wo, 0);
        var resp = await c.SendAsync(Post($"/api/v2/work-orders/{wo}/legs/{legId}/advance",
            "{\"toPhase\":\"SETTING\"}", "\"AAAA\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    // ── Rework ─────────────────────────────────────────────────────

    [Fact]
    public async Task Rework_resets_leg_to_prepress()
    {
        var (wo, etag) = await SeedT3Async("rew");
        var c = await ClientAsync("op-p11-rew");
        await MaterializeAsync(c, wo, etag);
        await SetLegPhaseAsync(wo, 0, "IPQC_WAIT");
        var (legId, legEtag) = await LegBySeqAsync(wo, 0);
        var resp = await c.SendAsync(Post($"/api/v2/work-orders/{wo}/legs/{legId}/rework",
            "{\"reason\":\"NG in lệch màu\"}", $"\"{legEtag}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<LegSetResponse>();
        Assert.Equal("PREPRESS", body!.LegPhase);
        // WO vẫn SPLIT (Q10 — chỉ leg về PREPRESS).
        Assert.Equal("SPLIT", body.WoMesPhase);
    }

    [Fact]
    public async Task Rework_requires_reason_422()
    {
        var (wo, etag) = await SeedT3Async("rewx");
        var c = await ClientAsync("op-p11-rewx");
        await MaterializeAsync(c, wo, etag);
        var (legId, legEtag) = await LegBySeqAsync(wo, 0);
        var resp = await c.SendAsync(Post($"/api/v2/work-orders/{wo}/legs/{legId}/rework",
            "{\"reason\":\"\"}", $"\"{legEtag}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    // ── Soak — concurrency PER-LEG ─────────────────────────────────

    [Trait("Category", "Soak")]
    [Fact]
    public async Task Concurrent_advance_same_leg_N_equals_10_one_winner()
    {
        var (wo, etag) = await SeedT3Async("soak");
        var c = await ClientAsync("op-p11-soak");
        await MaterializeAsync(c, wo, etag);
        var (legId, legEtag) = await LegBySeqAsync(wo, 0);

        var tasks = Enumerable.Range(0, 10).Select(_ =>
            c.SendAsync(Post($"/api/v2/work-orders/{wo}/legs/{legId}/advance",
                "{\"toPhase\":\"SETTING\"}", $"\"{legEtag}\"", Guid.NewGuid().ToString())));
        var results = await Task.WhenAll(tasks);

        var ok = results.Count(r => r.StatusCode == HttpStatusCode.OK);
        var conflict = results.Count(r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(1, ok);
        Assert.Equal(9, conflict);
    }

    // ── Audit wire-mirror (Rule 7.3) ───────────────────────────────

    [Fact]
    public async Task Materialize_emits_WO_SPLIT_FORKED_visible_via_wire_audit_log()
    {
        var (wo, etag) = await SeedT3Async("audit");
        var admin = await ClientAsync("admin-p11-audit", UserRole.Admin);
        await admin.SendAsync(Post($"/api/v2/work-orders/{wo}/legs/materialize", "{}", $"\"{etag}\"", Guid.NewGuid().ToString()));
        var log = await admin.GetStringAsync("/api/v2/audit/log?action=WO_SPLIT_FORKED");
        Assert.Contains("WO_SPLIT_FORKED", log);
        Assert.Contains(wo.ToString(), log);
    }
}
