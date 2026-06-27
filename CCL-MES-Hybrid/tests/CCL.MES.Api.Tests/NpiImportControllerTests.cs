using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Api.Tests;

/// <summary>
/// P10.5 follow-up — wire tests for POST /api/v2/npi/{kind}/import.
/// CSV import for the NPI grids (SpecHub "Import…" parity): tolerant
/// header mapping, append semantics, key-missing rows skipped.
/// </summary>
public sealed class NpiImportControllerTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public NpiImportControllerTests(MesApiFactory fx) => _fx = fx;

    private sealed record ImportResult(string Kind, int Inserted, int Skipped);

    private async Task<HttpClient> EngineerClientAsync(string user)
    {
        await _fx.SeedUserAsync(user, "P@ss!1", "Engineer");
        var c = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(c, user, "P@ss!1");
        return c;
    }

    private static MultipartFormDataContent Csv(string content)
    {
        var form = new MultipartFormDataContent();
        var fc = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        fc.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(fc, "file", "test.csv");
        return form;
    }

    [Fact]
    public async Task Import_requires_auth()
    {
        var c = _fx.CreateClient();
        var resp = await c.PostAsync("/api/v2/npi/structures/import",
            Csv("Parent Part No,Component Part\nA,B\n"));
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Import_unknown_kind_returns_404()
    {
        var c = await EngineerClientAsync("npi-imp-404");
        var resp = await c.PostAsync("/api/v2/npi/bogus/import", Csv("a,b\n1,2\n"));
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Import_structures_inserts_mapped_rows_and_skips_blank()
    {
        var c = await EngineerClientAsync("npi-imp-ok");
        var tag = Guid.NewGuid().ToString("N")[..6];
        var csv =
            "Parent Part No,Parent Part Description,Component Part,Component Part Description,Qty Per Assembly,Scrap Factor (%),Structure Type\n" +
            $"IMP-{tag},Parent one,C-1,\"Comp, one\",2.5,3,DIECUT\n" +     // quoted comma in description
            $"IMP-{tag},Parent one,C-2,Comp two,1,5,SCREEN\n" +
            ",,,,,,\n";                                                    // blank → skipped

        var resp = await c.PostAsync("/api/v2/npi/structures/import", Csv(csv));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var r = await resp.Content.ReadFromJsonAsync<ImportResult>();
        Assert.NotNull(r);
        Assert.Equal(2, r!.Inserted);
        Assert.True(r.Skipped >= 1);

        // Rows are queryable + the quoted-comma description survived.
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var rows = await db.ManufacturingStructures.AsNoTracking()
            .Where(x => x.ParentPart == $"IMP-{tag}").ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, x => x.ComponentPart == "C-1" && x.ComponentDescription == "Comp, one" && x.QtyAssembly == 2.5);
        Assert.Contains(rows, x => x.ComponentPart == "C-2" && x.StructureType == "SCREEN");
    }

    [Fact]
    public async Task Import_no_file_returns_422()
    {
        var c = await EngineerClientAsync("npi-imp-nofile");
        var resp = await c.PostAsync("/api/v2/npi/structures/import", new MultipartFormDataContent());
        // Empty multipart → either the handler's 422 (no_file) or the
        // framework's 400 before binding; both mean "no usable file".
        Assert.Contains(resp.StatusCode, new[] { HttpStatusCode.UnprocessableEntity, HttpStatusCode.BadRequest });
    }
}
