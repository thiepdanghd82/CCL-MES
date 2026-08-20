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

    private sealed record ImportResult(string Kind, int Inserted, int Updated, int Skipped);

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

    // ── rawmaterials-bom-xlsx-import ──────────────────────────────────────

    /// <summary>Build a "Materials BOM"-shaped .xlsx in memory: blank row 1,
    /// header row 2 (name order differs from entity order), data from row 3.
    /// Returns a multipart form with an .xlsx filename so the controller
    /// routes to the ClosedXML decoder.</summary>
    private static MultipartFormDataContent Xlsx(params (string partNo, string thickness, string supplier)[] rows)
    {
        using var wb = new ClosedXML.Excel.XLWorkbook();
        var ws = wb.AddWorksheet("Sheet1");
        var header = new[]
        {
            "Part No", "Part Description In Use", "Mother code", "Dimension/ Quality",
            "Width (mm)", "Part Type", "Planner", "Inventory UoM",
            "Accounting Group Description", "Part Product Family",
            "Part Product Family Description", "Type Designation", "Price",
            "Price incl. Tax", "Currency", "Price Unit Measure",
            "Supplier Manufacturing Leadtime", "Thickness", "Lead Time Code",
            "Supplier ID", "Supplier Name",
        };
        for (var c = 0; c < header.Length; c++) ws.Cell(2, c + 1).Value = header[c];  // row 1 blank
        var r = 3;
        foreach (var row in rows)
        {
            ws.Cell(r, 1).Value = row.partNo;
            ws.Cell(r, 2).Value = "Desc " + row.partNo;
            ws.Cell(r, 3).Value = "MC-" + row.partNo;
            ws.Cell(r, 5).Value = 270;              // NUMERIC cell (like IFS export)
            ws.Cell(r, 13).Value = 17512;           // NUMERIC cell
            // Thickness as a NUMBER cell — mirrors the real "Materials BOM"
            // export where GetString() would format culture-dependently
            // ("0,045" on a comma-decimal culture) and corrupt to 45. Writing
            // a string here would hide that bug (Text cells pass through raw).
            ws.Cell(r, 18).Value = double.Parse(row.thickness,
                System.Globalization.CultureInfo.InvariantCulture);
            ws.Cell(r, 21).Value = row.supplier;
            r++;
        }
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        var form = new MultipartFormDataContent();
        var fc = new ByteArrayContent(ms.ToArray());
        fc.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(fc, "file", "Materials BOM.xlsx");
        return form;
    }

    [Fact]
    public async Task Import_rawmaterials_xlsx_auto_detects_header_and_maps_bom_columns()
    {
        var c = await EngineerClientAsync("npi-imp-xlsx");
        var tag = Guid.NewGuid().ToString("N")[..6];
        var pn = $"XL-{tag}";

        var resp = await c.PostAsync("/api/v2/npi/rawmaterials/import",
            Xlsx((pn, "4.4999999999999998E-2", "Supplier X")));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var r = await resp.Content.ReadFromJsonAsync<ImportResult>();
        Assert.NotNull(r);
        Assert.Equal(1, r!.Inserted);
        Assert.Equal(0, r.Updated);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var row = await db.RawMaterials.AsNoTracking().SingleAsync(x => x.PartNo == pn);
        Assert.Equal($"MC-{pn}", row.MotherCode);
        Assert.Equal(270, row.WidthMm);
        Assert.Equal(0.045, row.Thickness!.Value, 6);
        Assert.Equal("Supplier X", row.SupplierName);
    }

    [Fact]
    public async Task Import_rawmaterials_xlsx_twice_updates_in_place()
    {
        var c = await EngineerClientAsync("npi-imp-xlsx2");
        var tag = Guid.NewGuid().ToString("N")[..6];
        var pn = $"XLU-{tag}";

        var r1 = await (await c.PostAsync("/api/v2/npi/rawmaterials/import",
            Xlsx((pn, "1E-2", "First"))))
            .Content.ReadFromJsonAsync<ImportResult>();
        Assert.Equal(1, r1!.Inserted);

        var r2 = await (await c.PostAsync("/api/v2/npi/rawmaterials/import",
            Xlsx((pn, "1E-2", "Second"))))
            .Content.ReadFromJsonAsync<ImportResult>();
        Assert.Equal(0, r2!.Inserted);
        Assert.Equal(1, r2.Updated);

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
        var rows = await db.RawMaterials.AsNoTracking().Where(x => x.PartNo == pn).ToListAsync();
        Assert.Single(rows);
        Assert.Equal("Second", rows[0].SupplierName);
    }

    [Fact]
    public async Task Import_rawmaterials_csv_without_header_returns_422_header_not_found()
    {
        var c = await EngineerClientAsync("npi-imp-nohdr");
        var resp = await c.PostAsync("/api/v2/npi/rawmaterials/import",
            Csv("Nope,Bogus,Columns\n1,2,3\n"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("import.header_not_found", body);
    }
}
