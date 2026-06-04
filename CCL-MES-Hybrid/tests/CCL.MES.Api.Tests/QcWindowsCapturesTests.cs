using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain;
using CCL.MES.Domain.Auth;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QcPlanUpsertRequest = CCL.MES.Shared.QcSpecs.QcPlanUpsertRequest;
using QcCriterionRowRequest = CCL.MES.Shared.QcSpecs.QcCriterionRowRequest;
using QcCaptureCreateRequest = CCL.MES.Shared.QcSpecs.QcCaptureCreateRequest;
using QcMutationError = CCL.MES.Shared.QcSpecs.QcMutationError;
using QcPlanUpsertResponse = CCL.MES.Shared.QcSpecs.QcPlanUpsertResponse;
using QcCaptureItem = CCL.MES.Shared.QcSpecs.QcCaptureItem;

namespace CCL.MES.Api.Tests;

/// <summary>
/// P10.5f — POST <c>/qc-specs/windows/upsert-stage</c> + POST
/// <c>/qc-specs/captures</c>. Coverage:
///   · Role gate: Admin/Engineer pass, Supervisor 403 with qc.forbidden
///   · Anonymous 401
///   · Per-stage atomic upsert: insert/update/delete diff applied,
///     audit SPEC_QC_PLAN_UPSERT + SPEC_QC_PLAN_UPSERT_DEVICE emitted
///   · Empty Name rejected (qc.invalid_row)
///   · Unknown stage rejected (qc.invalid_stage)
///   · Cross-revision criterion rejected (qc.not_found)
///   · Capture FAIL without reason → qc.reason_required
///   · Capture FAIL with unknown reason → qc.invalid_reason
///   · Capture FAIL with valid Scrap reason → 200 + audit SPEC_QC_CAPTURE
///   · Capture PASS without reason → 200
/// </summary>
public sealed class QcWindowsCapturesTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public QcWindowsCapturesTests(MesApiFactory fx) => _fx = fx;

    private async Task<HttpClient> ClientAsync(string username, string role, string? department = null)
    {
        await _fx.SeedUserAsync(username, "P@ss!", role, department: department);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, username, "P@ss!");
        return client;
    }

    private async Task<long> SeedRevisionAsync(string code)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var customer = new Customer { Code = $"C-{code}", Name = $"C {code}" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var product = new Product { ProductCode = code, Name = code, CustomerId = customer.Id };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        var rev = new ProductRevision
        {
            ProductId = product.Id,
            SpecCode = code,
            Title = code,
            RevisionCode = "A",
            Status = ProductRevisionStatus.Draft,
            Print = new SpecPrint { ProcessCode = "SILKSCREEN", NumColors = 0 },
        };
        db.ProductRevisions.Add(rev);
        await db.SaveChangesAsync();
        return rev.Id;
    }

    private async Task SeedScrapReasonAsync(string code, string labelVi = "Lỗi mã thử")
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        if (await db.ReasonCodes.AnyAsync(r => r.Code == code)) return;
        db.ReasonCodes.Add(new ReasonCode
        {
            Code = code,
            LabelVi = labelVi,
            LabelEn = code,
            Kind = ReasonCodeKind.Scrap,
            Active = true,
            Sort = 1,
        });
        await db.SaveChangesAsync();
    }

    // ── Auth + role gate ────────────────────────────────────────────

    [Fact]
    public async Task Upsert_anonymous_returns_401()
    {
        var anon = _fx.CreateClient();
        var resp = await anon.PostAsJsonAsync("/api/v2/qc-specs/windows/upsert-stage/1",
            new QcPlanUpsertRequest { Stage = "IpqcPrint", Rows = new() });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Upsert_Supervisor_returns_403_qc_forbidden()
    {
        var client = await ClientAsync("sup-qc", UserRole.Supervisor);
        var revId = await SeedRevisionAsync("PRD-QC-SUP");
        var resp = await client.PostAsJsonAsync(
            $"/api/v2/qc-specs/windows/upsert-stage/{revId}",
            new QcPlanUpsertRequest
            {
                Stage = "IpqcPrint",
                Rows = new() { new QcCriterionRowRequest { Name = "Check 1" } },
            });
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<QcMutationError>();
        Assert.Equal("qc.forbidden", err!.Code);
    }

    [Fact]
    public async Task Upsert_anonymous_capture_returns_401()
    {
        var anon = _fx.CreateClient();
        var resp = await anon.PostAsJsonAsync("/api/v2/qc-specs/captures/1",
            new QcCaptureCreateRequest { CriterionId = 1, Result = "Pass" });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Capture_Supervisor_returns_403_qc_forbidden()
    {
        var client = await ClientAsync("sup-cap", UserRole.Supervisor);
        var revId = await SeedRevisionAsync("PRD-CAP-SUP");
        var resp = await client.PostAsJsonAsync(
            $"/api/v2/qc-specs/captures/{revId}",
            new QcCaptureCreateRequest { CriterionId = 99999, Result = "Pass" });
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<QcMutationError>();
        Assert.Equal("qc.forbidden", err!.Code);
    }

    // ── Upsert validation ───────────────────────────────────────────

    [Fact]
    public async Task Upsert_unknown_stage_returns_422_qc_invalid_stage()
    {
        var client = await ClientAsync("eng-stage", UserRole.Engineer, department: "npi");
        var revId = await SeedRevisionAsync("PRD-INV-STAGE");
        var resp = await client.PostAsJsonAsync(
            $"/api/v2/qc-specs/windows/upsert-stage/{revId}",
            new QcPlanUpsertRequest
            {
                Stage = "WhateverElse",
                Rows = new() { new QcCriterionRowRequest { Name = "x" } },
            });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<QcMutationError>();
        Assert.Equal("qc.invalid_stage", err!.Code);
    }

    [Fact]
    public async Task Upsert_blank_name_returns_422_qc_invalid_row()
    {
        var client = await ClientAsync("eng-blank", UserRole.Engineer);
        var revId = await SeedRevisionAsync("PRD-BLANK");
        var resp = await client.PostAsJsonAsync(
            $"/api/v2/qc-specs/windows/upsert-stage/{revId}",
            new QcPlanUpsertRequest
            {
                Stage = "IpqcPrint",
                Rows = new()
                {
                    new QcCriterionRowRequest { Name = "Valid criterion" },
                    new QcCriterionRowRequest { Name = "   " },
                },
            });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<QcMutationError>();
        Assert.Equal("qc.invalid_row", err!.Code);
    }

    [Fact]
    public async Task Upsert_revision_not_found_returns_404()
    {
        var client = await ClientAsync("eng-norev", UserRole.Engineer);
        var resp = await client.PostAsJsonAsync(
            "/api/v2/qc-specs/windows/upsert-stage/9999999",
            new QcPlanUpsertRequest
            {
                Stage = "IpqcPrint",
                Rows = new() { new QcCriterionRowRequest { Name = "x" } },
            });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<QcMutationError>();
        Assert.Equal("qc.not_found", err!.Code);
    }

    // ── Upsert happy path + atomic diff + device audit ──────────────

    [Fact]
    public async Task Upsert_inserts_updates_and_deletes_in_one_save()
    {
        var client = await ClientAsync("eng-atomic", UserRole.Engineer);
        var revId = await SeedRevisionAsync("PRD-ATOMIC");
        const string deviceId = "0193fb00-0000-0000-0000-qcatomic000001";
        client.DefaultRequestHeaders.Add("X-Device-Id", deviceId);

        // First save — 3 inserts.
        var first = await client.PostAsJsonAsync(
            $"/api/v2/qc-specs/windows/upsert-stage/{revId}",
            new QcPlanUpsertRequest
            {
                Stage = "IpqcPrint",
                Rows = new()
                {
                    new QcCriterionRowRequest { Name = "A", Target = "ok", Method = "visual" },
                    new QcCriterionRowRequest { Name = "B", Target = "ok2" },
                    new QcCriterionRowRequest { Name = "C" },
                },
            });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<QcPlanUpsertResponse>();
        Assert.Equal(3, firstBody!.Window.Criteria.Count);

        // Build a diff: keep A by Id, drop B (no Id submitted), insert D.
        var keepA = firstBody.Window.Criteria.First(c => c.Name == "A").Id;
        var keepC = firstBody.Window.Criteria.First(c => c.Name == "C").Id;
        var second = await client.PostAsJsonAsync(
            $"/api/v2/qc-specs/windows/upsert-stage/{revId}",
            new QcPlanUpsertRequest
            {
                Stage = "IpqcPrint",
                Rows = new()
                {
                    new QcCriterionRowRequest { Id = keepA, Name = "A-updated", Target = "new" },
                    new QcCriterionRowRequest { Id = keepC, Name = "C" },
                    new QcCriterionRowRequest { Name = "D" }, // insert
                },
            });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondBody = await second.Content.ReadFromJsonAsync<QcPlanUpsertResponse>();
        Assert.Equal(3, secondBody!.Window.Criteria.Count);
        Assert.Equal(new[] { "A-updated", "C", "D" },
            secondBody.Window.Criteria.OrderBy(c => c.Seq).Select(c => c.Name).ToArray());

        // Audit chain — both saves emit SPEC_QC_PLAN_UPSERT + one
        // SPEC_QC_PLAN_UPSERT_DEVICE per call.
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var legacy = await db.AuditLogs
            .Where(a => a.Action == "SPEC_QC_PLAN_UPSERT")
            .CountAsync();
        Assert.True(legacy >= 2);
        var device = await db.AuditLogs
            .Where(a => a.Action == "SPEC_QC_PLAN_UPSERT_DEVICE" && a.TargetId == deviceId)
            .CountAsync();
        Assert.Equal(2, device);
    }

    // ── Capture path ────────────────────────────────────────────────

    private async Task<(long RevId, long CriterionId)> SeedRevAndCriterionAsync(string code)
    {
        var revId = await SeedRevisionAsync(code);
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var window = new SpecQcWindow
        {
            ProductRevisionId = revId,
            Stage = QcStage.IpqcPrint,
            Title = "IPQC · Print",
            Status = SpecQcWindowStatus.Draft,
            RejectAction = QcRejectAction.Escalate,
        };
        db.SpecQcWindows.Add(window);
        await db.SaveChangesAsync();
        var criterion = new QcCriterion
        {
            SpecQcWindowId = window.Id,
            Seq = 0,
            Name = "Print color",
            CriterionType = QcCriterionType.Visual,
            Required = true,
        };
        db.QcCriteria.Add(criterion);
        await db.SaveChangesAsync();
        return (revId, criterion.Id);
    }

    [Fact]
    public async Task Capture_PASS_without_reason_succeeds()
    {
        var client = await ClientAsync("eng-pass", UserRole.Engineer);
        var (revId, critId) = await SeedRevAndCriterionAsync("PRD-CAP-PASS");

        var resp = await client.PostAsJsonAsync(
            $"/api/v2/qc-specs/captures/{revId}",
            new QcCaptureCreateRequest
            {
                CriterionId = critId,
                Result = "Pass",
                Measurement = "12.5mm",
            });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<QcCaptureItem>();
        Assert.NotNull(body);
        Assert.Equal(CCL.MES.Shared.QcSpecs.QcCaptureResult.Pass, body!.Result);
        Assert.Equal("12.5mm", body.Measurement);
    }

    [Fact]
    public async Task Capture_FAIL_without_reason_returns_422_qc_reason_required()
    {
        var client = await ClientAsync("eng-fail-noreason", UserRole.Engineer);
        var (revId, critId) = await SeedRevAndCriterionAsync("PRD-CAP-FAIL-NR");
        var resp = await client.PostAsJsonAsync(
            $"/api/v2/qc-specs/captures/{revId}",
            new QcCaptureCreateRequest { CriterionId = critId, Result = "Fail" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<QcMutationError>();
        Assert.Equal("qc.reason_required", err!.Code);
    }

    [Fact]
    public async Task Capture_FAIL_with_unknown_reason_returns_422_qc_invalid_reason()
    {
        var client = await ClientAsync("eng-fail-badreason", UserRole.Engineer);
        var (revId, critId) = await SeedRevAndCriterionAsync("PRD-CAP-FAIL-BR");
        var resp = await client.PostAsJsonAsync(
            $"/api/v2/qc-specs/captures/{revId}",
            new QcCaptureCreateRequest
            {
                CriterionId = critId,
                Result = "Fail",
                NgReasonCode = "SC-UNKNOWN",
            });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<QcMutationError>();
        Assert.Equal("qc.invalid_reason", err!.Code);
    }

    [Fact]
    public async Task Capture_FAIL_with_valid_reason_succeeds_and_emits_audit()
    {
        var client = await ClientAsync("eng-fail-ok", UserRole.Engineer);
        const string deviceId = "0193fb00-0000-0000-0000-qccapture0001";
        client.DefaultRequestHeaders.Add("X-Device-Id", deviceId);
        await SeedScrapReasonAsync("SC-COLOR", "Sai màu");
        var (revId, critId) = await SeedRevAndCriterionAsync("PRD-CAP-FAIL-OK");

        var resp = await client.PostAsJsonAsync(
            $"/api/v2/qc-specs/captures/{revId}",
            new QcCaptureCreateRequest
            {
                CriterionId = critId,
                Result = "Fail",
                NgReasonCode = "SC-COLOR",
                Comment = "Mẫu lệch khỏi pantone target",
            });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<QcCaptureItem>();
        Assert.Equal(CCL.MES.Shared.QcSpecs.QcCaptureResult.Fail, body!.Result);
        Assert.Equal("SC-COLOR", body.NgReasonCode);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var legacy = await db.AuditLogs
            .Where(a => a.Action == "SPEC_QC_CAPTURE")
            .CountAsync();
        Assert.True(legacy >= 1);
        var device = await db.AuditLogs
            .Where(a => a.Action == "SPEC_QC_CAPTURE_DEVICE" && a.TargetId == deviceId)
            .CountAsync();
        Assert.Equal(1, device);
    }

    [Fact]
    public async Task Capture_unknown_criterion_returns_404_qc_not_found()
    {
        var client = await ClientAsync("eng-cap-404", UserRole.Engineer);
        var revId = await SeedRevisionAsync("PRD-CAP-404");
        var resp = await client.PostAsJsonAsync(
            $"/api/v2/qc-specs/captures/{revId}",
            new QcCaptureCreateRequest { CriterionId = 9999999, Result = "Pass" });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<QcMutationError>();
        Assert.Equal("qc.not_found", err!.Code);
    }

    [Fact]
    public async Task Capture_cross_revision_criterion_returns_404()
    {
        var client = await ClientAsync("eng-cap-cross", UserRole.Engineer);
        var (revA, critA) = await SeedRevAndCriterionAsync("PRD-CROSS-A");
        var revB = await SeedRevisionAsync("PRD-CROSS-B");

        // Use revB url but critA from revA — server's belong-to check fires.
        var resp = await client.PostAsJsonAsync(
            $"/api/v2/qc-specs/captures/{revB}",
            new QcCaptureCreateRequest { CriterionId = critA, Result = "Pass" });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<QcMutationError>();
        Assert.Equal("qc.not_found", err!.Code);
    }

    [Fact]
    public async Task Capture_unknown_result_returns_422_qc_invalid_result()
    {
        var client = await ClientAsync("eng-cap-badresult", UserRole.Engineer);
        var (revId, critId) = await SeedRevAndCriterionAsync("PRD-CAP-BAD-RES");
        var resp = await client.PostAsJsonAsync(
            $"/api/v2/qc-specs/captures/{revId}",
            new QcCaptureCreateRequest { CriterionId = critId, Result = "Maybe" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<QcMutationError>();
        Assert.Equal("qc.invalid_result", err!.Code);
    }
}
