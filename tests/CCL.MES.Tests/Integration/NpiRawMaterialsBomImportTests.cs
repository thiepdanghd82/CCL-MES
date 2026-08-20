using CCL.MES.Application.Services;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Tests.Integration._Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CCL.MES.Tests.Integration;

/// <summary>
/// rawmaterials-bom-xlsx-import — unit coverage for the HYBRID
/// <see cref="NpiImportService"/> (the grid-based, format-agnostic engine
/// behind POST /api/v2/npi/{kind}/import). Distinct from the legacy
/// replace-all <c>NpiImport.NpiImportService</c> exercised by
/// <see cref="NpiImportServiceTests"/>.
///
/// Covers: header auto-detect (header not at row 0), "Materials BOM" alias
/// mapping into all 11 new columns, upsert-by-PartNo idempotency, scientific
/// notation parsing (Thickness "4.5E-2"), and header-not-found signalling.
/// Runs on isolated /tmp SQLite migrated to the live chain (so the new
/// columns exist) — never touches live ccl_mes.db.
/// </summary>
public sealed class NpiRawMaterialsBomImportTests : IDisposable
{
    private readonly IsolatedDbFixture _fx;
    public NpiRawMaterialsBomImportTests() => _fx = new IsolatedDbFixture();
    public void Dispose() => _fx.Dispose();

    // The real "Materials BOM" layout: row 0 is blank, row 1 is the header,
    // data begins at row 2. Column order deliberately differs from the entity
    // property order to prove name-based (not position-based) matching.
    private static IReadOnlyList<IReadOnlyList<string>> MaterialsBomGrid(params string[][] dataRows)
    {
        var grid = new List<IReadOnlyList<string>>
        {
            new[] { "", "", "", "", "" },   // blank first row (real export shape)
            new[]
            {
                "Part No", "Part Description In Use", "Mother code", "Dimension/ Quality",
                "Width (mm)", "Part Type", "Planner", "Inventory UoM",
                "Accounting Group Description", "Part Product Family",
                "Part Product Family Description", "Type Designation", "Price",
                "Price incl. Tax", "Currency", "Price Unit Measure",
                "Supplier Manufacturing Leadtime", "Thickness", "Lead Time Code",
                "Supplier ID", "Supplier Name",
            },
        };
        foreach (var r in dataRows) grid.Add(r);
        return grid;
    }

    private static string[] SampleRow(string partNo) => new[]
    {
        partNo, "(PT26) / PTMWG-D0850S (270mm x 500M)", "PTMWG-D0850S", "270mm x 500M",
        "270", "Purchased (raw)", "RMP FLEX", "m2", "Raw material", "RAWLB",
        "Raw Material - Label stock", "PET (label stock)", "17512", "17512", "VND",
        "m2", "30", "4.4999999999999998E-2", "Purchased", "VHM581",
        "Công Ty Cổ Phần Vũ Hoàng Minh",
    };

    private NpiImportService NewService() => new(_fx.NewContext());

    [Fact]
    public async Task Header_auto_detected_when_not_first_row_and_all_bom_fields_map()
    {
        var grid = MaterialsBomGrid(SampleRow("30031543"));

        var result = await NewService().ImportAsync("rawmaterials", grid, "tester");

        Assert.Equal(1, result.Inserted);
        Assert.Equal(0, result.Updated);
        Assert.Equal(0, result.Skipped);

        using var db = _fx.NewContext();
        var row = await db.RawMaterials.AsNoTracking().SingleAsync(x => x.PartNo == "30031543");
        Assert.Equal("(PT26) / PTMWG-D0850S (270mm x 500M)", row.PartDescription);
        Assert.Equal("PTMWG-D0850S", row.MotherCode);
        Assert.Equal("270mm x 500M", row.DimensionQuality);
        Assert.Equal(270, row.WidthMm);
        Assert.Equal("Purchased (raw)", row.PartType);
        Assert.Equal("RMP FLEX", row.Planner);
        Assert.Equal("m2", row.InventoryUom);
        Assert.Equal("Raw material", row.AccountingGroupDescription);
        Assert.Equal("RAWLB", row.ProductFamily);
        Assert.Equal("Raw Material - Label stock", row.ProductFamilyDescription);
        Assert.Equal("PET (label stock)", row.TypeDesignation);
        Assert.Equal(17512, row.Price);
        Assert.Equal(17512, row.PriceInclTax);
        Assert.Equal("VND", row.Currency);
        Assert.Equal("m2", row.PriceUom);
        Assert.Equal(30, row.SupplierLeadtimeDays);
        Assert.Equal("Purchased", row.LeadTimeCode);
        Assert.Equal("VHM581", row.SupplierId);
        Assert.Equal("Công Ty Cổ Phần Vũ Hoàng Minh", row.SupplierName);
    }

    [Fact]
    public async Task Thickness_scientific_notation_parses_to_double()
    {
        var grid = MaterialsBomGrid(SampleRow("30031543"));

        await NewService().ImportAsync("rawmaterials", grid, "tester");

        using var db = _fx.NewContext();
        var row = await db.RawMaterials.AsNoTracking().SingleAsync(x => x.PartNo == "30031543");
        Assert.NotNull(row.Thickness);
        Assert.Equal(0.045, row.Thickness!.Value, precision: 6);
    }

    [Fact]
    public async Task Reimport_same_partno_updates_not_duplicates()
    {
        var grid1 = MaterialsBomGrid(SampleRow("30031543"));
        var svc1 = NewService();
        var r1 = await svc1.ImportAsync("rawmaterials", grid1, "tester");
        Assert.Equal(1, r1.Inserted);
        Assert.Equal(0, r1.Updated);

        // Second import: same PartNo, a changed field (Price 17512 → 99999).
        var changed = SampleRow("30031543");
        changed[12] = "99999";   // Price column
        var grid2 = MaterialsBomGrid(changed);
        var svc2 = NewService();   // fresh context (new request scope)
        var r2 = await svc2.ImportAsync("rawmaterials", grid2, "tester");

        Assert.Equal(0, r2.Inserted);
        Assert.Equal(1, r2.Updated);

        using var db = _fx.NewContext();
        var rows = await db.RawMaterials.AsNoTracking().Where(x => x.PartNo == "30031543").ToListAsync();
        Assert.Single(rows);                       // NOT duplicated
        Assert.Equal(99999, rows[0].Price);        // updated in place
        Assert.NotNull(rows[0].UpdatedAt);
    }

    [Fact]
    public async Task Reimport_does_not_overwrite_existing_value_with_blank_cell()
    {
        var grid1 = MaterialsBomGrid(SampleRow("30031543"));
        await NewService().ImportAsync("rawmaterials", grid1, "tester");

        // Second import: SupplierName cell blanked → must keep the prior value.
        var blanked = SampleRow("30031543");
        blanked[20] = "";   // Supplier Name column
        var grid2 = MaterialsBomGrid(blanked);
        await NewService().ImportAsync("rawmaterials", grid2, "tester");

        using var db = _fx.NewContext();
        var row = await db.RawMaterials.AsNoTracking().SingleAsync(x => x.PartNo == "30031543");
        Assert.Equal("Công Ty Cổ Phần Vũ Hoàng Minh", row.SupplierName);
    }

    [Fact]
    public async Task Blank_partno_rows_are_skipped()
    {
        var blankRow = new string[21];
        for (var i = 0; i < 21; i++) blankRow[i] = "";
        var grid = MaterialsBomGrid(SampleRow("30031543"), blankRow);

        var result = await NewService().ImportAsync("rawmaterials", grid, "tester");

        Assert.Equal(1, result.Inserted);
        Assert.True(result.Skipped >= 1);
    }

    [Fact]
    public async Task Import_tolerates_preexisting_duplicate_partno_in_table()
    {
        // Regression: prod ccl_mes.db held 92 PartNo groups duplicated by
        // historical append-only imports. The upsert preload used
        // ToDictionary(PartNo) which threw "same key already added" and the
        // whole import aborted (surfaced at the wire as import.xlsx_unreadable).
        // Seed two rows sharing one PartNo; import must not throw and must
        // update the FIRST occurrence, leaving the stale twin untouched.
        using (var seed = _fx.NewContext())
        {
            seed.RawMaterials.Add(new RawMaterial { PartNo = "30030262", Price = 1, CreatedAt = DateTime.UtcNow });
            seed.RawMaterials.Add(new RawMaterial { PartNo = "30030262", Price = 2, CreatedAt = DateTime.UtcNow });
            await seed.SaveChangesAsync();
        }

        var changed = SampleRow("30030262");
        changed[12] = "55555";   // Price
        var grid = MaterialsBomGrid(changed);

        var result = await NewService().ImportAsync("rawmaterials", grid, "tester");

        Assert.Equal(0, result.Inserted);   // no new row for the existing key
        Assert.Equal(1, result.Updated);

        using var db = _fx.NewContext();
        var rows = await db.RawMaterials.AsNoTracking()
            .Where(x => x.PartNo == "30030262").OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(2, rows.Count);              // twin NOT deleted, NOT added-to
        Assert.Equal(55555, rows[0].Price);       // first occurrence updated
        Assert.Equal(2, rows[1].Price);           // stale twin left untouched
    }

    [Fact]
    public async Task No_header_row_throws_header_not_found()
    {
        var grid = new List<IReadOnlyList<string>>
        {
            new[] { "Nonsense", "Columns", "Here" },
            new[] { "1", "2", "3" },
        };

        await Assert.ThrowsAsync<ImportHeaderNotFoundException>(() =>
            NewService().ImportAsync("rawmaterials", grid, "tester"));
    }
}
