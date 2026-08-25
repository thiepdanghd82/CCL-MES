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
/// IPQC first-article (h-2) — WoIpqcMaterialController coverage.
///
/// Contract mirrors IpqcReviewController: 200 / 400 (no Idem) / 404 / 409
/// (stale If-Match) / 422 (invalid_phase / invalid_status / invalid_material_line
/// / invalid_reason_code / material.invalid_outcome / material.invalid_reason /
/// material.not_divergent / material.same_user_as_confirmer) / 428 (no If-Match).
///
/// Soft-lock (Q1): a divergent lot → PendingEngineer at confirm; GoRun blocked
/// with ipqc.material_divergence_unresolved until an Engineer waiver Approves.
/// Dual-sig: waiver approver ≠ confirmer (flag default ON) → WO_IPQC_MATERIAL_APPROVE_DENIED.
/// </summary>
public sealed class WoIpqcMaterialControllerTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public WoIpqcMaterialControllerTests(MesApiFactory fx) => _fx = fx;

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
            WoNo = "WO-H2-" + Guid.NewGuid().ToString("N")[..6],
            CustomerId = customer.Id,
            ProductId = product.Id,
            ProductName = product.Name,
            TargetQty = 1000,
            Uom = "pcs",
            CurrentStep = ProcessStepCode.IpqcApproval,
            MesPhase = mesPhase,
            Status = WoStatus.InProgress,
        };
        db.WorkOrders.Add(wo);
        await db.SaveChangesAsync();
        var rv = await db.WorkOrders.AsNoTracking().Where(w => w.Id == wo.Id)
            .Select(w => w.RowVersion).SingleAsync();
        return (wo.Id, Convert.ToBase64String(rv));
    }

    /// <summary>Add one BOM material line. When <paramref name="matched"/>, also
    /// creates a Released MaterialLot (PartNo == code) linked to a Pass IQC and
    /// wires the WoMaterial shadow FK — so the join reconciles (not divergent).
    /// Otherwise the line has no lot (divergent: ShadowFkNull + IqcNotPass + LotNotReleased).</summary>
    private async Task SeedMaterialAsync(long woId, int bomLineIdx, string code, bool matched)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var tok = Guid.NewGuid().ToString("N")[..6];
        var wm = new WoMaterial
        {
            WorkOrderId = woId, BomLineIdx = bomLineIdx, MaterialCode = code,
            MaterialDescription = code + " desc", QtyRequired = 10, Uom = "pcs",
            LotNo = matched ? $"LOT-{code}-{tok}" : $"SCAN-{code}-{tok}",
        };
        db.WoMaterials.Add(wm);
        await db.SaveChangesAsync();

        if (matched)
        {
            var iqc = new IqcInspection
            {
                Group = "Materials", PartNo = code, BatchNumber = "B1",
                LotNumber = $"LOT-{code}-{tok}", ReceiptNo = $"IQC-{code}-{tok}",
                ReceivedDate = DateTime.UtcNow, Quantity = 100, Result = QcResult.Pass,
            };
            db.IqcInspections.Add(iqc);
            await db.SaveChangesAsync();
            var lot = new MaterialLot
            {
                LotNo = $"LOT-{code}-{tok}", PartNo = code, ReceivedAt = DateTime.UtcNow,
                QtyReceived = 100, QtyAvailable = 100,
                Status = nameof(MaterialLotStatus.Released), IqcInspectionId = iqc.Id, Uom = "pcs",
            };
            db.MaterialLots.Add(lot);
            await db.SaveChangesAsync();
            db.Entry(wm).Property("MaterialLotId").CurrentValue = lot.Id;
            await db.SaveChangesAsync();
        }
    }

    /// <summary>Seed a legacy 4-slot IPQC check all-OK so the item readiness gate
    /// passes — lets the GoRun material gate be tested in isolation.</summary>
    private async Task SeedAllOkCheckAsync(long woId)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        db.WoIpqcChecks.Add(new WoIpqcCheck
        {
            WorkOrderId = woId,
            MaterialStatus = IpqcCheckStatus.Ok, PrintAStatus = IpqcCheckStatus.Ok,
            PrintBStatus = IpqcCheckStatus.Ok, PrintCStatus = IpqcCheckStatus.Ok,
            Judgment = IpqcJudgment.Pending, QaOutcome = QaOutcome.Pending,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Directly seed a materialised material check row in a given waiver
    /// state (for the GoRun-gate test without going through confirm).</summary>
    private async Task SeedMaterialCheckRowAsync(
        long woId, int bomLineIdx, IpqcCheckStatus status, DivergenceApprovalStatus approval)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        db.WoIpqcMaterialChecks.Add(new WoIpqcMaterialCheck
        {
            WorkOrderId = woId, BomLineIdx = bomLineIdx, MaterialCode = "M-" + bomLineIdx,
            Status = status, DivergenceApprovalStatus = approval,
            DivergenceKind = approval == DivergenceApprovalStatus.NotRequired ? "None" : "IqcNotPass",
            DivergenceFlags = approval == DivergenceApprovalStatus.NotRequired ? 0 : 2,
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedScrapReasonAsync(string code)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        if (!await db.ReasonCodes.AnyAsync(r => r.Code == code))
        {
            db.ReasonCodes.Add(new ReasonCode { Code = code, LabelEn = code, LabelVi = code, Kind = ReasonCodeKind.Scrap, Sort = 10 });
            await db.SaveChangesAsync();
        }
    }

    private async Task<HttpClient> ClientAsync(string user, string role)
    {
        await _fx.SeedUserAsync(user, "P@ss!1", role);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, user, "P@ss!1");
        return client;
    }

    private static HttpRequestMessage Mk(HttpMethod method, string path, string body, string? ifMatch, string? idem)
    {
        var req = new HttpRequestMessage(method, path) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (ifMatch is not null) req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        if (idem is not null) req.Headers.TryAddWithoutValidation("Idempotency-Key", idem);
        return req;
    }

    private async Task<string> EtagAsync(long woId)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var rv = await db.WorkOrders.AsNoTracking().Where(w => w.Id == woId).Select(w => w.RowVersion).SingleAsync();
        return Convert.ToBase64String(rv);
    }

    private string PutPath(long wo, int idx) => $"/api/v2/work-orders/{wo}/ipqc/material-system/{idx}";
    private string ApprovePath(long wo, int idx) => $"/api/v2/work-orders/{wo}/ipqc/material-system/{idx}/approve-divergence";

    // ── GET ────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_returns_view_and_lazy_materialises_rows()
    {
        var (wo, _) = await SeedWoAsync();
        await SeedMaterialAsync(wo, 0, "AAA", matched: true);
        await SeedMaterialAsync(wo, 1, "BBB", matched: false);
        var client = await ClientAsync("qc-h2-get", UserRole.Qc);

        var resp = await client.GetAsync($"/api/v2/work-orders/{wo}/ipqc/material-system");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var view = await resp.Content.ReadFromJsonAsync<IpqcMaterialSystemView>();
        Assert.NotNull(view);
        Assert.Equal(2, view!.Rows.Count);

        var matchedRow = view.Rows.Single(r => r.BomLineIdx == 0);
        Assert.False(matchedRow.IsDivergent);
        Assert.StartsWith("IQC-AAA", matchedRow.SourceIqcReceiptNo!);

        var divergentRow = view.Rows.Single(r => r.BomLineIdx == 1);
        Assert.True(divergentRow.IsDivergent);

        // Rows persisted (lazy materialise).
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        Assert.Equal(2, await db.WoIpqcMaterialChecks.CountAsync(r => r.WorkOrderId == wo));
    }

    [Fact]
    public async Task Get_unknown_wo_returns_404()
    {
        var client = await ClientAsync("qc-h2-404", UserRole.Qc);
        var resp = await client.GetAsync($"/api/v2/work-orders/{long.MaxValue}/ipqc/material-system");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ── Prelude 428 / 400 / 409 ───────────────────────────────────

    [Fact]
    public async Task Confirm_missing_ifmatch_428()
    {
        var (wo, _) = await SeedWoAsync();
        await SeedMaterialAsync(wo, 0, "AAA", matched: true);
        var client = await ClientAsync("qc-h2-428", UserRole.Qc);
        var resp = await client.SendAsync(Mk(HttpMethod.Put, PutPath(wo, 0), "{\"status\":\"Ok\"}", null, Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.PreconditionRequired, resp.StatusCode);
    }

    [Fact]
    public async Task Confirm_missing_idem_400()
    {
        var (wo, etag) = await SeedWoAsync();
        await SeedMaterialAsync(wo, 0, "AAA", matched: true);
        var client = await ClientAsync("qc-h2-400", UserRole.Qc);
        var resp = await client.SendAsync(Mk(HttpMethod.Put, PutPath(wo, 0), "{\"status\":\"Ok\"}", $"\"{etag}\"", null));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Confirm_stale_ifmatch_409()
    {
        var (wo, _) = await SeedWoAsync();
        await SeedMaterialAsync(wo, 0, "AAA", matched: true);
        var client = await ClientAsync("qc-h2-409", UserRole.Qc);
        var resp = await client.SendAsync(Mk(HttpMethod.Put, PutPath(wo, 0), "{\"status\":\"Ok\"}", "\"STALE==\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    // ── 422 guards ────────────────────────────────────────────────

    [Fact]
    public async Task Confirm_wrong_phase_422()
    {
        var (wo, etag) = await SeedWoAsync("RUNNING");
        await SeedMaterialAsync(wo, 0, "AAA", matched: true);
        var client = await ClientAsync("qc-h2-phase", UserRole.Qc);
        var resp = await client.SendAsync(Mk(HttpMethod.Put, PutPath(wo, 0), "{\"status\":\"Ok\"}", $"\"{etag}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("wo.invalid_phase", err!.Code);
    }

    [Fact]
    public async Task Confirm_invalid_status_422()
    {
        var (wo, etag) = await SeedWoAsync();
        await SeedMaterialAsync(wo, 0, "AAA", matched: true);
        var client = await ClientAsync("qc-h2-status", UserRole.Qc);
        var resp = await client.SendAsync(Mk(HttpMethod.Put, PutPath(wo, 0), "{\"status\":\"Maybe\"}", $"\"{etag}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("ipqc.invalid_status", err!.Code);
    }

    [Fact]
    public async Task Confirm_unknown_bomline_422_invalid_material_line()
    {
        var (wo, etag) = await SeedWoAsync();
        await SeedMaterialAsync(wo, 0, "AAA", matched: true);
        var client = await ClientAsync("qc-h2-line", UserRole.Qc);
        var resp = await client.SendAsync(Mk(HttpMethod.Put, PutPath(wo, 99), "{\"status\":\"Ok\"}", $"\"{etag}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("ipqc.invalid_material_line", err!.Code);
    }

    [Fact]
    public async Task Confirm_ng_missing_reason_422()
    {
        var (wo, etag) = await SeedWoAsync();
        await SeedMaterialAsync(wo, 0, "AAA", matched: true);
        var client = await ClientAsync("qc-h2-ng", UserRole.Qc);
        var resp = await client.SendAsync(Mk(HttpMethod.Put, PutPath(wo, 0), "{\"status\":\"Ng\"}", $"\"{etag}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("ipqc.invalid_reason_code", err!.Code);
    }

    // ── Happy confirm — matched vs divergent ──────────────────────

    [Fact]
    public async Task Confirm_matched_material_OK_resolves()
    {
        var (wo, etag) = await SeedWoAsync();
        await SeedMaterialAsync(wo, 0, "AAA", matched: true);
        var client = await ClientAsync("qc-h2-ok", UserRole.Qc);
        var resp = await client.SendAsync(Mk(HttpMethod.Put, PutPath(wo, 0), "{\"status\":\"Ok\"}", $"\"{etag}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<IpqcMaterialSetResponse>();
        Assert.True(body!.Ok);
        Assert.Equal("NotRequired", body.RowApprovalStatus);
        Assert.True(body.AllResolved);
    }

    [Fact]
    public async Task Confirm_divergent_material_sets_PendingEngineer_and_audits()
    {
        var (wo, etag) = await SeedWoAsync();
        await SeedMaterialAsync(wo, 0, "AAA", matched: false); // no lot → divergent
        var client = await ClientAsync("qc-h2-div", UserRole.Qc);
        var resp = await client.SendAsync(Mk(HttpMethod.Put, PutPath(wo, 0), "{\"status\":\"Ok\"}", $"\"{etag}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<IpqcMaterialSetResponse>();
        Assert.Equal("PendingEngineer", body!.RowApprovalStatus);
        Assert.False(body.AllResolved);
        Assert.True(body.AnyPendingWaiver);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var audit = await db.AuditLogs.FirstOrDefaultAsync(a =>
            a.Action == "WO_IPQC_MATERIAL_CHECK" && a.TargetId == wo.ToString());
        Assert.NotNull(audit);
        Assert.Contains("requires_waiver", audit!.Detail);
    }

    // ── Approve-divergence (EngineerWaive policy) ─────────────────

    [Fact]
    public async Task ApproveDivergence_operator_forbidden_403()
    {
        var (wo, _) = await SeedWoAsync();
        await SeedMaterialCheckRowAsync(wo, 0, IpqcCheckStatus.Ng, DivergenceApprovalStatus.PendingEngineer);
        var op = await ClientAsync("op-h2-403", UserRole.Operator);
        var etag = await EtagAsync(wo);
        var resp = await op.SendAsync(Mk(HttpMethod.Post, ApprovePath(wo, 0), "{\"outcome\":\"Approve\",\"reason\":\"ok\"}", $"\"{etag}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task ApproveDivergence_engineer_distinct_approves_and_resolves()
    {
        var (wo, etag) = await SeedWoAsync();
        await SeedMaterialAsync(wo, 0, "AAA", matched: false);
        // QC confirms the divergent row → PendingEngineer.
        var qc = await ClientAsync("qc-h2-appr-confirmer", UserRole.Qc);
        var c = await qc.SendAsync(Mk(HttpMethod.Put, PutPath(wo, 0), "{\"status\":\"Ok\"}", $"\"{etag}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, c.StatusCode);

        var eng = await ClientAsync("eng-h2-appr", UserRole.Engineer);
        var etag2 = await EtagAsync(wo);
        var resp = await eng.SendAsync(Mk(HttpMethod.Post, ApprovePath(wo, 0), "{\"outcome\":\"Approve\",\"reason\":\"Lô thay thế đã kiểm\"}", $"\"{etag2}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<IpqcMaterialSetResponse>();
        Assert.Equal("Approved", body!.RowApprovalStatus);
        Assert.True(body.AllResolved);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var audit = await db.AuditLogs.FirstOrDefaultAsync(a =>
            a.Action == "WO_IPQC_MATERIAL_APPROVE" && a.TargetId == wo.ToString());
        Assert.NotNull(audit);
        Assert.Contains("\"flag_state\":\"on\"", audit!.Detail);
    }

    [Fact]
    public async Task ApproveDivergence_same_user_as_confirmer_422_denied()
    {
        // Admin has BOTH IpqcSubmit + EngineerWaive → same admin confirms then
        // tries to waive → dual-sig violation.
        var (wo, etag) = await SeedWoAsync();
        await SeedMaterialAsync(wo, 0, "AAA", matched: false);
        var admin = await ClientAsync("admin-h2-same", UserRole.Admin);
        var c = await admin.SendAsync(Mk(HttpMethod.Put, PutPath(wo, 0), "{\"status\":\"Ok\"}", $"\"{etag}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, c.StatusCode);

        var etag2 = await EtagAsync(wo);
        var resp = await admin.SendAsync(Mk(HttpMethod.Post, ApprovePath(wo, 0), "{\"outcome\":\"Approve\",\"reason\":\"self\"}", $"\"{etag2}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("material.same_user_as_confirmer", err!.Code);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var denied = await db.AuditLogs.FirstOrDefaultAsync(a =>
            a.Action == "WO_IPQC_MATERIAL_APPROVE_DENIED" && a.TargetId == wo.ToString());
        Assert.NotNull(denied);
        var falsePositive = await db.AuditLogs.AnyAsync(a =>
            a.Action == "WO_IPQC_MATERIAL_APPROVE" && a.TargetId == wo.ToString());
        Assert.False(falsePositive, "WO_IPQC_MATERIAL_APPROVE MUST NOT emit on dual-sig violation.");
    }

    [Fact]
    public async Task ApproveDivergence_not_divergent_422()
    {
        var (wo, etag) = await SeedWoAsync();
        await SeedMaterialAsync(wo, 0, "AAA", matched: true);
        var qc = await ClientAsync("qc-h2-nd-confirm", UserRole.Qc);
        var c = await qc.SendAsync(Mk(HttpMethod.Put, PutPath(wo, 0), "{\"status\":\"Ok\"}", $"\"{etag}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, c.StatusCode);

        var eng = await ClientAsync("eng-h2-nd", UserRole.Engineer);
        var etag2 = await EtagAsync(wo);
        var resp = await eng.SendAsync(Mk(HttpMethod.Post, ApprovePath(wo, 0), "{\"outcome\":\"Approve\",\"reason\":\"x\"}", $"\"{etag2}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("material.not_divergent", err!.Code);
    }

    [Fact]
    public async Task ApproveDivergence_invalid_outcome_422()
    {
        var (wo, _) = await SeedWoAsync();
        await SeedMaterialCheckRowAsync(wo, 0, IpqcCheckStatus.Ng, DivergenceApprovalStatus.PendingEngineer);
        var eng = await ClientAsync("eng-h2-outcome", UserRole.Engineer);
        var etag = await EtagAsync(wo);
        var resp = await eng.SendAsync(Mk(HttpMethod.Post, ApprovePath(wo, 0), "{\"outcome\":\"Perhaps\",\"reason\":\"x\"}", $"\"{etag}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("material.invalid_outcome", err!.Code);
    }

    // ── GoRun gate (soft-lock) ────────────────────────────────────

    [Fact]
    public async Task GoRun_blocked_when_material_unresolved_then_allowed_after_waiver()
    {
        var (wo, _) = await SeedWoAsync("IPQC_WAIT");
        await SeedAllOkCheckAsync(wo);                       // item readiness satisfied
        await SeedMaterialCheckRowAsync(wo, 0, IpqcCheckStatus.Ng, DivergenceApprovalStatus.PendingEngineer);
        var qc = await ClientAsync("qc-h2-gorun", UserRole.Qc);

        var etag = await EtagAsync(wo);
        var blocked = await qc.SendAsync(Mk(HttpMethod.Post,
            $"/api/v2/work-orders/{wo}/ipqc/judgment", "{\"judgment\":\"GoRun\"}", $"\"{etag}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, blocked.StatusCode);
        var err = await blocked.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("ipqc.material_divergence_unresolved", err!.Code);

        // Engineer waives → GoRun now succeeds.
        var eng = await ClientAsync("eng-h2-gorun", UserRole.Engineer);
        var etag2 = await EtagAsync(wo);
        var appr = await eng.SendAsync(Mk(HttpMethod.Post, ApprovePath(wo, 0), "{\"outcome\":\"Approve\",\"reason\":\"waive\"}", $"\"{etag2}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, appr.StatusCode);

        var etag3 = await EtagAsync(wo);
        var ok = await qc.SendAsync(Mk(HttpMethod.Post,
            $"/api/v2/work-orders/{wo}/ipqc/judgment", "{\"judgment\":\"GoRun\"}", $"\"{etag3}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var body = await ok.Content.ReadFromJsonAsync<IpqcSetResponse>();
        Assert.Equal("IPQC_APPROVED", body!.MesPhase);
    }

    // ── Audit wire-mirror (R7.3) ──────────────────────────────────

    [Fact]
    public async Task Audit_visibility_via_wire_audit_log_endpoint()
    {
        var (wo, etag) = await SeedWoAsync();
        await SeedMaterialAsync(wo, 0, "AAA", matched: false);
        var qc = await ClientAsync("qc-h2-wire", UserRole.Qc);
        var c = await qc.SendAsync(Mk(HttpMethod.Put, PutPath(wo, 0), "{\"status\":\"Ok\"}", $"\"{etag}\"", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.OK, c.StatusCode);

        var admin = await ClientAsync("admin-h2-wire", UserRole.Admin);
        var resp = await admin.GetAsync("/api/v2/audit/log?action=WO_IPQC_MATERIAL_CHECK&page=1&pageSize=50");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var payload = await resp.Content.ReadAsStringAsync();
        Assert.Contains("WO_IPQC_MATERIAL_CHECK", payload);
    }
}
