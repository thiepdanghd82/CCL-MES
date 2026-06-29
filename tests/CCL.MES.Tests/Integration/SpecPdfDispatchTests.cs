using System.Globalization;
using CCL.MES.Application.SpecDetail;
using CCL.MES.Application.SpecExport;
using CCL.MES.Domain;
using CCL.MES.Infrastructure.SpecExport;
using MigraDoc.DocumentObjectModel;
using Xunit;

namespace CCL.MES.Tests.Integration;

/// <summary>
/// Phase 9 T2a — Port of <c>scripts/VerifyPrB</c> (template dispatch
/// across SILK / FLEXO / GENERIC × 3 planner variants). Validates that
/// <see cref="PdfSpecSheetExporter"/> produces a non-empty PDF byte[] +
/// throws no exception for each shape; the visible-layout check is the
/// human-eyeball step on the rendered PDFs (kept out of scope — pixel-
/// diff testing is fragile against MigraDoc font metric changes).
///
/// <para>
/// NO EF / DI / DbContext — exporter is a pure function of DTO + context.
/// </para>
/// </summary>
public class SpecPdfDispatchTests
{
    private static readonly SpecExportContext Ctx = new(
        Title:             "T2a Verify",
        FilterDescription: null,
        GeneratedAt:       new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc),
        GeneratedBy:       "phase9-t2a",
        Culture:           CultureInfo.InvariantCulture);

    // ── Each test boxed for parameterized invocation via Theory ────────

    [Fact]
    public void Silk_planner_renders_non_empty_pdf()
    {
        var bytes = new PdfSpecSheetExporter().Export(BuildSilk(), Ctx);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
        AssertPdfMagicHeader(bytes);
    }

    [Fact]
    public void Flexo_planner_renders_non_empty_pdf()
    {
        var bytes = new PdfSpecSheetExporter().Export(BuildFlexo(), Ctx);
        Assert.NotEmpty(bytes);
        AssertPdfMagicHeader(bytes);
    }

    [Fact]
    public void Generic_planner_with_no_rows_still_renders_via_warning_paragraph()
    {
        // INDIGO empty per VerifyPrB case 3 — exercises the "generic +
        // warning + no-data paragraph" branch.
        var bytes = new PdfSpecSheetExporter().Export(BuildGenericEmpty("INDIGO"), Ctx);
        Assert.NotEmpty(bytes);
        AssertPdfMagicHeader(bytes);
    }

    // ── Print PDF paper size + orientation (toolbar pickers) ───────────

    [Fact]
    public void Detail_sheet_defaults_to_A4_portrait()
    {
        var setup = SpecPdfDocumentBuilder
            .BuildDetailSheet(BuildFlexo(), Ctx).LastSection.PageSetup;
        Assert.Equal(PageFormat.A4, setup.PageFormat);
        Assert.Equal(Orientation.Portrait, setup.Orientation);
    }

    [Theory]
    [InlineData("A3", "Landscape", PageFormat.A3, Orientation.Landscape)]
    [InlineData("letter", "landscape", PageFormat.Letter, Orientation.Landscape)]
    [InlineData("Legal", "Portrait", PageFormat.Legal, Orientation.Portrait)]
    [InlineData("bogus", "bogus", PageFormat.A4, Orientation.Portrait)] // unknown → safe defaults
    public void Detail_sheet_applies_page_size_and_orientation(
        string pageSize, string orientation, PageFormat expectedFormat, Orientation expectedOrientation)
    {
        var setup = SpecPdfDocumentBuilder
            .BuildDetailSheet(BuildFlexo(), Ctx, pageSize, orientation).LastSection.PageSetup;
        Assert.Equal(expectedFormat, setup.PageFormat);
        Assert.Equal(expectedOrientation, setup.Orientation);
    }

    [Fact]
    public void Detail_sheet_landscape_A3_renders_non_empty_pdf()
    {
        var bytes = new PdfSpecSheetExporter().Export(BuildFlexo(), Ctx, "A3", "Landscape");
        Assert.NotEmpty(bytes);
        AssertPdfMagicHeader(bytes);
    }

    [Theory]
    [InlineData("Portrait", false)]
    [InlineData("Landscape", true)]
    public void Rendered_pdf_page_width_exceeds_height_only_in_landscape(
        string orientation, bool expectWide)
    {
        var bytes = new PdfSpecSheetExporter().Export(BuildFlexo(), Ctx, "A4", orientation);
        using var ms = new MemoryStream(bytes);
        var pdf = PdfSharp.Pdf.IO.PdfReader.Open(ms, PdfSharp.Pdf.IO.PdfDocumentOpenMode.ReadOnly);
        var page = pdf.Pages[0];
        var isWide = page.Width.Point > page.Height.Point;
        Assert.Equal(expectWide, isWide);
    }

    // Fit-to-page: detail-sheet tables must widen to fill the chosen paper so
    // landscape / A3 don't leave a big right-hand gap (SpecHub parity fix).
    private static double WidestTableCm(MigraDoc.DocumentObjectModel.Document doc)
    {
        double max = 0;
        var section = doc.LastSection!;
        foreach (var item in section.Elements)
        {
            if (item is MigraDoc.DocumentObjectModel.Tables.Table t)
            {
                double sum = 0;
                foreach (MigraDoc.DocumentObjectModel.Tables.Column c in t.Columns)
                    sum += c.Width.Centimeter;
                if (sum > max) max = sum;
            }
        }
        return max;
    }

    [Fact]
    public void Detail_sheet_tables_scale_wider_to_fill_landscape_and_a3()
    {
        var portraitA4 = WidestTableCm(
            SpecPdfDocumentBuilder.BuildDetailSheet(BuildFlexo(), Ctx, "A4", "Portrait"));
        var landscapeA4 = WidestTableCm(
            SpecPdfDocumentBuilder.BuildDetailSheet(BuildFlexo(), Ctx, "A4", "Landscape"));
        var landscapeA3 = WidestTableCm(
            SpecPdfDocumentBuilder.BuildDetailSheet(BuildFlexo(), Ctx, "A3", "Landscape"));

        // Landscape widens vs portrait; A3 widens further still.
        Assert.True(landscapeA4 > portraitA4 + 1.0,
            $"landscape ({landscapeA4:F1}cm) should exceed portrait ({portraitA4:F1}cm)");
        Assert.True(landscapeA3 > landscapeA4 + 1.0,
            $"A3 landscape ({landscapeA3:F1}cm) should exceed A4 landscape ({landscapeA4:F1}cm)");
        // A4 landscape widest table fills most of the ~27.3cm content width.
        Assert.True(landscapeA4 > 24.0,
            $"A4 landscape table ({landscapeA4:F1}cm) should fill most of the 27.3cm content area");
    }

    [Fact]
    public void Generic_planner_with_silk_shape_rows_reuses_silk_table()
    {
        // LETTER + silk-style rows per VerifyPrB case 4.
        var bytes = new PdfSpecSheetExporter().Export(BuildGenericWithSilkRows("LETTER"), Ctx);
        Assert.NotEmpty(bytes);
        AssertPdfMagicHeader(bytes);
    }

    [Fact]
    public void Generic_planner_with_flexo_cut_rows_reuses_flexo_cut_table()
    {
        // DIECUT + flexo-cut rows per VerifyPrB case 5.
        var bytes = new PdfSpecSheetExporter().Export(BuildGenericWithFlexoCut("DIECUT"), Ctx);
        Assert.NotEmpty(bytes);
        AssertPdfMagicHeader(bytes);
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static void AssertPdfMagicHeader(byte[] bytes)
    {
        // PDF spec § 7.5.2 — file MUST start with %PDF- magic. Catches
        // accidental empty stream / wrong content-type / corrupt write.
        Assert.True(bytes.Length >= 8, "PDF must be at least 8 bytes");
        Assert.Equal((byte)'%', bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'D', bytes[2]);
        Assert.Equal((byte)'F', bytes[3]);
        Assert.Equal((byte)'-', bytes[4]);
    }

    private static SpecDetailDto BuildSilk() => new()
    {
        Id = 1001,
        SpecCode = "VERIFY-SILK-001",
        Title = "Synthetic Silk Spec",
        RevisionCode = "A",
        Status = ProductRevisionStatus.Draft,
        RefNo = "REF-001",
        InspectionLevel = "A",
        Planner = "SILK",
        ProcessCode = "SILKSCREEN",
        IsSilkscreen = true,
        IsFlexo = false,
        ProductCode = "P-SILK-001",
        ProductName = "Silk Test Part",
        CustomerName = "CCL Vietnam",
        SubstrateType = "PET 100um",
        AdhesiveType = "Acrylic",
        PrintingCavity = 8,
        LengthPitchMm = 320.0,
        ProductSizeWmm = 60.0,
        ProductSizeHmm = 40.0,
        CreatedAt = new DateTime(2026, 5, 26, 0, 0, 0, DateTimeKind.Utc),
        CreatedBy = "verify",
        PrintColors = Enumerable.Range(1, 10).Select(i => new SpecPrintColorRow(
            Seq: i, Surface: "Top", Color: $"Color{i}", InkName: $"Ink {i}",
            InkCode: $"IC-{i:D3}", Maker: "Sakata", Retarder: "R-A", Viscosity: 22.0, Speed: 80.0,
            Squeegee: "YR", Dry: "OVEN", TemperatureC: 80.0, TimeMin: 5, Uv: "Y",
            EmulsionUm: 12.0, PlateSize: "300x300", Mesh: "180T", AngleDeg: 22.5,
            PlateCode: $"PL-{i:D3}", ControlNo: i, Remark: null)).ToList(),
    };

    private static SpecDetailDto BuildFlexo() => new()
    {
        Id = 1002,
        SpecCode = "VERIFY-FLEXO-001",
        Title = "Synthetic Flexo Spec",
        RevisionCode = "A",
        Status = ProductRevisionStatus.Approved,
        RefNo = "REF-002",
        InspectionLevel = "B",
        Planner = "FLEXO",
        ProcessCode = "FLEXO",
        IsSilkscreen = false,
        IsFlexo = true,
        ProductCode = "P-FLEXO-001",
        ProductName = "Flexo Test Part",
        CustomerName = "CCL Vietnam",
        SubstrateType = "PP 80um",
        ProductSizeWmm = 100.0,
        ProductSizeHmm = 50.0,
        CreatedAt = new DateTime(2026, 5, 19, 0, 0, 0, DateTimeKind.Utc),
        ApprovedAt = new DateTime(2026, 5, 26, 0, 0, 0, DateTimeKind.Utc),
        ApprovedBy = "verify",
        FlexoPrintRows = Enumerable.Range(1, 2).Select(i => new FlexoPrintRow(
            Seq: i, Process: $"Process-{i}", Material: "PP", Thickness: "80um",
            Size: "100x50", Cylinders: "Z=85", PitchMm: "320", Speed: "60",
            TensionHead: "30", TensionEnd: "28", TensionRoll: "32",
            PlateCavity: "8", Tension: "30")).ToList(),
        FlexoCuttingRows = Enumerable.Range(1, 3).Select(i => new FlexoCuttingRow(
            Seq: i, Process: $"Cut-{i}", Lamination: "Tape-A", Size: "100x50",
            CutterLot: "L-A", CutterName: $"Cutter-{i}", PcsPerSheet: 8,
            CuttingCavity: 8, PitchMm: 320.0, Packing: "Roll",
            PaperSpeed: 60.0, CuttingSpeed: 60.0, CuttingPressure: 3.0,
            HeadTension: 30.0, RollTension: 32.0)).ToList(),
        FlexoInkRows = Enumerable.Range(1, 3).Select(i => new FlexoInkRow(
            Seq: i, Color: $"Color-{i}", InkCode: $"IC-F-{i:D3}",
            InkDescription: $"Flexo Ink {i}", Brand: "Sakata",
            Anilox: "600/4.0", PlateCode: $"PL-F-{i:D3}",
            Pressure: 3.0, UvPowerW: 240.0, IrPowerW: 180.0)).ToList(),
    };

    private static SpecDetailDto BuildGenericEmpty(string planner) => new()
    {
        Id = 1003,
        SpecCode = $"VERIFY-{planner}-EMPTY",
        Title = $"Synthetic {planner} (no data)",
        RevisionCode = "A",
        Status = ProductRevisionStatus.Draft,
        RefNo = "REF-003",
        InspectionLevel = "A",
        Planner = planner,
        ProcessCode = planner,
        IsSilkscreen = false,
        IsFlexo = false,
        ProductCode = "P-GEN-001",
        ProductName = "Generic Test Part",
        CustomerName = "CCL Vietnam",
        SubstrateType = "Unknown",
        CreatedAt = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        CreatedBy = "verify",
    };

    private static SpecDetailDto BuildGenericWithSilkRows(string planner) => new()
    {
        Id = 1004,
        SpecCode = $"VERIFY-{planner}-SILK-ROWS",
        Title = $"Synthetic {planner} (silk-shape rows)",
        RevisionCode = "A",
        Status = ProductRevisionStatus.InReview,
        RefNo = "REF-004",
        InspectionLevel = "A",
        Planner = planner,
        ProcessCode = planner == "LETTER" ? "LETTERPRESS" : planner,
        IsSilkscreen = false,
        IsFlexo = false,
        ProductCode = "P-LETTER-001",
        ProductName = "Letterpress Test Part",
        CustomerName = "CCL Vietnam",
        SubstrateType = "Paper",
        CreatedAt = new DateTime(2026, 5, 30, 0, 0, 0, DateTimeKind.Utc),
        CreatedBy = "verify",
        PrintColors = Enumerable.Range(1, 3).Select(i => new SpecPrintColorRow(
            Seq: i, Surface: "Top", Color: $"Color-{i}", InkName: $"Letter Ink {i}",
            InkCode: $"LI-{i:D3}", Maker: "TOYO", Retarder: null, Viscosity: null, Speed: null,
            Squeegee: null, Dry: "ND", TemperatureC: null, TimeMin: null, Uv: null,
            EmulsionUm: null, PlateSize: "200x200", Mesh: null, AngleDeg: null,
            PlateCode: $"LP-{i:D3}", ControlNo: i, Remark: "Letterpress run")).ToList(),
    };

    private static SpecDetailDto BuildGenericWithFlexoCut(string planner) => new()
    {
        Id = 1005,
        SpecCode = $"VERIFY-{planner}-CUT-ROWS",
        Title = $"Synthetic {planner} (flexo-cut rows)",
        RevisionCode = "A",
        Status = ProductRevisionStatus.Released,
        RefNo = "REF-005",
        InspectionLevel = "A",
        Planner = planner,
        ProcessCode = planner == "DIECUT" ? "ROTARY_CUT" : planner,
        IsSilkscreen = false,
        IsFlexo = false,
        ProductCode = "P-DIECUT-001",
        ProductName = "Die-cut Test Part",
        CustomerName = "CCL Vietnam",
        SubstrateType = "Cardstock",
        CreatedAt = new DateTime(2026, 5, 31, 0, 0, 0, DateTimeKind.Utc),
        CreatedBy = "verify",
        FlexoCuttingRows = Enumerable.Range(1, 2).Select(i => new FlexoCuttingRow(
            Seq: i, Process: $"DieCut-{i}", Lamination: null, Size: "100x50",
            CutterLot: "L-DC", CutterName: $"DC-{i}", PcsPerSheet: 16,
            CuttingCavity: 16, PitchMm: 200.0, Packing: "Sheet",
            PaperSpeed: 30.0, CuttingSpeed: 30.0, CuttingPressure: 4.5,
            HeadTension: null, RollTension: null)).ToList(),
    };
}
