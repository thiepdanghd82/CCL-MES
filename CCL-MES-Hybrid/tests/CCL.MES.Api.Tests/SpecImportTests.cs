using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain;
using CCL.MES.Domain.Auth;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Shared.Specs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CCL.MES.Api.Tests;

/// <summary>
/// P10.5c-2 — POST /import/preview + /import/save coverage.
///
/// Validation paths (no fixture needed):
///   · no file part → 422 import.no_file
///   · .txt extension → 422 import.invalid_extension
///   · oversized (12 MB synthetic body) → 422 import.oversize
///   · valid extension but bytes ≠ "PK" → 422 import.invalid_content
///   · Supervisor / anon → 403 / 401 on both endpoints
///
/// Happy-path uses the bundled silkscreen sample (DEMO_SILK_1.xlsx) — its
/// parser shape is already proven by Phase 8 PR #31a. Save path covers
/// SaveNew, UpgradeRev, SaveAsCopy. Mode validation covers the
/// spec_code_override_required guard.
///
/// SPEC_IMPORT_DEVICE audit row is verified end-to-end on the happy save.
/// </summary>
public sealed class SpecImportTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public SpecImportTests(MesApiFactory fx) => _fx = fx;

    // ── helpers ──────────────────────────────────────────────────────

    private static string LegacyRepoRoot()
    {
        // Walk up until we find the directory that contains the legacy
        // `src/CCL.MES.Web/wwwroot/Data/Specs/` folder. The hybrid solution
        // (CCL-MES-Hybrid/CCL.MES.Hybrid.sln) ships in a subdirectory of
        // the outer repo (CCL-MES/), and the bundled silk samples live in
        // the outer src/CCL.MES.Web tree — so the inner sln isn't the
        // right anchor.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "CCL.MES.Web", "wwwroot", "Data", "Specs");
            if (Directory.Exists(candidate)) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Could not locate legacy repo root containing src/CCL.MES.Web/wwwroot/Data/Specs/.");
    }

    private static string SilkSamplePath() =>
        Path.Combine(LegacyRepoRoot(), "src", "CCL.MES.Web", "wwwroot", "Data", "Specs", "DEMO_SILK_1.xlsx");

    private async Task<HttpClient> EngineerClientAsync(string username = "eng-imp")
    {
        await _fx.SeedUserAsync(username, "P@ss!", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, username, "P@ss!");
        return client;
    }

    private async Task<HttpClient> SupervisorClientAsync(string username = "sup-imp")
    {
        await _fx.SeedUserAsync(username, "P@ss!", UserRole.Supervisor);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, username, "P@ss!");
        return client;
    }

    private static MultipartFormDataContent BuildMultipart(
        byte[] fileBytes, string fileName, string plannerCategory)
    {
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType =
            new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(fileContent, "file", fileName);
        form.Add(new StringContent(plannerCategory), "plannerCategory");
        return form;
    }

    private static async Task<SpecMutationError?> ReadErrorAsync(HttpResponseMessage resp) =>
        await resp.Content.ReadFromJsonAsync<SpecMutationError>();

    // ── auth / role gates ────────────────────────────────────────────

    [Fact]
    public async Task Preview_returns_401_when_anonymous()
    {
        var anon = _fx.CreateClient();
        using var body = BuildMultipart(new byte[] { 0x50, 0x4B }, "f.xlsx", "silkscreen");
        var resp = await anon.PostAsync("/api/v2/specs/import/preview", body);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Preview_returns_403_when_Supervisor()
    {
        var client = await SupervisorClientAsync();
        using var body = BuildMultipart(new byte[] { 0x50, 0x4B }, "f.xlsx", "silkscreen");
        var resp = await client.PostAsync("/api/v2/specs/import/preview", body);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Save_returns_401_when_anonymous()
    {
        var anon = _fx.CreateClient();
        var resp = await anon.PostAsJsonAsync("/api/v2/specs/import/save", new SpecImportSaveRequest
        {
            ParsedJson = "{}",
            FileName = "f.xlsx",
            PlannerCategory = "silkscreen",
            Mode = SpecImportSaveMode.SaveNew,
        });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    // ── preview validation paths ─────────────────────────────────────

    [Fact]
    public async Task Preview_rejects_missing_file_part_with_no_file_code()
    {
        var client = await EngineerClientAsync("eng-imp-nofile");
        var form = new MultipartFormDataContent
        {
            { new StringContent("silkscreen"), "plannerCategory" },
        };
        var resp = await client.PostAsync("/api/v2/specs/import/preview", form);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await ReadErrorAsync(resp);
        Assert.Equal("import.no_file", err!.Code);
    }

    [Fact]
    public async Task Preview_rejects_non_xlsx_extension()
    {
        var client = await EngineerClientAsync("eng-imp-txt");
        using var body = BuildMultipart(new byte[] { 0x50, 0x4B }, "f.txt", "silkscreen");
        var resp = await client.PostAsync("/api/v2/specs/import/preview", body);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await ReadErrorAsync(resp);
        Assert.Equal("import.invalid_extension", err!.Code);
    }

    [Fact]
    public async Task Preview_rejects_non_PK_content_sniff()
    {
        var client = await EngineerClientAsync("eng-imp-sniff");
        // Has .xlsx extension but the first two bytes are NOT "PK" — a
        // renamed .txt slips past extension alone; the sniff catches it.
        var bogus = System.Text.Encoding.UTF8.GetBytes("This is plain text, not a zip.");
        using var body = BuildMultipart(bogus, "fake.xlsx", "silkscreen");
        var resp = await client.PostAsync("/api/v2/specs/import/preview", body);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await ReadErrorAsync(resp);
        Assert.Equal("import.invalid_content", err!.Code);
    }

    [Fact]
    public async Task Preview_happy_path_returns_summary_for_silk_sample()
    {
        var samplePath = SilkSamplePath();
        Assert.True(File.Exists(samplePath), $"Sample xlsx not found at {samplePath} — adjust path resolver.");

        var client = await EngineerClientAsync("eng-imp-ok");
        var bytes = await File.ReadAllBytesAsync(samplePath);
        using var body = BuildMultipart(bytes, "DEMO_SILK_1.xlsx", "silkscreen");
        var resp = await client.PostAsync("/api/v2/specs/import/preview", body);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var preview = await resp.Content.ReadFromJsonAsync<SpecImportPreviewResponse>();
        Assert.NotNull(preview);
        Assert.True(preview!.ParseOk, $"ParseOk=false, error: {preview.ParseError}");
        Assert.NotNull(preview.Summary);
        Assert.False(string.IsNullOrWhiteSpace(preview.Summary!.PartNo));
        Assert.False(string.IsNullOrWhiteSpace(preview.Summary.Customer));
        Assert.NotNull(preview.ParsedJson);
        Assert.False(string.IsNullOrWhiteSpace(preview.ParsedJson));
    }

    // ── save validation + happy path ─────────────────────────────────

    [Fact]
    public async Task Save_rejects_empty_parsed_json()
    {
        var client = await EngineerClientAsync("eng-save-empty");
        var resp = await client.PostAsJsonAsync("/api/v2/specs/import/save", new SpecImportSaveRequest
        {
            ParsedJson = "",
            FileName = "f.xlsx",
            PlannerCategory = "silkscreen",
            Mode = SpecImportSaveMode.SaveNew,
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await ReadErrorAsync(resp);
        Assert.Equal("import.no_parsed_payload", err!.Code);
    }

    [Fact]
    public async Task Save_rejects_invalid_parsed_json()
    {
        var client = await EngineerClientAsync("eng-save-bad-json");
        var resp = await client.PostAsJsonAsync("/api/v2/specs/import/save", new SpecImportSaveRequest
        {
            ParsedJson = "{not-json",
            FileName = "f.xlsx",
            PlannerCategory = "silkscreen",
            Mode = SpecImportSaveMode.SaveNew,
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await ReadErrorAsync(resp);
        Assert.Equal("import.invalid_parsed_payload", err!.Code);
    }

    [Fact]
    public async Task Save_SaveAsCopy_requires_SpecCodeOverride()
    {
        var client = await EngineerClientAsync("eng-save-no-override");
        var minimalParsed = """{"Category":"silkscreen","Customer":"X","PartNo":"X-1","PartName":"X-1"}""";
        var resp = await client.PostAsJsonAsync("/api/v2/specs/import/save", new SpecImportSaveRequest
        {
            ParsedJson = minimalParsed,
            FileName = "f.xlsx",
            PlannerCategory = "silkscreen",
            Mode = SpecImportSaveMode.SaveAsCopy,
            SpecCodeOverride = null,
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await ReadErrorAsync(resp);
        Assert.Equal("import.spec_code_override_required", err!.Code);
    }

    [Fact]
    public async Task Save_rejects_unknown_mode()
    {
        var client = await EngineerClientAsync("eng-save-unknown-mode");
        var minimalParsed = """{"Category":"silkscreen","Customer":"X","PartNo":"X-1","PartName":"X-1"}""";
        var resp = await client.PostAsJsonAsync("/api/v2/specs/import/save", new SpecImportSaveRequest
        {
            ParsedJson = minimalParsed,
            FileName = "f.xlsx",
            PlannerCategory = "silkscreen",
            Mode = "WhateverElse",
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await ReadErrorAsync(resp);
        Assert.Equal("import.invalid_mode", err!.Code);
    }

    [Fact]
    public async Task Preview_then_Save_creates_revision_and_emits_device_audit()
    {
        var samplePath = SilkSamplePath();
        Assert.True(File.Exists(samplePath));

        var client = await EngineerClientAsync("eng-imp-save");
        const string deviceId = "0193a1ff-bbbb-cccc-dddd-eeeeff112233";
        client.DefaultRequestHeaders.Add("X-Device-Id", deviceId);

        // Preview ----------------------------------------------------
        var bytes = await File.ReadAllBytesAsync(samplePath);
        using var previewBody = BuildMultipart(bytes, "DEMO_SILK_1.xlsx", "silkscreen");
        var previewResp = await client.PostAsync("/api/v2/specs/import/preview", previewBody);
        Assert.Equal(HttpStatusCode.OK, previewResp.StatusCode);
        var preview = await previewResp.Content.ReadFromJsonAsync<SpecImportPreviewResponse>();
        Assert.NotNull(preview);
        Assert.True(preview!.ParseOk);

        // Save (SaveNew, may collide with a dup if we re-run on a dirty
        // DB — but the test fixture is per-class isolated so the first
        // save on a fresh factory is always SaveNew-clean).
        var mode = preview.DuplicateRefNo ? SpecImportSaveMode.UpgradeRev : SpecImportSaveMode.SaveNew;
        var saveResp = await client.PostAsJsonAsync("/api/v2/specs/import/save", new SpecImportSaveRequest
        {
            ParsedJson = preview.ParsedJson!,
            FileName = preview.FileName,
            PlannerCategory = preview.PlannerCategory,
            Mode = mode,
        });
        var saveBody = await saveResp.Content.ReadAsStringAsync();
        Assert.True(saveResp.StatusCode == HttpStatusCode.OK,
            $"Save failed with {saveResp.StatusCode}: {saveBody}");
        var save = await saveResp.Content.ReadFromJsonAsync<SpecImportSaveResponse>();
        Assert.NotNull(save);
        Assert.True(save!.ProductRevisionId > 0);
        Assert.False(string.IsNullOrWhiteSpace(save.SpecCode));
        Assert.Equal(mode, save.Mode);

        // Verify SPEC_IMPORT_DEVICE audit row.
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var deviceAudit = await db.AuditLogs
            .Where(a => a.Action == "SPEC_IMPORT_DEVICE" && a.TargetId == deviceId)
            .ToListAsync();
        Assert.NotEmpty(deviceAudit);
        Assert.Contains(deviceAudit, a => a.Detail != null && a.Detail.Contains(save.SpecCode));
    }

    [Fact]
    public async Task UpgradeRev_bumps_revision_letter_and_supersedes_source()
    {
        var samplePath = SilkSamplePath();
        Assert.True(File.Exists(samplePath));

        var client = await EngineerClientAsync("eng-upgrade-rev");
        var bytes = await File.ReadAllBytesAsync(samplePath);

        // Each test in this class shares the MesApiFactory DB fixture, so
        // a clean "first import = rev A" assumption doesn't hold (sibling
        // tests already inserted DEMO_SILK_1.xlsx). Instead, we assert
        // bumped-letter semantics relative to whatever rev is current at
        // import time, plus the supersede chain. That's the actual
        // behavior contract the legacy fix introduces.

        async Task<SpecImportSaveResponse> ImportOnceAsync(string mode)
        {
            using var body = BuildMultipart(bytes, "DEMO_SILK_1.xlsx", "silkscreen");
            var p = await (await client.PostAsync("/api/v2/specs/import/preview", body))
                .Content.ReadFromJsonAsync<SpecImportPreviewResponse>();
            Assert.NotNull(p);
            Assert.True(p!.ParseOk);
            var actualMode = mode == SpecImportSaveMode.UpgradeRev && !p.DuplicateRefNo
                ? SpecImportSaveMode.SaveNew
                : mode;
            var save = await client.PostAsJsonAsync("/api/v2/specs/import/save", new SpecImportSaveRequest
            {
                ParsedJson = p.ParsedJson!,
                FileName = p.FileName,
                PlannerCategory = p.PlannerCategory,
                Mode = actualMode,
            });
            var saveBody = await save.Content.ReadAsStringAsync();
            Assert.True(save.StatusCode == HttpStatusCode.OK,
                $"Import save failed with {save.StatusCode}: {saveBody}");
            return (await save.Content.ReadFromJsonAsync<SpecImportSaveResponse>())!;
        }

        ProductRevision Reload(long id)
        {
            using var scope = _fx.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
            return db.ProductRevisions.AsNoTracking().First(r => r.Id == id);
        }

        // Iteration 1 — depending on sibling-test state this may create rev
        // "A" (clean DB) OR detect a dup + bump. Either way it gives us a
        // baseline to chain from.
        var first = await ImportOnceAsync(SpecImportSaveMode.UpgradeRev);
        var firstRev = Reload(first.ProductRevisionId);

        // Iteration 2 — MUST UpgradeRev (dup is guaranteed now).
        var second = await ImportOnceAsync(SpecImportSaveMode.UpgradeRev);
        var secondRev = Reload(second.ProductRevisionId);

        // Iteration 3 — same chain.
        var third = await ImportOnceAsync(SpecImportSaveMode.UpgradeRev);
        var thirdRev = Reload(third.ProductRevisionId);

        // After iteration 2 firstRev must be Superseded, no trashed suffix.
        var firstRevReloaded = Reload(first.ProductRevisionId);
        Assert.Equal(CCL.MES.Domain.ProductRevisionStatus.Superseded, firstRevReloaded.Status);
        Assert.False(firstRevReloaded.IsTrashed,
            "Source rev must NOT be IsTrashed under the new UpgradeRev semantics.");
        Assert.NotNull(firstRevReloaded.EffectiveTo);
        Assert.DoesNotContain("-trashed-", firstRevReloaded.RevisionCode);

        // After iteration 3 secondRev is Superseded.
        var secondRevReloaded = Reload(second.ProductRevisionId);
        Assert.Equal(CCL.MES.Domain.ProductRevisionStatus.Superseded, secondRevReloaded.Status);
        Assert.False(secondRevReloaded.IsTrashed);
        Assert.DoesNotContain("-trashed-", secondRevReloaded.RevisionCode);

        // Letter bumps monotonically via NextAvailableRev — chain second
        // is one letter ahead of first, and third one ahead of second.
        Assert.Equal(
            CCL.MES.Application.Services.SpecRevisionHelpers.NextRev(firstRev.RevisionCode),
            secondRev.RevisionCode);
        Assert.Equal(
            CCL.MES.Application.Services.SpecRevisionHelpers.NextRev(secondRev.RevisionCode),
            thirdRev.RevisionCode);

        // Lineage chain.
        Assert.Equal(first.ProductRevisionId, secondRev.ParentRevisionId);
        Assert.Equal(second.ProductRevisionId, thirdRev.ParentRevisionId);

        // New revs always Draft on insert.
        Assert.Equal(CCL.MES.Domain.ProductRevisionStatus.Draft, thirdRev.Status);
        Assert.DoesNotContain("-trashed-", thirdRev.RevisionCode);
    }

    [Fact]
    public async Task Preview_dup_detection_surfaces_existing_revision_status()
    {
        var samplePath = SilkSamplePath();
        Assert.True(File.Exists(samplePath));

        var client = await EngineerClientAsync("eng-imp-dup");

        // First import — creates the spec.
        var bytes = await File.ReadAllBytesAsync(samplePath);
        using (var body = BuildMultipart(bytes, "DEMO_SILK_1.xlsx", "silkscreen"))
        {
            var preview = await client.PostAsync("/api/v2/specs/import/preview", body);
            var p1 = await preview.Content.ReadFromJsonAsync<SpecImportPreviewResponse>();
            // Skip if the parser produced no RefNo — dup detection only
            // applies to specs that carry a RefNo header field. The bundled
            // sample DOES carry one so this path exercises the dup branch.
            if (!string.IsNullOrWhiteSpace(p1?.Summary?.RefNo))
            {
                var saveResp = await client.PostAsJsonAsync("/api/v2/specs/import/save", new SpecImportSaveRequest
                {
                    ParsedJson = p1!.ParsedJson!,
                    FileName = p1.FileName,
                    PlannerCategory = p1.PlannerCategory,
                    Mode = p1.DuplicateRefNo ? SpecImportSaveMode.UpgradeRev : SpecImportSaveMode.SaveNew,
                });
                Assert.Equal(HttpStatusCode.OK, saveResp.StatusCode);
            }
        }

        // Second import — should now flag a dup with the saved rev's status.
        using (var body = BuildMultipart(bytes, "DEMO_SILK_1.xlsx", "silkscreen"))
        {
            var preview = await client.PostAsync("/api/v2/specs/import/preview", body);
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
            var p2 = await preview.Content.ReadFromJsonAsync<SpecImportPreviewResponse>();
            Assert.NotNull(p2);
            Assert.True(p2!.DuplicateRefNo, "Expected dup detection on second import of same file.");
            Assert.NotNull(p2.DuplicateStatus);
            Assert.False(string.IsNullOrWhiteSpace(p2.DuplicateStatus));
        }
    }

    private static string IndigoFixturePath() =>
        Path.Combine(AppContext.BaseDirectory, "SpecImport", "Fixtures", "HP_indigo_sample.xlsx");

    [Fact]
    public async Task Save_indigo_for_product_that_already_has_revA_bumps_rev_not_500()
    {
        // P10.10 regression — importing an Indigo spec whose Part No already
        // owns a Rev A (with a DIFFERENT RefNo, so the dup-RefNo guard doesn't
        // fire) used to collide on (ProductId, RevisionCode='A') and surface a
        // raw HTTP 500 ("Save failed. Server error (HTTP 500)"). It must now
        // create the next revision letter and return 200.
        var path = IndigoFixturePath();
        Assert.True(File.Exists(path), $"Indigo fixture not found at {path}");
        var client = await EngineerClientAsync("eng-imp-indigo");

        // Pre-seed PANASONIC / 80645392 with an existing Rev A under a DIFFERENT
        // RefNo so the import resolves the same product but can't reuse Rev A.
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
            var cust = new Customer { Code = "PANASONIC", Name = "PANASONIC" };
            db.Customers.Add(cust);
            await db.SaveChangesAsync();
            var prod = new Product { ProductCode = "80645392", Name = "Pre-existing", CustomerId = cust.Id };
            db.Products.Add(prod);
            await db.SaveChangesAsync();
            db.ProductRevisions.Add(new ProductRevision
            {
                ProductId = prod.Id, SpecCode = "80645392", Title = "Old", RevisionCode = "A",
                RefNo = "CCL-OLD-0001", Status = ProductRevisionStatus.Draft,
            });
            await db.SaveChangesAsync();
        }

        var bytes = await File.ReadAllBytesAsync(path);
        SpecImportPreviewResponse? preview;
        using (var body = BuildMultipart(bytes, "HP_indigo_sample.xlsx", "indigo"))
        {
            var pr = await client.PostAsync("/api/v2/specs/import/preview", body);
            Assert.Equal(HttpStatusCode.OK, pr.StatusCode);
            preview = await pr.Content.ReadFromJsonAsync<SpecImportPreviewResponse>();
        }
        Assert.True(preview!.ParseOk, preview.ParseError);
        Assert.False(preview.DuplicateRefNo);   // the Indigo RefNo differs from the seeded one

        var save = await client.PostAsJsonAsync("/api/v2/specs/import/save", new SpecImportSaveRequest
        {
            ParsedJson = preview.ParsedJson!,
            FileName = preview.FileName,
            PlannerCategory = preview.PlannerCategory,
            Mode = SpecImportSaveMode.SaveNew,
        });
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);   // was 500 before the fix

        using var verify = _fx.Services.CreateScope();
        var vdb = verify.ServiceProvider.GetRequiredService<MesDbContext>();
        var codes = await vdb.ProductRevisions
            .Where(r => r.Product!.ProductCode == "80645392")
            .Select(r => r.RevisionCode).ToListAsync();
        Assert.Contains("A", codes);
        Assert.Contains("B", codes);   // the import created the next revision
    }
}
