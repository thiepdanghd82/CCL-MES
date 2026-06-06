using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain;
using CCL.MES.Domain.Auth;
using CCL.MES.Domain.Entities;
using CCL.MES.Domain.StateMachine;
using CCL.MES.Infrastructure;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.WorkOrders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Api.Tests;

/// <summary>
/// P10.7a-2.2 — coverage for POST /api/v2/admin/work-orders/{id}/force-phase.
/// Henry-confirmed contract (per §8 forceable matrix discussion):
///   * AdminOnly policy (Q1 option A)
///   * Body { TargetStep, ReasonCode (one of 6 REC-* codes), ReasonNote 1-500 } (Q2)
///   * Status codes mirror /advance: 428 missing If-Match / 409 stale | unforceable / 400 missing Idempotency-Key / 422 body
///   * SYS_RECOVERY audit detail JSON shape: { wo_id, wo_no, from_phase, to_phase, reason: { code, note }, sys_user_id }
///   * Child-table rows preserved per §8.1 B3
/// </summary>
public sealed class AdminWorkOrdersForcePhaseTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public AdminWorkOrdersForcePhaseTests(MesApiFactory fx) => _fx = fx;

    // ── Seed helpers ───────────────────────────────────────────────

    private async Task<WorkOrder> SeedWoAsync(
        string woNo,
        ProcessStepCode step = ProcessStepCode.OpSetting,
        string mesPhase = "SETTING")
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
            CurrentStep = step,
            MesPhase = mesPhase,
            Status = WoStatus.InProgress,
            MaterialsReady = true,
            SetupConfirmed = true,
        };
        db.WorkOrders.Add(wo);
        await db.SaveChangesAsync();
        return wo;
    }

    private async Task<HttpClient> AdminClientAsync(string user)
    {
        await _fx.SeedUserAsync(user, "P@ss!1", UserRole.Admin);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, user, "P@ss!1");
        await _fx.SeedRecoveryDataAsync();    // ensures REC-* codes + sys-recovery user exist
        return client;
    }

    private async Task<HttpClient> EngineerClientAsync(string user)
    {
        await _fx.SeedUserAsync(user, "P@ss!1", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, user, "P@ss!1");
        return client;
    }

    private async Task<string> EtagOfAsync(long id)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var rv = await db.WorkOrders.AsNoTracking()
            .Where(w => w.Id == id)
            .Select(w => w.RowVersion)
            .SingleAsync();
        return Convert.ToBase64String(rv);
    }

    private static HttpRequestMessage PostForce(long id, string body, string? ifMatch, string? idemKey)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v2/admin/work-orders/{id}/force-phase")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (ifMatch is not null) req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        if (idemKey is not null) req.Headers.TryAddWithoutValidation("Idempotency-Key", idemKey);
        return req;
    }

    private static string BodyOf(string targetStep, string reasonCode, string reasonNote) =>
        JsonSerializer.Serialize(new { TargetStep = targetStep, ReasonCode = reasonCode, ReasonNote = reasonNote });

    // ── Role-gating ─────────────────────────────────────────────────

    [Fact]
    public async Task Engineer_gets_403_on_force_phase()
    {
        var client = await EngineerClientAsync("eng-force-1");
        var wo = await SeedWoAsync("WO-FP-403");
        var etag = await EtagOfAsync(wo.Id);
        var req = PostForce(wo.Id,
            BodyOf("PrePressCheck", "REC-OP-WEDGE", "engineer attempt"),
            ifMatch: $"\"{etag}\"", idemKey: Guid.NewGuid().ToString());
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    // ── Concurrency contract ────────────────────────────────────────

    [Fact]
    public async Task Missing_IfMatch_returns_428()
    {
        var client = await AdminClientAsync("adm-fp-428");
        var wo = await SeedWoAsync("WO-FP-428");
        var req = PostForce(wo.Id,
            BodyOf("PrePressCheck", "REC-OP-WEDGE", "no if-match"),
            ifMatch: null, idemKey: Guid.NewGuid().ToString());
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.PreconditionRequired, resp.StatusCode);
    }

    [Fact]
    public async Task Missing_IdempotencyKey_returns_400()
    {
        var client = await AdminClientAsync("adm-fp-400");
        var wo = await SeedWoAsync("WO-FP-400");
        var etag = await EtagOfAsync(wo.Id);
        var req = PostForce(wo.Id,
            BodyOf("PrePressCheck", "REC-OP-WEDGE", "no idem"),
            ifMatch: $"\"{etag}\"", idemKey: null);
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Stale_IfMatch_returns_409_with_state_conflict_and_audit()
    {
        var client = await AdminClientAsync("adm-fp-409stale");
        var wo = await SeedWoAsync("WO-FP-STALE");
        var stale = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        var req = PostForce(wo.Id,
            BodyOf("PrePressCheck", "REC-OP-WEDGE", "stale match"),
            ifMatch: $"\"{stale}\"", idemKey: Guid.NewGuid().ToString());
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        var body = (await resp.Content.ReadFromJsonAsync<ForcePhaseResponse>())!;
        Assert.Equal("wo.state_conflict", body.ErrorCode);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var conflictAudits = await db.AuditLogs
            .Where(a => a.Action == "WO_STATE_CONFLICT" && a.TargetId == wo.Id.ToString())
            .CountAsync();
        Assert.True(conflictAudits >= 1, "WO_STATE_CONFLICT audit row must be emitted on stale If-Match");
    }

    // ── 404 ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Missing_WO_returns_404()
    {
        var client = await AdminClientAsync("adm-fp-404");
        var req = PostForce(9_999_999,
            BodyOf("PrePressCheck", "REC-OP-WEDGE", "missing wo"),
            ifMatch: "\"AAAA\"", idemKey: Guid.NewGuid().ToString());
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ── Body validation ────────────────────────────────────────────

    [Fact]
    public async Task Unknown_target_step_returns_422_invalid_target_step()
    {
        var client = await AdminClientAsync("adm-fp-badtarget");
        var wo = await SeedWoAsync("WO-FP-BADTARGET");
        var etag = await EtagOfAsync(wo.Id);
        var req = PostForce(wo.Id,
            BodyOf("ThisIsNotAStep", "REC-OP-WEDGE", "bad target"),
            ifMatch: $"\"{etag}\"", idemKey: Guid.NewGuid().ToString());
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = (await resp.Content.ReadFromJsonAsync<ApiError>())!;
        Assert.Equal("force.invalid_target_step", err.Code);
    }

    [Fact]
    public async Task Non_recovery_reason_code_returns_422_invalid_reason_code()
    {
        var client = await AdminClientAsync("adm-fp-pause-code");
        var wo = await SeedWoAsync("WO-FP-PAUSECODE");
        var etag = await EtagOfAsync(wo.Id);
        var req = PostForce(wo.Id,
            BodyOf("PrePressCheck", "ML-MAT", "pause code, not recovery"),
            ifMatch: $"\"{etag}\"", idemKey: Guid.NewGuid().ToString());
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = (await resp.Content.ReadFromJsonAsync<ApiError>())!;
        Assert.Equal("force.invalid_reason_code", err.Code);
    }

    [Fact]
    public async Task Empty_reason_note_returns_422_invalid_reason_note()
    {
        var client = await AdminClientAsync("adm-fp-emptynote");
        var wo = await SeedWoAsync("WO-FP-EMPTYNOTE");
        var etag = await EtagOfAsync(wo.Id);
        var req = PostForce(wo.Id,
            BodyOf("PrePressCheck", "REC-OP-WEDGE", ""),
            ifMatch: $"\"{etag}\"", idemKey: Guid.NewGuid().ToString());
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = (await resp.Content.ReadFromJsonAsync<ApiError>())!;
        Assert.Equal("force.invalid_reason_note", err.Code);
    }

    [Fact]
    public async Task Overlong_reason_note_returns_422()
    {
        var client = await AdminClientAsync("adm-fp-longnote");
        var wo = await SeedWoAsync("WO-FP-LONGNOTE");
        var etag = await EtagOfAsync(wo.Id);
        var note = new string('x', 501);
        var req = PostForce(wo.Id,
            BodyOf("PrePressCheck", "REC-OP-WEDGE", note),
            ifMatch: $"\"{etag}\"", idemKey: Guid.NewGuid().ToString());
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = (await resp.Content.ReadFromJsonAsync<ApiError>())!;
        Assert.Equal("force.invalid_reason_note", err.Code);
    }

    // ── State-machine guards (Q4a / Q4b / Q1) ───────────────────────

    [Fact]
    public async Task Same_state_force_returns_422_same_state_force()
    {
        var client = await AdminClientAsync("adm-fp-samestate");
        // WO at SETTING (MesPhase=SETTING, CurrentStep=OpSetting). Attempting to
        // force to OpSetting projects to SETTING again — same-state.
        var wo = await SeedWoAsync("WO-FP-SAMESTATE", ProcessStepCode.OpSetting, "SETTING");
        var etag = await EtagOfAsync(wo.Id);
        var req = PostForce(wo.Id,
            BodyOf("OpSetting", "REC-OP-WEDGE", "same state"),
            ifMatch: $"\"{etag}\"", idemKey: Guid.NewGuid().ToString());
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = (await resp.Content.ReadFromJsonAsync<ApiError>())!;
        Assert.Equal("force.same_state_force", err.Code);
    }

    [Fact]
    public async Task Unforceable_transition_DONE_to_PREPRESS_returns_422_unforceable_transition()
    {
        var client = await AdminClientAsync("adm-fp-done");
        // WO at Closed (MesPhase=DONE). DONE is a terminal source per §2.2.
        // Per P10.7a-2.3 status-code rationale: semantic guard rejection
        // (forbidden FOREVER by §3.1) → 422, not 409 (which is reserved
        // for stale If-Match concurrency drift).
        var wo = await SeedWoAsync("WO-FP-DONE", ProcessStepCode.Closed, "DONE");
        var etag = await EtagOfAsync(wo.Id);
        var req = PostForce(wo.Id,
            BodyOf("PrePressCheck", "REC-OP-WEDGE", "trying to revive a DONE wo"),
            ifMatch: $"\"{etag}\"", idemKey: Guid.NewGuid().ToString());
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = (await resp.Content.ReadFromJsonAsync<ApiError>())!;
        Assert.Equal("force.unforceable_transition", err.Code);
    }

    [Fact]
    public async Task Unforceable_transition_SETTING_to_running_returns_422_unforceable_transition()
    {
        var client = await AdminClientAsync("adm-fp-skip");
        // WO at SETTING. Force-to-RUNNING skips IPQC — not in 11-cell set.
        var wo = await SeedWoAsync("WO-FP-SKIP", ProcessStepCode.OpSetting, "SETTING");
        var etag = await EtagOfAsync(wo.Id);
        var req = PostForce(wo.Id,
            BodyOf("Running", "REC-OP-WEDGE", "skipping IPQC"),
            ifMatch: $"\"{etag}\"", idemKey: Guid.NewGuid().ToString());
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = (await resp.Content.ReadFromJsonAsync<ApiError>())!;
        Assert.Equal("force.unforceable_transition", err.Code);
    }

    // ── 409 sentinel: ONLY stale If-Match returns 409 ──────────────
    // Locks the rationale into the test belt — if a future PR
    // overloads 409 with another guard, this assertion + the matching
    // 422 fixtures above triangulate the regression instantly.

    [Fact]
    public async Task Only_stale_ifmatch_returns_409_unforceable_returns_422()
    {
        var client = await AdminClientAsync("adm-fp-409-sentinel");
        var wo = await SeedWoAsync("WO-FP-409SENT", ProcessStepCode.Closed, "DONE");
        var etag = await EtagOfAsync(wo.Id);

        // Path A — unforceable_transition + valid If-Match → 422 (NOT 409).
        var a = await client.SendAsync(PostForce(wo.Id,
            BodyOf("PrePressCheck", "REC-OP-WEDGE", "guard rejection"),
            ifMatch: $"\"{etag}\"", idemKey: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, a.StatusCode);

        // Path B — stale If-Match → 409 (the reserved concurrency code).
        var stale = Convert.ToBase64String(new byte[] { 9, 9, 9, 9, 9, 9, 9, 9 });
        var b = await client.SendAsync(PostForce(wo.Id,
            BodyOf("Closed", "REC-OP-WEDGE", "stale + would-be-forceable target"),
            ifMatch: $"\"{stale}\"", idemKey: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.Conflict, b.StatusCode);
        var bBody = (await b.Content.ReadFromJsonAsync<ForcePhaseResponse>())!;
        Assert.Equal("wo.state_conflict", bBody.ErrorCode);
    }

    // ── Happy: SETTING → PREPRESS (the §8.1 archetype) ──────────────

    [Fact]
    public async Task Happy_setting_to_prepress_updates_phase_etag_history_and_emits_audit()
    {
        var client = await AdminClientAsync("adm-fp-happy-set2pre");
        var wo = await SeedWoAsync("WO-FP-HAPPY1", ProcessStepCode.OpSetting, "SETTING");
        var preEtag = await EtagOfAsync(wo.Id);

        var req = PostForce(wo.Id,
            BodyOf("PrePressCheck", "REC-OP-WEDGE", "operator A left mid-shift"),
            ifMatch: $"\"{preEtag}\"", idemKey: Guid.NewGuid().ToString());
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = (await resp.Content.ReadFromJsonAsync<ForcePhaseResponse>())!;
        Assert.True(body.Ok);
        Assert.Equal("PrePressCheck", body.CurrentStep);
        Assert.False(string.IsNullOrEmpty(body.ETag));
        Assert.NotEqual(preEtag, body.ETag);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var fresh = await db.WorkOrders.AsNoTracking().SingleAsync(w => w.Id == wo.Id);
        Assert.Equal("PREPRESS", fresh.MesPhase);
        Assert.Equal(ProcessStepCode.PrePressCheck, fresh.CurrentStep);

        var hist = await db.WoStatusHistories.AsNoTracking()
            .Where(h => h.WorkOrderId == wo.Id && h.Action == "SysRecovery")
            .SingleAsync();
        Assert.Equal(ProcessStepCode.OpSetting, hist.FromStep);
        Assert.Equal(ProcessStepCode.PrePressCheck, hist.ToStep);
        Assert.Equal("operator A left mid-shift", hist.Reason);

        var sysRec = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Action == "SYS_RECOVERY" && a.TargetId == wo.Id.ToString())
            .SingleAsync();
        Assert.Contains("\"from_phase\":\"SETTING\"", sysRec.Detail);
        Assert.Contains("\"to_phase\":\"PREPRESS\"", sysRec.Detail);
        Assert.Contains("REC-OP-WEDGE", sysRec.Detail);
        Assert.Contains("sys_user_id", sysRec.Detail);
    }

    // ── Happy: * → CANCELLED (target legacy Closed projects to CANCELLED) ──

    [Fact]
    public async Task Happy_running_to_cancelled_lands_wo_at_cancelled_and_keeps_run_history()
    {
        var client = await AdminClientAsync("adm-fp-happy-cancel");
        var wo = await SeedWoAsync("WO-FP-HAPPY2", ProcessStepCode.Running, "RUNNING");

        // Pre-seed a child-table row mimicking the §8.1 B3 preservation contract.
        // We don't have wo_setting_log / run_sessions yet (those land 7c+), so
        // use the existing WoStatusHistory table as a proxy. The endpoint MUST
        // NOT delete this pre-existing history row.
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
            db.WoStatusHistories.Add(new WoStatusHistory
            {
                WorkOrderId = wo.Id,
                FromStep = ProcessStepCode.OpSetting,
                ToStep = ProcessStepCode.Running,
                Action = "Advance",
                ByUser = "operator",
                Reason = "pre-force run start",
                CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            });
            await db.SaveChangesAsync();
        }

        var preEtag = await EtagOfAsync(wo.Id);
        var req = PostForce(wo.Id,
            BodyOf("Closed", "REC-HW-FAULT", "press hydraulic failed; cancel run"),
            ifMatch: $"\"{preEtag}\"", idemKey: Guid.NewGuid().ToString());
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var verifyScope = _fx.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<MesDbContext>();
        var fresh = await verifyDb.WorkOrders.AsNoTracking().SingleAsync(w => w.Id == wo.Id);
        Assert.Equal("CANCELLED", fresh.MesPhase);

        // Pre-existing child row still there (§8.1 B3).
        var preExistingHistoryRow = await verifyDb.WoStatusHistories.AsNoTracking()
            .Where(h => h.WorkOrderId == wo.Id && h.Action == "Advance" && h.Reason == "pre-force run start")
            .SingleOrDefaultAsync();
        Assert.NotNull(preExistingHistoryRow);
    }

    // ── Wire-level audit visibility (closes the "test green, runtime broken" gap
    //    surfaced by Henry's first checkpoint run — DbContext-only assertions
    //    pass even when the /audit/log endpoint route is wrong or its filter
    //    drops the row. This fixture exercises the same wire path the
    //    checkpoint script uses).

    [Fact]
    public async Task Sys_recovery_audit_row_visible_via_wire_audit_log_endpoint()
    {
        var client = await AdminClientAsync("adm-fp-wire-audit");
        var wo = await SeedWoAsync("WO-FP-WIRE", ProcessStepCode.OpSetting, "SETTING");
        var preEtag = await EtagOfAsync(wo.Id);

        var force = await client.SendAsync(PostForce(wo.Id,
            BodyOf("PrePressCheck", "REC-OP-WEDGE", "wire-visibility test"),
            ifMatch: $"\"{preEtag}\"", idemKey: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, force.StatusCode);

        // Hit the SAME endpoint + filter shape that checkpoint-7a-2.sh uses.
        // Route: /api/v2/audit/log (NOT /api/v2/admin/audit/log — that path
        // is 404 and would silently mute every wire-level assertion).
        // Filter: action=SYS_RECOVERY (the endpoint does NOT accept
        // targetType/targetId; we filter by action + grep the response body
        // for this WO's targetId).
        var auditResp = await client.GetAsync($"/api/v2/audit/log?action=SYS_RECOVERY&page=1&pageSize=50");
        Assert.Equal(HttpStatusCode.OK, auditResp.StatusCode);

        var body = await auditResp.Content.ReadAsStringAsync();
        Assert.Contains($"\"targetId\":\"{wo.Id}\"", body);
        Assert.Contains("REC-OP-WEDGE", body);
        // The detail field is a JSON STRING containing JSON — the outer
        // serialiser escapes the inner double-quotes. So the substring on
        // the wire is \"from_phase\":\"SETTING\" (literal backslash + quote
        // in the response body). Match the escaped form to avoid the
        // "test green, wire shape changed" trap.
        Assert.Contains("\\\"from_phase\\\":\\\"SETTING\\\"", body);
        Assert.Contains("\\\"to_phase\\\":\\\"PREPRESS\\\"", body);
    }

    // ── Concurrency soak — mirror 7a-1.4 N=10 pattern (force-phase ────
    //    soak is smaller because admin recovery is rare; 10 parallel
    //    admin calls is the worst credible scenario — two ops opening
    //    the same wedged-WO drawer + both forcing simultaneously).

    [Fact]
    [Trait("Category", "Soak")]
    public async Task Concurrent_force_phase_N_equals_10_yields_one_winner_and_nine_state_conflicts()
    {
        var client = await AdminClientAsync("adm-fp-soak");
        var wo = await SeedWoAsync("WO-FP-SOAK", ProcessStepCode.OpSetting, "SETTING");
        var startEtag = await EtagOfAsync(wo.Id);

        const int N = 10;
        var tasks = Enumerable.Range(0, N).Select(_ =>
            client.SendAsync(PostForce(wo.Id,
                BodyOf("PrePressCheck", "REC-OP-WEDGE", "soak"),
                ifMatch: $"\"{startEtag}\"", idemKey: Guid.NewGuid().ToString())));
        var responses = await Task.WhenAll(tasks);

        var oks = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var conflicts = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(1, oks);
        Assert.Equal(N - 1, conflicts);

        // Each of the N-1 conflicts must have emitted a WO_STATE_CONFLICT
        // audit row (matches /advance soak from 7a-1.4).
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var conflictAudits = await db.AuditLogs
            .CountAsync(a => a.Action == "WO_STATE_CONFLICT" && a.TargetId == wo.Id.ToString());
        Assert.True(conflictAudits >= N - 1,
            $"Expected ≥{N - 1} WO_STATE_CONFLICT audit rows, got {conflictAudits}");

        // Exactly one SYS_RECOVERY row (the winner).
        var sysRecovery = await db.AuditLogs
            .CountAsync(a => a.Action == "SYS_RECOVERY" && a.TargetId == wo.Id.ToString());
        Assert.Equal(1, sysRecovery);
    }

    // ── Idempotency replay (matches /advance) ───────────────────────

    [Fact]
    public async Task Replay_with_same_idempotency_key_returns_replayed_header()
    {
        var client = await AdminClientAsync("adm-fp-replay");
        var wo = await SeedWoAsync("WO-FP-REPLAY", ProcessStepCode.OpSetting, "SETTING");
        var etag = await EtagOfAsync(wo.Id);
        var key = Guid.NewGuid().ToString();
        var body = BodyOf("PrePressCheck", "REC-OP-WEDGE", "first call");

        var resp1 = await client.SendAsync(PostForce(wo.Id, body, $"\"{etag}\"", key));
        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);

        var resp2 = await client.SendAsync(PostForce(wo.Id, body, $"\"{etag}\"", key));
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);
        Assert.True(resp2.Headers.Contains("Idempotency-Replayed"),
            "second call with same Idempotency-Key must carry Idempotency-Replayed header");
    }
}
