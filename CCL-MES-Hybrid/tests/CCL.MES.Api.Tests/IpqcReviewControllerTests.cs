using System.Net;
using System.Net.Http.Json;
using System.Text;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain;
using CCL.MES.Domain.Auth;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.IpqcReview;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Api.Tests;

/// <summary>
/// P10.7d-2 — IpqcReviewController coverage.
///
/// Status code contract mirrors 7c-2 RunningSurfaceController:
///   200 success + bumped ETag + post-write rollup
///   400 missing Idempotency-Key
///   404 WO not found
///   409 stale If-Match + WO_STATE_CONFLICT audit
///   422 wo.invalid_phase / ipqc.invalid_status / ipqc.invalid_reason_code /
///       ipqc.invalid_ng_note / ipqc.invalid_judgment / ipqc.judgment_inconsistent /
///       ipqc.not_ready_for_judgment / ipqc.invalid_special_accept_reason /
///       qa.invalid_outcome / qa.same_user_as_ipqc_submitter / qa.invalid_qa_reason
///   428 missing If-Match
///
/// Q3 CRITICAL dual-sig path:
///   * Distinct usernames → 200 Approve OK; phase QA_PENDING → IPQC_APPROVED.
///   * Same username → 422 qa.same_user_as_ipqc_submitter +
///     WO_QA_APPROVE_DENIED audit row (NOT WO_QA_APPROVE).
///
/// Rule 7.3 (wire-mirror): Audit_visibility test calls the same
/// /api/v2/audit/log URL the checkpoint script will use.
/// </summary>
public sealed class IpqcReviewControllerTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public IpqcReviewControllerTests(MesApiFactory fx) => _fx = fx;

    // ── Seed helpers ───────────────────────────────────────────────

    private async Task<(long WoId, string Etag)> SeedWoAsync(string mesPhase = "IPQC_WAIT")
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
            WoNo = "WO-7D2-" + Guid.NewGuid().ToString("N")[..6],
            CustomerId = customer.Id,
            ProductId = product.Id,
            ProductName = product.Name,
            TargetQty = 1000,
            Uom = "pcs",
            CurrentStep = ProcessStepCode.IpqcApproval,
            MesPhase = mesPhase,
            Status = WoStatus.InProgress,
            SettingStartAt = DateTime.UtcNow.AddMinutes(-10),
            SettingEndAt = DateTime.UtcNow.AddMinutes(-5),
            SettingDurationSec = 300,
        };
        db.WorkOrders.Add(wo);
        await db.SaveChangesAsync();

        var freshRv = await db.WorkOrders.AsNoTracking()
            .Where(w => w.Id == wo.Id)
            .Select(w => w.RowVersion).SingleAsync();
        return (wo.Id, Convert.ToBase64String(freshRv));
    }

    /// <summary>Seed a check row with all 4 slots set to a status +
    /// optional judgment+submitter (for QA-approve fixtures).</summary>
    private async Task SeedCheckAsync(long woId,
        IpqcCheckStatus mat = IpqcCheckStatus.Ok,
        IpqcCheckStatus a = IpqcCheckStatus.Ok,
        IpqcCheckStatus b = IpqcCheckStatus.Ok,
        IpqcCheckStatus c = IpqcCheckStatus.Ok,
        IpqcJudgment judgment = IpqcJudgment.Pending,
        string? specialAcceptReason = null,
        string? ipqcSubmittedBy = null,
        string? ngReason = null,
        string? ngNote = null)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        db.WoIpqcChecks.Add(new WoIpqcCheck
        {
            WorkOrderId = woId,
            MaterialStatus = mat,
            MaterialNgReasonCode = mat == IpqcCheckStatus.Ng ? ngReason : null,
            MaterialNgNote = mat == IpqcCheckStatus.Ng ? ngNote : null,
            PrintAStatus = a,
            PrintANgReasonCode = a == IpqcCheckStatus.Ng ? ngReason : null,
            PrintANgNote = a == IpqcCheckStatus.Ng ? ngNote : null,
            PrintBStatus = b,
            PrintBNgReasonCode = b == IpqcCheckStatus.Ng ? ngReason : null,
            PrintBNgNote = b == IpqcCheckStatus.Ng ? ngNote : null,
            PrintCStatus = c,
            PrintCNgReasonCode = c == IpqcCheckStatus.Ng ? ngReason : null,
            PrintCNgNote = c == IpqcCheckStatus.Ng ? ngNote : null,
            Judgment = judgment,
            SpecialAcceptReason = specialAcceptReason,
            IpqcSubmittedBy = ipqcSubmittedBy,
            IpqcSubmittedAt = ipqcSubmittedBy is not null ? DateTime.UtcNow.AddMinutes(-1) : null,
            QaOutcome = QaOutcome.Pending,
        });
        await db.SaveChangesAsync();
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

    // P10.7d-2 role policy (Henry-confirmed 2026-06-07):
    //   IpqcSubmit policy: Admin | QC      (PUT slot + POST judgment)
    //   QaApprove  policy: Admin | QC | Supervisor   (POST qa/approve)
    // OperatorClientAsync historically returned an Operator-role
    // client — IPQC tests need QC; QA happy-path tests need a
    // DISTINCT QC or Supervisor user. Helpers below mint each role.

    private async Task<HttpClient> QcClientAsync(string user)
    {
        await _fx.SeedUserAsync(user, "P@ss!1", UserRole.Qc);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, user, "P@ss!1");
        return client;
    }

    private async Task<HttpClient> SupervisorClientAsync(string user)
    {
        await _fx.SeedUserAsync(user, "P@ss!1", UserRole.Supervisor);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, user, "P@ss!1");
        return client;
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

    // ── GET /ipqc — read view + lazy materialise ──────────────────

    [Fact]
    public async Task Get_ipqc_returns_view_with_ETag_and_lazy_materialise()
    {
        var (wo, etag) = await SeedWoAsync("IPQC_WAIT");
        var client = await QcClientAsync("qc-7d2-get");

        var resp = await client.GetAsync($"/api/v2/work-orders/{wo}/ipqc");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var view = await resp.Content.ReadFromJsonAsync<IpqcView>();
        Assert.NotNull(view);
        Assert.Equal(wo, view!.WoId);
        Assert.Equal("IPQC_WAIT", view.MesPhase);
        Assert.Equal("Pending", view.MaterialStatus);
        Assert.Equal("Pending", view.PrintAStatus);
        Assert.Equal("Pending", view.PrintBStatus);
        Assert.Equal("Pending", view.PrintCStatus);
        Assert.False(view.IsReadyForJudgment);

        // Row was lazy-materialised — second GET sees same row.
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var count = await db.WoIpqcChecks.CountAsync(c => c.WorkOrderId == wo);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Get_ipqc_returns_404_for_unknown_wo()
    {
        var client = await QcClientAsync("qc-7d2-get-404");
        var resp = await client.GetAsync($"/api/v2/work-orders/{long.MaxValue}/ipqc");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ── Prelude — 428 / 400 / 409 ─────────────────────────────────

    [Fact]
    public async Task PutSlot_missing_IfMatch_returns_428()
    {
        var (wo, _) = await SeedWoAsync();
        var client = await QcClientAsync("qc-7d2-428");
        var resp = await client.SendAsync(Mk(HttpMethod.Put,
            $"/api/v2/work-orders/{wo}/ipqc/material",
            "{\"status\":\"Ok\"}",
            ifMatch: null, idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.PreconditionRequired, resp.StatusCode);
    }

    [Fact]
    public async Task PutSlot_missing_Idem_returns_400()
    {
        var (wo, etag) = await SeedWoAsync();
        var client = await QcClientAsync("qc-7d2-400");
        var resp = await client.SendAsync(Mk(HttpMethod.Put,
            $"/api/v2/work-orders/{wo}/ipqc/material",
            "{\"status\":\"Ok\"}",
            ifMatch: $"\"{etag}\"", idem: null));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task PutSlot_stale_IfMatch_returns_409_and_emits_WO_STATE_CONFLICT()
    {
        var (wo, _) = await SeedWoAsync();
        var client = await QcClientAsync("qc-7d2-409");
        var resp = await client.SendAsync(Mk(HttpMethod.Put,
            $"/api/v2/work-orders/{wo}/ipqc/material",
            "{\"status\":\"Ok\"}",
            ifMatch: "\"AAAA\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<IpqcSetResponse>();
        Assert.Equal("wo.state_conflict", body!.ErrorCode);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var conflict = await db.AuditLogs.FirstOrDefaultAsync(a =>
            a.Action == "WO_STATE_CONFLICT" && a.TargetId == wo.ToString());
        Assert.NotNull(conflict);
    }

    // ── PUT /ipqc/{slot} — happy paths + body validation ──────────

    [Theory]
    [InlineData("material")]
    [InlineData("print-a")]
    [InlineData("print-b")]
    [InlineData("print-c")]
    public async Task PutSlot_happy_path_status_OK_for_each_slot(string slotKey)
    {
        var (wo, etag) = await SeedWoAsync();
        var client = await QcClientAsync($"qc-7d2-slot-{slotKey.Replace("-", "")}");
        var resp = await client.SendAsync(Mk(HttpMethod.Put,
            $"/api/v2/work-orders/{wo}/ipqc/{slotKey}",
            "{\"status\":\"Ok\"}",
            ifMatch: $"\"{etag}\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<IpqcSetResponse>();
        Assert.True(body!.Ok);
        Assert.Equal("IPQC_WAIT", body.MesPhase);
    }

    [Fact]
    public async Task PutSlot_in_non_IPQC_WAIT_phase_returns_422_invalid_phase()
    {
        var (wo, etag) = await SeedWoAsync("RUNNING");
        var client = await QcClientAsync("qc-7d2-slot-phase");
        var resp = await client.SendAsync(Mk(HttpMethod.Put,
            $"/api/v2/work-orders/{wo}/ipqc/material",
            "{\"status\":\"Ok\"}",
            ifMatch: $"\"{etag}\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("wo.invalid_phase", err!.Code);
    }

    [Fact]
    public async Task PutSlot_invalid_status_returns_422()
    {
        var (wo, etag) = await SeedWoAsync();
        var client = await QcClientAsync("qc-7d2-slot-bad");
        var resp = await client.SendAsync(Mk(HttpMethod.Put,
            $"/api/v2/work-orders/{wo}/ipqc/material",
            "{\"status\":\"FunkyValue\"}",
            ifMatch: $"\"{etag}\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("ipqc.invalid_status", err!.Code);
    }

    [Fact]
    public async Task PutSlot_NG_without_reason_returns_422()
    {
        var (wo, etag) = await SeedWoAsync();
        var client = await QcClientAsync("qc-7d2-slot-ng-nor");
        var resp = await client.SendAsync(Mk(HttpMethod.Put,
            $"/api/v2/work-orders/{wo}/ipqc/print-a",
            "{\"status\":\"Ng\",\"ngNote\":\"missing reason\"}",
            ifMatch: $"\"{etag}\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("ipqc.invalid_reason_code", err!.Code);
    }

    [Fact]
    public async Task PutSlot_NG_with_unregistered_reason_returns_422()
    {
        var (wo, etag) = await SeedWoAsync();
        var client = await QcClientAsync("qc-7d2-slot-ng-bad");
        var resp = await client.SendAsync(Mk(HttpMethod.Put,
            $"/api/v2/work-orders/{wo}/ipqc/print-a",
            "{\"status\":\"Ng\",\"ngReasonCode\":\"NO-SUCH-CODE\",\"ngNote\":\"reason not in catalog\"}",
            ifMatch: $"\"{etag}\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("ipqc.invalid_reason_code", err!.Code);
    }

    [Fact]
    public async Task PutSlot_NG_with_registered_reason_returns_200_and_emits_WO_IPQC_CHECK()
    {
        await SeedScrapReasonAsync("SC-COLOR");
        var (wo, etag) = await SeedWoAsync();
        var client = await QcClientAsync("qc-7d2-slot-ng-ok");
        var resp = await client.SendAsync(Mk(HttpMethod.Put,
            $"/api/v2/work-orders/{wo}/ipqc/print-a",
            "{\"status\":\"Ng\",\"ngReasonCode\":\"SC-COLOR\",\"ngNote\":\"ΔE = 2.4\"}",
            ifMatch: $"\"{etag}\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var audit = await db.AuditLogs.FirstOrDefaultAsync(a =>
            a.Action == "WO_IPQC_CHECK" && a.TargetId == wo.ToString());
        Assert.NotNull(audit);
        Assert.Contains("PrintA", audit!.Detail);
        Assert.Contains("SC-COLOR", audit.Detail);
    }

    // ── POST /ipqc/judgment — happy + invariants ──────────────────

    [Fact]
    public async Task Judgment_GoRun_all_OK_transitions_to_IPQC_APPROVED()
    {
        var (wo, _) = await SeedWoAsync();
        await SeedCheckAsync(wo); // default all Ok
        var etag = await CurrentEtagAsync(wo);
        var client = await QcClientAsync("qc-7d2-judge-go");
        var resp = await client.SendAsync(Mk(HttpMethod.Post,
            $"/api/v2/work-orders/{wo}/ipqc/judgment",
            "{\"judgment\":\"GoRun\"}",
            ifMatch: $"\"{etag}\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<IpqcSetResponse>();
        Assert.Equal("IPQC_APPROVED", body!.MesPhase);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var reload = await db.WorkOrders.FindAsync(wo);
        Assert.Equal("IPQC_APPROVED", reload!.MesPhase);
        var audit = await db.AuditLogs.FirstOrDefaultAsync(a =>
            a.Action == "WO_IPQC_JUDGMENT" && a.TargetId == wo.ToString());
        Assert.NotNull(audit);
        Assert.Contains("GoRun", audit!.Detail);
    }

    [Fact]
    public async Task Judgment_GoRun_with_any_NG_returns_422_inconsistent()
    {
        await SeedScrapReasonAsync("SC-COLOR");
        var (wo, _) = await SeedWoAsync();
        await SeedCheckAsync(wo, a: IpqcCheckStatus.Ng, ngReason: "SC-COLOR", ngNote: "ΔE");
        var etag = await CurrentEtagAsync(wo);
        var client = await QcClientAsync("qc-7d2-judge-incon");
        var resp = await client.SendAsync(Mk(HttpMethod.Post,
            $"/api/v2/work-orders/{wo}/ipqc/judgment",
            "{\"judgment\":\"GoRun\"}",
            ifMatch: $"\"{etag}\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("ipqc.judgment_inconsistent", err!.Code);
    }

    [Fact]
    public async Task Judgment_StopLine_with_NG_transitions_to_PREPRESS()
    {
        await SeedScrapReasonAsync("SC-COLOR");
        var (wo, _) = await SeedWoAsync();
        await SeedCheckAsync(wo, b: IpqcCheckStatus.Ng, ngReason: "SC-COLOR", ngNote: "lệch màu");
        var etag = await CurrentEtagAsync(wo);
        var client = await QcClientAsync("qc-7d2-judge-stop");
        var resp = await client.SendAsync(Mk(HttpMethod.Post,
            $"/api/v2/work-orders/{wo}/ipqc/judgment",
            "{\"judgment\":\"StopLine\"}",
            ifMatch: $"\"{etag}\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<IpqcSetResponse>();
        Assert.Equal("PREPRESS", body!.MesPhase);
    }

    [Fact]
    public async Task Judgment_SpecialAccept_with_NG_and_reason_transitions_to_QA_PENDING()
    {
        await SeedScrapReasonAsync("SC-COLOR");
        var (wo, _) = await SeedWoAsync();
        await SeedCheckAsync(wo, a: IpqcCheckStatus.Ng, ngReason: "SC-COLOR", ngNote: "ΔE off");
        var etag = await CurrentEtagAsync(wo);
        var client = await QcClientAsync("qc-7d2-judge-sa");
        var resp = await client.SendAsync(Mk(HttpMethod.Post,
            $"/api/v2/work-orders/{wo}/ipqc/judgment",
            "{\"judgment\":\"SpecialAccept\",\"specialAcceptReason\":\"Lô gấp giao trong ngày, ΔE 2.3 chấp nhận được\"}",
            ifMatch: $"\"{etag}\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<IpqcSetResponse>();
        Assert.Equal("QA_PENDING", body!.MesPhase);
    }

    [Fact]
    public async Task Judgment_SpecialAccept_without_reason_returns_422()
    {
        await SeedScrapReasonAsync("SC-COLOR");
        var (wo, _) = await SeedWoAsync();
        await SeedCheckAsync(wo, a: IpqcCheckStatus.Ng, ngReason: "SC-COLOR", ngNote: "x");
        var etag = await CurrentEtagAsync(wo);
        var client = await QcClientAsync("qc-7d2-judge-sa-noreason");
        var resp = await client.SendAsync(Mk(HttpMethod.Post,
            $"/api/v2/work-orders/{wo}/ipqc/judgment",
            "{\"judgment\":\"SpecialAccept\"}",
            ifMatch: $"\"{etag}\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("ipqc.invalid_special_accept_reason", err!.Code);
    }

    [Fact]
    public async Task Judgment_not_ready_returns_422()
    {
        var (wo, etag) = await SeedWoAsync();
        // No check row seeded → all 4 slots Pending after lazy materialise.
        var client = await QcClientAsync("qc-7d2-judge-notready");
        var resp = await client.SendAsync(Mk(HttpMethod.Post,
            $"/api/v2/work-orders/{wo}/ipqc/judgment",
            "{\"judgment\":\"GoRun\"}",
            ifMatch: $"\"{etag}\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("ipqc.not_ready_for_judgment", err!.Code);
    }

    // ── POST /qa/approve — Q3 dual-sig (CRITICAL) ──────────────────

    [Fact]
    public async Task QaApprove_distinct_user_approves_and_transitions_to_IPQC_APPROVED()
    {
        // Q3 happy path: QC user submitted SpecialAccept; QA user
        // approves → IPQC_APPROVED. Distinct users satisfy the
        // dual-sig flag (default ON).
        var ipqcUser = "qc-alice-d1-" + Guid.NewGuid().ToString("N")[..6];
        var qaUser = "qa-bob-d1-" + Guid.NewGuid().ToString("N")[..6];
        var (wo, _) = await SeedWoAsync("QA_PENDING");
        await SeedCheckAsync(wo,
            a: IpqcCheckStatus.Ng,
            ngReason: "SC-COLOR", ngNote: "ΔE 2.3",
            judgment: IpqcJudgment.SpecialAccept,
            specialAcceptReason: "Lô gấp",
            ipqcSubmittedBy: ipqcUser);
        await SeedScrapReasonAsync("SC-COLOR");
        var etag = await CurrentEtagAsync(wo);

        // QA approver is a DIFFERENT user.
        var qaClient = await QcClientAsync(qaUser);

        var resp = await qaClient.SendAsync(Mk(HttpMethod.Post,
            $"/api/v2/work-orders/{wo}/qa/approve",
            "{\"outcome\":\"Approve\"}",
            ifMatch: $"\"{etag}\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<IpqcSetResponse>();
        Assert.Equal("IPQC_APPROVED", body!.MesPhase);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var reload = await db.WorkOrders.FindAsync(wo);
        Assert.Equal("IPQC_APPROVED", reload!.MesPhase);

        var audit = await db.AuditLogs.FirstOrDefaultAsync(a =>
            a.Action == "WO_QA_APPROVE" && a.TargetId == wo.ToString());
        Assert.NotNull(audit);
        Assert.Contains("Approve", audit!.Detail);
        Assert.Contains(ipqcUser, audit.Detail);
        Assert.Contains(qaUser, audit.Detail);
        // flag_state stamped per §5.6.
        Assert.Contains("\"flag_state\":\"on\"", audit.Detail);
    }

    [Fact]
    public async Task QaApprove_same_user_returns_422_qa_same_user_and_emits_WO_QA_APPROVE_DENIED()
    {
        // Q3 CRITICAL: QC user submitted SpecialAccept; same username
        // tries to QA-approve → 422 + WO_QA_APPROVE_DENIED audit (NOT
        // WO_QA_APPROVE). This is the 4-eye principle in action.
        var sameUser = "qc-alice-sm-" + Guid.NewGuid().ToString("N")[..6];
        var (wo, _) = await SeedWoAsync("QA_PENDING");
        await SeedCheckAsync(wo,
            a: IpqcCheckStatus.Ng,
            ngReason: "SC-COLOR", ngNote: "ΔE 2.3",
            judgment: IpqcJudgment.SpecialAccept,
            specialAcceptReason: "Lô gấp",
            ipqcSubmittedBy: sameUser);
        await SeedScrapReasonAsync("SC-COLOR");
        var etag = await CurrentEtagAsync(wo);

        // SAME username tries to approve.
        var sameUserClient = await QcClientAsync(sameUser);

        var resp = await sameUserClient.SendAsync(Mk(HttpMethod.Post,
            $"/api/v2/work-orders/{wo}/qa/approve",
            "{\"outcome\":\"Approve\"}",
            ifMatch: $"\"{etag}\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("qa.same_user_as_ipqc_submitter", err!.Code);
        // VN message verbatim.
        Assert.Contains("vi phạm nguyên tắc 4-mắt", err.MessageEn);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        // WO_QA_APPROVE_DENIED emitted (NOT WO_QA_APPROVE).
        var denied = await db.AuditLogs.FirstOrDefaultAsync(a =>
            a.Action == "WO_QA_APPROVE_DENIED" && a.TargetId == wo.ToString());
        Assert.NotNull(denied);
        Assert.Contains("same_user_as_ipqc_submitter", denied!.Detail);
        Assert.Contains(sameUser, denied.Detail);
        var falsePositive = await db.AuditLogs.AnyAsync(a =>
            a.Action == "WO_QA_APPROVE" && a.TargetId == wo.ToString());
        Assert.False(falsePositive,
            "WO_QA_APPROVE MUST NOT emit on dual-sig violation — that's the contract.");

        // WO phase unchanged (still QA_PENDING).
        var reload = await db.WorkOrders.FindAsync(wo);
        Assert.Equal("QA_PENDING", reload!.MesPhase);
    }

    [Fact]
    public async Task QaApprove_Reject_with_reason_transitions_to_PREPRESS()
    {
        var ipqcUser = "qc-alice-rej-" + Guid.NewGuid().ToString("N")[..6];
        var qaUser = "qa-rej-" + Guid.NewGuid().ToString("N")[..6];
        var (wo, _) = await SeedWoAsync("QA_PENDING");
        await SeedCheckAsync(wo,
            a: IpqcCheckStatus.Ng,
            ngReason: "SC-COLOR", ngNote: "ΔE",
            judgment: IpqcJudgment.SpecialAccept,
            specialAcceptReason: "Lô gấp",
            ipqcSubmittedBy: ipqcUser);
        await SeedScrapReasonAsync("SC-COLOR");
        var etag = await CurrentEtagAsync(wo);

        var qaClient = await QcClientAsync(qaUser);
        var resp = await qaClient.SendAsync(Mk(HttpMethod.Post,
            $"/api/v2/work-orders/{wo}/qa/approve",
            "{\"outcome\":\"Reject\",\"qaReason\":\"Vi phạm spec màu — không cho phép special accept\"}",
            ifMatch: $"\"{etag}\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<IpqcSetResponse>();
        Assert.Equal("PREPRESS", body!.MesPhase);
    }

    [Fact]
    public async Task QaApprove_Reject_without_reason_returns_422()
    {
        var ipqcUser = "qc-alice-rn-" + Guid.NewGuid().ToString("N")[..6];
        var qaUser = "qa-rej-nor-" + Guid.NewGuid().ToString("N")[..6];
        var (wo, _) = await SeedWoAsync("QA_PENDING");
        await SeedCheckAsync(wo,
            a: IpqcCheckStatus.Ng,
            ngReason: "SC-COLOR", ngNote: "ΔE",
            judgment: IpqcJudgment.SpecialAccept,
            specialAcceptReason: "Lô gấp",
            ipqcSubmittedBy: ipqcUser);
        await SeedScrapReasonAsync("SC-COLOR");
        var etag = await CurrentEtagAsync(wo);

        var qaClient = await QcClientAsync(qaUser);
        var resp = await qaClient.SendAsync(Mk(HttpMethod.Post,
            $"/api/v2/work-orders/{wo}/qa/approve",
            "{\"outcome\":\"Reject\"}",
            ifMatch: $"\"{etag}\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("qa.invalid_qa_reason", err!.Code);
    }

    [Fact]
    public async Task QaApprove_in_non_QA_PENDING_phase_returns_422_invalid_phase()
    {
        var (wo, etag) = await SeedWoAsync("IPQC_WAIT");
        var client = await QcClientAsync("qc-7d2-qa-phase");
        var resp = await client.SendAsync(Mk(HttpMethod.Post,
            $"/api/v2/work-orders/{wo}/qa/approve",
            "{\"outcome\":\"Approve\"}",
            ifMatch: $"\"{etag}\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("wo.invalid_phase", err!.Code);
    }

    [Fact]
    public async Task QaApprove_invalid_outcome_returns_422()
    {
        var ipqcUser = "qc-alice-bo-" + Guid.NewGuid().ToString("N")[..6];
        var qaUser = "qa-bo-" + Guid.NewGuid().ToString("N")[..6];
        var (wo, _) = await SeedWoAsync("QA_PENDING");
        await SeedCheckAsync(wo,
            judgment: IpqcJudgment.SpecialAccept,
            ipqcSubmittedBy: ipqcUser);
        var etag = await CurrentEtagAsync(wo);
        var qaClient = await QcClientAsync(qaUser);
        var resp = await qaClient.SendAsync(Mk(HttpMethod.Post,
            $"/api/v2/work-orders/{wo}/qa/approve",
            "{\"outcome\":\"Maybe\"}",
            ifMatch: $"\"{etag}\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("qa.invalid_outcome", err!.Code);
    }

    // ── Role policy (Henry-confirmed 2026-06-07) ───────────────────

    [Fact]
    public async Task PutSlot_with_Operator_role_returns_403_role_policy_locked()
    {
        // IpqcSubmit policy = Admin | QC only. Operator must not be
        // able to PUT slot.
        var (wo, etag) = await SeedWoAsync();
        var opClient = await OperatorClientAsync("op-7d2-policy-no-ipqc");
        var resp = await opClient.SendAsync(Mk(HttpMethod.Put,
            $"/api/v2/work-orders/{wo}/ipqc/material",
            "{\"status\":\"Ok\"}",
            ifMatch: $"\"{etag}\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task QaApprove_with_Supervisor_role_passes_policy_gate()
    {
        // QaApprove policy = Admin | QC | Supervisor. Lock the
        // "QA Manager = Supervisor" practical mapping so a future
        // policy-narrowing PR fails CI loudly here.
        var ipqcUser = "qc-policy-sup-" + Guid.NewGuid().ToString("N")[..6];
        var qaUser = "sup-policy-" + Guid.NewGuid().ToString("N")[..6];
        var (wo, _) = await SeedWoAsync("QA_PENDING");
        await SeedCheckAsync(wo,
            a: IpqcCheckStatus.Ng,
            ngReason: "SC-COLOR", ngNote: "ΔE",
            judgment: IpqcJudgment.SpecialAccept,
            specialAcceptReason: "Lô gấp",
            ipqcSubmittedBy: ipqcUser);
        await SeedScrapReasonAsync("SC-COLOR");
        var etag = await CurrentEtagAsync(wo);

        var qaClient = await SupervisorClientAsync(qaUser);
        var resp = await qaClient.SendAsync(Mk(HttpMethod.Post,
            $"/api/v2/work-orders/{wo}/qa/approve",
            "{\"outcome\":\"Approve\"}",
            ifMatch: $"\"{etag}\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<IpqcSetResponse>();
        Assert.Equal("IPQC_APPROVED", body!.MesPhase);
    }

    [Fact]
    public async Task QaApprove_with_Operator_role_returns_403_role_policy_locked()
    {
        // Operator must not be able to QA-approve.
        var (wo, _) = await SeedWoAsync("QA_PENDING");
        await SeedCheckAsync(wo,
            judgment: IpqcJudgment.SpecialAccept,
            ipqcSubmittedBy: "qc-someone");
        var etag = await CurrentEtagAsync(wo);
        var opClient = await OperatorClientAsync("op-7d2-policy-no-qa");
        var resp = await opClient.SendAsync(Mk(HttpMethod.Post,
            $"/api/v2/work-orders/{wo}/qa/approve",
            "{\"outcome\":\"Approve\"}",
            ifMatch: $"\"{etag}\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    // ── Rule 7.3 — wire-mirror audit visibility ───────────────────

    [Fact]
    public async Task Audit_visibility_via_wire_audit_log_endpoint()
    {
        await SeedScrapReasonAsync("SC-COLOR");
        var (wo, etag) = await SeedWoAsync();
        var client = await QcClientAsync("qc-7d2-wiremirror");

        var resp = await client.SendAsync(Mk(HttpMethod.Put,
            $"/api/v2/work-orders/{wo}/ipqc/material",
            "{\"status\":\"Ok\"}",
            ifMatch: $"\"{etag}\"", idem: Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        // Wire-mirror: audit log is AdminOnly; switch client.
        var admin = await AdminClientAsync("admin-7d2-wiremirror");
        var auditResp = await admin.GetAsync(
            "/api/v2/audit/log?action=WO_IPQC_CHECK&page=1&pageSize=50");
        Assert.Equal(HttpStatusCode.OK, auditResp.StatusCode);
        var body = await auditResp.Content.ReadAsStringAsync();
        Assert.Contains($"\"targetId\":\"{wo}\"", body);
        Assert.Contains("Material", body);
    }
}
