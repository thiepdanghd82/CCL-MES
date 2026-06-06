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
using CCL.MES.Shared.RunningSurface;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Api.Tests;

/// <summary>
/// P10.7c-2 — RunningSurfaceController coverage.
///
/// Status code contract mirrors /advance + /prepress:
///   200 success + bumped ETag + post-write state
///   400 missing Idempotency-Key
///   404 WO not found
///   409 stale If-Match + WO_STATE_CONFLICT audit
///   422 invalid_phase / invalid_qty_delta / invalid_reason_code /
///       invalid_ng_note / invalid_correction_reason / no_active_session /
///       no_active_pause / no_production / linked_entry_* /
///       setting_not_started
///   428 missing If-Match
///
/// Critical condition #1 (counter race): the
/// Concurrent_run_qty_add_N_equals_10 soak asserts exactly 1 of 10
/// parallel /run/qty against the same If-Match wins; the other 9
/// get 409 wo.state_conflict + each emits WO_STATE_CONFLICT.
///
/// Rule 7.3 (wire-mirror): the
/// Audit_visibility_via_wire_audit_log_endpoint test calls the same
/// /api/v2/audit/log URL the checkpoint script will use.
/// </summary>
public sealed class RunningSurfaceControllerTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public RunningSurfaceControllerTests(MesApiFactory fx) => _fx = fx;

    // ── Seed helpers ───────────────────────────────────────────────

    private async Task<(long WoId, string Etag)> SeedWoAsync(string mesPhase)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var customer = new Customer { Code = "C-" + Guid.NewGuid().ToString("N")[..6], Name = "Cust" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var product = new Product { ProductCode = "P-" + Guid.NewGuid().ToString("N")[..6], Name = "Prod", CustomerId = customer.Id };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        var wo = new WorkOrder
        {
            WoNo = "WO-7C2-" + Guid.NewGuid().ToString("N")[..6],
            CustomerId = customer.Id,
            ProductId = product.Id,
            ProductName = product.Name,
            TargetQty = 1000,
            Uom = "pcs",
            CurrentStep = ProcessStepCode.PrePressCheck,
            MesPhase = mesPhase,
            Status = WoStatus.InProgress,
        };
        if (mesPhase is "RUNNING" or "PAUSED")
        {
            wo.SettingStartAt = DateTime.UtcNow.AddMinutes(-30);
            wo.SettingEndAt = DateTime.UtcNow.AddMinutes(-25);
            wo.SettingDurationSec = 300;
        }
        else if (mesPhase == "SETTING")
        {
            wo.SettingStartAt = DateTime.UtcNow.AddMinutes(-5);
        }
        db.WorkOrders.Add(wo);
        await db.SaveChangesAsync();

        // L11 — SQLite INSERT trigger bumps RowVersion AFTER EF reads it
        // through RETURNING. Re-read via AsNoTracking to get the fresh
        // value the controller will compare against If-Match.
        var freshRv = await db.WorkOrders.AsNoTracking()
            .Where(w => w.Id == wo.Id)
            .Select(w => w.RowVersion).SingleAsync();
        return (wo.Id, Convert.ToBase64String(freshRv));
    }

    private async Task<long> SeedRunSessionAsync(long woId, bool open = true)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var session = new WoRunSession
        {
            WoId = woId,
            StartedAt = DateTime.UtcNow.AddMinutes(-10),
            EndedAt = open ? (DateTime?)null : DateTime.UtcNow.AddMinutes(-1),
            StartedBy = "alice",
            EndedBy = open ? null : "alice",
        };
        db.WoRunSessions.Add(session);
        await db.SaveChangesAsync();
        return session.Id;
    }

    private async Task<long> SeedActivePauseAsync(long woId, long? sessionId, string reasonCode = "ML-MAT")
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var pause = new WoPauseEvent
        {
            WoId = woId,
            RunSessionId = sessionId,
            StartedAt = DateTime.UtcNow.AddMinutes(-1),
            ReasonCode = reasonCode,
            StartedBy = "alice",
        };
        db.WoPauseEvents.Add(pause);
        await db.SaveChangesAsync();
        return pause.Id;
    }

    private async Task<long> SeedQtyEntryAsync(long woId, long sessionId, int doneDelta)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var entry = new WoQtyEntry
        {
            WoId = woId,
            RunSessionId = sessionId,
            Ts = DateTime.UtcNow.AddMinutes(-2),
            QtyDoneDelta = doneDelta,
            QtyNgDelta = 0,
            EnteredBy = "alice",
        };
        db.WoQtyEntries.Add(entry);
        var wo = await db.WorkOrders.FindAsync(woId);
        wo!.QtyDoneCached += doneDelta;
        await db.SaveChangesAsync();
        return entry.Id;
    }

    private async Task SeedPauseReasonAsync(string code = "ML-MAT")
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        if (!await db.ReasonCodes.AnyAsync(r => r.Code == code))
        {
            db.ReasonCodes.Add(new ReasonCode { Code = code, LabelEn = code, LabelVi = code, Kind = ReasonCodeKind.Pause, Sort = 10 });
            await db.SaveChangesAsync();
        }
    }

    private async Task SeedScrapReasonAsync(string code = "SC-COLOR")
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        if (!await db.ReasonCodes.AnyAsync(r => r.Code == code))
        {
            db.ReasonCodes.Add(new ReasonCode { Code = code, LabelEn = code, LabelVi = code, Kind = ReasonCodeKind.Scrap, Sort = 10 });
            await db.SaveChangesAsync();
        }
    }

    private async Task<HttpClient> OperatorClientAsync(string user)
    {
        await _fx.SeedUserAsync(user, "P@ss!1", UserRole.Operator);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, user, "P@ss!1");
        return client;
    }

    private async Task<HttpClient> AdminClientAsync(string user)
    {
        await _fx.SeedUserAsync(user, "P@ss!1", UserRole.Admin);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, user, "P@ss!1");
        return client;
    }

    private static HttpRequestMessage Post(string path, string body, string? ifMatch, string? idem)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, path)
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

    // ── Prelude — common to all endpoints ──────────────────────────

    [Fact]
    public async Task SettingDone_missing_IfMatch_returns_428()
    {
        var (wo, _) = await SeedWoAsync("SETTING");
        var client = await OperatorClientAsync("op-7c2-428");
        var resp = await client.SendAsync(Post(
            $"/api/v2/work-orders/{wo}/setting/done", "{}", ifMatch: null, idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.PreconditionRequired, resp.StatusCode);
    }

    [Fact]
    public async Task SettingDone_missing_Idem_returns_400()
    {
        var (wo, etag) = await SeedWoAsync("SETTING");
        var client = await OperatorClientAsync("op-7c2-400");
        var resp = await client.SendAsync(Post(
            $"/api/v2/work-orders/{wo}/setting/done", "{}", ifMatch: $"\"{etag}\"", idem: null));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task SettingDone_stale_IfMatch_returns_409_and_emits_WO_STATE_CONFLICT()
    {
        var (wo, _) = await SeedWoAsync("SETTING");
        var client = await OperatorClientAsync("op-7c2-409");
        var resp = await client.SendAsync(Post(
            $"/api/v2/work-orders/{wo}/setting/done", "{}", ifMatch: "\"AAAA\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<RunningSurfaceSetResponse>();
        Assert.NotNull(body);
        Assert.Equal("wo.state_conflict", body!.ErrorCode);
        Assert.NotEmpty(body.ETag);

        // Confirm audit row landed
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var conflict = await db.AuditLogs.FirstOrDefaultAsync(a =>
            a.Action == "WO_STATE_CONFLICT" && a.TargetId == wo.ToString());
        Assert.NotNull(conflict);
    }

    // ── SettingDone — happy + invalid_phase ────────────────────────

    [Fact]
    public async Task SettingDone_happy_transitions_to_IPQC_WAIT_and_stamps_duration()
    {
        var (wo, etag) = await SeedWoAsync("SETTING");
        var client = await OperatorClientAsync("op-7c2-set-ok");
        var resp = await client.SendAsync(Post(
            $"/api/v2/work-orders/{wo}/setting/done", "{}", ifMatch: $"\"{etag}\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<RunningSurfaceSetResponse>();
        Assert.True(body!.Ok);
        Assert.Equal("IPQC_WAIT", body.MesPhase);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var reload = await db.WorkOrders.FindAsync(wo);
        Assert.Equal("IPQC_WAIT", reload!.MesPhase);
        Assert.NotNull(reload.SettingEndAt);
        Assert.NotNull(reload.SettingDurationSec);
    }

    [Fact]
    public async Task SettingDone_in_PREPRESS_returns_422_invalid_phase()
    {
        var (wo, etag) = await SeedWoAsync("PREPRESS");
        var client = await OperatorClientAsync("op-7c2-set-ph");
        var resp = await client.SendAsync(Post(
            $"/api/v2/work-orders/{wo}/setting/done", "{}", ifMatch: $"\"{etag}\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("wo.invalid_phase", err!.Code);
    }

    // ── RunStart ──────────────────────────────────────────────────

    [Fact]
    public async Task RunStart_in_IPQC_APPROVED_creates_session_and_transitions_to_RUNNING()
    {
        var (wo, etag) = await SeedWoAsync("IPQC_APPROVED");
        var client = await OperatorClientAsync("op-7c2-rs-ok");
        var resp = await client.SendAsync(Post(
            $"/api/v2/work-orders/{wo}/run/start", "{}", $"\"{etag}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<RunningSurfaceSetResponse>();
        Assert.Equal("RUNNING", body!.MesPhase);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var session = await db.WoRunSessions.SingleAsync(s => s.WoId == wo);
        Assert.Null(session.EndedAt);
    }

    [Fact]
    public async Task RunStart_in_SETTING_returns_422_invalid_phase()
    {
        var (wo, etag) = await SeedWoAsync("SETTING");
        var client = await OperatorClientAsync("op-7c2-rs-ph");
        var resp = await client.SendAsync(Post(
            $"/api/v2/work-orders/{wo}/run/start", "{}", $"\"{etag}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    // ── RunQty ─────────────────────────────────────────────────────

    [Fact]
    public async Task RunQty_happy_increments_cache_and_appends_entry()
    {
        var (wo, etag) = await SeedWoAsync("RUNNING");
        var session = await SeedRunSessionAsync(wo);
        var client = await OperatorClientAsync("op-7c2-q-ok");
        var resp = await client.SendAsync(Post(
            $"/api/v2/work-orders/{wo}/run/qty",
            "{\"qtyDoneDelta\":100,\"qtyNgDelta\":0}",
            $"\"{etag}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<RunningSurfaceSetResponse>();
        Assert.Equal(100, body!.QtyDoneCached);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        Assert.Equal(1, await db.WoQtyEntries.CountAsync(e => e.WoId == wo));
    }

    [Fact]
    public async Task RunQty_negative_delta_returns_422_invalid_qty_delta()
    {
        var (wo, etag) = await SeedWoAsync("RUNNING");
        await SeedRunSessionAsync(wo);
        var client = await OperatorClientAsync("op-7c2-q-neg");
        var resp = await client.SendAsync(Post(
            $"/api/v2/work-orders/{wo}/run/qty",
            "{\"qtyDoneDelta\":-10,\"qtyNgDelta\":0}",
            $"\"{etag}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("running.invalid_qty_delta", err!.Code);
    }

    [Fact]
    public async Task RunQty_NG_without_reason_returns_422()
    {
        var (wo, etag) = await SeedWoAsync("RUNNING");
        await SeedRunSessionAsync(wo);
        var client = await OperatorClientAsync("op-7c2-q-ng-noreason");
        var resp = await client.SendAsync(Post(
            $"/api/v2/work-orders/{wo}/run/qty",
            "{\"qtyDoneDelta\":0,\"qtyNgDelta\":5,\"ngNote\":\"x\"}",
            $"\"{etag}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [Fact]
    public async Task RunQty_NG_with_valid_Scrap_code_succeeds()
    {
        var (wo, etag) = await SeedWoAsync("RUNNING");
        await SeedRunSessionAsync(wo);
        await SeedScrapReasonAsync("SC-COLOR");
        var client = await OperatorClientAsync("op-7c2-q-ng-ok");
        var resp = await client.SendAsync(Post(
            $"/api/v2/work-orders/{wo}/run/qty",
            "{\"qtyDoneDelta\":0,\"qtyNgDelta\":5,\"ngReasonCode\":\"SC-COLOR\",\"ngNote\":\"biên màu lệch\"}",
            $"\"{etag}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    // ── RunQty Correct (Q5) ───────────────────────────────────────

    [Fact]
    public async Task RunQtyCorrect_appends_negative_delta_with_LinkedEntryId()
    {
        var (wo, _) = await SeedWoAsync("RUNNING");
        var session = await SeedRunSessionAsync(wo);
        var priorEntry = await SeedQtyEntryAsync(wo, session, 500);

        var etag = await CurrentEtagAsync(wo);
        var client = await OperatorClientAsync("op-7c2-corr-ok");
        var bodyJson = JsonSerializer.Serialize(new
        {
            linkedEntryId = priorEntry,
            qtyDoneDelta = -50,
            qtyNgDelta = 0,
            correctionReason = "miscounted +500 → 450 actual",
        });
        var resp = await client.SendAsync(Post(
            $"/api/v2/work-orders/{wo}/run/qty/correct",
            bodyJson, $"\"{etag}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<RunningSurfaceSetResponse>();
        Assert.Equal(450, body!.QtyDoneCached);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var correction = await db.WoQtyEntries.SingleAsync(e => e.LinkedEntryId == priorEntry);
        Assert.Equal(-50, correction.QtyDoneDelta);
        Assert.Contains("miscounted", correction.CorrectionReason);
    }

    [Fact]
    public async Task RunQtyCorrect_empty_reason_returns_422()
    {
        var (wo, _) = await SeedWoAsync("RUNNING");
        var session = await SeedRunSessionAsync(wo);
        var priorEntry = await SeedQtyEntryAsync(wo, session, 500);
        var etag = await CurrentEtagAsync(wo);
        var client = await OperatorClientAsync("op-7c2-corr-empty");
        var bodyJson = "{\"linkedEntryId\":" + priorEntry + ",\"qtyDoneDelta\":-50,\"qtyNgDelta\":0,\"correctionReason\":\"\"}";
        var resp = await client.SendAsync(Post(
            $"/api/v2/work-orders/{wo}/run/qty/correct",
            bodyJson, $"\"{etag}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("running.invalid_correction_reason", err!.Code);
    }

    [Fact]
    public async Task RunQtyCorrect_linked_entry_from_different_WO_returns_422()
    {
        var (wo1, _) = await SeedWoAsync("RUNNING");
        var (wo2, _) = await SeedWoAsync("RUNNING");
        var s2 = await SeedRunSessionAsync(wo2);
        var foreignEntry = await SeedQtyEntryAsync(wo2, s2, 200);
        var etag = await CurrentEtagAsync(wo1);
        var client = await OperatorClientAsync("op-7c2-corr-foreign");
        var bodyJson = "{\"linkedEntryId\":" + foreignEntry + ",\"qtyDoneDelta\":-50,\"qtyNgDelta\":0,\"correctionReason\":\"x\"}";
        var resp = await client.SendAsync(Post(
            $"/api/v2/work-orders/{wo1}/run/qty/correct",
            bodyJson, $"\"{etag}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("running.linked_entry_wrong_wo", err!.Code);
    }

    [Fact]
    public async Task RunQtyCorrect_allowed_in_PAUSED()
    {
        // Q5 amendment: corrections accepted in RUNNING OR PAUSED.
        var (wo, _) = await SeedWoAsync("PAUSED");
        var session = await SeedRunSessionAsync(wo, open: false);
        var pause = await SeedActivePauseAsync(wo, session);
        var priorEntry = await SeedQtyEntryAsync(wo, session, 300);
        var etag = await CurrentEtagAsync(wo);
        var client = await OperatorClientAsync("op-7c2-corr-paused");
        var bodyJson = "{\"linkedEntryId\":" + priorEntry + ",\"qtyDoneDelta\":-30,\"qtyNgDelta\":0,\"correctionReason\":\"paused-paperwork\"}";
        var resp = await client.SendAsync(Post(
            $"/api/v2/work-orders/{wo}/run/qty/correct",
            bodyJson, $"\"{etag}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    // ── Pause ──────────────────────────────────────────────────────

    [Fact]
    public async Task Pause_happy_transitions_to_PAUSED_and_creates_pause_event()
    {
        var (wo, etag) = await SeedWoAsync("RUNNING");
        await SeedRunSessionAsync(wo);
        await SeedPauseReasonAsync("ML-MAT");
        var client = await OperatorClientAsync("op-7c2-p-ok");
        var resp = await client.SendAsync(Post(
            $"/api/v2/work-orders/{wo}/run/pause",
            "{\"reasonCode\":\"ML-MAT\",\"note\":\"nguyên liệu chậm\"}",
            $"\"{etag}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var reload = await db.WorkOrders.FindAsync(wo);
        Assert.Equal("PAUSED", reload!.MesPhase);
        var pause = await db.WoPauseEvents.SingleAsync(p => p.WoId == wo);
        Assert.Null(pause.EndedAt);
        Assert.Equal("ML-MAT", pause.ReasonCode);
    }

    [Fact]
    public async Task Pause_unregistered_reason_returns_422()
    {
        var (wo, etag) = await SeedWoAsync("RUNNING");
        await SeedRunSessionAsync(wo);
        var client = await OperatorClientAsync("op-7c2-p-bad");
        var resp = await client.SendAsync(Post(
            $"/api/v2/work-orders/{wo}/run/pause",
            "{\"reasonCode\":\"NOT-A-PAUSE-CODE\",\"note\":\"x\"}",
            $"\"{etag}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("running.invalid_reason_code", err!.Code);
    }

    // ── Resume ─────────────────────────────────────────────────────

    [Fact]
    public async Task Resume_happy_transitions_to_RUNNING_and_opens_new_session()
    {
        var (wo, etag) = await SeedWoAsync("PAUSED");
        var oldSession = await SeedRunSessionAsync(wo, open: false);
        await SeedActivePauseAsync(wo, oldSession);
        var client = await OperatorClientAsync("op-7c2-resume-ok");
        var resp = await client.SendAsync(Post(
            $"/api/v2/work-orders/{wo}/run/resume", "{}",
            $"\"{etag}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var reload = await db.WorkOrders.FindAsync(wo);
        Assert.Equal("RUNNING", reload!.MesPhase);
        var sessions = await db.WoRunSessions.Where(s => s.WoId == wo).OrderBy(s => s.Id).ToListAsync();
        Assert.Equal(2, sessions.Count);
        Assert.Null(sessions[1].EndedAt); // newest session (just opened by Resume) is live
    }

    // ── Finish ─────────────────────────────────────────────────────

    [Fact]
    public async Task Finish_from_RUNNING_transitions_to_FQC_PENDING()
    {
        var (wo, _) = await SeedWoAsync("RUNNING");
        var session = await SeedRunSessionAsync(wo);
        await SeedQtyEntryAsync(wo, session, 100); // production > 0
        var etag = await CurrentEtagAsync(wo);
        var client = await OperatorClientAsync("op-7c2-fin-run");
        var resp = await client.SendAsync(Post(
            $"/api/v2/work-orders/{wo}/run/finish", "{}",
            $"\"{etag}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var reload = await db.WorkOrders.FindAsync(wo);
        Assert.Equal("FQC_PENDING", reload!.MesPhase);
        var sessionReload = await db.WoRunSessions.SingleAsync(s => s.Id == session);
        Assert.NotNull(sessionReload.EndedAt);
    }

    [Fact]
    public async Task Finish_from_PAUSED_closes_active_pause_Q6()
    {
        var (wo, _) = await SeedWoAsync("PAUSED");
        var session = await SeedRunSessionAsync(wo, open: false);
        await SeedQtyEntryAsync(wo, session, 100);
        var pause = await SeedActivePauseAsync(wo, session);
        var etag = await CurrentEtagAsync(wo);
        var client = await OperatorClientAsync("op-7c2-fin-paused");
        var resp = await client.SendAsync(Post(
            $"/api/v2/work-orders/{wo}/run/finish", "{}",
            $"\"{etag}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var reload = await db.WorkOrders.FindAsync(wo);
        Assert.Equal("FQC_PENDING", reload!.MesPhase);
        var pauseReload = await db.WoPauseEvents.SingleAsync(p => p.Id == pause);
        Assert.NotNull(pauseReload.EndedAt); // Q6 — controller stamped on finish
    }

    [Fact]
    public async Task Finish_with_zero_QtyDoneCached_returns_422_no_production()
    {
        var (wo, etag) = await SeedWoAsync("RUNNING");
        await SeedRunSessionAsync(wo);
        var client = await OperatorClientAsync("op-7c2-fin-noprod");
        var resp = await client.SendAsync(Post(
            $"/api/v2/work-orders/{wo}/run/finish", "{}",
            $"\"{etag}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("running.no_production", err!.Code);
    }

    // ── Concurrency soak — Critical condition #1 ──────────────────

    [Fact]
    [Trait("Category", "Soak")]
    public async Task Concurrent_run_qty_add_N_equals_10_exactly_one_winner()
    {
        var (wo, baseEtag) = await SeedWoAsync("RUNNING");
        await SeedRunSessionAsync(wo);
        var client = await OperatorClientAsync("op-7c2-soak");

        // Spawn 10 parallel /run/qty against the SAME If-Match.
        var tasks = Enumerable.Range(0, 10).Select(async i =>
        {
            var resp = await client.SendAsync(Post(
                $"/api/v2/work-orders/{wo}/run/qty",
                $"{{\"qtyDoneDelta\":100,\"qtyNgDelta\":0}}",
                $"\"{baseEtag}\"", Guid.NewGuid().ToString()));
            var body = await resp.Content.ReadFromJsonAsync<RunningSurfaceSetResponse>();
            return (resp.StatusCode, body);
        }).ToArray();

        var results = await Task.WhenAll(tasks);
        var winners = results.Count(r => r.StatusCode == HttpStatusCode.OK);
        var losers = results.Count(r => r.StatusCode == HttpStatusCode.Conflict &&
                                       r.body?.ErrorCode == "wo.state_conflict");
        Assert.Equal(1, winners);
        Assert.Equal(9, losers);

        // Net cached counter must reflect EXACTLY 1 winner.
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var reload = await db.WorkOrders.FindAsync(wo);
        Assert.Equal(100, reload!.QtyDoneCached);

        // 9 WO_STATE_CONFLICT audit rows for this WO.
        var conflictCount = await db.AuditLogs.CountAsync(a =>
            a.Action == "WO_STATE_CONFLICT" && a.TargetId == wo.ToString());
        Assert.Equal(9, conflictCount);
    }

    // ── Rule 7.3 wire-mirror ──────────────────────────────────────

    [Fact]
    public async Task Audit_visibility_via_wire_audit_log_endpoint()
    {
        var (wo, etag) = await SeedWoAsync("RUNNING");
        await SeedRunSessionAsync(wo);
        var client = await OperatorClientAsync("op-7c2-wiremirror");

        // 1 qty add
        var addResp = await client.SendAsync(Post(
            $"/api/v2/work-orders/{wo}/run/qty",
            "{\"qtyDoneDelta\":100,\"qtyNgDelta\":0}",
            $"\"{etag}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, addResp.StatusCode);

        // Wire-mirror — same URL the checkpoint script will use.
        // Audit log is AdminOnly; switch to admin client for the read.
        var admin = await AdminClientAsync("admin-7c2-wiremirror");
        var auditResp = await admin.GetAsync(
            "/api/v2/audit/log?action=WO_RUN_QTY_ADD&page=1&pageSize=50");
        Assert.Equal(HttpStatusCode.OK, auditResp.StatusCode);
        var body = await auditResp.Content.ReadAsStringAsync();
        Assert.Contains($"\"targetId\":\"{wo}\"", body);
        Assert.Contains("qty_done_delta", body);
    }
}
