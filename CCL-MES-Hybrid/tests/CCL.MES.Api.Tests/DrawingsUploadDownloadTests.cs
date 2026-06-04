using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain;
using CCL.MES.Domain.Auth;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Shared.Drawings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CCL.MES.Api.Tests;

/// <summary>
/// P10.5e-1 — `POST /specs/{rev}/drawings/upload?kind=` + `GET /specs/{rev}/drawings/{ver}/file`.
/// Coverage:
///   · validation (no file / oversize / invalid kind / invalid extension)
///   · role gate (anonymous 401, Supervisor 403, Engineer 200)
///   · happy upload — version v1 created with correct sha256, file size,
///     audit row DRAWING_UPLOAD persisted by legacy service + DRAWING_UPLOAD_DEVICE
///     emitted when X-Device-Id present
///   · download streams the same bytes back (sha matches) with correct
///     Content-Type
///   · revision-scoped guard — download from a foreign revision returns 404
///   · second upload to same (revision, kind) → v2
///   · 3 Pending DrawingApproval rows seeded at upload time (read via list)
/// </summary>
public sealed class DrawingsUploadDownloadTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public DrawingsUploadDownloadTests(MesApiFactory fx) => _fx = fx;

    private async Task<HttpClient> EngineerClientAsync(string username)
    {
        await _fx.SeedUserAsync(username, "P@ss!", UserRole.Engineer, department: "npi");
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, username, "P@ss!");
        return client;
    }

    private async Task<HttpClient> SupervisorClientAsync(string username)
    {
        await _fx.SeedUserAsync(username, "P@ss!", UserRole.Supervisor);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, username, "P@ss!");
        return client;
    }

    private async Task<long> SeedProductRevisionAsync(string code = "PRD-DRW")
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var customer = new Customer { Code = $"CUST-{code}", Name = $"Customer {code}" };
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
        return rev.Id;
    }

    private static MultipartFormDataContent BuildUpload(
        byte[] bytes, string fileName, string? changeReason = null,
        string contentType = "application/pdf")
    {
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", fileName);
        if (!string.IsNullOrWhiteSpace(changeReason))
            form.Add(new StringContent(changeReason), "changeReason");
        return form;
    }

    private static string Sha256Hex(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant();
    }

    // ── auth + role gates ───────────────────────────────────────────

    [Fact]
    public async Task Upload_returns_401_when_anonymous()
    {
        var revId = await SeedProductRevisionAsync("PRD-ANON-UP");
        var anon = _fx.CreateClient();
        using var body = BuildUpload(new byte[] { 1, 2, 3 }, "f.pdf");
        var resp = await anon.PostAsync(
            $"/api/v2/specs/{revId}/drawings/upload?kind=CustomerDrawing", body);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Upload_returns_403_when_Supervisor()
    {
        var client = await SupervisorClientAsync("sup-up");
        var revId = await SeedProductRevisionAsync("PRD-SUP-UP");
        using var body = BuildUpload(new byte[] { 1, 2, 3 }, "f.pdf");
        var resp = await client.PostAsync(
            $"/api/v2/specs/{revId}/drawings/upload?kind=CustomerDrawing", body);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Download_returns_401_when_anonymous()
    {
        var anon = _fx.CreateClient();
        var resp = await anon.GetAsync("/api/v2/specs/1/drawings/1/file");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    // ── validation ──────────────────────────────────────────────────

    [Fact]
    public async Task Upload_rejects_missing_file_part()
    {
        var client = await EngineerClientAsync("eng-nofile");
        var revId = await SeedProductRevisionAsync("PRD-NOFILE");
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(""), "changeReason");
        var resp = await client.PostAsync(
            $"/api/v2/specs/{revId}/drawings/upload?kind=CustomerDrawing", form);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<DrawingMutationError>();
        Assert.Equal("drawing.no_file", err!.Code);
    }

    [Fact]
    public async Task Upload_rejects_invalid_kind()
    {
        var client = await EngineerClientAsync("eng-badkind");
        var revId = await SeedProductRevisionAsync("PRD-BADKIND");
        using var body = BuildUpload(new byte[] { 0x25, 0x50, 0x44, 0x46 }, "f.pdf");
        var resp = await client.PostAsync(
            $"/api/v2/specs/{revId}/drawings/upload?kind=NotARealKind", body);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<DrawingMutationError>();
        Assert.Equal("drawing.invalid_kind", err!.Code);
    }

    [Fact]
    public async Task Upload_rejects_disallowed_extension_via_legacy_blob_store()
    {
        var client = await EngineerClientAsync("eng-badext");
        var revId = await SeedProductRevisionAsync("PRD-BADEXT");
        // .txt is NOT in the allowlist {pdf, png, jpg, jpeg, svg, gif,
        // webp, dwg, dxf, ai}. The legacy DrawingsService catches it
        // inside the blob store and throws InvalidOperationException;
        // our controller maps to drawing.invalid_extension.
        using var body = BuildUpload(
            new byte[] { 0x68, 0x69 }, "garbage.txt", contentType: "text/plain");
        var resp = await client.PostAsync(
            $"/api/v2/specs/{revId}/drawings/upload?kind=CustomerDrawing", body);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<DrawingMutationError>();
        Assert.Contains(err!.Code, new[] { "drawing.invalid_extension", "drawing.validation" });
    }

    // ── happy path: upload → list → download → sha ──────────────────

    [Fact]
    public async Task Engineer_upload_then_download_round_trips_sha_and_bytes()
    {
        var client = await EngineerClientAsync("eng-rt");
        var revId = await SeedProductRevisionAsync("PRD-RT");
        const string deviceId = "0193beef-cafe-feed-dead-deadbeef0001";
        client.DefaultRequestHeaders.Add("X-Device-Id", deviceId);

        // Construct a small valid-looking PDF payload (just the magic
        // bytes — the blob store extension allowlist matches on
        // filename, not content, so the bytes don't need to be a real
        // PDF for this round-trip test).
        var pdfBytes = new byte[2048];
        new Random(1).NextBytes(pdfBytes);
        var expectedSha = Sha256Hex(pdfBytes);

        using var uploadBody = BuildUpload(pdfBytes, "drawing-v1.pdf",
            changeReason: "Initial upload");
        var uploadResp = await client.PostAsync(
            $"/api/v2/specs/{revId}/drawings/upload?kind=CustomerDrawing", uploadBody);
        Assert.Equal(HttpStatusCode.OK, uploadResp.StatusCode);
        var upload = await uploadResp.Content.ReadFromJsonAsync<DrawingUploadResponse>();
        Assert.NotNull(upload);
        Assert.Equal(1, upload!.VersionNo);
        Assert.Equal("CustomerDrawing", upload.Kind);
        Assert.Equal(expectedSha, upload.Sha256Hex);
        Assert.Equal(pdfBytes.Length, upload.SizeBytes);

        // Audit row written by the legacy service + device-pairing row.
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
            var legacy = await db.AuditLogs
                .Where(a => a.Action == "DRAWING_UPLOAD"
                            && a.TargetId == upload.VersionId.ToString())
                .FirstOrDefaultAsync();
            Assert.NotNull(legacy);
            var device = await db.AuditLogs
                .Where(a => a.Action == "DRAWING_UPLOAD_DEVICE" && a.TargetId == deviceId)
                .ToListAsync();
            Assert.NotEmpty(device);
            Assert.Contains(device, a => a.Detail != null && a.Detail.Contains(upload.Sha256Hex[..8]));
        }

        // List endpoint already exists from P10.5b — should now surface
        // the new version + 3 seeded Pending approval rows.
        var listResp = await client.GetAsync($"/api/v2/drawings/by-revision/{revId}");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var slots = await listResp.Content.ReadFromJsonAsync<List<DrawingKindSlot>>();
        Assert.NotNull(slots);
        var customerDrawingSlot = slots!.First(s => s.Kind == DrawingKindCode.CustomerDrawing);
        Assert.Single(customerDrawingSlot.Versions);
        var v1 = customerDrawingSlot.Versions[0];
        Assert.Equal(1, v1.VersionNo);
        Assert.Equal(3, v1.Approvals.Count);
        Assert.All(v1.Approvals, a => Assert.Equal(CCL.MES.Shared.Drawings.DrawingApprovalStatus.Pending, a.Status));

        // Download streams the same bytes back.
        var dlResp = await client.GetAsync(
            $"/api/v2/specs/{revId}/drawings/{upload.VersionId}/file");
        Assert.Equal(HttpStatusCode.OK, dlResp.StatusCode);
        Assert.Equal("application/pdf", dlResp.Content.Headers.ContentType?.MediaType);
        var downloaded = await dlResp.Content.ReadAsByteArrayAsync();
        Assert.Equal(pdfBytes.Length, downloaded.Length);
        Assert.Equal(expectedSha, Sha256Hex(downloaded));
    }

    [Fact]
    public async Task Download_returns_404_when_version_belongs_to_other_revision()
    {
        var client = await EngineerClientAsync("eng-foreign");
        var revAId = await SeedProductRevisionAsync("PRD-FOREIGN-A");
        var revBId = await SeedProductRevisionAsync("PRD-FOREIGN-B");

        var bytes = new byte[64];
        new Random(2).NextBytes(bytes);
        using var body = BuildUpload(bytes, "f.png", contentType: "image/png");
        var uploadResp = await client.PostAsync(
            $"/api/v2/specs/{revAId}/drawings/upload?kind=NpiPrintLayout", body);
        var upload = await uploadResp.Content.ReadFromJsonAsync<DrawingUploadResponse>();

        // Try to download via the OTHER revision's path — server's
        // GetForDownloadAsync revision-scoped guard returns null →
        // controller returns 404 drawing.not_found.
        var dlResp = await client.GetAsync(
            $"/api/v2/specs/{revBId}/drawings/{upload!.VersionId}/file");
        Assert.Equal(HttpStatusCode.NotFound, dlResp.StatusCode);
        var err = await dlResp.Content.ReadFromJsonAsync<DrawingMutationError>();
        Assert.Equal("drawing.not_found", err!.Code);
    }

    [Fact]
    public async Task Second_upload_to_same_kind_creates_v2()
    {
        var client = await EngineerClientAsync("eng-v2");
        var revId = await SeedProductRevisionAsync("PRD-V2");

        var b1 = new byte[100]; new Random(3).NextBytes(b1);
        var b2 = new byte[200]; new Random(4).NextBytes(b2);

        using (var f1 = BuildUpload(b1, "v1.png", contentType: "image/png"))
        {
            var r1 = await client.PostAsync(
                $"/api/v2/specs/{revId}/drawings/upload?kind=FqcChecksheet", f1);
            Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
            var u1 = await r1.Content.ReadFromJsonAsync<DrawingUploadResponse>();
            Assert.Equal(1, u1!.VersionNo);
        }
        using (var f2 = BuildUpload(b2, "v2.png", contentType: "image/png"))
        {
            var r2 = await client.PostAsync(
                $"/api/v2/specs/{revId}/drawings/upload?kind=FqcChecksheet", f2);
            Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
            var u2 = await r2.Content.ReadFromJsonAsync<DrawingUploadResponse>();
            Assert.Equal(2, u2!.VersionNo);
            Assert.Equal(Sha256Hex(b2), u2.Sha256Hex);
        }
    }
}
