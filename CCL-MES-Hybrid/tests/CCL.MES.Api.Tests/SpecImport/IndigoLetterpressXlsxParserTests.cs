using CCL.MES.Infrastructure.SpecImport;

namespace CCL.MES.Api.Tests.SpecImport;

/// <summary>
/// P10.10 — dedicated parser coverage for the Indigo (HP) and Letterpress (LP)
/// seal templates, asserted against copies of the real Data/Specs reference
/// files (HP_AWW9917CJ1C0-0C2 / LP_LY2506001). Locks the label-anchored
/// extraction so the silkscreen-fallback warning no longer fires and the key
/// header / product / ink-row fields land correctly.
/// </summary>
public sealed class IndigoLetterpressXlsxParserTests
{
    private static readonly string FixtureDir =
        Path.Combine(AppContext.BaseDirectory, "SpecImport", "Fixtures");

    private static CCL.MES.Application.SpecImport.ParsedSpecDto ParseFixture(string file, string category)
    {
        var parser = new IndigoLetterpressXlsxParser();
        using var fs = File.OpenRead(Path.Combine(FixtureDir, file));
        return parser.Parse(fs, category);
    }

    [Fact]
    public void Indigo_HP_sample_parses_header_product_and_ink_rows()
    {
        var spec = ParseFixture("HP_indigo_sample.xlsx", "indigo");

        Assert.Equal("indigo", spec.Category);
        Assert.Equal("CCL-Seal-31215", spec.RefNo);
        Assert.Equal("PANASONIC", spec.Customer);
        Assert.Equal("80645392", spec.PartNo);
        Assert.Equal("AWW9917CJ1C0-0C2", spec.PartName);
        Assert.Equal(481.5, spec.ProductSizeW);
        Assert.Equal(230, spec.ProductSizeH);

        // 5 real ink colours (CYAN/MAGENTA/YELLOW/BLACK/WHITE) — blank rows 6/7 skipped.
        Assert.Equal(5, spec.FlexoInkRows.Count);
        var first = spec.FlexoInkRows[0];
        Assert.Equal("CYAN", first.Color);
        Assert.Equal("Q91", first.InkCode);
        Assert.Contains("CYAN", first.InkDescription);
        Assert.Equal("Rieckermann", first.Brand);

        // The single HP INDIGO print process row is captured.
        Assert.Single(spec.FlexoPrintRows);

        // No silkscreen-fallback warning (dedicated parser ran).
        Assert.DoesNotContain(spec.Warnings, w => w.Contains("silkscreen layout fallback"));
        // The Inspection-Level label trap must NOT leak the REF NO label.
        Assert.DoesNotContain("tham chiếu", spec.InspectionLevel);
    }

    [Fact]
    public void Letterpress_LP_sample_parses_header_product_and_ink_rows()
    {
        var spec = ParseFixture("LP_letterpress_sample.xlsx", "letterpress");

        Assert.Equal("letterpress", spec.Category);
        Assert.Equal("CCL-Seal-11554", spec.RefNo);
        Assert.Equal("94", spec.InspectionLevel);   // "Spec 94" stamp
        Assert.Equal("BIVN", spec.Customer);
        Assert.Equal("LY2506001", spec.PartNo);
        Assert.Equal("LABEL IGNITION 45X106", spec.PartName);
        Assert.Equal(106, spec.ProductSizeW);
        Assert.Equal(44.8, spec.ProductSizeH);

        // 6 ink rows (FD3 / UV15 / HH204 / HH203 / UV7 / UV7).
        Assert.Equal(6, spec.FlexoInkRows.Count);
        var first = spec.FlexoInkRows[0];
        Assert.Equal("FD3", first.InkCode);
        Assert.Equal("TOYO", first.Brand);

        // Print + cut process rows captured (cutter HSS-R-16).
        Assert.Single(spec.FlexoPrintRows);
        Assert.Single(spec.FlexoCuttingRows);
        Assert.Equal("HSS-R-16", spec.FlexoCuttingRows[0].CutterName);

        Assert.DoesNotContain(spec.Warnings, w => w.Contains("silkscreen layout fallback"));
    }
}
