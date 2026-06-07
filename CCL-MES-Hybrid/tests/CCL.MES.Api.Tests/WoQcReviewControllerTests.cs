using System.Net;
using System.Net.Http.Json;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain;
using CCL.MES.Domain.Auth;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.WoQcReview;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Api.Tests;

/// <summary>
/// P10.7e-2 — WoQcReviewController coverage. Focuses on the Q5
/// 3-sig invariants (Inspector ≠ Reviewer ≠ Approver) since they
/// are the showcase compliance feature for OQC; FQC single-sig
/// path + the L19 DTO MesPhase projection + R7.3 audit wire-mirror
/// share the same coverage rules as 7d.
///
/// Status code contract mirrors 7d IpqcReviewController:
///   200 success + bumped ETag + post-write rollup
///   400 missing Idempotency-Key
///   404 WO not found OR invalid {kind}
///   409 stale If-Match + WO_STATE_CONFLICT audit
///   422 wo.invalid_phase / qc.invalid_kind / qc.invalid_status /
///       qc.invalid_reason_code / qc.invalid_ng_note / qc.invalid_judgment
///       / qc.not_ready_for_judgment / qc.invalid_reason /
///       oqc.same_user_as_inspector / oqc.same_user_as_reviewer /
///       oqc.signature_out_of_order
///   428 missing If-Match
///
/// Q5 CRITICAL 3-sig paths:
///   ❶ Reviewer ≠ Inspector ⇒ 422 oqc.same_user_as_inspector +
///                            WO_OQC_REVIEW_DENIED audit
///   ❷ Approver ≠ Reviewer  ⇒ 422 oqc.same_user_as_reviewer  +
///                            WO_OQC_APPROVE_DENIED audit
///   ❸ Approver ≠ Inspector ⇒ 422 oqc.same_user_as_inspector  +
///                            WO_OQC_APPROVE_DENIED audit
///   ❹ Happy 3-distinct ⇒ 200 + WO advances OQC_PENDING → SHIPPED +
///                       WO_OQC_APPROVE + WO_SHIPPED audits.
///
/// Rule 7.3 (wire-mirror): Audit_visibility test calls the same
/// /api/v2/audit/log URL the checkpoint-7e-2.sh script will use.
/// </summary>
public sealed class WoQcReviewControllerTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public WoQcReviewControllerTests(MesApiFactory fx) => _fx = fx;

    // ── Seed helpers ───────────────────────────────────────────────

    private async Task<(long WoId, string Etag)> SeedWoAsync(string mesPhase = "OQC_PENDING")
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
            WoNo = "WO-7E2-" + Guid.NewGuid().ToString("N")[..6],
            CustomerId = customer.Id,
            ProductId = product.Id,
            ProductName = product.Name,
            TargetQty = 1000,
            Uom = "pcs",
            CurrentStep = ProcessStepCode.Oqc,
            MesPhase = mesPhase,
            Status = WoStatus.InProgress,
        };
        db.WorkOrders.Add(wo);
        await db.SaveChangesAsync();
        var freshRv = await db.WorkOrders.AsNoTracking()
            .Where(w => w.Id == wo.Id).Select(w => w.RowVersion).SingleAsync();
        return (wo.Id, Convert.ToBase64String(freshRv));
    }

    // P10.7e-3 FIX — minimal 1-item snapshot used by the legacy fixtures.
    // Profile-aware readiness now gates on snapshot item count; a
    // non-empty {sections:[{items:[{key:…}]}]} payload keeps legacy
    // 1-item ready-state semantics without triggering the controller's
    // empty-snapshot heal path (which would resolve to the canonical
    // 12-item FQC / 28-item OQC profile and the fixture's single seeded
    // item would no longer satisfy readiness).
    private const string MinimalOqcProfileJson =
        "{\"sections\":[{\"id\":\"x\",\"title\":\"x\",\"items\":[{\"key\":\"appearance\"}]}]}";
    private const string MinimalFqcProfileJson =
        "{\"sections\":[{\"id\":\"x\",\"title\":\"x\",\"items\":[{\"key\":\"fqc_appearance\"}]}]}";

    /// <summary>Seed an OQC check row that's ready for judgment
    /// (1 item, status=Ok). Caller supplies InspectedBy/ReviewedBy
    /// to position the test at the appropriate signature step.</summary>
    private async Task SeedOqcReadyCheckAsync(long woId,
        string? inspectedBy = null,
        string? reviewedBy = null)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var check = new WoQcCheck
        {
            WorkOrderId = woId,
            QcKind = "OQC",
            ProfileSnapshotJson = MinimalOqcProfileJson,
            Judgment = WoQcJudgment.Pending,
            InspectedBy = inspectedBy,
            InspectedAt = inspectedBy is null ? null : DateTime.UtcNow.AddMinutes(-1),
            ReviewedBy = reviewedBy,
            ReviewedAt = reviewedBy is null ? null : DateTime.UtcNow.AddMinutes(-0.5),
        };
        check.Items.Add(new WoQcCheckItem
        {
            ItemKey = "appearance",
            Status = IpqcCheckStatus.Ok,
        });
        db.WoQcChecks.Add(check);
        await db.SaveChangesAsync();
    }

    /// <summary>Seed an FQC check row that's ready for judgment.</summary>
    private async Task SeedFqcReadyCheckAsync(long woId, IpqcCheckStatus status = IpqcCheckStatus.Ok)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var check = new WoQcCheck
        {
            WorkOrderId = woId,
            QcKind = "FQC",
            ProfileSnapshotJson = MinimalFqcProfileJson,
            Judgment = WoQcJudgment.Pending,
        };
        check.Items.Add(new WoQcCheckItem
        {
            ItemKey = "fqc_appearance",
            Status = status,
        });
        db.WoQcChecks.Add(check);
        await db.SaveChangesAsync();
    }

    private async Task<HttpClient> QcClientAsync(string user)
    {
        await _fx.SeedUserAsync(user, "P@ss!1", UserRole.Qc);
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

    private static HttpRequestMessage Post(string path, object? body = null) =>
        new(HttpMethod.Post, path) { Content = JsonContent.Create(body ?? new { }) };

    private static void AddOptimisticHeaders(HttpRequestMessage req, string etag)
    {
        req.Headers.TryAddWithoutValidation("If-Match", $"\"{etag}\"");
        req.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());
    }

    // ═══════════════════════════════════════════════════════════════
    // GET — view + L19 DTO MesPhase projection
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Get_returns_view_with_MesPhase_projected_per_L19_amendment()
    {
        var client = await QcClientAsync("qc-7e2-get-" + Guid.NewGuid().ToString("N")[..6]);
        var (woId, _) = await SeedWoAsync(mesPhase: "FQC_PENDING");

        var resp = await client.GetAsync($"/api/v2/work-orders/{woId}/qc/fqc");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var view = await resp.Content.ReadFromJsonAsync<WoQcView>();
        Assert.NotNull(view);
        Assert.Equal(woId, view!.WoId);
        Assert.Equal("FQC", view.QcKind);
        Assert.False(string.IsNullOrEmpty(view.MesPhase),
            "L19 amendment: every WO-returning DTO MUST project canonical MesPhase.");
        Assert.Equal("FQC_PENDING", view.MesPhase);
    }

    [Fact]
    public async Task Get_returns_422_qc_invalid_kind_for_unknown_kind()
    {
        var client = await QcClientAsync("qc-7e2-bad-kind-" + Guid.NewGuid().ToString("N")[..6]);
        var (woId, _) = await SeedWoAsync(mesPhase: "FQC_PENDING");

        var resp = await client.GetAsync($"/api/v2/work-orders/{woId}/qc/bogus");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("qc.invalid_kind", err!.Code);
    }

    // ═══════════════════════════════════════════════════════════════
    // FQC judgment — single-sig Inspector
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Fqc_judgment_Pass_advances_to_OQC_PENDING()
    {
        var client = await QcClientAsync("qc-7e2-fqc-pass-" + Guid.NewGuid().ToString("N")[..6]);
        var (woId, etag) = await SeedWoAsync(mesPhase: "FQC_PENDING");
        await SeedFqcReadyCheckAsync(woId, IpqcCheckStatus.Ok);

        var req = Post($"/api/v2/work-orders/{woId}/qc/fqc/judgment",
            new SubmitFqcJudgmentRequest { Judgment = "Pass" });
        AddOptimisticHeaders(req, etag);
        var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<WoQcSetResponse>();
        Assert.True(body!.Ok);
        Assert.Equal("OQC_PENDING", body.MesPhase);
    }

    [Fact]
    public async Task Fqc_judgment_Reject_advances_to_PREPRESS_with_reason()
    {
        var client = await QcClientAsync("qc-7e2-fqc-rej-" + Guid.NewGuid().ToString("N")[..6]);
        var (woId, etag) = await SeedWoAsync(mesPhase: "FQC_PENDING");
        await SeedFqcReadyCheckAsync(woId, IpqcCheckStatus.Ng);

        var req = Post($"/api/v2/work-orders/{woId}/qc/fqc/judgment",
            new SubmitFqcJudgmentRequest
            {
                Judgment = "Reject",
                JudgmentReason = "Lô không đạt — đề nghị rework PREPRESS",
            });
        AddOptimisticHeaders(req, etag);
        var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<WoQcSetResponse>();
        Assert.True(body!.Ok);
        Assert.Equal("PREPRESS", body.MesPhase);
    }

    [Fact]
    public async Task Fqc_judgment_Reject_without_reason_returns_422_qc_invalid_reason()
    {
        var client = await QcClientAsync("qc-7e2-fqc-rej-nr-" + Guid.NewGuid().ToString("N")[..6]);
        var (woId, etag) = await SeedWoAsync(mesPhase: "FQC_PENDING");
        await SeedFqcReadyCheckAsync(woId, IpqcCheckStatus.Ng);

        var req = Post($"/api/v2/work-orders/{woId}/qc/fqc/judgment",
            new SubmitFqcJudgmentRequest { Judgment = "Reject" });
        AddOptimisticHeaders(req, etag);
        var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("qc.invalid_reason", err!.Code);
    }

    // ═══════════════════════════════════════════════════════════════
    // Q5 ❶ — Reviewer = Inspector → 422 oqc.same_user_as_inspector
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Oqc_review_with_same_user_as_inspector_returns_422_and_DENIED_audit()
    {
        var same = "qc-7e2-same-" + Guid.NewGuid().ToString("N")[..6];
        var client = await QcClientAsync(same);
        var (woId, etag) = await SeedWoAsync(mesPhase: "OQC_PENDING");
        await SeedOqcReadyCheckAsync(woId, inspectedBy: same);

        var req = Post($"/api/v2/work-orders/{woId}/qc/oqc/review", new OqcReviewRequest());
        AddOptimisticHeaders(req, etag);
        var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<WoQcSetResponse>();
        Assert.False(body!.Ok);
        Assert.Equal("oqc.same_user_as_inspector", body.ErrorCode);

        // Audit DENIED row emitted INSTEAD of WO_OQC_REVIEW.
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var denyCount = await db.AuditLogs
            .CountAsync(a => a.Action == "WO_OQC_REVIEW_DENIED" && a.TargetId == woId.ToString());
        Assert.Equal(1, denyCount);
        var reviewCount = await db.AuditLogs
            .CountAsync(a => a.Action == "WO_OQC_REVIEW" && a.TargetId == woId.ToString());
        Assert.Equal(0, reviewCount);
    }

    // ═══════════════════════════════════════════════════════════════
    // Q5 ❷ — Approver = Reviewer → 422 oqc.same_user_as_reviewer
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Oqc_approve_with_same_user_as_reviewer_returns_422_and_DENIED_audit()
    {
        var inspector = "qc-7e2-ins-" + Guid.NewGuid().ToString("N")[..6];
        var reviewer = "qc-7e2-rev-" + Guid.NewGuid().ToString("N")[..6];
        await _fx.SeedUserAsync(inspector, "P@ss!1", UserRole.Qc);
        var reviewerClient = await QcClientAsync(reviewer);
        var (woId, etag) = await SeedWoAsync(mesPhase: "OQC_PENDING");
        await SeedOqcReadyCheckAsync(woId, inspectedBy: inspector, reviewedBy: reviewer);

        // Reviewer attempts to also Approve.
        var req = Post($"/api/v2/work-orders/{woId}/qc/oqc/approve",
            new OqcApproveRequest { Outcome = "Approve" });
        AddOptimisticHeaders(req, etag);
        var resp = await reviewerClient.SendAsync(req);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<WoQcSetResponse>();
        Assert.False(body!.Ok);
        Assert.Equal("oqc.same_user_as_reviewer", body.ErrorCode);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var denyCount = await db.AuditLogs
            .CountAsync(a => a.Action == "WO_OQC_APPROVE_DENIED" && a.TargetId == woId.ToString());
        Assert.Equal(1, denyCount);
    }

    // ═══════════════════════════════════════════════════════════════
    // Q5 ❸ — Approver = Inspector → 422 oqc.same_user_as_inspector
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Oqc_approve_with_same_user_as_inspector_returns_422_and_DENIED_audit()
    {
        var inspector = "qc-7e2-ins2-" + Guid.NewGuid().ToString("N")[..6];
        var reviewer = "qc-7e2-rev2-" + Guid.NewGuid().ToString("N")[..6];
        var inspectorClient = await QcClientAsync(inspector);
        await _fx.SeedUserAsync(reviewer, "P@ss!1", UserRole.Qc);
        var (woId, etag) = await SeedWoAsync(mesPhase: "OQC_PENDING");
        await SeedOqcReadyCheckAsync(woId, inspectedBy: inspector, reviewedBy: reviewer);

        // Inspector attempts to Approve (same as Inspector, but ≠ Reviewer).
        var req = Post($"/api/v2/work-orders/{woId}/qc/oqc/approve",
            new OqcApproveRequest { Outcome = "Approve" });
        AddOptimisticHeaders(req, etag);
        var resp = await inspectorClient.SendAsync(req);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<WoQcSetResponse>();
        Assert.False(body!.Ok);
        Assert.Equal("oqc.same_user_as_inspector", body.ErrorCode);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var denyCount = await db.AuditLogs
            .CountAsync(a => a.Action == "WO_OQC_APPROVE_DENIED" && a.TargetId == woId.ToString());
        Assert.Equal(1, denyCount);
    }

    // ═══════════════════════════════════════════════════════════════
    // Q5 ❹ — Happy 3-distinct → 200 + SHIPPED + WO_OQC_APPROVE +
    //         WO_SHIPPED audits in same SaveChanges
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Oqc_approve_with_3_distinct_users_advances_to_SHIPPED_with_both_audits()
    {
        var inspector = "qc-7e2-ins3-" + Guid.NewGuid().ToString("N")[..6];
        var reviewer = "qc-7e2-rev3-" + Guid.NewGuid().ToString("N")[..6];
        var approver = "qc-7e2-app3-" + Guid.NewGuid().ToString("N")[..6];
        await _fx.SeedUserAsync(inspector, "P@ss!1", UserRole.Qc);
        await _fx.SeedUserAsync(reviewer, "P@ss!1", UserRole.Qc);
        var approverClient = await QcClientAsync(approver);
        var (woId, etag) = await SeedWoAsync(mesPhase: "OQC_PENDING");
        await SeedOqcReadyCheckAsync(woId, inspectedBy: inspector, reviewedBy: reviewer);

        var req = Post($"/api/v2/work-orders/{woId}/qc/oqc/approve",
            new OqcApproveRequest { Outcome = "Approve" });
        AddOptimisticHeaders(req, etag);
        var resp = await approverClient.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<WoQcSetResponse>();
        Assert.True(body!.Ok);
        Assert.Equal("SHIPPED", body.MesPhase);

        // Both audits stamped — same SaveChanges so forensic replay correlates.
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var approveCount = await db.AuditLogs
            .CountAsync(a => a.Action == "WO_OQC_APPROVE" && a.TargetId == woId.ToString());
        var shippedCount = await db.AuditLogs
            .CountAsync(a => a.Action == "WO_SHIPPED" && a.TargetId == woId.ToString());
        Assert.Equal(1, approveCount);
        Assert.Equal(1, shippedCount);

        // ApprovedBy stamped on the check row.
        var check = await db.WoQcChecks.AsNoTracking()
            .FirstAsync(c => c.WorkOrderId == woId && c.QcKind == "OQC");
        Assert.Equal(approver, check.ApprovedBy);
    }

    // ═══════════════════════════════════════════════════════════════
    // OQC Reject → FQC_PENDING re-loop (Q2 transient)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Oqc_approve_with_outcome_Reject_advances_to_FQC_PENDING_re_loop()
    {
        var inspector = "qc-7e2-rej-ins-" + Guid.NewGuid().ToString("N")[..6];
        var reviewer = "qc-7e2-rej-rev-" + Guid.NewGuid().ToString("N")[..6];
        var approver = "qc-7e2-rej-app-" + Guid.NewGuid().ToString("N")[..6];
        await _fx.SeedUserAsync(inspector, "P@ss!1", UserRole.Qc);
        await _fx.SeedUserAsync(reviewer, "P@ss!1", UserRole.Qc);
        var approverClient = await QcClientAsync(approver);
        var (woId, etag) = await SeedWoAsync(mesPhase: "OQC_PENDING");
        await SeedOqcReadyCheckAsync(woId, inspectedBy: inspector, reviewedBy: reviewer);

        var req = Post($"/api/v2/work-orders/{woId}/qc/oqc/approve",
            new OqcApproveRequest
            {
                Outcome = "Reject",
                JudgmentReason = "Phát hiện sai số sau review — escalate FQC",
            });
        AddOptimisticHeaders(req, etag);
        var resp = await approverClient.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<WoQcSetResponse>();
        Assert.True(body!.Ok);
        Assert.Equal("FQC_PENDING", body.MesPhase);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var rejectCount = await db.AuditLogs.CountAsync(a =>
            a.Action == "WO_OQC_REJECT_TO_FQC_PENDING" && a.TargetId == woId.ToString());
        Assert.Equal(1, rejectCount);
    }

    // ═══════════════════════════════════════════════════════════════
    // Signature out-of-order guards
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Oqc_review_without_Inspector_returns_422_signature_out_of_order()
    {
        var client = await QcClientAsync("qc-7e2-ooo-r-" + Guid.NewGuid().ToString("N")[..6]);
        var (woId, etag) = await SeedWoAsync(mesPhase: "OQC_PENDING");
        await SeedOqcReadyCheckAsync(woId, inspectedBy: null);  // no Inspector

        var req = Post($"/api/v2/work-orders/{woId}/qc/oqc/review", new OqcReviewRequest());
        AddOptimisticHeaders(req, etag);
        var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("oqc.signature_out_of_order", err!.Code);
    }

    [Fact]
    public async Task Oqc_approve_without_Reviewer_returns_422_signature_out_of_order()
    {
        var inspector = "qc-7e2-ooo-i-" + Guid.NewGuid().ToString("N")[..6];
        var approver = "qc-7e2-ooo-a-" + Guid.NewGuid().ToString("N")[..6];
        await _fx.SeedUserAsync(inspector, "P@ss!1", UserRole.Qc);
        var client = await QcClientAsync(approver);
        var (woId, etag) = await SeedWoAsync(mesPhase: "OQC_PENDING");
        await SeedOqcReadyCheckAsync(woId, inspectedBy: inspector);  // no Reviewer

        var req = Post($"/api/v2/work-orders/{woId}/qc/oqc/approve",
            new OqcApproveRequest { Outcome = "Approve" });
        AddOptimisticHeaders(req, etag);
        var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("oqc.signature_out_of_order", err!.Code);
    }

    // ═══════════════════════════════════════════════════════════════
    // R7.3 — audit wire-mirror
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Audit_visibility_via_wire_audit_log_endpoint_for_WO_OQC_APPROVE()
    {
        // Drive a full Q5 happy path so a WO_OQC_APPROVE row lands;
        // then verify it surfaces on /api/v2/audit/log — the exact URL
        // the checkpoint-7e-2.sh script will probe.
        var admin = await AdminClientAsync("admin-7e2-audit-" + Guid.NewGuid().ToString("N")[..6]);

        var inspector = "qc-7e2-wm-i-" + Guid.NewGuid().ToString("N")[..6];
        var reviewer = "qc-7e2-wm-r-" + Guid.NewGuid().ToString("N")[..6];
        var approver = "qc-7e2-wm-a-" + Guid.NewGuid().ToString("N")[..6];
        await _fx.SeedUserAsync(inspector, "P@ss!1", UserRole.Qc);
        await _fx.SeedUserAsync(reviewer, "P@ss!1", UserRole.Qc);
        var approverClient = await QcClientAsync(approver);
        var (woId, etag) = await SeedWoAsync(mesPhase: "OQC_PENDING");
        await SeedOqcReadyCheckAsync(woId, inspectedBy: inspector, reviewedBy: reviewer);

        var req = Post($"/api/v2/work-orders/{woId}/qc/oqc/approve",
            new OqcApproveRequest { Outcome = "Approve" });
        AddOptimisticHeaders(req, etag);
        var resp = await approverClient.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        // Wire-mirror probe — same URL the checkpoint uses.
        var auditResp = await admin.GetAsync("/api/v2/audit/log?action=WO_OQC_APPROVE&page=1&pageSize=50");
        Assert.Equal(HttpStatusCode.OK, auditResp.StatusCode);
        var auditBody = await auditResp.Content.ReadAsStringAsync();
        Assert.Contains($"\"targetId\":\"{woId}\"", auditBody);
    }

    // ═══════════════════════════════════════════════════════════════
    // P10.7e-3 FIX (Henry RCA on PR #123) — profile materialisation
    // ═══════════════════════════════════════════════════════════════
    //
    // Closes L23: real materialisation path MUST return items keyed by
    // the seeded profile. The previous coverage only exercised seeded
    // check rows with hand-rolled snapshots; the operator-visible "GET
    // view on fresh WO" path was never proven to render items.

    [Fact]
    public async Task Get_fresh_fqc_view_materialises_12_items_from_seeded_profile()
    {
        var client = await QcClientAsync("qc-7e3-fqc-mat-" + Guid.NewGuid().ToString("N")[..6]);
        var (woId, _) = await SeedWoAsync(mesPhase: "FQC_PENDING");

        var resp = await client.GetAsync($"/api/v2/work-orders/{woId}/qc/fqc");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var view = await resp.Content.ReadFromJsonAsync<WoQcView>();
        Assert.NotNull(view);
        Assert.Equal("FQC", view!.QcKind);
        Assert.False(string.IsNullOrEmpty(view.ProfileSnapshotJson),
            "L23: lazy materialise MUST seed the snapshot from the Q4 default chain.");
        Assert.NotEqual("{}", view.ProfileSnapshotJson);
        Assert.Equal(12, view.Items.Count);
        Assert.Contains("lot_size", view.Items.Select(i => i.ItemKey));
        Assert.Contains("dim_4", view.Items.Select(i => i.ItemKey));
        Assert.All(view.Items, item => Assert.Equal("Pending", item.Status));
        Assert.False(view.IsReadyForJudgment,
            "Operator hasn't touched items yet; readiness MUST stay false.");
    }

    [Fact]
    public async Task Get_fresh_oqc_view_materialises_28_items_from_seeded_profile()
    {
        var client = await QcClientAsync("qc-7e3-oqc-mat-" + Guid.NewGuid().ToString("N")[..6]);
        var (woId, _) = await SeedWoAsync(mesPhase: "OQC_PENDING");

        var resp = await client.GetAsync($"/api/v2/work-orders/{woId}/qc/oqc");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var view = await resp.Content.ReadFromJsonAsync<WoQcView>();
        Assert.NotNull(view);
        Assert.Equal("OQC", view!.QcKind);
        Assert.Equal(28, view.Items.Count);
        // RoHS heavy-metal items — CCL-10-F6 compliance leg.
        var keys = view.Items.Select(i => i.ItemKey).ToHashSet();
        Assert.Contains("cr_ppm", keys);
        Assert.Contains("pb_ppm", keys);
        Assert.Contains("sb_ppm", keys);
    }

    [Fact]
    public async Task Get_view_heals_legacy_empty_snapshot_with_seeded_profile()
    {
        // Pre-fix rows materialised with ProfileSnapshotJson="{}" should
        // self-heal on next read so existing in-flight checks pick up
        // the seeded profile without operator action.
        var client = await QcClientAsync("qc-7e3-heal-" + Guid.NewGuid().ToString("N")[..6]);
        var (woId, _) = await SeedWoAsync(mesPhase: "FQC_PENDING");
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
            var check = new WoQcCheck
            {
                WorkOrderId = woId,
                QcKind = "FQC",
                ProfileSnapshotJson = "{}", // legacy empty
                Judgment = WoQcJudgment.Pending,
            };
            db.WoQcChecks.Add(check);
            await db.SaveChangesAsync();
        }

        var resp = await client.GetAsync($"/api/v2/work-orders/{woId}/qc/fqc");
        var view = await resp.Content.ReadFromJsonAsync<WoQcView>();
        Assert.NotNull(view);
        Assert.NotEqual("{}", view!.ProfileSnapshotJson);
        Assert.Equal(12, view.Items.Count);
    }

    [Fact]
    public async Task Fqc_judgment_Pass_with_partial_items_returns_422_not_ready()
    {
        // 12-item profile; operator only completed 5 → judgment must gate.
        // Pre-fix: row-only rule said "ready" because all 5 persisted rows
        // were non-Pending. Fix: profile-expected count of 12 keeps it gated.
        var inspector = "qc-7e3-partial-" + Guid.NewGuid().ToString("N")[..6];
        var client = await QcClientAsync(inspector);
        var (woId, etag) = await SeedWoAsync(mesPhase: "FQC_PENDING");

        // Materialise the profile + add only 5 of 12 items as Ok.
        var initView = await client.GetAsync($"/api/v2/work-orders/{woId}/qc/fqc");
        Assert.Equal(HttpStatusCode.OK, initView.StatusCode);
        var view = await initView.Content.ReadFromJsonAsync<WoQcView>();
        Assert.NotNull(view);
        var currentEtag = view!.ETag;

        var fiveKeys = view.Items.Take(5).Select(i => i.ItemKey).ToList();
        foreach (var key in fiveKeys)
        {
            var putReq = new HttpRequestMessage(HttpMethod.Put,
                $"/api/v2/work-orders/{woId}/qc/fqc/items/{Uri.EscapeDataString(key)}")
            {
                Content = JsonContent.Create(new SetWoQcItemRequest { Status = "Ok" }),
            };
            AddOptimisticHeaders(putReq, currentEtag);
            var putResp = await client.SendAsync(putReq);
            Assert.Equal(HttpStatusCode.OK, putResp.StatusCode);
            var putBody = await putResp.Content.ReadFromJsonAsync<WoQcSetResponse>();
            currentEtag = putBody!.ETag;
        }

        // Re-read to confirm IsReadyForJudgment = false despite 5 Ok rows.
        var midView = await client.GetAsync($"/api/v2/work-orders/{woId}/qc/fqc");
        var midViewBody = await midView.Content.ReadFromJsonAsync<WoQcView>();
        Assert.False(midViewBody!.IsReadyForJudgment,
            "L23: 5 of 12 profile items resolved MUST NOT be 'ready'. " +
            "Pre-fix: row-only rollup said ready=true → operator could silently bypass the profile.");

        // Attempt judgment → server gates with 422 qc.not_ready_for_judgment.
        var judgmentReq = Post($"/api/v2/work-orders/{woId}/qc/fqc/judgment",
            new SubmitFqcJudgmentRequest { Judgment = "Pass" });
        AddOptimisticHeaders(judgmentReq, currentEtag);
        var judgmentResp = await client.SendAsync(judgmentReq);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, judgmentResp.StatusCode);
        var err = await judgmentResp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("qc.not_ready_for_judgment", err!.Code);
    }
}
