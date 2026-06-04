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
using DrawingMutationError = CCL.MES.Shared.Drawings.DrawingMutationError;
using DrawingDecideRequest = CCL.MES.Shared.Drawings.DrawingDecideRequest;
using DrawingDecideResponse = CCL.MES.Shared.Drawings.DrawingDecideResponse;
using SharedDrawingVersionStatus = CCL.MES.Shared.Drawings.DrawingVersionStatus;
using SharedDrawingSlotStatus = CCL.MES.Shared.Drawings.DrawingSlotStatus;

namespace CCL.MES.Api.Tests;

/// <summary>
/// P10.5e-2 — `POST /api/v2/specs/{rev}/drawings/{ver}/decide`. Coverage:
///   · CanActAs 4-case truth table end-to-end through HTTP
///   · Comment-required-on-Reject server guard
///   · Invalid-state guard (cannot decide on Superseded)
///   · State transitions: 3× Approved → version Approved + parent
///     supersede sweep + DRAWING_DECIDE + DRAWING_SUPERSEDE audit
///   · X-Device-Id → DRAWING_DECIDE_DEVICE pairing
///   · Role/Decision string validation
///
/// Tests construct ProductRevisions + Drawings + Versions directly via
/// the DbContext to avoid forwarding through the upload pipeline (the
/// upload coverage is in DrawingsUploadDownloadTests). Approvals are
/// seeded with status Pending so the decide flow can mutate them.
/// </summary>
public sealed class DrawingsDecideTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public DrawingsDecideTests(MesApiFactory fx) => _fx = fx;

    private async Task<HttpClient> ClientAsync(string username, string role, string? department)
    {
        await _fx.SeedUserAsync(username, "P@ss!", role, department: department);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, username, "P@ss!");
        return client;
    }

    private async Task<(long RevId, long DrawingId, long VersionId)> SeedDrawingWithVersionAsync(
        string code, DrawingVersionStatus versionStatus = DrawingVersionStatus.Draft)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var customer = new Customer { Code = $"CUST-DEC-{code}", Name = $"Customer DEC {code}" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var product = new Product { ProductCode = code, Name = code, CustomerId = customer.Id };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        var rev = new ProductRevision
        {
            ProductId = product.Id,
            SpecCode = code,
            Title = $"Title {code}",
            RevisionCode = "A",
            Status = ProductRevisionStatus.Draft,
            Print = new SpecPrint { ProcessCode = "SILKSCREEN", NumColors = 0 },
        };
        db.ProductRevisions.Add(rev);
        await db.SaveChangesAsync();
        var drawing = new Drawing
        {
            ProductRevisionId = rev.Id,
            Kind = DrawingKind.CustomerDrawing,
            Title = "CustomerDrawing",
            Status = DrawingStatus.Draft,
        };
        db.Drawings.Add(drawing);
        await db.SaveChangesAsync();
        var version = new DrawingVersion
        {
            DrawingId = drawing.Id,
            VersionNo = 1,
            FileName = "v1.pdf",
            StorageKey = $"drawings/{rev.Id}/{drawing.Id}/v1_deadbeef.pdf",
            FileHash = "deadbeef" + new string('0', 56),
            FileSize = 1024,
            Status = versionStatus,
            UploadedAt = DateTime.UtcNow,
            UploadedBy = "seed",
        };
        db.DrawingVersions.Add(version);
        await db.SaveChangesAsync();
        // Seed 3 Pending approvals (legacy upload would seed these too).
        foreach (var role in new[] { DrawingApprovalRole.Npi, DrawingApprovalRole.Production, DrawingApprovalRole.Qc })
        {
            db.DrawingApprovals.Add(new DrawingApproval
            {
                DrawingVersionId = version.Id,
                Role = role,
                Status = DrawingApprovalStatus.Pending,
            });
        }
        await db.SaveChangesAsync();
        drawing.CurrentVersionId = version.Id;
        await db.SaveChangesAsync();
        return (rev.Id, drawing.Id, version.Id);
    }

    private static DrawingDecideRequest Approve(string role, string? comment = null) =>
        new() { Role = role, Decision = "Approved", Comment = comment };

    private static DrawingDecideRequest Reject(string role, string? comment) =>
        new() { Role = role, Decision = "Rejected", Comment = comment };

    // ── Auth + JWT department claim ─────────────────────────────────

    [Fact]
    public async Task Anonymous_returns_401()
    {
        var anon = _fx.CreateClient();
        var resp = await anon.PostAsJsonAsync("/api/v2/specs/1/drawings/1/decide", Approve("Npi"));
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    // ── CanActAs truth table — Admin override ───────────────────────

    [Fact]
    public async Task Admin_can_act_on_any_chip()
    {
        var client = await ClientAsync("admin-dec", UserRole.Admin, department: null);
        var (revId, _, vId) = await SeedDrawingWithVersionAsync("PRD-ADM");

        foreach (var role in new[] { "Npi", "Production", "Qc" })
        {
            var resp = await client.PostAsJsonAsync(
                $"/api/v2/specs/{revId}/drawings/{vId}/decide", Approve(role));
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }
    }

    // ── Engineer + Department gates ─────────────────────────────────

    [Fact]
    public async Task Engineer_npi_can_act_only_on_Npi_chip()
    {
        var client = await ClientAsync("eng-npi", UserRole.Engineer, department: "npi");
        var (revId, _, vId) = await SeedDrawingWithVersionAsync("PRD-NPI");

        var ok = await client.PostAsJsonAsync(
            $"/api/v2/specs/{revId}/drawings/{vId}/decide", Approve("Npi"));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var forbidden1 = await client.PostAsJsonAsync(
            $"/api/v2/specs/{revId}/drawings/{vId}/decide", Approve("Production"));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden1.StatusCode);
        var err = await forbidden1.Content.ReadFromJsonAsync<DrawingMutationError>();
        Assert.Equal("drawing.department_mismatch", err!.Code);

        var forbidden2 = await client.PostAsJsonAsync(
            $"/api/v2/specs/{revId}/drawings/{vId}/decide", Approve("Qc"));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden2.StatusCode);
    }

    [Fact]
    public async Task Supervisor_can_act_on_Production_chip_only()
    {
        var client = await ClientAsync("sup-prod", UserRole.Supervisor, department: null);
        var (revId, _, vId) = await SeedDrawingWithVersionAsync("PRD-SUP");

        var ok = await client.PostAsJsonAsync(
            $"/api/v2/specs/{revId}/drawings/{vId}/decide", Approve("Production"));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var fail = await client.PostAsJsonAsync(
            $"/api/v2/specs/{revId}/drawings/{vId}/decide", Approve("Npi"));
        Assert.Equal(HttpStatusCode.Forbidden, fail.StatusCode);
    }

    [Fact]
    public async Task Engineer_qc_cannot_act_on_Production_chip()
    {
        var client = await ClientAsync("eng-qc-prod", UserRole.Engineer, department: "qc");
        var (revId, _, vId) = await SeedDrawingWithVersionAsync("PRD-QC-PROD");

        var fail = await client.PostAsJsonAsync(
            $"/api/v2/specs/{revId}/drawings/{vId}/decide", Approve("Production"));
        Assert.Equal(HttpStatusCode.Forbidden, fail.StatusCode);
    }

    // ── Comment-required-on-Reject server guard ─────────────────────

    [Fact]
    public async Task Reject_without_comment_returns_422_comment_required()
    {
        var client = await ClientAsync("admin-rej", UserRole.Admin, department: null);
        var (revId, _, vId) = await SeedDrawingWithVersionAsync("PRD-REJ-NOCOMM");

        var resp = await client.PostAsJsonAsync(
            $"/api/v2/specs/{revId}/drawings/{vId}/decide", Reject("Npi", comment: null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<DrawingMutationError>();
        Assert.Equal("drawing.comment_required", err!.Code);
    }

    [Fact]
    public async Task Reject_with_whitespace_only_comment_also_rejects()
    {
        var client = await ClientAsync("admin-rej-ws", UserRole.Admin, department: null);
        var (revId, _, vId) = await SeedDrawingWithVersionAsync("PRD-REJ-WS");

        var resp = await client.PostAsJsonAsync(
            $"/api/v2/specs/{revId}/drawings/{vId}/decide", Reject("Npi", comment: "   "));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<DrawingMutationError>();
        Assert.Equal("drawing.comment_required", err!.Code);
    }

    [Fact]
    public async Task Reject_with_real_comment_succeeds_and_sets_version_to_Rejected()
    {
        var client = await ClientAsync("admin-rej-ok", UserRole.Admin, department: null);
        var (revId, _, vId) = await SeedDrawingWithVersionAsync("PRD-REJ-OK");

        var resp = await client.PostAsJsonAsync(
            $"/api/v2/specs/{revId}/drawings/{vId}/decide",
            Reject("Npi", comment: "Sai layout — bố cục lệch 2 mm."));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<DrawingDecideResponse>();
        Assert.NotNull(body);
        Assert.Equal(SharedDrawingVersionStatus.Rejected, body!.VersionStatus);
    }

    // ── State transitions: 3× Approved → version Approved ─────────

    [Fact]
    public async Task Three_role_Approved_transitions_version_to_Approved_and_drawing_to_Approved()
    {
        var client = await ClientAsync("admin-full", UserRole.Admin, department: null);
        var (revId, drawingId, vId) = await SeedDrawingWithVersionAsync("PRD-FULL");

        const string deviceId = "0193abcd-0000-0000-0000-decidefull0001";
        client.DefaultRequestHeaders.Add("X-Device-Id", deviceId);

        // Decide 1: Npi → PendingApproval
        var r1 = await client.PostAsJsonAsync(
            $"/api/v2/specs/{revId}/drawings/{vId}/decide", Approve("Npi"));
        var b1 = await r1.Content.ReadFromJsonAsync<DrawingDecideResponse>();
        Assert.Equal(SharedDrawingVersionStatus.PendingApproval, b1!.VersionStatus);

        // Decide 2: Production → still PendingApproval
        var r2 = await client.PostAsJsonAsync(
            $"/api/v2/specs/{revId}/drawings/{vId}/decide", Approve("Production"));
        var b2 = await r2.Content.ReadFromJsonAsync<DrawingDecideResponse>();
        Assert.Equal(SharedDrawingVersionStatus.PendingApproval, b2!.VersionStatus);

        // Decide 3: Qc → Approved
        var r3 = await client.PostAsJsonAsync(
            $"/api/v2/specs/{revId}/drawings/{vId}/decide", Approve("Qc"));
        var b3 = await r3.Content.ReadFromJsonAsync<DrawingDecideResponse>();
        Assert.Equal(SharedDrawingVersionStatus.Approved, b3!.VersionStatus);
        Assert.Equal(SharedDrawingSlotStatus.Approved, b3.DrawingStatus);
        Assert.Equal(0, b3.SupersededCount); // no older siblings yet

        // DRAWING_DECIDE audit row for each (3 total) + DRAWING_DECIDE_DEVICE.
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var legacyAudits = await db.AuditLogs
            .Where(a => a.Action == "DRAWING_DECIDE")
            .ToListAsync();
        Assert.Equal(3, legacyAudits.Count(a => a.Detail != null && a.Detail.Contains($"\"version_id\":{vId}")));
        var deviceAudits = await db.AuditLogs
            .Where(a => a.Action == "DRAWING_DECIDE_DEVICE" && a.TargetId == deviceId)
            .ToListAsync();
        Assert.Equal(3, deviceAudits.Count);
    }

    [Fact]
    public async Task Approving_v2_supersedes_v1_via_legacy_sweep()
    {
        var client = await ClientAsync("admin-supersede", UserRole.Admin, department: null);
        var (revId, drawingId, v1Id) = await SeedDrawingWithVersionAsync("PRD-SUP-SWEEP");

        // Make v1 Approved end-to-end so the parent Drawing.Status =
        // Approved + the sweep target list isn't empty when v2 lands.
        foreach (var role in new[] { "Npi", "Production", "Qc" })
        {
            await client.PostAsJsonAsync(
                $"/api/v2/specs/{revId}/drawings/{v1Id}/decide", Approve(role));
        }

        // Seed v2 manually (test fixture — bypass upload).
        long v2Id;
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
            var v2 = new DrawingVersion
            {
                DrawingId = drawingId,
                VersionNo = 2,
                FileName = "v2.pdf",
                StorageKey = $"drawings/{revId}/{drawingId}/v2_aaaabbbb.pdf",
                FileHash = "aaaabbbb" + new string('0', 56),
                FileSize = 2048,
                Status = DrawingVersionStatus.Draft,
                UploadedAt = DateTime.UtcNow,
                UploadedBy = "seed",
            };
            db.DrawingVersions.Add(v2);
            await db.SaveChangesAsync();
            foreach (var role in new[] { DrawingApprovalRole.Npi, DrawingApprovalRole.Production, DrawingApprovalRole.Qc })
            {
                db.DrawingApprovals.Add(new DrawingApproval
                {
                    DrawingVersionId = v2.Id,
                    Role = role,
                    Status = DrawingApprovalStatus.Pending,
                });
            }
            await db.SaveChangesAsync();
            v2Id = v2.Id;
        }

        foreach (var role in new[] { "Npi", "Production", "Qc" })
        {
            await client.PostAsJsonAsync(
                $"/api/v2/specs/{revId}/drawings/{v2Id}/decide", Approve(role));
        }

        // v1 must now be Superseded; supersede sweep increments
        // SupersededCount on the last (3rd) decide call.
        using var verifyScope = _fx.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<MesDbContext>();
        var v1Reloaded = await verifyDb.DrawingVersions.AsNoTracking().FirstAsync(x => x.Id == v1Id);
        var v2Reloaded = await verifyDb.DrawingVersions.AsNoTracking().FirstAsync(x => x.Id == v2Id);
        Assert.Equal(DrawingVersionStatus.Superseded, v1Reloaded.Status);
        Assert.Equal(DrawingVersionStatus.Approved, v2Reloaded.Status);

        var supersedeAudits = await verifyDb.AuditLogs
            .Where(a => a.Action == "DRAWING_SUPERSEDE")
            .ToListAsync();
        Assert.NotEmpty(supersedeAudits);
        Assert.Contains(supersedeAudits, a => a.Detail != null && a.Detail.Contains($"\"superseded_version_id\":{v1Id}"));
    }

    [Fact]
    public async Task Cannot_decide_on_Superseded_version_returns_422_invalid_state()
    {
        var client = await ClientAsync("admin-locked", UserRole.Admin, department: null);
        var (revId, _, vId) = await SeedDrawingWithVersionAsync(
            "PRD-LOCKED", versionStatus: DrawingVersionStatus.Superseded);

        var resp = await client.PostAsJsonAsync(
            $"/api/v2/specs/{revId}/drawings/{vId}/decide", Approve("Npi"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<DrawingMutationError>();
        Assert.Equal("drawing.invalid_state", err!.Code);
    }

    [Fact]
    public async Task Wrong_revision_returns_404_not_found()
    {
        var client = await ClientAsync("admin-wrong-rev", UserRole.Admin, department: null);
        var (revAId, _, vAId) = await SeedDrawingWithVersionAsync("PRD-A");
        var (revBId, _, _) = await SeedDrawingWithVersionAsync("PRD-B");

        var resp = await client.PostAsJsonAsync(
            $"/api/v2/specs/{revBId}/drawings/{vAId}/decide", Approve("Npi"));
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<DrawingMutationError>();
        Assert.Equal("drawing.not_found", err!.Code);
    }

    [Fact]
    public async Task Unknown_role_returns_422_invalid_role()
    {
        var client = await ClientAsync("admin-badrole", UserRole.Admin, department: null);
        var (revId, _, vId) = await SeedDrawingWithVersionAsync("PRD-BADROLE");

        var resp = await client.PostAsJsonAsync(
            $"/api/v2/specs/{revId}/drawings/{vId}/decide",
            new DrawingDecideRequest { Role = "Lead", Decision = "Approved" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<DrawingMutationError>();
        Assert.Equal("drawing.invalid_role", err!.Code);
    }

    [Fact]
    public async Task Unknown_decision_returns_422_invalid_decision()
    {
        var client = await ClientAsync("admin-baddec", UserRole.Admin, department: null);
        var (revId, _, vId) = await SeedDrawingWithVersionAsync("PRD-BADDEC");

        var resp = await client.PostAsJsonAsync(
            $"/api/v2/specs/{revId}/drawings/{vId}/decide",
            new DrawingDecideRequest { Role = "Npi", Decision = "Maybe" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<DrawingMutationError>();
        Assert.Equal("drawing.invalid_decision", err!.Code);
    }
}
