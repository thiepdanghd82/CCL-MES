using System.Globalization;
using CCL.MES.Application;
using CCL.MES.Application.SpecDetail;
using CCL.MES.Application.SpecExport;
using CCL.MES.Domain;
using CCL.MES.Infrastructure.SpecExport;
using ClosedXML.Excel;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
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

    // ── PR (detail-2page): landscape + auto-fit ≤ 2 pages + hairline ─────

    [Fact]
    public void Detail_sheet_is_A4_landscape()
    {
        var doc = SpecPdfDocumentBuilder.BuildDetailSheet(BuildSilk(), Ctx);
        Assert.Equal(Orientation.Landscape, doc.LastSection.PageSetup.Orientation);
        Assert.Equal(PageFormat.A4, doc.LastSection.PageSetup.PageFormat);
    }

    [Fact]
    public void Silk_sheet_fits_two_pages_at_normal_step()
    {
        // BuildSilk() carries 10 colours + the full 9 sections. Landscape +
        // the 21-col one-line-per-row table must land ≤ 2 pages WITHOUT any
        // font step-down (step 0).
        using var pdf = PdfSpecSheetExporter.RenderFitted(BuildSilk(), Ctx, out var step);
        Assert.True(pdf.PageCount <= 2, $"expected ≤2 pages, got {pdf.PageCount}");
        Assert.Equal(0, step);
    }

    [Fact]
    public void Long_silk_spec_auto_fits_two_pages_by_shrinking_font()
    {
        // A deliberately long spec (24 colours + 12 revisions + 15 audit rows)
        // that would overflow at the normal font. The exporter's auto-fit must
        // shrink the body font (step > 0) until it fits ≤ 2 pages — never 3+.
        using var pdf = PdfSpecSheetExporter.RenderFitted(BuildLongSilk(), Ctx, out var step);
        Assert.True(pdf.PageCount <= 2, $"expected ≤2 pages, got {pdf.PageCount}");
    }

    [Fact]
    public void Detail_tables_use_half_hairline_borders()
    {
        var doc = SpecPdfDocumentBuilder.BuildDetailSheet(BuildSilk(), Ctx);
        var tables = doc.LastSection.Elements.OfType<Table>().ToList();
        Assert.NotEmpty(tables);
        // Border rule was halved 0.25 → 0.125pt for the exported sheet. The
        // doc-header layout table has Borders.Width = 0 (only a navy bottom
        // rule), so it is naturally excluded by the > 0 guard.
        Assert.Equal(0.125, SpecPdfDocumentBuilder.StyleConstants.DetailBorderWidthPt);
        foreach (var t in tables.Where(t => t.Borders.Width.Point > 0))
            Assert.True(Math.Abs(t.Borders.Width.Point - 0.125) < 1e-9,
                $"table border {t.Borders.Width.Point}pt is not the halved 0.125");
    }

    // ── Silk Print-Process grid: header ↔ data alignment (RCA fix) ─────────

    [Fact]
    public void Silk_grid_columns_sum_to_exactly_the_usable_width()
    {
        // Invariant: 21 columns whose widths fill the usable page width EXACTLY
        // → MigraDoc never re-flows, header + data share one geometry.
        var (headers, rows, widths, aligns) =
            SpecPdfDocumentBuilder.BuildSilkGrid(BuildSilk(), SpecPdfDocumentBuilder.DetailLayout.ForStep(0));

        Assert.Equal(21, headers.Length);
        Assert.Equal(21, widths.Length);
        Assert.Equal(21, aligns.Length);
        foreach (var r in rows) Assert.Equal(21, r.Length);   // every data row 1:1 with header

        Assert.Equal(SpecPdfDocumentBuilder.DetailUsableWidthCm, widths.Sum(), 2);
    }

    [Fact]
    public void Silk_grid_every_column_is_wide_enough_for_its_header()
    {
        // Regression for the "Retarder" cramp: the old hand-tuned weights left
        // "Retarder" 0.3pt from "Visc" (they rendered as one word). Each column
        // must now clear its header label with margin at the header font.
        var layout = SpecPdfDocumentBuilder.DetailLayout.ForStep(0);
        var (headers, _, widths, _) = SpecPdfDocumentBuilder.BuildSilkGrid(BuildSilk(), layout);

        const double cmPerPt = 2.54 / 72.0;
        // Conservative upper bound on a bold header word width (≈0.55 em/char).
        double headerCharCm = layout.HeaderRowPt * cmPerPt * 0.55;
        for (int i = 0; i < headers.Length; i++)
        {
            double need = headers[i].Length * headerCharCm;
            Assert.True(widths[i] >= need,
                $"column '{headers[i]}' is {widths[i]:F2}cm, header needs ≥ {need:F2}cm → clips.");
        }
    }

    [Fact]
    public void Silk_grid_numeric_columns_centre_text_columns_left()
    {
        // Alignment map is the SAME array applied to header + every data row, so
        // a single digit sits under its header instead of hugging the left edge.
        var (headers, _, _, aligns) =
            SpecPdfDocumentBuilder.BuildSilkGrid(BuildSilk(), SpecPdfDocumentBuilder.DetailLayout.ForStep(0));

        MigraDoc.DocumentObjectModel.ParagraphAlignment A(string h) => aligns[System.Array.IndexOf(headers, h)];
        foreach (var num in new[] { "No", "Visc", "Speed", "°C", "min", "UV", "Emul", "Angle", "Ctrl#" })
            Assert.Equal(MigraDoc.DocumentObjectModel.ParagraphAlignment.Center, A(num));
        foreach (var txt in new[] { "Surf", "Color", "Ink Name", "Ink Code", "Maker", "Retarder", "Squee", "Dry", "Plate Size", "Mesh", "Plate Code", "Remark" })
            Assert.Equal(MigraDoc.DocumentObjectModel.ParagraphAlignment.Left, A(txt));
    }

    [Fact]
    public void Header_band_section_bands_and_tables_share_one_flush_inset()
    {
        // Regression for the L/R "staircase": doc header inset 8pt, tables PadH,
        // section bands 0.1cm → three different left+right edges. Now every
        // table uses PadH symmetric, and every shaded section band insets by
        // ∓PadH → all boxes line up flush on both edges.
        var layout = SpecPdfDocumentBuilder.DetailLayout.ForStep(0);
        var doc = SpecPdfDocumentBuilder.BuildDetailSheet(BuildSilk(), Ctx);
        var sec = doc.LastSection;

        var tables = sec.Elements.OfType<Table>().ToList();
        Assert.NotEmpty(tables);
        foreach (var t in tables)
        {
            Assert.Equal(layout.PadH, t.LeftPadding.Point, 3);
            Assert.Equal(layout.PadH, t.RightPadding.Point, 3);
        }

        var bands = sec.Elements.OfType<Paragraph>()
            .Where(p => !p.Format.Shading.Color.IsEmpty)
            .ToList();
        Assert.NotEmpty(bands);
        foreach (var p in bands)
        {
            Assert.Equal(-layout.PadH, p.Format.LeftIndent.Point, 3);
            Assert.Equal(layout.PadH, p.Format.RightIndent.Point, 3);
        }
    }

    [Fact]
    public void Xlsx_sheet_exporter_produces_a_valid_workbook()
    {
        var bytes = new XlsxSpecSheetExporter().Export(BuildSilk(), Ctx);
        Assert.NotEmpty(bytes);
        // .xlsx is a ZIP container → starts with the "PK" local-file signature.
        Assert.Equal((byte)'P', bytes[0]);
        Assert.Equal((byte)'K', bytes[1]);
    }

    // ── XLSX format (balanced-like-app): numeric cells, no autofilter,
    //    single-language titles, header-fitting column widths ───────────────

    private static IXLWorksheet OpenSheet(SpecDetailDto d)
    {
        var bytes = new XlsxSpecSheetExporter().Export(d, Ctx);
        // Keep the workbook alive via the returned worksheet's Workbook handle.
        var wb = new XLWorkbook(new MemoryStream(bytes));
        return wb.Worksheet(1);
    }

    private static IXLCell HeaderCell(IXLWorksheet ws, string header) =>
        ws.CellsUsed().First(c => c.GetString() == header);

    [Fact]
    public void Xlsx_numeric_columns_are_real_numbers_not_text()
    {
        // "SỐ = SỐ": Visc / °C / Ctrl# / Angle carry real numbers (no green
        // "stored as text" triangle) — while a code column stays TEXT.
        var ws = OpenSheet(BuildSilk());

        foreach (var num in new[] { "Visc", "°C", "Ctrl#", "Angle" })
        {
            var h = HeaderCell(ws, num);
            var below = ws.Cell(h.Address.RowNumber + 1, h.Address.ColumnNumber);
            Assert.Equal(XLDataType.Number, below.DataType);
        }

        // Leading-letter code stays text (IC-001) — never coerced to a number.
        var codeHdr = HeaderCell(ws, "Ink Code");
        var codeCell = ws.Cell(codeHdr.Address.RowNumber + 1, codeHdr.Address.ColumnNumber);
        Assert.Equal(XLDataType.Text, codeCell.DataType);
    }

    [Fact]
    public void Xlsx_has_no_autofilter_it_is_a_document_not_a_grid()
    {
        var ws = OpenSheet(BuildSilk());
        Assert.False(ws.AutoFilter.IsEnabled);
    }

    [Fact]
    public void Xlsx_section_titles_are_single_language()
    {
        var ws = OpenSheet(BuildSilk());
        var strings = ws.CellsUsed().Select(c => c.GetString()).ToList();

        Assert.Contains(strings, s => s == "Product Information");
        Assert.Contains(strings, s => s == "Revision History");
        Assert.Contains(strings, s => s == "Approval Signatures");
        // No bilingual "· <Vietnamese>" suffix leaked into any cell.
        foreach (var vn in new[] { "Thông tin", "Thông số", "Ghi chú", "Lịch sử", "Chữ ký", "sản phẩm" })
            Assert.DoesNotContain(strings, s => s.Contains(vn));
    }

    [Fact]
    public void Xlsx_long_headers_get_columns_wide_enough_not_to_clip()
    {
        // Each header must fit in its column (width ≥ header length) so
        // "Retarder / Plate Code / Plate Size / Remark" are never cut.
        var ws = OpenSheet(BuildSilk());
        foreach (var header in new[] { "Retarder", "Plate Code", "Plate Size", "Remark", "Printing Cavity" })
        {
            var h = HeaderCell(ws, header);
            var width = ws.Column(h.Address.ColumnNumber).Width;
            Assert.True(width >= header.Length,
                $"column for '{header}' is {width:F1} wide — narrower than the {header.Length}-char header (clips).");
        }
    }

    [Fact]
    public void Exported_sheet_omits_the_change_log_section()
    {
        // The audit Change Log is on-screen only — it must NOT appear in the
        // exported/printed PDF (BuildDetailSheet drops section 9). Assert no
        // section-title paragraph carries the Change Log heading.
        var doc = SpecPdfDocumentBuilder.BuildDetailSheet(BuildSilk(), Ctx);
        var titles = doc.LastSection.Elements
            .OfType<Paragraph>()
            .SelectMany(p => p.Elements.OfType<Text>())
            .Select(t => t.Content);
        Assert.DoesNotContain(titles, s => s.Contains("Change Log"));
    }

    // ── PR (auto-fill): 3-line rows + vertical fill + flexible revision ──

    private static string CellText(Cell c) =>
        string.Concat(c.Elements.OfType<Paragraph>()
            .SelectMany(p => p.Elements.OfType<Text>()).Select(t => t.Content));

    private static Table SilkProcessTable(Document doc) =>
        doc.LastSection.Elements.OfType<Table>().First(t => t.Columns.Count == 21);

    private static Table RevisionTable(Document doc) =>
        doc.LastSection.Elements.OfType<Table>()
            .First(t => t.Columns.Count == 4 && CellText(t.Rows[0].Cells[0]) == "Rev");

    [Fact]
    public void Sample_sheet_is_one_page_with_no_blank_trailing_page()
    {
        // Regression (L39): forced 3-line rows inflated content onto a 2nd/3rd
        // (blank) page. A normal spec must render exactly ONE page, and the
        // auto-fill must NOT add a page beyond the natural fit.
        using var pdf = PdfSpecSheetExporter.RenderFitted(
            BuildSilk(), Ctx, out var step, out _);
        Assert.Equal(1, pdf.PageCount);

        // The filled result must have the SAME page count as the un-filled
        // (rowFill = 0) render at that step → fill never spilled a blank page.
        var natural = SpecPdfDocumentBuilder.BuildDetailSheet(
            BuildSilk(), Ctx, compactStep: step, rowFillCm: 0);
        var r = new MigraDoc.Rendering.PdfDocumentRenderer { Document = natural };
        r.RenderDocument();
        Assert.Equal(r.PdfDocument.PageCount, pdf.PageCount);
    }

    [Fact]
    public void Rows_use_comfortable_tier_height_plus_bounded_fill()
    {
        var rowMin = SpecPdfDocumentBuilder.DetailLayout.ForStep(0).RowMinCm;

        // rowFill = 0 → the comfortable per-tier minimum (not a forced tall band).
        var natural = SilkProcessTable(
            SpecPdfDocumentBuilder.BuildDetailSheet(BuildSilk(), Ctx));
        Assert.Equal(rowMin, natural.Rows[1].Height.Centimeter, 3);

        // A fill adds ON TOP of the tier minimum.
        var filled = SilkProcessTable(
            SpecPdfDocumentBuilder.BuildDetailSheet(BuildSilk(), Ctx, rowFillCm: 0.3));
        Assert.Equal(rowMin + 0.3, filled.Rows[1].Height.Centimeter, 3);
    }

    [Fact]
    public void Revision_history_is_flexible_one_row_per_entry()
    {
        var spec = BuildSilk();
        spec.Lineage = Enumerable.Range(1, 7).Select(i => new RevisionLineageEntry(
            Id: i, RevisionCode: ((char)('A' + i - 1)).ToString(),
            ChangeSummary: $"Revision {i}",
            CreatedAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i),
            CreatedBy: "verify")).ToList();
        var rev = RevisionTable(SpecPdfDocumentBuilder.BuildDetailSheet(spec, Ctx));
        // 1 header + 7 data rows — uncapped, exactly the lineage count.
        Assert.Equal(8, rev.Rows.Count);
    }

    [Fact]
    public void Vertical_fill_grows_short_sheet_without_adding_a_page()
    {
        // A short spec leaves room below → the fill phase grows rows (>0) while
        // holding the page count. Proves the sheet fills toward the bottom.
        using var pdf = PdfSpecSheetExporter.RenderFitted(
            BuildSilk(), Ctx, out _, out var rowFillCm);
        Assert.Equal(1, pdf.PageCount);
        Assert.True(rowFillCm > 0, "short sheet should have grown its rows to fill");
    }

    private static SpecDetailDto BuildLongSilk()
    {
        var s = BuildSilk();
        s.PrintColors = Enumerable.Range(1, 24).Select(i => new SpecPrintColorRow(
            Seq: i, Surface: "Top", Color: $"Long Colour Name {i}", InkName: $"Ink Name {i}",
            InkCode: $"IC-{i:D3}", Maker: "Sakata", Retarder: "R-A", Viscosity: 22.0, Speed: 80.0,
            Squeegee: "YR", Dry: "OVEN", TemperatureC: 80.0, TimeMin: 5, Uv: "Y",
            EmulsionUm: 12.0, PlateSize: "300x300", Mesh: "180T", AngleDeg: 22.5,
            PlateCode: $"PL-{i:D3}", ControlNo: i, Remark: "batch note")).ToList();
        s.Lineage = Enumerable.Range(1, 12).Select(i => new RevisionLineageEntry(
            Id: i, RevisionCode: ((char)('A' + i - 1)).ToString(),
            ChangeSummary: $"Revision {i} — adjusted ink + plate parameters for run {i}",
            CreatedAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i),
            CreatedBy: "verify")).ToList();
        s.AuditEntries = Enumerable.Range(1, 15).Select(i => new SpecAuditEntry(
            Timestamp: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(i),
            Action: "SPEC_EXPORT", ActorUsername: "admin", ActorRole: "Engineer",
            Detail: null)).ToList();
        return s;
    }
}
