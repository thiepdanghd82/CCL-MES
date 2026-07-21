using System.Net;
using System.Net.Http.Json;
using CCL.MES.Api.Services;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Shared.Quality;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Api.Tests;

/// <summary>
/// Quality → Traceability (frozen-snapshot model). Locks the invariants that
/// matter most: a snapshot is DEAD (immutable — editing the source entity
/// afterwards must NOT change it), freeze is idempotent by ContentHash, a
/// real re-confirm bumps Version, a not-frozen phase reads back null (empty
/// state), list search is case-insensitive, and the read endpoints enforce
/// the QcRead policy (401 anon / 403 operator / 200 QC).
/// </summary>
public sealed class TraceabilityControllerTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public TraceabilityControllerTests(MesApiFactory fx) => _fx = fx;

    private async Task<HttpClient> ClientAsync(string user, string role)
    {
        await _fx.SeedUserAsync(user, "P@ss!1", role);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, user, "P@ss!1");
        return client;
    }

    private async Task<long> SeedWoWithMaterialsAsync(string woNo, params (string code, string lot, PrepressCheckStatus st)[] mats)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var customer = new Customer { Code = "C-" + Guid.NewGuid().ToString("N")[..6], Name = "Cust" };
        db.Customers.Add(customer); await db.SaveChangesAsync();
        var product = new Product { ProductCode = "PC-" + Guid.NewGuid().ToString("N")[..6], Name = "Prod", CustomerId = customer.Id };
        db.Products.Add(product); await db.SaveChangesAsync();
        var wo = new WorkOrder
        {
            WoNo = woNo, CustomerId = customer.Id, ProductId = product.Id, ProductName = "Prod",
            TargetQty = 1000, ProducedQty = 0, Uom = "pcs", MesPhase = "SETTING", Status = WoStatus.InProgress,
        };
        db.WorkOrders.Add(wo); await db.SaveChangesAsync();
        int idx = 0;
        foreach (var m in mats)
            db.WoMaterials.Add(new WoMaterial
            {
                WorkOrderId = wo.Id, BomLineIdx = idx++, MaterialCode = m.code,
                QtyRequired = 500, Uom = "m2", LotNo = m.lot, Status = m.st,
            });
        await db.SaveChangesAsync();
        return wo.Id;
    }

    private async Task FreezeAsync(long woId, string phase, string actor = "tester")
    {
        using var scope = _fx.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ITraceFreezeService>();
        await svc.FreezeAsync(woId, phase, actor);
    }

    private async Task MutateMaterialAsync(long woId, Action<WoMaterial> mutate)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        foreach (var m in await db.WoMaterials.Where(x => x.WorkOrderId == woId).ToListAsync())
            mutate(m);
        await db.SaveChangesAsync();
    }

    private static int SnapshotCount(MesApiFactory fx, long woId, string phase)
    {
        using var scope = fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        return db.WoTraceSnapshots.Count(s => s.WoId == woId && s.Phase == phase);
    }

    // ── Immutability (the point of the whole feature) ──
    [Fact]
    public async Task Frozen_product_snapshot_is_immutable_when_source_material_changes()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var woId = await SeedWoWithMaterialsAsync($"IMMUT-{tag}", ("M1", "LOT-ORIG", PrepressCheckStatus.Ok));
        await FreezeAsync(woId, TracePhase.Product);

        // Mutate the SOURCE after freezing.
        await MutateMaterialAsync(woId, m => { m.LotNo = "LOT-CHANGED"; m.Status = PrepressCheckStatus.Ng; });

        var client = await ClientAsync($"immut-qc-{tag}", "QC");
        var detail = await client.GetFromJsonAsync<TraceabilityDetailDto>($"/api/v2/quality/traceability/IMMUT-{tag}");
        var item = Assert.Single(detail!.Product!.Payload.Items);
        // Snapshot kept the ORIGINAL literal values, not the mutated ones.
        Assert.Equal("Ok", item.Status);
        Assert.Equal("LOT-ORIG", item.Extra!["lotNo"]);
    }

    // ── Product payload: carries part_scan + part_description, drops scrap ──
    [Fact]
    public async Task Product_freeze_carries_part_scan_and_description_and_drops_scrap()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var woId = await SeedWoWithMaterialsAsync($"PSCAN-{tag}", ("30030532", "L1", PrepressCheckStatus.Ok));
        await MutateMaterialAsync(woId, m =>
        {
            m.MaterialDescription = "BOPP GLOSS";
            m.PartScan = "30030532-0145";        // bare code, resolved via BOM row
            m.PartScanDescription = "BOPP GLOSS";
        });
        await FreezeAsync(woId, TracePhase.Product);

        var client = await ClientAsync($"pscan-qc-{tag}", "QC");
        var detail = await client.GetFromJsonAsync<TraceabilityDetailDto>($"/api/v2/quality/traceability/PSCAN-{tag}");
        var item = Assert.Single(detail!.Product!.Payload.Items);

        Assert.Equal("30030532-0145", item.Extra!["partScan"]);
        Assert.Equal("BOPP GLOSS", item.Extra!["partDescription"]);
        Assert.Equal("1", item.Extra!["no"]);
        Assert.Equal("30030532", item.Extra!["partNo"]);
        // Scrap keys are no longer frozen into the Product payload.
        Assert.False(item.Extra!.ContainsKey("scrapFactor"));
        Assert.False(item.Extra!.ContainsKey("scrapPercent"));
    }

    // ── Idempotent + version bump ──
    [Fact]
    public async Task Freeze_is_idempotent_by_content_hash_then_bumps_version_on_real_change()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var woId = await SeedWoWithMaterialsAsync($"IDEMP-{tag}", ("M1", "L1", PrepressCheckStatus.Ok));

        await FreezeAsync(woId, TracePhase.Product);
        await FreezeAsync(woId, TracePhase.Product);   // identical content → NOOP
        Assert.Equal(1, SnapshotCount(_fx, woId, TracePhase.Product));

        await MutateMaterialAsync(woId, m => m.Status = PrepressCheckStatus.Ng);
        await FreezeAsync(woId, TracePhase.Product);   // content changed → version 2
        Assert.Equal(2, SnapshotCount(_fx, woId, TracePhase.Product));

        // Detail shows the NEWEST version (Ng).
        var client = await ClientAsync($"idemp-qc-{tag}", "QC");
        var detail = await client.GetFromJsonAsync<TraceabilityDetailDto>($"/api/v2/quality/traceability/IDEMP-{tag}");
        Assert.Equal(2, detail!.Product!.Version);
        Assert.Equal("Ng", detail.Product.Payload.Items[0].Status);
    }

    // ── Flexible: payload only carries the items actually present ──
    [Fact]
    public async Task Payload_only_contains_items_actually_inspected_per_wo()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var woA = await SeedWoWithMaterialsAsync($"FLEXA-{tag}", ("A1", "L", PrepressCheckStatus.Ok));
        var woB = await SeedWoWithMaterialsAsync($"FLEXB-{tag}", ("B1", "L", PrepressCheckStatus.Ok), ("B2", "L", PrepressCheckStatus.Ok), ("B3", "L", PrepressCheckStatus.Ng));
        await FreezeAsync(woA, TracePhase.Product);
        await FreezeAsync(woB, TracePhase.Product);

        var client = await ClientAsync($"flex-qc-{tag}", "QC");
        var a = await client.GetFromJsonAsync<TraceabilityDetailDto>($"/api/v2/quality/traceability/FLEXA-{tag}");
        var b = await client.GetFromJsonAsync<TraceabilityDetailDto>($"/api/v2/quality/traceability/FLEXB-{tag}");
        Assert.Single(a!.Product!.Payload.Items);
        Assert.Equal(3, b!.Product!.Payload.Items.Count);
    }

    // ── Empty state: a not-frozen phase reads back null ──
    [Fact]
    public async Task Not_frozen_phase_is_null_in_detail()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var woId = await SeedWoWithMaterialsAsync($"EMPTY-{tag}", ("M1", "L", PrepressCheckStatus.Ok));
        await FreezeAsync(woId, TracePhase.Product);   // only Product frozen

        var client = await ClientAsync($"empty-qc-{tag}", "QC");
        var detail = await client.GetFromJsonAsync<TraceabilityDetailDto>($"/api/v2/quality/traceability/EMPTY-{tag}");
        Assert.NotNull(detail!.Product);
        Assert.Null(detail.Ipqc);
        Assert.Null(detail.Fqc);
        Assert.Null(detail.Oqc);
    }

    // ── List search is case-insensitive ──
    [Fact]
    public async Task List_search_is_case_insensitive()
    {
        var tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var woId = await SeedWoWithMaterialsAsync($"LIST-{tag}", ("M1", "L", PrepressCheckStatus.Ok));
        await FreezeAsync(woId, TracePhase.Product);

        var client = await ClientAsync($"list-qc-{tag}", "QC");
        var page = await client.GetFromJsonAsync<TraceListPage>($"/api/v2/quality/traceability?search=list-{tag.ToLowerInvariant()}");
        Assert.Contains(page!.Items, r => r.WoNo == $"LIST-{tag}");
        var row = page.Items.Single(r => r.WoNo == $"LIST-{tag}");
        Assert.Contains(TracePhase.Product, row.FrozenPhases);
    }

    // ── Auth gate ──
    [Fact]
    public async Task List_requires_auth() =>
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _fx.CreateClient().GetAsync("/api/v2/quality/traceability")).StatusCode);

    [Fact]
    public async Task Operator_gets_403()
    {
        var client = await ClientAsync("trace-op", "Operator");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v2/quality/traceability")).StatusCode);
    }

    [Fact]
    public async Task Detail_of_unknown_wo_returns_404()
    {
        var client = await ClientAsync("trace-404-qc", "QC");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v2/quality/traceability/NOPE-999")).StatusCode);
    }

    // ── Real-time index (WoTraceIndex) ──
    private async Task TouchAsync(string woNo)
    {
        using var scope = _fx.Services.CreateScope();
        var idx = scope.ServiceProvider.GetRequiredService<ITraceIndexService>();
        await idx.TouchAsync(woNo);
    }

    [Fact]
    public async Task Scan_touch_lists_the_wo_before_any_phase_is_frozen()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        await SeedWoWithMaterialsAsync($"SCAN-{tag}", ("M1", "L", PrepressCheckStatus.Ok));
        await TouchAsync($"SCAN-{tag}");   // scan/find — no freeze yet

        var client = await ClientAsync($"scan-qc-{tag}", "QC");
        var page = await client.GetFromJsonAsync<TraceListPage>($"/api/v2/quality/traceability?search=SCAN-{tag}");
        var row = Assert.Single(page!.Items);
        Assert.Equal($"SCAN-{tag}", row.WoNo);
        Assert.Empty(row.FrozenPhases);           // appears with NO frozen phase
        Assert.Equal("SETTING", row.CurrentMesPhase);
    }

    [Fact]
    public async Task Touch_is_idempotent_no_duplicate_index_row()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        await SeedWoWithMaterialsAsync($"IDX-{tag}", ("M1", "L", PrepressCheckStatus.Ok));
        await TouchAsync($"IDX-{tag}");
        await TouchAsync($"IDX-{tag}");   // re-scan

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        Assert.Equal(1, db.WoTraceIndexes.Count(x => x.WoNo == $"IDX-{tag}"));
    }

    [Fact]
    public async Task Freeze_sets_index_flags_without_touching_the_snapshot()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var woId = await SeedWoWithMaterialsAsync($"SPLIT-{tag}", ("M1", "LOT-A", PrepressCheckStatus.Ok));
        await FreezeAsync(woId, TracePhase.Product);

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
            var idx = db.WoTraceIndexes.Single(x => x.WoId == woId);
            Assert.True(idx.ProductFrozen);         // index reflects the freeze
            Assert.NotNull(idx.LatestFrozenAtUtc);
        }
        // Snapshot still immutable after the index update.
        await MutateMaterialAsync(woId, m => m.LotNo = "CHANGED");
        var client = await ClientAsync($"split-qc-{tag}", "QC");
        var detail = await client.GetFromJsonAsync<TraceabilityDetailDto>($"/api/v2/quality/traceability/SPLIT-{tag}");
        Assert.Equal("LOT-A", detail!.Product!.Payload.Items[0].Extra!["lotNo"]);
    }

    [Fact]
    public async Task Backfill_requires_admin_and_indexes_plus_freezes_concluded_phases()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var woId = await SeedWoWithMaterialsAsync($"BF-{tag}", ("M1", "L", PrepressCheckStatus.Ok));
        // Advance to SHIPPED with concluded IPQC + FQC + OQC.
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
            var wo = db.WorkOrders.Single(w => w.Id == woId);
            wo.MesPhase = "SHIPPED";
            db.WoIpqcChecks.Add(new WoIpqcCheck { WorkOrderId = woId, Judgment = IpqcJudgment.GoRun, IpqcSubmittedBy = "qc" });
            db.WoQcChecks.Add(new WoQcCheck { WorkOrderId = woId, QcKind = "FQC", Judgment = WoQcJudgment.Pass, InspectedBy = "i" });
            db.WoQcChecks.Add(new WoQcCheck { WorkOrderId = woId, QcKind = "OQC", Judgment = WoQcJudgment.Pass, InspectedBy = "i", ReviewedBy = "r", ApprovedBy = "a" });
            await db.SaveChangesAsync();
        }

        // Operator → 403 on backfill.
        var op = await ClientAsync($"bf-op-{tag}", "Operator");
        Assert.Equal(HttpStatusCode.Forbidden, (await op.PostAsync("/api/v2/quality/traceability/backfill", null)).StatusCode);

        // Admin → 200; WO now indexed with all 4 phases frozen.
        var admin = await ClientAsync($"bf-admin-{tag}", "Admin");
        var resp = await admin.PostAsync("/api/v2/quality/traceability/backfill", null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var page = await admin.GetFromJsonAsync<TraceListPage>($"/api/v2/quality/traceability?search=BF-{tag}");
        var row = Assert.Single(page!.Items);
        Assert.Contains(TracePhase.Product, row.FrozenPhases);
        Assert.Contains(TracePhase.Ipqc, row.FrozenPhases);
        Assert.Contains(TracePhase.Fqc, row.FrozenPhases);
        Assert.Contains(TracePhase.Oqc, row.FrozenPhases);
    }

    private sealed record TraceListPage(List<TraceListRow> Items, int Total, int Page, int PageSize);
}
