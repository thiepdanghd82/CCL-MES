using System.Net;
using System.Net.Http.Headers;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain;
using CCL.MES.Domain.Auth;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CCL.MES.Api.Tests;

/// <summary>
/// P10.5g — Integration tests for the new Hybrid Spec export surface:
///   - <c>GET /api/v2/specs/export/csv|xlsx|pdf</c>
///   - <c>GET /api/v2/specs/export/{revisionId}/sheet/pdf</c>
///
/// Three things matter and only the controller can prove them — the
/// pure-helper tests cover the filename + filter-description compose
/// path separately.
///
/// 1. <b>RBAC</b> — every endpoint is gated by the <c>NpiSpecRead</c>
///    policy (Admin / Supervisor / Engineer). QC / unauthenticated
///    callers must see 403 / 401 even when the URL is otherwise valid.
/// 2. <b>Content-Type + body shape</b> — CSV must lead with the UTF-8
///    BOM (<c>EF BB BF</c>) so Vietnamese ×/× characters survive Excel
///    locale autodetect; XLSX must lead with the ZIP magic
///    (<c>PK\x03\x04</c>); PDF must lead with <c>%PDF-</c>.
/// 3. <b>Audit emit</b> — every success path appends a
///    <c>SPEC_EXPORT</c> row; <c>X-Device-Id</c> presence appends a
///    paired <c>SPEC_EXPORT_DEVICE</c> row (W4 pattern). Absent header
///    means the device row stays absent.
/// </summary>
public sealed class SpecsExportTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public SpecsExportTests(MesApiFactory fx) => _fx = fx;

    private async Task<long> SeedOneAsync(string codeSuffix)
    {
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var customer = new Customer { Code = $"CUST-EXP-{codeSuffix}", Name = $"Export Test Customer {codeSuffix}" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var product = new Product { ProductCode = $"PRD-EXP-{codeSuffix}", Name = $"Product Export {codeSuffix}", CustomerId = customer.Id };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        var rev = new ProductRevision
        {
            ProductId = product.Id,
            SpecCode = $"SPEC-EXP-{codeSuffix}",
            Title = $"Title {codeSuffix}",
            RevisionCode = "A",
            Status = ProductRevisionStatus.Draft,
            Print = new SpecPrint { ProcessCode = "FLEXO", NumColors = 4 },
        };
        db.ProductRevisions.Add(rev);
        await db.SaveChangesAsync();
        return rev.Id;
    }

    private async Task<HttpClient> EngineerClientAsync(string username)
    {
        await _fx.SeedUserAsync(username, "P@ss!", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, username, "P@ss!");
        return client;
    }

    private async Task<HttpClient> QcClientAsync(string username)
    {
        await _fx.SeedUserAsync(username, "P@ss!", UserRole.Qc);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, username, "P@ss!");
        return client;
    }

    // ── List exports ────────────────────────────────────────────────

    [Fact]
    public async Task Csv_export_returns_utf8_bom_and_csv_content_type()
    {
        await SeedOneAsync("csv");
        var client = await EngineerClientAsync("eng-exp-csv");

        var resp = await client.GetAsync("/api/v2/specs/export/csv?view=Active");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.StartsWith("text/csv", resp.Content.Headers.ContentType?.MediaType);

        var bytes = await resp.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 3, "CSV body must be larger than the BOM itself.");
        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);
    }

    [Fact]
    public async Task Xlsx_export_returns_zip_magic_and_xlsx_content_type()
    {
        await SeedOneAsync("xlsx");
        var client = await EngineerClientAsync("eng-exp-xlsx");

        var resp = await client.GetAsync("/api/v2/specs/export/xlsx?view=Active");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            resp.Content.Headers.ContentType?.MediaType);

        var bytes = await resp.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 4);
        Assert.Equal((byte)'P', bytes[0]);
        Assert.Equal((byte)'K', bytes[1]);
        Assert.Equal(0x03, bytes[2]);
        Assert.Equal(0x04, bytes[3]);
    }

    [Fact]
    public async Task Pdf_export_returns_pdf_header_and_pdf_content_type()
    {
        await SeedOneAsync("pdf");
        var client = await EngineerClientAsync("eng-exp-pdf");

        var resp = await client.GetAsync("/api/v2/specs/export/pdf?view=Active");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("application/pdf", resp.Content.Headers.ContentType?.MediaType);

        var bytes = await resp.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 5);
        Assert.Equal((byte)'%', bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'D', bytes[2]);
        Assert.Equal((byte)'F', bytes[3]);
        Assert.Equal((byte)'-', bytes[4]);
    }

    [Fact]
    public async Task Filename_carries_NpiSpecLibrary_prefix_and_extension()
    {
        await SeedOneAsync("fn");
        var client = await EngineerClientAsync("eng-exp-fn");

        var resp = await client.GetAsync("/api/v2/specs/export/xlsx?view=Active");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var cd = resp.Content.Headers.ContentDisposition;
        Assert.NotNull(cd);
        var filename = cd!.FileNameStar ?? cd.FileName;
        Assert.NotNull(filename);
        // The filename comes back quoted from ASP.NET — strip both layers
        // (RFC 5987 percent-encoding + quoted-string wrap) before asserting.
        var unquoted = filename!.Trim('"');
        Assert.StartsWith("NpiSpecLibrary_", unquoted);
        Assert.EndsWith(".xlsx", unquoted);
    }

    [Fact]
    public async Task Sheet_pdf_returns_pdf_with_SpecSheet_filename()
    {
        var revId = await SeedOneAsync("sheet");
        var client = await EngineerClientAsync("eng-exp-sheet");

        var resp = await client.GetAsync($"/api/v2/specs/export/{revId}/sheet/pdf");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("application/pdf", resp.Content.Headers.ContentType?.MediaType);

        var cd = resp.Content.Headers.ContentDisposition;
        Assert.NotNull(cd);
        var filename = (cd!.FileNameStar ?? cd.FileName)!.Trim('"');
        Assert.StartsWith("SpecSheet_", filename);
        Assert.EndsWith(".pdf", filename);

        var bytes = await resp.Content.ReadAsByteArrayAsync();
        Assert.Equal((byte)'%', bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
    }

    [Fact]
    public async Task Sheet_pdf_returns_404_for_unknown_revision()
    {
        var client = await EngineerClientAsync("eng-exp-404");

        var resp = await client.GetAsync("/api/v2/specs/export/999999999/sheet/pdf");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ── RBAC ────────────────────────────────────────────────────────

    [Fact]
    public async Task Qc_role_is_denied_on_list_export()
    {
        await SeedOneAsync("qcd");
        var client = await QcClientAsync("qc-exp-deny");

        var resp = await client.GetAsync("/api/v2/specs/export/csv?view=Active");
        // RBAC NpiSpecRead = Admin/Supervisor/Engineer. QC role gets 403.
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_call_is_denied_on_sheet_pdf()
    {
        var revId = await SeedOneAsync("anon");
        var client = _fx.CreateClient();

        var resp = await client.GetAsync($"/api/v2/specs/export/{revId}/sheet/pdf");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    // ── Audit emit + device pairing ─────────────────────────────────

    [Fact]
    public async Task Audit_emits_SPEC_EXPORT_on_successful_list_call()
    {
        await SeedOneAsync("audit-list");
        var client = await EngineerClientAsync("eng-exp-audit");

        var resp = await client.GetAsync("/api/v2/specs/export/csv?view=Active&planner=FLEXO");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var row = await db.AuditLogs
            .Where(a => a.Action == "SPEC_EXPORT" && a.ActorUsername == "eng-exp-audit")
            .OrderByDescending(a => a.Timestamp)
            .FirstOrDefaultAsync();
        Assert.NotNull(row);
        Assert.Contains("csv", row!.Detail ?? "");
        Assert.Contains("FLEXO", row.Detail ?? "");
    }

    [Fact]
    public async Task X_device_id_header_emits_paired_SPEC_EXPORT_DEVICE_row()
    {
        await SeedOneAsync("audit-dev");
        var client = await EngineerClientAsync("eng-exp-device");

        var msg = new HttpRequestMessage(HttpMethod.Get, "/api/v2/specs/export/csv?view=Active");
        msg.Headers.Add("X-Device-Id", "DEV-EXPORT-PAIR");
        var resp = await client.SendAsync(msg);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var paired = await db.AuditLogs
            .Where(a => a.Action == "SPEC_EXPORT_DEVICE" && a.TargetId == "DEV-EXPORT-PAIR")
            .OrderByDescending(a => a.Timestamp)
            .FirstOrDefaultAsync();
        Assert.NotNull(paired);
        Assert.Contains("csv", paired!.Detail ?? "");
    }

    [Fact]
    public async Task Missing_device_header_does_not_emit_device_audit_row()
    {
        await SeedOneAsync("audit-no-dev");
        var client = await EngineerClientAsync("eng-exp-no-device");

        var resp = await client.GetAsync("/api/v2/specs/export/csv?view=Active");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var paired = await db.AuditLogs
            .Where(a => a.Action == "SPEC_EXPORT_DEVICE" && a.ActorUsername == "eng-exp-no-device")
            .FirstOrDefaultAsync();
        Assert.Null(paired);
    }
}
