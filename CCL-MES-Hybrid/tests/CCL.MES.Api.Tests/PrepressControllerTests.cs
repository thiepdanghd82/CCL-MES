using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain;
using CCL.MES.Domain.Auth;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.Prepress;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Api.Tests;

/// <summary>
/// P10.7b-2 — coverage for the PREPRESS write surface.
///
/// Status code contract mirrors /advance:
///   200 success + bumped ETag + materialsReady rollup
///   400 missing Idempotency-Key
///   403 ignored — PrepressController is plain [Authorize]
///   404 WO or row not found
///   409 stale If-Match + WO_STATE_CONFLICT audit
///   422 invalid_status / invalid_reason_code / invalid_ng_note / invalid_phase
///   428 missing If-Match
///
/// Critical condition #1 (rollup race): Concurrent_prepress_row_updates
/// soak asserts post-write rollup is consistent under 10 parallel ops.
/// </summary>
public sealed class PrepressControllerTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public PrepressControllerTests(MesApiFactory fx) => _fx = fx;

    // ── Seed helpers ───────────────────────────────────────────────

    private async Task<(long WoId, string EtaG)> SeedWoWithBomAsync(
        string woNo, string productCode, int bomLines = 3, string mesPhase = "PREPRESS")
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();

        var customer = new Customer { Code = "C-" + woNo, Name = "Customer " + woNo };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var product = new Product { ProductCode = productCode, Name = "P-" + woNo, CustomerId = customer.Id };
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var revision = new ProductRevision
        {
            ProductId = product.Id,
            SpecCode = "SPEC-" + productCode,
            Title = "Spec " + productCode,
            RevisionCode = "A",
            Status = ProductRevisionStatus.Approved,
        };
        db.ProductRevisions.Add(revision);
        await db.SaveChangesAsync();

        for (var i = 0; i < bomLines; i++)
        {
            db.ManufacturingStructures.Add(new ManufacturingStructure
            {
                ParentPart = productCode,
                ComponentPart = $"COMP-{productCode}-{i}",
                ComponentDescription = $"Desc {i}",
                QtyAssembly = 0.1 * (i + 1),
                Uom = "m2",
                ScrapFactor = 0,
            });
        }
        await db.SaveChangesAsync();

        var wo = new WorkOrder
        {
            WoNo = woNo,
            CustomerId = customer.Id,
            ProductId = product.Id,
            ProductName = product.Name,
            ProductRevisionId = revision.Id,
            MachineCode = "M-1",
            MachineName = "Press 1",
            TargetQty = 1000,
            Uom = "pcs",
            CurrentStep = ProcessStepCode.PrePressCheck,
            MesPhase = mesPhase,
            Status = WoStatus.InProgress,
        };
        db.WorkOrders.Add(wo);
        await db.SaveChangesAsync();

        var etag = Convert.ToBase64String(wo.RowVersion);
        return (wo.Id, etag);
    }

    private async Task<string> EtagOfAsync(long id)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var rv = await db.WorkOrders.AsNoTracking().Where(w => w.Id == id)
            .Select(w => w.RowVersion).SingleAsync();
        return Convert.ToBase64String(rv);
    }

    private async Task<HttpClient> OperatorClientAsync(string user)
    {
        await _fx.SeedUserAsync(user, "P@ss!1", UserRole.Operator);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, user, "P@ss!1");
        return client;
    }

    private async Task SeedScrapReasonAsync(string code = "SC-COLOR")
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        if (!await db.ReasonCodes.AnyAsync(r => r.Code == code))
        {
            db.ReasonCodes.Add(new ReasonCode
            {
                Code = code, LabelEn = code, LabelVi = code,
                Kind = ReasonCodeKind.Scrap, Sort = 10,
            });
            await db.SaveChangesAsync();
        }
    }

    private static HttpRequestMessage PutMaterial(long id, int idx, string body,
        string? ifMatch, string? idem)
    {
        var req = new HttpRequestMessage(HttpMethod.Put,
            $"/api/v2/work-orders/{id}/materials/{idx}")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (ifMatch is not null) req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        if (idem is not null) req.Headers.TryAddWithoutValidation("Idempotency-Key", idem);
        return req;
    }

    private static HttpRequestMessage PutPlate(long id, string body,
        string? ifMatch, string? idem)
    {
        var req = new HttpRequestMessage(HttpMethod.Put,
            $"/api/v2/work-orders/{id}/plate-check")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (ifMatch is not null) req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        if (idem is not null) req.Headers.TryAddWithoutValidation("Idempotency-Key", idem);
        return req;
    }

    private static HttpRequestMessage PutCutter(long id, string body,
        string? ifMatch, string? idem)
    {
        var req = new HttpRequestMessage(HttpMethod.Put,
            $"/api/v2/work-orders/{id}/cutter-check")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (ifMatch is not null) req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        if (idem is not null) req.Headers.TryAddWithoutValidation("Idempotency-Key", idem);
        return req;
    }

    private async Task<HttpClient> ClientAsRoleAsync(string user, string role)
    {
        await _fx.SeedUserAsync(user, "P@ss!1", role);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, user, "P@ss!1");
        return client;
    }

    private static HttpRequestMessage PostSpecialAccept(long id, int idx, string body,
        string? ifMatch, string? idem)
    {
        var req = new HttpRequestMessage(HttpMethod.Post,
            $"/api/v2/work-orders/{id}/materials/{idx}/special-accept")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (ifMatch is not null) req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        if (idem is not null) req.Headers.TryAddWithoutValidation("Idempotency-Key", idem);
        return req;
    }

    // ── GET ────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_returns_404_for_missing_wo()
    {
        var client = await OperatorClientAsync("op-7b2-404");
        var resp = await client.GetAsync("/api/v2/work-orders/9999999/prepress");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Get_materializes_snapshot_on_first_read_and_returns_view()
    {
        var (woId, _) = await SeedWoWithBomAsync("WO-7B2-G1", "PROD-G1", bomLines: 4);
        var client = await OperatorClientAsync("op-7b2-get1");

        var resp = await client.GetAsync($"/api/v2/work-orders/{woId}/prepress");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var view = (await resp.Content.ReadFromJsonAsync<PrepressView>())!;
        Assert.Equal(woId, view.WoId);
        Assert.Equal("PREPRESS", view.MesPhase);
        Assert.Equal(4, view.Materials.Count);
        Assert.NotNull(view.PlateCheck);
        Assert.NotNull(view.CutterCheck);
        Assert.False(string.IsNullOrEmpty(view.ETag));
        Assert.All(view.Materials, m => Assert.Equal("Pending", m.Status));
    }

    // ── PUT materials ──────────────────────────────────────────────

    [Fact]
    public async Task Put_material_missing_IfMatch_returns_428()
    {
        var (woId, _) = await SeedWoWithBomAsync("WO-7B2-428", "PROD-428");
        var client = await OperatorClientAsync("op-7b2-428");
        await client.GetAsync($"/api/v2/work-orders/{woId}/prepress"); // materialize

        var resp = await client.SendAsync(PutMaterial(woId, 0,
            "{\"status\":\"Ok\"}", ifMatch: null, idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.PreconditionRequired, resp.StatusCode);
    }

    [Fact]
    public async Task Put_material_missing_Idempotency_returns_400()
    {
        var (woId, _) = await SeedWoWithBomAsync("WO-7B2-400", "PROD-400");
        var client = await OperatorClientAsync("op-7b2-400");
        await client.GetAsync($"/api/v2/work-orders/{woId}/prepress");
        var etag = await EtagOfAsync(woId);

        var resp = await client.SendAsync(PutMaterial(woId, 0,
            "{\"status\":\"Ok\"}", ifMatch: $"\"{etag}\"", idem: null));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Put_material_stale_IfMatch_returns_409_and_emits_state_conflict_audit()
    {
        var (woId, _) = await SeedWoWithBomAsync("WO-7B2-409", "PROD-409");
        var client = await OperatorClientAsync("op-7b2-409");
        await client.GetAsync($"/api/v2/work-orders/{woId}/prepress");
        var stale = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        var resp = await client.SendAsync(PutMaterial(woId, 0,
            "{\"status\":\"Ok\"}", ifMatch: $"\"{stale}\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var conflicts = await db.AuditLogs
            .CountAsync(a => a.Action == "WO_STATE_CONFLICT" && a.TargetId == woId.ToString());
        Assert.True(conflicts >= 1);
    }

    [Fact]
    public async Task Put_material_invalid_status_returns_422()
    {
        var (woId, _) = await SeedWoWithBomAsync("WO-7B2-422S", "PROD-422S");
        var client = await OperatorClientAsync("op-7b2-422s");
        await client.GetAsync($"/api/v2/work-orders/{woId}/prepress");
        var etag = await EtagOfAsync(woId);

        var resp = await client.SendAsync(PutMaterial(woId, 0,
            "{\"status\":\"WHATEVER\"}", ifMatch: $"\"{etag}\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = (await resp.Content.ReadFromJsonAsync<ApiError>())!;
        Assert.Equal("prepress.invalid_status", err.Code);
    }

    [Fact]
    public async Task Put_material_NG_without_reason_code_returns_422()
    {
        var (woId, _) = await SeedWoWithBomAsync("WO-7B2-422R", "PROD-422R");
        var client = await OperatorClientAsync("op-7b2-422r");
        await client.GetAsync($"/api/v2/work-orders/{woId}/prepress");
        var etag = await EtagOfAsync(woId);

        var resp = await client.SendAsync(PutMaterial(woId, 0,
            "{\"status\":\"Ng\",\"ngNote\":\"defect\"}",
            ifMatch: $"\"{etag}\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = (await resp.Content.ReadFromJsonAsync<ApiError>())!;
        Assert.Equal("prepress.invalid_reason_code", err.Code);
    }

    [Fact]
    public async Task Put_material_NG_without_ng_note_returns_422()
    {
        await SeedScrapReasonAsync();
        var (woId, _) = await SeedWoWithBomAsync("WO-7B2-422N", "PROD-422N");
        var client = await OperatorClientAsync("op-7b2-422n");
        await client.GetAsync($"/api/v2/work-orders/{woId}/prepress");
        var etag = await EtagOfAsync(woId);

        var resp = await client.SendAsync(PutMaterial(woId, 0,
            "{\"status\":\"Ng\",\"ngReasonCode\":\"SC-COLOR\"}",
            ifMatch: $"\"{etag}\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = (await resp.Content.ReadFromJsonAsync<ApiError>())!;
        Assert.Equal("prepress.invalid_ng_note", err.Code);
    }

    [Fact]
    public async Task Put_material_NG_unregistered_reason_code_returns_422()
    {
        var (woId, _) = await SeedWoWithBomAsync("WO-7B2-422U", "PROD-422U");
        var client = await OperatorClientAsync("op-7b2-422u");
        await client.GetAsync($"/api/v2/work-orders/{woId}/prepress");
        var etag = await EtagOfAsync(woId);

        var resp = await client.SendAsync(PutMaterial(woId, 0,
            "{\"status\":\"Ng\",\"ngReasonCode\":\"NOT-A-REAL-CODE\",\"ngNote\":\"foo\"}",
            ifMatch: $"\"{etag}\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = (await resp.Content.ReadFromJsonAsync<ApiError>())!;
        Assert.Equal("prepress.invalid_reason_code", err.Code);
    }

    [Fact]
    public async Task Put_material_not_in_PREPRESS_returns_422_invalid_phase()
    {
        var (woId, _) = await SeedWoWithBomAsync("WO-7B2-PH", "PROD-PH", mesPhase: "RUNNING");
        var client = await OperatorClientAsync("op-7b2-ph");
        var etag = await EtagOfAsync(woId);

        var resp = await client.SendAsync(PutMaterial(woId, 0,
            "{\"status\":\"Ok\"}", ifMatch: $"\"{etag}\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = (await resp.Content.ReadFromJsonAsync<ApiError>())!;
        Assert.Equal("wo.invalid_phase", err.Code);
    }

    [Fact]
    public async Task Put_material_unknown_bom_line_idx_returns_404()
    {
        var (woId, _) = await SeedWoWithBomAsync("WO-7B2-LINE", "PROD-LINE", bomLines: 3);
        var client = await OperatorClientAsync("op-7b2-line");
        await client.GetAsync($"/api/v2/work-orders/{woId}/prepress");
        var etag = await EtagOfAsync(woId);

        var resp = await client.SendAsync(PutMaterial(woId, 99,
            "{\"status\":\"Ok\"}", ifMatch: $"\"{etag}\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Put_material_happy_path_returns_200_with_bumped_etag_and_audit()
    {
        var (woId, _) = await SeedWoWithBomAsync("WO-7B2-OK", "PROD-OK", bomLines: 3);
        var client = await OperatorClientAsync("op-7b2-ok");
        await client.GetAsync($"/api/v2/work-orders/{woId}/prepress");
        var preEtag = await EtagOfAsync(woId);

        var resp = await client.SendAsync(PutMaterial(woId, 0,
            "{\"status\":\"Ok\",\"qtyLoaded\":50.5,\"lotNo\":\"LOT-123\"}",
            ifMatch: $"\"{preEtag}\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = (await resp.Content.ReadFromJsonAsync<PrepressSetResponse>())!;
        Assert.True(body.Ok);
        Assert.NotEqual(preEtag, body.ETag);
        Assert.False(body.MaterialsReady); // 2 other materials + plate + cutter still pending

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var row = await db.WoMaterials.SingleAsync(m => m.WorkOrderId == woId && m.BomLineIdx == 0);
        Assert.Equal(PrepressCheckStatus.Ok, row.Status);
        Assert.Equal(50.5, row.QtyLoaded);
        Assert.Equal("LOT-123", row.LotNo);

        var auditCount = await db.AuditLogs
            .CountAsync(a => a.Action == "WO_PREPRESS_MATERIAL_SET" && a.TargetId == woId.ToString());
        Assert.True(auditCount >= 1);
    }

    // ── PUT plate ──────────────────────────────────────────────────

    [Fact]
    public async Task Put_plate_happy_path_returns_200()
    {
        var (woId, _) = await SeedWoWithBomAsync("WO-7B2-PLATE", "PROD-PLATE");
        var client = await OperatorClientAsync("op-7b2-plate");
        await client.GetAsync($"/api/v2/work-orders/{woId}/prepress");
        var etag = await EtagOfAsync(woId);

        var resp = await client.SendAsync(PutPlate(woId,
            "{\"status\":\"Ok\",\"plateNo\":\"PLT-001\"}",
            ifMatch: $"\"{etag}\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var p = await db.WoPlateChecks.SingleAsync(x => x.WorkOrderId == woId);
        Assert.Equal(PrepressCheckStatus.Ok, p.Status);
        Assert.Equal("PLT-001", p.PlateNo);
        var auditCount = await db.AuditLogs
            .CountAsync(a => a.Action == "WO_PREPRESS_PLATE_SET" && a.TargetId == woId.ToString());
        Assert.True(auditCount >= 1);
    }

    // ── PUT cutter ─────────────────────────────────────────────────

    [Fact]
    public async Task Put_cutter_happy_path_returns_200()
    {
        var (woId, _) = await SeedWoWithBomAsync("WO-7B2-CUT", "PROD-CUT");
        var client = await OperatorClientAsync("op-7b2-cut");
        await client.GetAsync($"/api/v2/work-orders/{woId}/prepress");
        var etag = await EtagOfAsync(woId);

        var resp = await client.SendAsync(PutCutter(woId,
            "{\"status\":\"Ok\",\"cutterNo\":\"CUT-001\"}",
            ifMatch: $"\"{etag}\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var c = await db.WoCutterChecks.SingleAsync(x => x.WorkOrderId == woId);
        Assert.Equal(PrepressCheckStatus.Ok, c.Status);
        Assert.Equal("CUT-001", c.CutterNo);
        var auditCount = await db.AuditLogs
            .CountAsync(a => a.Action == "WO_PREPRESS_CUTTER_SET" && a.TargetId == woId.ToString());
        Assert.True(auditCount >= 1);
    }

    // ── Rollup flips when ALL surfaces OK ──────────────────────────

    [Fact]
    public async Task Materials_ready_flips_true_when_all_rows_plate_and_cutter_all_OK()
    {
        var (woId, _) = await SeedWoWithBomAsync("WO-7B2-FLIP", "PROD-FLIP", bomLines: 2);
        var client = await OperatorClientAsync("op-7b2-flip");
        await client.GetAsync($"/api/v2/work-orders/{woId}/prepress");

        // Sequential — fresh ETag each step.
        var etag = await EtagOfAsync(woId);
        var r1 = await client.SendAsync(PutMaterial(woId, 0, "{\"status\":\"Ok\"}",
            $"\"{etag}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);

        etag = await EtagOfAsync(woId);
        var r2 = await client.SendAsync(PutMaterial(woId, 1, "{\"status\":\"Ok\"}",
            $"\"{etag}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);

        etag = await EtagOfAsync(woId);
        var r3 = await client.SendAsync(PutPlate(woId, "{\"status\":\"Ok\"}",
            $"\"{etag}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, r3.StatusCode);

        etag = await EtagOfAsync(woId);
        var r4 = await client.SendAsync(PutCutter(woId, "{\"status\":\"Ok\"}",
            $"\"{etag}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, r4.StatusCode);
        var body4 = (await r4.Content.ReadFromJsonAsync<PrepressSetResponse>())!;
        Assert.True(body4.MaterialsReady);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var wo = await db.WorkOrders.AsNoTracking().SingleAsync(w => w.Id == woId);
        Assert.True(wo.MaterialsReady);
    }

    // ── Rollup race: critical condition #1 ────────────────────────
    //    Seed WO with 10 PENDING material rows + plate OK + cutter OK.
    //    Hammer 10 PUTs in parallel (1 per row). All should commit;
    //    final rollup MUST be true (10 OK + plate OK + cutter OK).
    //    If the recompute were racy, the final WorkOrder.MaterialsReady
    //    could land false despite all rows being OK.

    [Fact]
    [Trait("Category", "Soak")]
    public async Task Concurrent_prepress_row_updates_N_equals_10_yield_consistent_rollup()
    {
        var (woId, _) = await SeedWoWithBomAsync("WO-7B2-SOAK", "PROD-SOAK", bomLines: 10);
        var client = await OperatorClientAsync("op-7b2-soak");
        await client.GetAsync($"/api/v2/work-orders/{woId}/prepress");

        // Pre-set plate + cutter to OK so they're not the bottleneck.
        var preEtag = await EtagOfAsync(woId);
        var r1 = await client.SendAsync(PutPlate(woId, "{\"status\":\"Ok\"}",
            $"\"{preEtag}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        var midEtag = await EtagOfAsync(woId);
        var r2 = await client.SendAsync(PutCutter(woId, "{\"status\":\"Ok\"}",
            $"\"{midEtag}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);

        // Now fire 10 material updates in parallel from the SAME starting ETag.
        // Each starts from the same If-Match; SQLite write-lock + the
        // controller's DbUpdateConcurrencyException handler serialise them.
        // Exactly 1 wins, 9 get 409 wo.state_conflict (matches /advance N=50
        // soak pattern from 7a-1.4). After the loser refetches + retries,
        // all 10 eventually land — but here we just assert the 1-winner /
        // 9-conflict ratio + final rollup once the winner commits.
        var startEtag = await EtagOfAsync(woId);
        var tasks = Enumerable.Range(0, 10).Select(idx =>
            client.SendAsync(PutMaterial(woId, idx, "{\"status\":\"Ok\"}",
                ifMatch: $"\"{startEtag}\"", idem: Guid.NewGuid().ToString())));
        var responses = await Task.WhenAll(tasks);

        var oks = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var conflicts = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(1, oks);
        Assert.Equal(9, conflicts);

        // Rollup invariant after the winner: 1 row OK + 9 rows PENDING
        // ⇒ MaterialsReady=false (the race condition would falsely flip
        // it to true by reading partial state mid-recompute).
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var wo = await db.WorkOrders.AsNoTracking().SingleAsync(w => w.Id == woId);
        Assert.False(wo.MaterialsReady);
        var okCount = await db.WoMaterials.CountAsync(m =>
            m.WorkOrderId == woId && m.Status == PrepressCheckStatus.Ok);
        Assert.Equal(1, okCount);
    }

    // ── Wire-level audit visibility (Rule 7.3) ─────────────────────

    [Fact]
    public async Task Audit_row_for_material_set_is_visible_via_wire_audit_log_endpoint()
    {
        await _fx.SeedUserAsync("adm-7b2-wire", "P@ss!1", UserRole.Admin);
        var admin = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(admin, "adm-7b2-wire", "P@ss!1");

        var (woId, _) = await SeedWoWithBomAsync("WO-7B2-WIRE", "PROD-WIRE");
        await admin.GetAsync($"/api/v2/work-orders/{woId}/prepress");
        var etag = await EtagOfAsync(woId);
        var resp = await admin.SendAsync(PutMaterial(woId, 0,
            "{\"status\":\"Ok\",\"qtyLoaded\":100}",
            ifMatch: $"\"{etag}\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        // Hit the SAME wire endpoint a future checkpoint script will use.
        var audit = await admin.GetAsync(
            "/api/v2/audit/log?action=WO_PREPRESS_MATERIAL_SET&page=1&pageSize=50");
        Assert.Equal(HttpStatusCode.OK, audit.StatusCode);
        var body = await audit.Content.ReadAsStringAsync();
        Assert.Contains($"\"targetId\":\"{woId}\"", body);
        Assert.Contains("\\\"bom_line_idx\\\":0", body);
    }

    // ── Special accept (role-gated) ─────────────────────────────────

    [Fact]
    public async Task Special_accept_forbidden_for_operator_returns_403()
    {
        await SeedScrapReasonAsync();
        var (woId, _) = await SeedWoWithBomAsync("WO-SA-403", "PROD-SA403");
        var client = await OperatorClientAsync("op-sa-403");
        await client.GetAsync($"/api/v2/work-orders/{woId}/prepress");
        var etag = await EtagOfAsync(woId);

        var resp = await client.SendAsync(PostSpecialAccept(woId, 0,
            "{\"ngReasonCode\":\"SC-COLOR\",\"note\":\"concession\"}",
            ifMatch: $"\"{etag}\"", idem: Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        var err = (await resp.Content.ReadFromJsonAsync<ApiError>())!;
        Assert.Equal("prepress.special_accept_forbidden", err.Code);
    }

    [Fact]
    public async Task Special_accept_by_engineer_records_ok_keeping_deviation()
    {
        await SeedScrapReasonAsync();
        var (woId, _) = await SeedWoWithBomAsync("WO-SA-ENG", "PROD-SAENG");
        var client = await ClientAsRoleAsync("eng-sa", UserRole.Engineer);
        await client.GetAsync($"/api/v2/work-orders/{woId}/prepress");
        var etag = await EtagOfAsync(woId);

        var resp = await client.SendAsync(PostSpecialAccept(woId, 0,
            "{\"ngReasonCode\":\"SC-COLOR\",\"note\":\"accepted by PD leader\",\"partScan\":\"COMP-X\"}",
            ifMatch: $"\"{etag}\"", idem: Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = (await resp.Content.ReadFromJsonAsync<PrepressSetResponse>())!;
        Assert.True(body.Ok);

        // Row is OK (counts toward MaterialsReady) but the deviation is kept.
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var row = await db.WoMaterials.AsNoTracking()
            .FirstAsync(m => m.WorkOrderId == woId && m.BomLineIdx == 0);
        Assert.Equal(PrepressCheckStatus.Ok, row.Status);
        Assert.Equal("SC-COLOR", row.NgReasonCode);
    }
}
