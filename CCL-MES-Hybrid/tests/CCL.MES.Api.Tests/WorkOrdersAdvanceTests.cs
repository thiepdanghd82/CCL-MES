using System.Net;
using System.Net.Http.Json;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain;
using CCL.MES.Domain.Auth;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.WorkOrders;
using Microsoft.Extensions.DependencyInjection;

namespace CCL.MES.Api.Tests;

/// <summary>
/// P10.3 W4 — coverage for the WO summary + advance endpoints:
///   GET /work-orders/by-no/{woNo}/summary  → 200 / 404
///   POST /work-orders/{id}/advance         → 200 ok=true / 200 ok=false (guard) / 404
/// + verifies that the X-Device-Id header emits the WO_ADVANCE_DEVICE
/// audit row alongside the existing WO_ADVANCE row.
/// </summary>
public sealed class WorkOrdersAdvanceTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public WorkOrdersAdvanceTests(MesApiFactory fx) => _fx = fx;

    private async Task<WorkOrder> SeedWoAsync(string woNo, ProcessStepCode step = ProcessStepCode.ReadyToRun, bool materialsReady = true, bool setupConfirmed = true)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();

        var customer = new Customer { Code = "CUST-" + woNo, Name = "Customer " + woNo };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var product = new Product { ProductCode = "PROD-" + woNo, Name = "Product " + woNo, CustomerId = customer.Id };
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
            CurrentStep = step,
            Status = WoStatus.InProgress,
            MaterialsReady = materialsReady,
            SetupConfirmed = setupConfirmed,
        };
        db.WorkOrders.Add(wo);
        await db.SaveChangesAsync();
        return wo;
    }

    [Fact]
    public async Task Summary_returns_404_when_wo_not_found()
    {
        await _fx.SeedUserAsync("wo1", "P@ss!", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "wo1", "P@ss!");

        var resp = await client.GetAsync("/api/v2/work-orders/by-no/DOES-NOT-EXIST/summary");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("work_order.not_found", err!.Code);
    }

    [Fact]
    public async Task Summary_returns_shape_for_existing_wo()
    {
        await _fx.SeedUserAsync("wo2", "P@ss!", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "wo2", "P@ss!");
        var wo = await SeedWoAsync("WO-W4-100", ProcessStepCode.ReadyToRun);

        var summary = await client.GetFromJsonAsync<WorkOrderSummary>(
            $"/api/v2/work-orders/by-no/{Uri.EscapeDataString(wo.WoNo)}/summary");

        Assert.NotNull(summary);
        Assert.Equal(wo.WoNo, summary!.WoNo);
        Assert.Equal("Customer WO-W4-100", summary.CustomerName);
        Assert.Equal("PROD-WO-W4-100", summary.ProductCode);
        Assert.Equal("ReadyToRun", summary.CurrentStep);
        // P10.7c-3 BUG-FIX — MesPhase MUST be projected so client can
        // dispatch on the canonical phase instead of the legacy
        // CurrentStep (which doesn't change in lock-step post-/setting/done).
        // Test seed leaves MesPhase at the entity default ("NEW") regardless
        // of the CurrentStep arg — that's fine; what matters is that the
        // controller surfaces the field non-empty so client dispatch works.
        Assert.False(string.IsNullOrEmpty(summary.MesPhase));
        // BadgeLabelKey gets populated by the WorkOrderStatusBadge mapper —
        // we don't assert the exact key here (avoid coupling to badge prose),
        // just that it's non-empty.
        Assert.False(string.IsNullOrEmpty(summary.BadgeLabelKey));
    }

    [Fact]
    public async Task Drawer_by_no_projects_MesPhase_per_L19_amendment()
    {
        // P10.7d-4 — Henry RCA on PR #120 step 13. The L19 fix (PR #115)
        // projected MesPhase on /by-no/{woNo}/summary so the UI
        // dispatch + auto-route would key on the canonical phase.
        // But the SIBLING bare /by-no/{woNo} drawer DTO was left
        // un-touched and silently returned no mesPhase field — broke
        // the checkpoint script's L21 wire assertion even though the
        // WO was correctly in IPQC_APPROVED in the DB.
        //
        // L19 amendment in this PR: EVERY endpoint that returns a WO
        // record MUST project canonical MesPhase. This test locks that
        // invariant for the drawer endpoint specifically; any future
        // refactor that drops MesPhase from WorkOrderDrawerView breaks
        // here at CI rather than at operator runtime.
        await _fx.SeedUserAsync("wo-drawer-mp", "P@ss!", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "wo-drawer-mp", "P@ss!");
        var wo = await SeedWoAsync("WO-DRAWER-MP", ProcessStepCode.ReadyToRun);

        var resp = await client.GetAsync(
            $"/api/v2/work-orders/by-no/{Uri.EscapeDataString(wo.WoNo)}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        // Read raw JSON so the assertion CANNOT be fooled by a DTO
        // rebind to a different shape — we're locking the wire
        // representation operators (+ shell scripts like checkpoint-7d-final)
        // actually see. Field name "mesPhase" matches camelCase per
        // ASP.NET Core's default JsonSerializerOptions.
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("mesPhase", out var phaseProp),
            "Drawer DTO MUST expose 'mesPhase' field (L19 amendment, PR #120). " +
            "Without it operator scripts that curl /by-no/{woNo} read empty + " +
            "fail L21 wire assertions even when the WO advanced correctly.");
        var phase = phaseProp.GetString();
        Assert.False(string.IsNullOrEmpty(phase),
            $"mesPhase MUST be non-empty (got '{phase}'). The entity default 'NEW' " +
            "is acceptable for legacy rows; an empty string means the DTO projection " +
            "dropped the field.");
    }

    [Fact]
    public async Task Advance_ok_path_returns_next_step()
    {
        await _fx.SeedUserAsync("wo3", "P@ss!", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "wo3", "P@ss!");
        // ReadyToRun → Running is unconditional, so this exercises the
        // success path without needing additional guard prep.
        var wo = await SeedWoAsync("WO-W4-200", ProcessStepCode.ReadyToRun);

        client.DefaultRequestHeaders.Add("X-Device-Id", "0193a1d9-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var resp = await PostAdvanceAsync(client, wo);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<AdvanceWorkOrderResponse>();
        Assert.NotNull(body);
        Assert.True(body!.Ok);
        Assert.Equal("Running", body.CurrentStep);
        Assert.Null(body.ErrorCode);
    }

    [Fact]
    public async Task Advance_guard_failure_returns_200_with_error_code()
    {
        await _fx.SeedUserAsync("wo4", "P@ss!", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "wo4", "P@ss!");
        // PrePressCheck without ProductRevisionId + MaterialsReady = guard fails.
        var wo = await SeedWoAsync("WO-W4-300", ProcessStepCode.PrePressCheck, materialsReady: false);

        var resp = await PostAdvanceAsync(client, wo);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<AdvanceWorkOrderResponse>();
        Assert.NotNull(body);
        Assert.False(body!.Ok);
        Assert.Equal("PrePressCheck", body.CurrentStep);
        Assert.Equal("RequiresSpecAndMaterials", body.ErrorCode);
    }

    [Fact]
    public async Task Advance_returns_404_when_wo_id_does_not_exist()
    {
        await _fx.SeedUserAsync("wo5", "P@ss!", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "wo5", "P@ss!");

        // P10.7a-1.3 — 404 check happens BEFORE the If-Match check (lookup
        // by id is the existence check), so a missing-id request without
        // any headers still surfaces 404.
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v2/work-orders/999999/advance");
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Advance_with_device_id_emits_paired_audit_row()
    {
        await _fx.SeedUserAsync("wo6", "P@ss!", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "wo6", "P@ss!");
        var wo = await SeedWoAsync("WO-W4-400", ProcessStepCode.ReadyToRun);
        var deviceId = "0193a1d9-cafe-cafe-cafe-cafecafecafe";

        client.DefaultRequestHeaders.Add("X-Device-Id", deviceId);
        var resp = await PostAdvanceAsync(client, wo);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var devAudit = db.AuditLogs
            .Where(a => a.Action == "WO_ADVANCE_DEVICE" && a.TargetId == deviceId)
            .ToList();
        Assert.NotEmpty(devAudit);
        Assert.Contains(devAudit, a => a.Detail != null && a.Detail.Contains(wo.WoNo));
    }

    [Fact]
    public async Task Advance_audit_from_to_capture_uses_before_value()
    {
        // Regression guard — caught during hardware verify on 2026-06-03.
        // WorkOrderService.AdvanceAsync re-queries the WO via the same EF
        // tracked context, so reading existing.CurrentStep AFTER the call
        // gives the AFTER value. Controller now captures `fromStep` before
        // the call. This test asserts the recorded "from" matches the
        // pre-advance step and "to" matches the post-advance step.
        await _fx.SeedUserAsync("wo7", "P@ss!", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "wo7", "P@ss!");
        var wo = await SeedWoAsync("WO-W4-500", ProcessStepCode.ReadyToRun);
        var deviceId = "0193a1d9-from-tocp-1234-567890abcdef";

        client.DefaultRequestHeaders.Add("X-Device-Id", deviceId);
        var resp = await PostAdvanceAsync(client, wo);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var devAudit = db.AuditLogs
            .Where(a => a.Action == "WO_ADVANCE_DEVICE" && a.TargetId == deviceId)
            .Single();
        Assert.NotNull(devAudit.Detail);
        Assert.Contains("\"from\":\"ReadyToRun\"", devAudit.Detail);
        Assert.Contains("\"to\":\"Running\"", devAudit.Detail);
    }

    // ═════════════════════════════════════════════════════════════════
    // P10.7a-1.3 — RowVersion + Idempotency-Key contract retrofit
    // ═════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Advance_without_IfMatch_returns_428()
    {
        await _fx.SeedUserAsync("wo-cn1", "P@ss!", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "wo-cn1", "P@ss!");
        var wo = await SeedWoAsync("WO-CN-100", ProcessStepCode.ReadyToRun);

        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v2/work-orders/{wo.Id}/advance");
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.PreconditionRequired, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("wo.if_match_required", err!.Code);
    }

    [Fact]
    public async Task Advance_without_IdempotencyKey_returns_400()
    {
        await _fx.SeedUserAsync("wo-cn2", "P@ss!", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "wo-cn2", "P@ss!");
        var wo = await SeedWoAsync("WO-CN-200", ProcessStepCode.ReadyToRun);

        var etag = await GetCurrentEtagAsync(client, wo.WoNo);
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v2/work-orders/{wo.Id}/advance");
        req.Headers.TryAddWithoutValidation("If-Match", $"\"{etag}\"");
        var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("wo.idempotency_key_required", err!.Code);
    }

    [Fact]
    public async Task Advance_with_stale_IfMatch_returns_409_and_emits_WO_STATE_CONFLICT()
    {
        await _fx.SeedUserAsync("wo-cn3", "P@ss!", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "wo-cn3", "P@ss!");
        var wo = await SeedWoAsync("WO-CN-300", ProcessStepCode.ReadyToRun);

        var resp = await PostAdvanceAsync(client, wo, ifMatchOverride: "AAAAAAAAAAA=");

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<AdvanceWorkOrderResponse>();
        Assert.NotNull(body);
        Assert.False(body!.Ok);
        Assert.Equal("wo.state_conflict", body.ErrorCode);
        Assert.False(string.IsNullOrEmpty(body.ETag));

        // Server returns current ETag in body + ETag header so client
        // can reload+retry without re-fetching the summary.
        Assert.NotNull(resp.Headers.ETag);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var conflictAudit = db.AuditLogs
            .Where(a => a.Action == "WO_STATE_CONFLICT" && a.TargetId == wo.Id.ToString())
            .Single();
        Assert.NotNull(conflictAudit.Detail);
        Assert.Contains("\"attempted_action\":\"advance\"", conflictAudit.Detail);
        Assert.Contains("\"client_version\":\"AAAAAAAAAAA=\"", conflictAudit.Detail);
        Assert.Contains("\"server_version\":", conflictAudit.Detail);
    }

    [Fact]
    public async Task Advance_with_valid_IfMatch_returns_200_with_new_ETag_in_body_and_header()
    {
        await _fx.SeedUserAsync("wo-cn4", "P@ss!", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "wo-cn4", "P@ss!");
        var wo = await SeedWoAsync("WO-CN-400", ProcessStepCode.ReadyToRun);

        var oldEtag = await GetCurrentEtagAsync(client, wo.WoNo);
        var resp = await PostAdvanceAsync(client, wo, ifMatchOverride: oldEtag);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<AdvanceWorkOrderResponse>();
        Assert.NotNull(body);
        Assert.True(body!.Ok);
        Assert.False(string.IsNullOrEmpty(body.ETag));
        // Post-advance ETag MUST differ from the pre-advance value (the
        // SQLite trigger bumps RowVersion on every UPDATE).
        Assert.NotEqual(oldEtag, body.ETag);
        Assert.NotNull(resp.Headers.ETag);
        Assert.Contains(body.ETag, resp.Headers.ETag!.Tag);
    }

    [Fact]
    public async Task Advance_with_unquoted_IfMatch_value_still_accepted()
    {
        // RFC 7232 requires quotes but naive HTTP libs sometimes omit
        // them. Server normalizes both forms.
        await _fx.SeedUserAsync("wo-cn5", "P@ss!", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "wo-cn5", "P@ss!");
        var wo = await SeedWoAsync("WO-CN-500", ProcessStepCode.ReadyToRun);

        var etag = await GetCurrentEtagAsync(client, wo.WoNo);
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v2/work-orders/{wo.Id}/advance");
        // No surrounding quotes
        req.Headers.TryAddWithoutValidation("If-Match", etag);
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Advance_with_weak_etag_prefix_normalised_and_accepted()
    {
        await _fx.SeedUserAsync("wo-cn6", "P@ss!", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "wo-cn6", "P@ss!");
        var wo = await SeedWoAsync("WO-CN-600", ProcessStepCode.ReadyToRun);

        var etag = await GetCurrentEtagAsync(client, wo.WoNo);
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v2/work-orders/{wo.Id}/advance");
        req.Headers.TryAddWithoutValidation("If-Match", $"W/\"{etag}\"");
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Advance_replay_same_idempotency_key_returns_stored_response()
    {
        await _fx.SeedUserAsync("wo-cn7", "P@ss!", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "wo-cn7", "P@ss!");
        var wo = await SeedWoAsync("WO-CN-700", ProcessStepCode.ReadyToRun);

        var etag = await GetCurrentEtagAsync(client, wo.WoNo);
        var key = Guid.NewGuid().ToString();
        var resp1 = await SendAdvanceAsync(client, wo, etag, key);
        var body1 = await resp1.Content.ReadAsStringAsync();
        var resp2 = await SendAdvanceAsync(client, wo, etag, key);
        var body2 = await resp2.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);
        Assert.Equal(body1, body2);
        Assert.True(resp2.Headers.Contains("Idempotency-Replayed"));

        // CurrentStep advanced ONCE — replay path didn't fire the state
        // machine a second time.
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var advances = db.AuditLogs
            .Count(a => a.Action == "WO_ADVANCE" && a.TargetId == wo.Id.ToString());
        Assert.Equal(1, advances);
    }

    [Fact]
    public async Task Advance_post_success_etag_can_drive_next_advance()
    {
        // Pre-Sprint test that the full chain works: GET → advance → use
        // returned ETag → next advance. Covers ReadyToRun → Running →
        // Fqc (which needs ProducedQty > 0; we set it during seed).
        await _fx.SeedUserAsync("wo-cn8", "P@ss!", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "wo-cn8", "P@ss!");
        var wo = await SeedWoAsync("WO-CN-800", ProcessStepCode.ReadyToRun);

        var etag1 = await GetCurrentEtagAsync(client, wo.WoNo);
        var resp1 = await PostAdvanceAsync(client, wo, ifMatchOverride: etag1);
        var body1 = await resp1.Content.ReadFromJsonAsync<AdvanceWorkOrderResponse>();
        Assert.True(body1!.Ok);
        var etag2 = body1.ETag;
        Assert.False(string.IsNullOrEmpty(etag2));
        Assert.NotEqual(etag1, etag2);

        // Set ProducedQty so Running → Fqc passes the legacy guard.
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
            var fresh = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .SingleAsync(db.WorkOrders, w => w.Id == wo.Id);
            fresh.ProducedQty = 100;
            await db.SaveChangesAsync();
        }
        // SQLite trigger bumps RowVersion AFTER the UPDATE statement
        // executes; EF's tracked entity still carries the pre-trigger
        // value. Re-read via the wire (the controller uses AsNoTracking
        // and reads the actual DB row) so the next advance gets the
        // current ETag.
        etag2 = await GetCurrentEtagAsync(client, wo.WoNo);

        var resp2 = await PostAdvanceAsync(client, wo, ifMatchOverride: etag2);
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);
        var body2 = await resp2.Content.ReadFromJsonAsync<AdvanceWorkOrderResponse>();
        Assert.True(body2!.Ok);
        Assert.Equal("Fqc", body2.CurrentStep);
    }

    [Fact]
    public async Task Summary_response_includes_ETag_in_body_and_header()
    {
        await _fx.SeedUserAsync("wo-cn9", "P@ss!", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "wo-cn9", "P@ss!");
        var wo = await SeedWoAsync("WO-CN-900", ProcessStepCode.ReadyToRun);

        var resp = await client.GetAsync(
            $"/api/v2/work-orders/by-no/{Uri.EscapeDataString(wo.WoNo)}/summary");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<WorkOrderSummary>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrEmpty(body!.ETag));
        Assert.NotNull(resp.Headers.ETag);
        Assert.Contains(body.ETag, resp.Headers.ETag!.Tag);
    }

    // P10.7a-1.4 — contract §10.6.3 soak at N=50. Higher fan-out than
    // the N=10 7a-1.3 sanity test so a future SQLite WAL or EF-Core
    // batching regression that only surfaces at scale fails CI rather
    // than at the operator's tap. Marked Category=Soak so a flaky-CI
    // workaround can `dotnet test --filter "Category!=Soak"` to skip
    // without losing other coverage.
    [Trait("Category", "Soak")]
    [Fact]
    public async Task Concurrent_advances_at_N_equals_50_yield_one_winner_and_49_conflicts()
    {
        await _fx.SeedUserAsync("wo-soak-50", "P@ss!", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "wo-soak-50", "P@ss!");
        var wo = await SeedWoAsync("WO-SOAK-N50", ProcessStepCode.ReadyToRun);

        var etag = await GetCurrentEtagAsync(client, wo.WoNo);
        const int N = 50;
        var tasks = Enumerable.Range(0, N).Select(_ =>
            PostAdvanceAsync(client, wo, ifMatchOverride: etag,
                idemKeyOverride: Guid.NewGuid().ToString())).ToArray();

        var results = await Task.WhenAll(tasks);
        var ok = results.Count(r => r.StatusCode == HttpStatusCode.OK);
        var conflicts = results.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        Assert.Equal(1, ok);
        Assert.Equal(N - 1, conflicts);

        // Audit-side invariant: exactly one WO_ADVANCE row + (N-1)
        // WO_STATE_CONFLICT rows. Locks "state machine fires once,
        // every loser leaves a forensic trail."
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        Assert.Equal(1, db.AuditLogs
            .Count(a => a.Action == "WO_ADVANCE" && a.TargetId == wo.Id.ToString()));
        Assert.Equal(N - 1, db.AuditLogs
            .Count(a => a.Action == "WO_STATE_CONFLICT" && a.TargetId == wo.Id.ToString()));
    }

    [Fact]
    public async Task Concurrent_advances_only_one_succeeds_others_get_409()
    {
        // Concurrency soak — N parallel advances with the SAME ETag.
        // Exactly one wins (RowVersion check passes; trigger bumps the
        // version atomically), the other N-1 get 409. Uses a smaller
        // N than the contract's 50 to keep CI runtime manageable; the
        // semantic is identical at any N > 1.
        await _fx.SeedUserAsync("wo-soak1", "P@ss!", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "wo-soak1", "P@ss!");
        var wo = await SeedWoAsync("WO-SOAK-100", ProcessStepCode.ReadyToRun);

        var etag = await GetCurrentEtagAsync(client, wo.WoNo);
        const int N = 10;
        var tasks = Enumerable.Range(0, N).Select(_ =>
            PostAdvanceAsync(client, wo, ifMatchOverride: etag,
                idemKeyOverride: Guid.NewGuid().ToString())).ToArray();

        var results = await Task.WhenAll(tasks);
        var ok = results.Count(r => r.StatusCode == HttpStatusCode.OK);
        var conflicts = results.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        Assert.Equal(1, ok);
        Assert.Equal(N - 1, conflicts);
    }

    [Fact]
    public async Task Concurrent_advances_emit_one_audit_per_conflict_attempt()
    {
        // Each 409 emits one WO_STATE_CONFLICT audit row — the forensic
        // trail of "N operators tried to drive this WO simultaneously".
        await _fx.SeedUserAsync("wo-soak2", "P@ss!", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "wo-soak2", "P@ss!");
        var wo = await SeedWoAsync("WO-SOAK-200", ProcessStepCode.ReadyToRun);

        var etag = await GetCurrentEtagAsync(client, wo.WoNo);
        const int N = 10;
        var tasks = Enumerable.Range(0, N).Select(_ =>
            PostAdvanceAsync(client, wo, ifMatchOverride: etag,
                idemKeyOverride: Guid.NewGuid().ToString())).ToArray();
        await Task.WhenAll(tasks);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var conflicts = db.AuditLogs
            .Count(a => a.Action == "WO_STATE_CONFLICT" && a.TargetId == wo.Id.ToString());
        // N - 1 conflict rows (one per loser). The winner emits
        // WO_ADVANCE, not WO_STATE_CONFLICT.
        Assert.Equal(N - 1, conflicts);
    }

    [Fact]
    public async Task Concurrent_advances_only_one_WO_ADVANCE_audit_row()
    {
        // Belt-and-suspenders: of N parallel attempts with same ETag,
        // exactly ONE legacy WO_ADVANCE row exists. Locks the
        // "state machine fires once" invariant.
        await _fx.SeedUserAsync("wo-soak3", "P@ss!", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "wo-soak3", "P@ss!");
        var wo = await SeedWoAsync("WO-SOAK-300", ProcessStepCode.ReadyToRun);

        var etag = await GetCurrentEtagAsync(client, wo.WoNo);
        const int N = 10;
        var tasks = Enumerable.Range(0, N).Select(_ =>
            PostAdvanceAsync(client, wo, ifMatchOverride: etag,
                idemKeyOverride: Guid.NewGuid().ToString())).ToArray();
        await Task.WhenAll(tasks);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var advances = db.AuditLogs
            .Count(a => a.Action == "WO_ADVANCE" && a.TargetId == wo.Id.ToString());
        Assert.Equal(1, advances);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private async Task<string> GetCurrentEtagAsync(HttpClient client, string woNo)
    {
        var resp = await client.GetAsync($"/api/v2/work-orders/by-no/{Uri.EscapeDataString(woNo)}/summary");
        if (!resp.IsSuccessStatusCode)
        {
            var text = await resp.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"GetCurrentEtag failed: {(int)resp.StatusCode} body={text}");
        }
        var summary = await resp.Content.ReadFromJsonAsync<WorkOrderSummary>();
        var etag = summary?.ETag ?? "";
        if (string.IsNullOrEmpty(etag))
        {
            throw new InvalidOperationException(
                $"GetCurrentEtag for {woNo} returned empty ETag. " +
                $"Summary body: {await resp.Content.ReadAsStringAsync()}");
        }
        return etag;
    }

    private async Task<HttpResponseMessage> PostAdvanceAsync(
        HttpClient client, WorkOrder wo,
        string? ifMatchOverride = null,
        string? idemKeyOverride = null)
    {
        var etag = ifMatchOverride ?? await GetCurrentEtagAsync(client, wo.WoNo);
        return await SendAdvanceAsync(client, wo, etag,
            idemKeyOverride ?? Guid.NewGuid().ToString());
    }

    private static async Task<HttpResponseMessage> SendAdvanceAsync(
        HttpClient client, WorkOrder wo, string etag, string idempotencyKey)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v2/work-orders/{wo.Id}/advance");
        req.Headers.TryAddWithoutValidation("If-Match", $"\"{etag}\"");
        req.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(req);
    }
}
