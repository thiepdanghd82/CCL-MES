using System.Globalization;
using CCL.MES.Application;
using CCL.MES.Application.SpecDetail;
using CCL.MES.Application.SpecExport;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDocOrientation = MigraDoc.DocumentObjectModel.Orientation;

namespace CCL.MES.Infrastructure.SpecExport;

/// <summary>
/// Phase 8 PR #31c — Reusable PDF document builder cho NPI Spec module.
///
/// Public API:
///   - <see cref="BuildListView"/>: 14-col list export (PR #31c primary use)
///   - <see cref="BuildEmpty"/>: blank Document với CCL header style — entry
///     point cho PR #33 single-spec detail sheet (caller append sections)
///   - <see cref="StyleConstants"/>: shared color + font + spacing constants
///
/// MigraDoc DOM → PdfDocumentRenderer → PDF byte[]. Cross-platform pure .NET
/// (PDFsharp-MigraDoc 6.2.4 — không native deps; chạy Linux/macOS/Windows).
///
/// Caller (XlsxSpecListExporter + PdfSpecListExporter + future PR #33
/// detail builder) chỉ định nội dung; STYLE constants centralized ở đây.
/// </summary>
public static class SpecPdfDocumentBuilder
{
    /// <summary>
    /// Shared style constants — reuse cho list + detail sheet PR #33.
    ///
    /// PR-A (showcard navy theme parity): swap palette từ SpecHub silk red /
    /// flexo royal-blue sang CCL-MES navy đồng nhất. Hex sources documented
    /// trong docs/PHASE8-SPEC-SHOWCARD-PLAN.md §4.1:
    ///   - PrimaryColorHex = #1f3864 (CCL-MES Login + IQC existing, đồng bộ app)
    ///   - MutedColorHex   = #6B7280 (tailwind gray-500, secondary text)
    ///   - HeaderBgHex     = #DDE6F3 (SpecHub direct navy-light, section title bar)
    ///   - BorderColorHex  = #B6C4DD (SpecHub direct navy-border)
    ///   - AccentColorHex  = #C00000 (user mốc customer red, Customer column highlight)
    ///   - AltRowBgHex     = #F5F8FC (user mốc navy-tinted alt row)
    ///   - ColHeaderBgHex  = #F0F2F5 (user mốc gray neutral col header — phân biệt section bar)
    ///   - ColHeaderTextHex= #6B7280 (tailwind gray-500)
    /// </summary>
    public static class StyleConstants
    {
        public const string PrimaryColorHex   = "#1F3864";   // CCL-MES navy (Login + IQC)
        public const string MutedColorHex     = "#6B7280";   // tailwind gray-500
        public const string HeaderBgHex       = "#DDE6F3";   // SpecHub navy-light (section title)
        public const string BorderColorHex    = "#B6C4DD";   // SpecHub navy-border
        public const string AccentColorHex    = "#C00000";   // Customer red (user mốc)
        public const string AltRowBgHex       = "#F5F8FC";   // Navy-tinted alt row (user mốc)
        public const string ColHeaderBgHex    = "#F0F2F5";   // Gray col header (user mốc)
        public const string ColHeaderTextHex  = "#6B7280";   // tailwind gray-500
        public const string NavyDarkHex       = "#1E3A73";   // SpecHub direct, heading text
        public const string NavyTintHex       = "#E8EEF7";   // SpecHub direct, cert/info bg
        public const string StampApprovedHex  = "#2E9B57";   // user mốc

        public const double TitleFontPt    = 14;
        public const double SubtitleFontPt = 9;
        public const double TableFontPt    = 8;
        public const double HeaderFontPt   = 8.5;
        public const double FooterFontPt   = 7.5;

        // PR (detail-2page): hairline table borders. Halved 0.25 → 0.125pt so
        // the exported sheet reads even finer (½ the previous weight). Kept in
        // one place — DetailLayout.BorderWidth + ApplyTableBorders + section
        // titles all read from here — so the whole detail sheet stays uniform.
        // List-view export keeps its own 0.4pt (see BuildListView).
        public const double DetailBorderWidthPt = 0.125;
    }

    /// <summary>
    /// PR (detail-2page) — tunable layout profile for the detail sheet so the
    /// exporter can AUTO-FIT the sheet to ≤ 2 pages: start at step 0 (normal),
    /// and if the rendered <c>PdfDocument.PageCount</c> exceeds 2, rebuild one
    /// step tighter (smaller body font + denser padding + tighter section
    /// spacing). Section titles + headers stay readable; only the body/table
    /// metrics shrink. Border width stays hairline at every step.
    /// </summary>
    public sealed class DetailLayout
    {
        public double TableBodyPt      { get; init; }   // flexo / remarks / rev / approval / changelog
        public double SmallTablePt     { get; init; }   // dense wide tables (product info, silk 21-col)
        public double SectionTitlePt   { get; init; }
        public double HeaderRowPt      { get; init; }
        public double BorderWidth      { get; init; } = StyleConstants.DetailBorderWidthPt;
        public double PadV             { get; init; }   // cell top/bottom padding
        public double PadH             { get; init; }   // cell left/right padding
        public double SectionSpaceBefore { get; init; }

        /// <summary>Comfortable MINIMUM row height (cm) for fillable rows at this
        /// tier. Roomy at the large tiers (few rows → airy, professional) and
        /// compact at the small tiers (many rows → pack them to still fit one
        /// page). Content is vertically centred so it never floats at the top.</summary>
        public double RowMinCm         { get; init; }

        /// <summary>Vertical auto-FILL — a SMALL bounded amount added on top of
        /// <see cref="RowMinCm"/> so a short sheet fills a little more of the
        /// page without stretching rows into grotesque bands. Binary-searched by
        /// the exporter (capped low). NOT part of ForStep.</summary>
        public double RowFillCm { get; set; }

        /// <summary>Largest step index <see cref="ForStep"/> accepts distinctly.
        /// The auto-fit keeps shrinking the font tier up to here so the sheet
        /// ALWAYS lands on one landscape page (only an extreme spec spills).</summary>
        public const int MaxStep = 6;

        public static DetailLayout ForStep(int step) => step switch
        {
            <= 0 => new DetailLayout { TableBodyPt = 7.5, SmallTablePt = 6.5, SectionTitlePt = 8.5, HeaderRowPt = 7.5, PadV = 0.6, PadH = 1.5, SectionSpaceBefore = 5.0, RowMinCm = 0.46 },
            1    => new DetailLayout { TableBodyPt = 7.0, SmallTablePt = 6.0, SectionTitlePt = 8.0, HeaderRowPt = 7.0, PadV = 0.5, PadH = 1.3, SectionSpaceBefore = 4.0, RowMinCm = 0.42 },
            2    => new DetailLayout { TableBodyPt = 6.5, SmallTablePt = 5.5, SectionTitlePt = 7.5, HeaderRowPt = 6.5, PadV = 0.4, PadH = 1.1, SectionSpaceBefore = 3.0, RowMinCm = 0.38 },
            3    => new DetailLayout { TableBodyPt = 6.0, SmallTablePt = 5.2, SectionTitlePt = 7.0, HeaderRowPt = 6.0, PadV = 0.35, PadH = 1.0, SectionSpaceBefore = 2.5, RowMinCm = 0.34 },
            4    => new DetailLayout { TableBodyPt = 5.5, SmallTablePt = 4.9, SectionTitlePt = 6.5, HeaderRowPt = 5.5, PadV = 0.3, PadH = 0.9, SectionSpaceBefore = 2.0, RowMinCm = 0.30 },
            5    => new DetailLayout { TableBodyPt = 5.0, SmallTablePt = 4.6, SectionTitlePt = 6.0, HeaderRowPt = 5.0, PadV = 0.28, PadH = 0.8, SectionSpaceBefore = 1.6, RowMinCm = 0.28 },
            _    => new DetailLayout { TableBodyPt = 4.6, SmallTablePt = 4.3, SectionTitlePt = 5.6, HeaderRowPt = 4.6, PadV = 0.25, PadH = 0.7, SectionSpaceBefore = 1.3, RowMinCm = 0.26 },
        };
    }

    /// <summary>Usable content width of the detail sheet = A4 landscape
    /// (29.7cm) − left/right margins (1.2cm each). Detail tables scale their
    /// columns to fill this so nothing is stranded at ~60% width with a big
    /// right-hand gap.</summary>
    private const double UsableWidthCm = 27.3;

    /// <summary>
    /// Build empty Document với CCL standard styles + page setup A4 landscape.
    /// Caller append sections. PR #33 sẽ dùng cho single-spec detail sheet
    /// (4 sub-section per category — silk colors / flexo print / flexo cut /
    /// flexo ink + signatures).
    /// </summary>
    public static Document BuildEmpty(string title, MigraDocOrientation orientation = MigraDocOrientation.Landscape)
    {
        // Local alias to keep the API parameter name self-explanatory.
        // (Keeps `MigraDocOrientation` only in the parameter signature.)
        var doc = new Document();
        doc.Info.Title = title;
        doc.Info.Author = "CCL Vietnam MES";

        var styles = doc.Styles;
        styles["Normal"]!.Font.Name = "Arial";
        styles["Normal"]!.Font.Size = StyleConstants.TableFontPt;

        var section = doc.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.Orientation = orientation;
        section.PageSetup.TopMargin = "1.5cm";
        section.PageSetup.BottomMargin = "1.5cm";
        section.PageSetup.LeftMargin = "1.2cm";
        section.PageSetup.RightMargin = "1.2cm";
        section.PageSetup.HeaderDistance = "0.6cm";
        section.PageSetup.FooterDistance = "0.6cm";

        return doc;
    }

    /// <summary>
    /// Build 14-col list view PDF (PR #31c). Header: title + filter desc +
    /// row count. Body: 14-col table với auto-fit columns. Footer: page X of Y +
    /// generated-by stamp.
    /// </summary>
    public static Document BuildListView(
        IReadOnlyList<ProductRevisionListItem> rows,
        SpecExportContext context)
    {
        var doc = BuildEmpty(context.Title);
        var section = doc.LastSection;
        var cols = SpecListColumns.All;

        // ── Page Header (title strip) ────────────────────────────────────────
        var hdr = section.Headers.Primary.AddParagraph();
        hdr.Format.Alignment = ParagraphAlignment.Left;
        var titleFmt = hdr.AddFormattedText(context.Title, TextFormat.Bold);
        titleFmt.Size = StyleConstants.TitleFontPt;
        titleFmt.Color = Color.Parse(StyleConstants.PrimaryColorHex);
        hdr.AddLineBreak();
        var stamp = hdr.AddFormattedText(
            $"Generated {context.GeneratedAt:yyyy-MM-dd HH:mm} · By {context.GeneratedBy} · Rows: {rows.Count}",
            TextFormat.NotBold);
        stamp.Size = StyleConstants.SubtitleFontPt;
        stamp.Color = Color.Parse(StyleConstants.MutedColorHex);

        if (!string.IsNullOrWhiteSpace(context.FilterDescription))
        {
            hdr.AddLineBreak();
            var f = hdr.AddFormattedText($"Filter: {context.FilterDescription}", TextFormat.Italic);
            f.Size = StyleConstants.SubtitleFontPt;
            f.Color = Color.Parse(StyleConstants.MutedColorHex);
        }

        // ── Page Footer ──────────────────────────────────────────────────────
        var ftr = section.Footers.Primary.AddParagraph();
        ftr.Format.Alignment = ParagraphAlignment.Center;
        ftr.Format.Font.Size = StyleConstants.FooterFontPt;
        ftr.Format.Font.Color = Color.Parse(StyleConstants.MutedColorHex);
        ftr.AddText("Page ");
        ftr.AddPageField();
        ftr.AddText(" / ");
        ftr.AddNumPagesField();

        // ── Body table ───────────────────────────────────────────────────────
        var table = section.AddTable();
        table.Borders.Color = Color.Parse(StyleConstants.BorderColorHex);
        table.Borders.Width = 0.4;
        table.LeftPadding = 2;
        table.RightPadding = 2;
        table.TopPadding = 1;
        table.BottomPadding = 1;
        table.Format.Font.Size = StyleConstants.TableFontPt;

        // Column widths tỉ lệ với SpecListColumn.WidthCh. Total page content
        // width A4 landscape minus margins ≈ 25.7cm. Sum WidthCh (# + 13 cols) =
        // 6 + 12+16+20+22+28+8+8+9+9+12+6+11+14 = 181. Scale 25.7cm / 181 ≈ 0.142cm/ch
        const double cmPerCh = 0.142;
        table.AddColumn(Unit.FromCentimeter(6 * cmPerCh));  // # column
        foreach (var c in cols)
        {
            table.AddColumn(Unit.FromCentimeter(c.WidthCh * cmPerCh));
        }

        // Header row (repeat per page via HeadingFormat)
        var headerRow = table.AddRow();
        headerRow.HeadingFormat = true;
        headerRow.Format.Font.Bold = true;
        headerRow.Format.Font.Size = StyleConstants.HeaderFontPt;
        headerRow.Shading.Color = Color.Parse(StyleConstants.HeaderBgHex);
        headerRow.Format.Alignment = ParagraphAlignment.Center;
        headerRow.Cells[0].AddParagraph("#");
        for (int c = 0; c < cols.Count; c++)
            headerRow.Cells[c + 1].AddParagraph(cols[c].Label);

        // Data rows
        for (int r = 0; r < rows.Count; r++)
        {
            var dataRow = table.AddRow();
            dataRow.Format.Alignment = ParagraphAlignment.Left;
            dataRow.Cells[0].AddParagraph((r + 1).ToString(context.Culture));
            dataRow.Cells[0].Format.Alignment = ParagraphAlignment.Right;
            var cells = SpecListColumns.ToDisplayCells(rows[r], context.Culture);
            for (int c = 0; c < cells.Length; c++)
            {
                var col = cols[c];
                var para = dataRow.Cells[c + 1].AddParagraph(cells[c] ?? "");
                if (col.Type == ColumnType.Int || col.Type == ColumnType.Decimal1)
                    para.Format.Alignment = ParagraphAlignment.Center;
            }
            // Alternating row shading cho readability
            if (r % 2 == 1)
                dataRow.Shading.Color = Color.Parse(StyleConstants.AltRowBgHex);
        }

        return doc;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Phase 8 PR #31d — Single-spec detail sheet PDF.
    //
    // Reuse BuildEmpty() entry point + StyleConstants. 9-section append
    // mirror EngineerSpecDetail.razor + SpecHub renderSilkscreenSpec/Flexo.
    // A4 portrait, sans-serif Arial (system font via SystemFontResolver).
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Build single-spec detail sheet PDF (PR #31d Q4). Reuse BuildEmpty +
    /// StyleConstants từ PR #31c. 9 section mirror web detail render.
    /// </summary>
    public static Document BuildDetailSheet(
        SpecDetailDto detail,
        SpecExportContext context,
        MigraDocOrientation orientation = MigraDocOrientation.Landscape,
        int compactStep = 0,
        double rowFillCm = 0.0)
    {
        // PR (detail-2page): A4 LANDSCAPE by default. The wide "Print Process"
        // table (21 cols) fits ONE line per row in landscape → the sheet drops
        // from 3 pages (portrait,每 row wrapping 2-3 lines) to ≤ 2. Orientation
        // stays a parameter so a caller can force Portrait if ever needed.
        // rowFillCm grows fillable rows so the sheet fills down toward the
        // bottom margin (the exporter binary-searches it).
        var layout = DetailLayout.ForStep(compactStep);
        layout.RowFillCm = rowFillCm;
        var doc = BuildEmpty(
            title: $"Spec Sheet {detail.RefNo ?? detail.SpecCode} Rev {detail.RevisionCode}",
            orientation: orientation);
        var section = doc.LastSection;

        // Page footer
        var ftr = section.Footers.Primary.AddParagraph();
        ftr.Format.Alignment = ParagraphAlignment.Center;
        ftr.Format.Font.Size = StyleConstants.FooterFontPt;
        ftr.Format.Font.Color = Color.Parse(StyleConstants.MutedColorHex);
        ftr.AddText($"{detail.SpecCode} Rev {detail.RevisionCode} · Generated {context.GeneratedAt:yyyy-MM-dd HH:mm} by {context.GeneratedBy} · Page ");
        ftr.AddPageField();
        ftr.AddText(" / ");
        ftr.AddNumPagesField();

        // PR-B: Planner-based template dispatch (was IsSilkscreen/IsFlexo flag-based).
        //   SILK / FLEXO → existing dedicated helpers (no render diff for current samples)
        //   GENERIC (INDIGO / LETTER / DIECUT / UNKNOWN) → warning paragraph + render
        //     only sections with data, mirroring Razor <SpecShowcard /> generic fallback.
        var template = ResolvePdfTemplate(detail.Planner);

        // 1. Doc header
        AppendDocHeader(section, detail, template);
        // 2. Compliance strip
        AppendComplianceStrip(section, detail);
        // 2b. Generic warning chip (PR-B Q5 — honest fallback signal in PDF too)
        if (template == "GENERIC")
            AppendGenericWarning(section, detail);
        // 3. Product Information
        AppendProductInfo(section, detail, layout);
        // 4. Print Parameters (silk only)
        if (template == "SILK")
            AppendPrintParams(section, detail, layout);
        // 5/5b/5c. Silk / Flexo 3 sub-tables / Generic data-driven sections
        if (template == "SILK")
        {
            AppendSilkPrintProcess(section, detail, layout);
        }
        else if (template == "FLEXO")
        {
            AppendFlexoPrinting(section, detail, layout);
            AppendFlexoCutting(section, detail, layout);
            AppendFlexoInk(section, detail, layout);
        }
        else // GENERIC
        {
            AppendGenericPrintSections(section, detail, layout);
        }
        // 6. Remarks
        AppendRemarks(section, detail, layout);
        // 7. Revision History
        AppendRevisionHistory(section, detail, layout);
        // 8. Approval Signatures (Option A render-only)
        AppendApprovalSignatures(section, detail, layout);
        // 9. Change Log — INTENTIONALLY omitted from the exported/printed sheet
        // (the audit timeline stays on-screen only). AppendChangeLog is kept as
        // a private helper in case a future "full audit" export needs it.

        return doc;
    }

    /// <summary>
    /// PR-B: mirror Razor SpecShowcard.ResolveTemplate logic — single dispatch
    /// from Planner enum. SILK / FLEXO → dedicated template; INDIGO / LETTER /
    /// DIECUT / UNKNOWN → generic fallback (only sections with data + warning).
    /// </summary>
    private static string ResolvePdfTemplate(string planner) => (planner ?? "UNKNOWN").Trim().ToUpperInvariant() switch
    {
        "SILK"  => "SILK",
        "FLEXO" => "FLEXO",
        _       => "GENERIC",
    };

    /// <summary>
    /// PR-B: <paramref name="template"/> drives the center-block label + subtitle.
    /// SILK / FLEXO render identical to PR-A; GENERIC swaps in "SPEC" + generic subtitle.
    /// </summary>
    private static void AppendDocHeader(Section section, SpecDetailDto d, string template = "SILK")
    {
        // Professional NAVY BAND header (mirrors the on-screen navy strip):
        // full-width navy-filled row, white text — company left / title centre /
        // REF + spec identity right. A subtle gold rule under the band separates
        // it from the body.
        var navy  = Color.Parse(StyleConstants.PrimaryColorHex);
        var band  = Colors.White;                       // text on the navy band
        var faint = Color.Parse("#C7D3EA");             // muted white-blue on navy

        var t = section.AddTable();
        t.Borders.Width = 0;
        t.Borders.Bottom.Width = 1.2;
        t.Borders.Bottom.Color = Color.Parse(StyleConstants.AccentColorHex); // slim accent rule
        t.LeftPadding = 8; t.RightPadding = 8; t.TopPadding = 5; t.BottomPadding = 5;
        t.AddColumn(Unit.FromCentimeter(UsableWidthCm * 6.0 / 18.0));
        t.AddColumn(Unit.FromCentimeter(UsableWidthCm * 7.0 / 18.0));
        t.AddColumn(Unit.FromCentimeter(UsableWidthCm * 5.0 / 18.0));
        var row = t.AddRow();
        row.Shading.Color = navy;
        row.VerticalAlignment = VerticalAlignment.Center;

        // Left — company
        var pCo = row.Cells[0].AddParagraph("Công ty TNHH CCL Design Việt Nam");
        pCo.Format.Font.Bold = true; pCo.Format.Font.Size = 9.5; pCo.Format.Font.Color = band;
        var pCoSub = row.Cells[0].AddParagraph("CCL Design Vietnam Co. Ltd");
        pCoSub.Format.Font.Size = 7; pCoSub.Format.Font.Color = faint;

        // Centre — full title + subtitle
        var (title, subtitle) = template switch
        {
            "FLEXO" => ("FLEXO LABEL SPECIFICATION", "Thông số kỹ thuật sản phẩm tiêu chuẩn In Nhãn Flexo"),
            "SILK"  => ("SILKSCREEN SPECIFICATION",  "Thông số kỹ thuật sản phẩm tiêu chuẩn In Lụa"),
            _       => ("PRODUCT SPECIFICATION",      "Thông số kỹ thuật sản phẩm"),
        };
        var pCenter = row.Cells[1].AddParagraph(title);
        pCenter.Format.Alignment = ParagraphAlignment.Center;
        pCenter.Format.Font.Size = 15; pCenter.Format.Font.Bold = true; pCenter.Format.Font.Color = band;
        var pSub = row.Cells[1].AddParagraph(subtitle);
        pSub.Format.Alignment = ParagraphAlignment.Center;
        pSub.Format.Font.Size = 7; pSub.Format.Font.Color = faint;

        // Right — REF NO + spec identity (Spec code · Rev · Status). No more
        // mislabelled "Inspection: <specCode>".
        var pRef = row.Cells[2].AddParagraph($"REF NO: {d.RefNo ?? "—"}");
        pRef.Format.Alignment = ParagraphAlignment.Right;
        pRef.Format.Font.Size = 9.5; pRef.Format.Font.Bold = true; pRef.Format.Font.Color = band;
        var specBits = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrWhiteSpace(d.SpecCode)) specBits.Add($"Spec {d.SpecCode}");
        specBits.Add($"Rev {d.RevisionCode}");
        if (!string.IsNullOrWhiteSpace(d.InspectionLevel)) specBits.Add($"Insp {d.InspectionLevel}");
        var pStamp = row.Cells[2].AddParagraph(string.Join("  ·  ", specBits) + $"   [{d.StatusDisplay}]");
        pStamp.Format.Alignment = ParagraphAlignment.Right;
        pStamp.Format.Font.Size = 7.5; pStamp.Format.Font.Color = faint;
    }

    /// <summary>
    /// PR-B: yellow warning paragraph rendered between Compliance strip and Product Info
    /// for GENERIC template — mirrors Razor SpecShowcard `.spec-generic-warning` chip.
    /// </summary>
    private static void AppendGenericWarning(Section section, SpecDetailDto d)
    {
        var label = (d.Planner ?? "UNKNOWN").Trim().ToUpperInvariant() switch
        {
            "LETTER" => "LETTERPRESS",
            "INDIGO" => "INDIGO",
            "DIECUT" => "DIE-CUT",
            _        => d.Planner ?? "UNKNOWN",
        };
        var p = section.AddParagraph(
            $"⚠ Generic preview — the standard {label} template will be added once a real CCL Vietnam sample is available.");
        p.Format.SpaceBefore = 4;
        p.Format.SpaceAfter = 8;
        p.Format.Font.Size = 8;
        p.Format.Font.Color = Color.Parse("#92400E");
        p.Format.Shading.Color = Color.Parse("#FEF3C7");
        p.Format.Borders.Color = Color.Parse("#FDE68A");
        p.Format.Borders.Width = 0.5;
        p.Format.LeftIndent = "0.15cm";
        p.Format.RightIndent = "0.15cm";
    }

    /// <summary>
    /// PR-B: Generic fallback — render only sub-spec sections WITH data
    /// (silk colors / flexo print / flexo cut / flexo ink). Reuse existing
    /// appenders where their shape fits (silk + flexo helpers already
    /// guard on empty rows via `spec-empty` cell). If NONE have data,
    /// emit an italic muted paragraph stating no print/cut/ink rows.
    /// </summary>
    private static void AppendGenericPrintSections(Section section, SpecDetailDto d, DetailLayout layout)
    {
        var hasSilkColors = d.PrintColors.Count > 0;
        var hasFlexoPrint = d.FlexoPrintRows.Count > 0;
        var hasFlexoCut   = d.FlexoCuttingRows.Count > 0;
        var hasFlexoInk   = d.FlexoInkRows.Count > 0;

        if (hasSilkColors) AppendSilkPrintProcess(section, d, layout);
        if (hasFlexoPrint) AppendFlexoPrinting(section, d, layout);
        if (hasFlexoCut)   AppendFlexoCutting(section, d, layout);
        if (hasFlexoInk)   AppendFlexoInk(section, d, layout);

        if (!hasSilkColors && !hasFlexoPrint && !hasFlexoCut && !hasFlexoInk)
        {
            var p = section.AddParagraph("— No print / cut / ink rows recorded yet —");
            p.Format.SpaceBefore = 8;
            p.Format.SpaceAfter = 8;
            p.Format.Font.Size = 9;
            p.Format.Font.Italic = true;
            p.Format.Font.Color = Color.Parse(StyleConstants.MutedColorHex);
            p.Format.Alignment = ParagraphAlignment.Center;
        }
    }

    private static void AppendComplianceStrip(Section section, SpecDetailDto d)
    {
        var p = section.AddParagraph($"Compliance: {string.Join(" · ", d.ComplianceChips)}");
        p.Format.SpaceBefore = 6;
        p.Format.SpaceAfter = 6;
        p.Format.Font.Size = 8;
        p.Format.Font.Color = Color.Parse(StyleConstants.MutedColorHex);
    }

    private static void AppendSectionTitle(Section section, string title, DetailLayout layout, string? bgHex = null)
    {
        var p = section.AddParagraph(title);
        p.Format.SpaceBefore = layout.SectionSpaceBefore;
        p.Format.SpaceAfter = 1;
        p.Format.Font.Bold = true;
        p.Format.Font.Size = layout.SectionTitlePt;
        p.Format.Shading.Color = Color.Parse(bgHex ?? StyleConstants.HeaderBgHex);
        p.Format.Borders.Color = Color.Parse(StyleConstants.BorderColorHex);
        p.Format.Borders.Width = layout.BorderWidth;
        p.Format.LeftIndent = "0.1cm";
        // Never orphan a section title at the bottom of a page — keep it with
        // the table/paragraph that follows so nothing spills to a stray page.
        p.Format.KeepWithNext = true;
    }

    private static void AppendProductInfo(Section section, SpecDetailDto d, DetailLayout layout)
    {
        AppendSectionTitle(section, "Product Information · Thông tin sản phẩm", layout);
        var t = section.AddTable();
        ApplyTableBorders(t, layout);
        t.Format.Font.Size = layout.SmallTablePt;
        var headers = d.IsFlexo
            ? new[] { "Customer", "Part No", "Part Name", "Version", "Size", "Substrate" }
            : new[] { "Customer", "Part No", "Part Name", "Material", "Mat Size", "Lamination", "Lam Size", "Lam Cav" };
        var values = d.IsFlexo
            ? new[] { d.CustomerName ?? "—", d.ProductCode, d.ProductName, "—",
                      d.ProductSizeDisplay, d.SubstrateType ?? "—" }
            : SilkProductValues(d);
        foreach (var _ in headers) t.AddColumn(Unit.FromCentimeter(UsableWidthCm / headers.Length));
        var hRow = t.AddRow();
        hRow.HeadingFormat = true;
        hRow.Format.Font.Bold = true;
        hRow.Shading.Color = Color.Parse(StyleConstants.HeaderBgHex);
        for (int i = 0; i < headers.Length; i++) hRow.Cells[i].AddParagraph(headers[i]);
        var dRow = t.AddRow();
        for (int i = 0; i < values.Length; i++) dRow.Cells[i].AddParagraph(values[i]);
    }

    private static string[] SilkProductValues(SpecDetailDto d)
    {
        var extra = ParseSilkMaterialExtra(d.MaterialExtraJson);
        return new[]
        {
            d.CustomerName ?? "—",
            d.ProductCode,
            d.ProductName,
            d.SubstrateType ?? "—",
            extra.MaterialSize ?? "—",
            extra.LaminationTape ?? "—",
            extra.LaminationSize ?? "—",
            extra.LaminationCavity ?? "—",
        };
    }

    private static (string? MaterialSize, string? LaminationTape, string? LaminationSize, string? LaminationCavity)
        ParseSilkMaterialExtra(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return (null, null, null, null);
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var r = doc.RootElement;
            string? GetStr(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : null;
            return (GetStr("material_size"), GetStr("lamination_tape"), GetStr("lamination_size"), GetStr("lamination_cavity"));
        }
        catch
        {
            return (null, null, null, null);
        }
    }

    private static void AppendPrintParams(Section section, SpecDetailDto d, DetailLayout layout)
    {
        AppendSectionTitle(section, "Print Parameters · Thông số in", layout);
        var t = section.AddTable();
        ApplyTableBorders(t, layout);
        t.Format.Font.Size = layout.TableBodyPt;
        for (int i = 0; i < 4; i++) t.AddColumn(Unit.FromCentimeter(UsableWidthCm / 4));
        var hRow = t.AddRow();
        hRow.HeadingFormat = true;
        hRow.Format.Font.Bold = true;
        hRow.Shading.Color = Color.Parse(StyleConstants.HeaderBgHex);
        hRow.Cells[0].AddParagraph("Printing Cavity");
        hRow.Cells[1].AddParagraph("Length Pitch (mm)");
        hRow.Cells[2].AddParagraph("Product Size");
        hRow.Cells[3].AddParagraph("Adhesive");
        var dRow = t.AddRow();
        dRow.Cells[0].AddParagraph(d.PrintingCavity?.ToString(CultureInfo.InvariantCulture) ?? "—");
        dRow.Cells[1].AddParagraph(d.LengthPitchMm?.ToString("N1", CultureInfo.InvariantCulture) ?? "—");
        dRow.Cells[2].AddParagraph(d.ProductSizeDisplay);
        dRow.Cells[3].AddParagraph(d.AdhesiveType ?? "—");
    }

    private static void AppendSilkPrintProcess(Section section, SpecDetailDto d, DetailLayout layout)
    {
        AppendSectionTitle(section, $"Print Process · Drying · Plate Parameter — {d.PrintColors.Count} colors", layout);
        if (d.PrintColors.Count == 0)
        {
            var p = section.AddParagraph("— No print rows —");
            p.Format.Font.Italic = true;
            p.Format.Font.Color = Color.Parse(StyleConstants.MutedColorHex);
            return;
        }
        var t = section.AddTable();
        ApplyTableBorders(t, layout);
        t.Format.Font.Size = layout.SmallTablePt;
        // PR (detail-2page): FULL 21-column silk table (was cut to 8 under the
        // old portrait constraint — data loss). In A4 landscape the usable
        // width (~27.3cm) holds every field ON ONE LINE per row, matching the
        // on-screen SpecShowcardFull. Widths ≈ content length so short codes
        // (Mesh/Angle/UV) stay narrow and Color/Ink Name get room → no wrap.
        var hdr = new[]
        {
            "No", "Surf", "Color", "Ink Name", "Ink Code", "Maker", "Retarder",
            "Visc", "Speed", "Squee", "Dry", "°C", "min", "UV", "Emul",
            "Plate Size", "Mesh", "Angle", "Plate Code", "Ctrl#", "Remark",
        };
        // Relative column WEIGHTS (not absolute cm). Long-content columns
        // (Color / Ink Name / Plate Code / Remark) get more; short codes stay
        // narrow. The weights are then SCALED to exactly fill the usable page
        // width so (a) the table isn't stranded at ~60% width with a big right
        // gap, and (b) long cells like "VIC-710 BLACK (1can=18kg)" fit on one
        // line instead of wrapping 2-3 rows.
        var weights = new[]
        {
            0.7, 0.8, 2.3, 3.3, 1.5, 1.5, 1.1,
            0.9, 0.9, 1.0, 1.0, 0.8, 0.8, 0.8, 1.0,
            1.7, 1.1, 1.0, 1.8, 0.9, 1.6,
        };
        AddScaledColumns(t, weights);
        var hRow = t.AddRow();
        hRow.HeadingFormat = true;   // repeat header on each page
        hRow.Format.Font.Bold = true;
        hRow.Format.Font.Size = layout.HeaderRowPt;
        hRow.Shading.Color = Color.Parse(StyleConstants.HeaderBgHex);
        for (int i = 0; i < hdr.Length; i++) hRow.Cells[i].AddParagraph(hdr[i]);

        string Num(double? v, string fmt) => v?.ToString(fmt, CultureInfo.InvariantCulture) ?? "—";
        var alt = false;
        foreach (var c in d.PrintColors)
        {
            var row = t.AddRow();
            SetFillRow(row, layout);
            if (alt) row.Shading.Color = Color.Parse(StyleConstants.AltRowBgHex);
            alt = !alt;
            row.Cells[0].AddParagraph(c.Seq.ToString(CultureInfo.InvariantCulture));
            row.Cells[1].AddParagraph(c.Surface ?? "—");
            row.Cells[2].AddParagraph(c.Color ?? "—");
            row.Cells[3].AddParagraph(c.InkName ?? "—");
            row.Cells[4].AddParagraph(c.InkCode ?? "—");
            row.Cells[5].AddParagraph(c.Maker ?? "—");
            row.Cells[6].AddParagraph(c.Retarder ?? "—");
            row.Cells[7].AddParagraph(Num(c.Viscosity, "N0"));
            row.Cells[8].AddParagraph(Num(c.Speed, "N0"));
            row.Cells[9].AddParagraph(c.Squeegee ?? "—");
            row.Cells[10].AddParagraph(c.Dry ?? "—");
            row.Cells[11].AddParagraph(Num(c.TemperatureC, "N0"));
            row.Cells[12].AddParagraph(c.TimeMin?.ToString(CultureInfo.InvariantCulture) ?? "—");
            row.Cells[13].AddParagraph(c.Uv ?? "—");
            row.Cells[14].AddParagraph(Num(c.EmulsionUm, "N0"));
            row.Cells[15].AddParagraph(c.PlateSize ?? "—");
            row.Cells[16].AddParagraph(c.Mesh ?? "—");
            row.Cells[17].AddParagraph(Num(c.AngleDeg, "N1"));
            row.Cells[18].AddParagraph(c.PlateCode ?? "—");
            row.Cells[19].AddParagraph(c.ControlNo?.ToString(CultureInfo.InvariantCulture) ?? "—");
            row.Cells[20].AddParagraph(c.Remark ?? "—");
        }
    }

    private static void AppendFlexoPrinting(Section section, SpecDetailDto d, DetailLayout layout)
    {
        AppendSectionTitle(section, $"Printing Information — {d.FlexoPrintRows.Count} processes", layout, "#CEE0FA");
        if (d.FlexoPrintRows.Count == 0) { AppendEmpty(section); return; }
        var t = section.AddTable();
        ApplyTableBorders(t, layout);
        t.Format.Font.Size = layout.TableBodyPt;
        var hdr = new[] { "Process", "Material", "Size", "Cyl", "Pitch", "Speed", "Plt Cav" };
        var widths = new[] { 3.5, 3.5, 2.0, 1.5, 2.0, 2.0, 1.5 };
        AddScaledColumns(t, widths);
        var hRow = t.AddRow();
        hRow.HeadingFormat = true;
        hRow.Format.Font.Bold = true;
        hRow.Shading.Color = Color.Parse(StyleConstants.HeaderBgHex);
        for (int i = 0; i < hdr.Length; i++) hRow.Cells[i].AddParagraph(hdr[i]);
        foreach (var r in d.FlexoPrintRows)
        {
            var row = t.AddRow();
            SetFillRow(row, layout);
            row.Cells[0].AddParagraph(r.Process ?? "—");
            row.Cells[1].AddParagraph(r.Material ?? "—");
            row.Cells[2].AddParagraph(r.Size ?? "—");
            row.Cells[3].AddParagraph(r.Cylinders ?? "—");
            row.Cells[4].AddParagraph(r.PitchMm ?? "—");
            row.Cells[5].AddParagraph(r.Speed ?? "—");
            row.Cells[6].AddParagraph(r.PlateCavity ?? "—");
        }
    }

    private static void AppendFlexoCutting(Section section, SpecDetailDto d, DetailLayout layout)
    {
        AppendSectionTitle(section, $"Cutting Information — {d.FlexoCuttingRows.Count} processes", layout, "#FFEACC");
        if (d.FlexoCuttingRows.Count == 0) { AppendEmpty(section); return; }
        var t = section.AddTable();
        ApplyTableBorders(t, layout);
        t.Format.Font.Size = layout.TableBodyPt;
        var hdr = new[] { "Process", "Lamination", "Cutter Name", "Pcs/Sh", "Cavity", "Pitch", "Packing" };
        var widths = new[] { 3.5, 2.5, 3.0, 1.3, 1.3, 1.5, 2.9 };
        AddScaledColumns(t, widths);
        var hRow = t.AddRow();
        hRow.HeadingFormat = true;
        hRow.Format.Font.Bold = true;
        hRow.Shading.Color = Color.Parse(StyleConstants.HeaderBgHex);
        for (int i = 0; i < hdr.Length; i++) hRow.Cells[i].AddParagraph(hdr[i]);
        foreach (var c in d.FlexoCuttingRows)
        {
            var row = t.AddRow();
            SetFillRow(row, layout);
            row.Cells[0].AddParagraph(c.Process ?? "—");
            row.Cells[1].AddParagraph(c.Lamination ?? "—");
            row.Cells[2].AddParagraph(c.CutterName ?? "—");
            row.Cells[3].AddParagraph(c.PcsPerSheet?.ToString(CultureInfo.InvariantCulture) ?? "—");
            row.Cells[4].AddParagraph(c.CuttingCavity?.ToString(CultureInfo.InvariantCulture) ?? "—");
            row.Cells[5].AddParagraph(c.PitchMm?.ToString("N1", CultureInfo.InvariantCulture) ?? "—");
            row.Cells[6].AddParagraph(c.Packing ?? "—");
        }
    }

    private static void AppendFlexoInk(Section section, SpecDetailDto d, DetailLayout layout)
    {
        AppendSectionTitle(section, $"Ink Information — {d.FlexoInkRows.Count} inks", layout, "#D8F0D6");
        if (d.FlexoInkRows.Count == 0) { AppendEmpty(section); return; }
        var t = section.AddTable();
        ApplyTableBorders(t, layout);
        t.Format.Font.Size = layout.TableBodyPt;
        var hdr = new[] { "No", "Color", "Ink Code", "Description", "Brand", "Anilox", "Plate", "UV" };
        var widths = new[] { 0.7, 2.5, 1.8, 4.0, 1.8, 1.8, 1.8, 1.6 };
        AddScaledColumns(t, widths);
        var hRow = t.AddRow();
        hRow.HeadingFormat = true;
        hRow.Format.Font.Bold = true;
        hRow.Shading.Color = Color.Parse(StyleConstants.HeaderBgHex);
        for (int i = 0; i < hdr.Length; i++) hRow.Cells[i].AddParagraph(hdr[i]);
        foreach (var i in d.FlexoInkRows)
        {
            var row = t.AddRow();
            SetFillRow(row, layout);
            row.Cells[0].AddParagraph(i.Seq.ToString());
            row.Cells[1].AddParagraph(i.Color ?? "—");
            row.Cells[2].AddParagraph(i.InkCode ?? "—");
            row.Cells[3].AddParagraph(i.InkDescription ?? "—");
            row.Cells[4].AddParagraph(i.Brand ?? "—");
            row.Cells[5].AddParagraph(i.Anilox ?? "—");
            row.Cells[6].AddParagraph(i.PlateCode ?? "—");
            row.Cells[7].AddParagraph(i.UvPowerW?.ToString("N0", CultureInfo.InvariantCulture) ?? "—");
        }
    }

    private static void AppendRemarks(Section section, SpecDetailDto d, DetailLayout layout)
    {
        AppendSectionTitle(section, "Remarks · Ghi chú", layout);
        if (d.IsFlexo)
        {
            var t = section.AddTable();
            ApplyTableBorders(t, layout);
            t.Format.Font.Size = layout.TableBodyPt;
            t.AddColumn(Unit.FromCentimeter(UsableWidthCm / 2));
            t.AddColumn(Unit.FromCentimeter(UsableWidthCm / 2));
            var hRow = t.AddRow();
            hRow.HeadingFormat = true;
            hRow.Format.Font.Bold = true;
            hRow.Cells[0].AddParagraph("Print Remarks");
            hRow.Cells[1].AddParagraph("Cut Remarks");
            var dRow = t.AddRow();
            dRow.Cells[0].AddParagraph(string.IsNullOrEmpty(d.RemarksText) ? "—" : d.RemarksText);
            dRow.Cells[1].AddParagraph(string.IsNullOrEmpty(d.RemarksCutText) ? "—" : d.RemarksCutText);
        }
        else
        {
            var p = section.AddParagraph(string.IsNullOrEmpty(d.RemarksText) ? "—" : $"※ {d.RemarksText}");
            p.Format.Font.Size = layout.TableBodyPt;
            p.Format.SpaceBefore = 2;
        }
    }

    private static void AppendRevisionHistory(Section section, SpecDetailDto d, DetailLayout layout)
    {
        AppendSectionTitle(section, "Revision History · Lịch sử thay đổi", layout);
        var t = section.AddTable();
        ApplyTableBorders(t, layout);
        t.Format.Font.Size = layout.TableBodyPt;
        // Rev / Contents / Date / By — weights scaled to fill the page width.
        t.AddColumn(Unit.FromCentimeter(UsableWidthCm * 1.5 / 18.0));
        t.AddColumn(Unit.FromCentimeter(UsableWidthCm * 11.5 / 18.0));
        t.AddColumn(Unit.FromCentimeter(UsableWidthCm * 2.5 / 18.0));
        t.AddColumn(Unit.FromCentimeter(UsableWidthCm * 2.5 / 18.0));
        var hRow = t.AddRow();
        hRow.HeadingFormat = true;
        hRow.Format.Font.Bold = true;
        hRow.Shading.Color = Color.Parse(StyleConstants.HeaderBgHex);
        hRow.Cells[0].AddParagraph("Rev");
        hRow.Cells[1].AddParagraph("Contents");
        hRow.Cells[2].AddParagraph("Date");
        hRow.Cells[3].AddParagraph("By");
        if (d.Lineage.Count == 0)
        {
            var row = t.AddRow();
            SetFillRow(row, layout);
            var cell0 = row.Cells[0]; cell0.MergeRight = 3;
            var p = cell0.AddParagraph("— No revision history —");
            p.Format.Font.Italic = true;
            p.Format.Alignment = ParagraphAlignment.Center;
        }
        // Revision History is FLEXIBLE — one row per lineage entry, uncapped.
        // Few revisions → rows grow taller (auto-fill); many → the table just
        // extends (and overflows to page 2 naturally if genuinely long).
        foreach (var lr in d.Lineage)
        {
            var row = t.AddRow();
            SetFillRow(row, layout);
            row.Cells[0].AddParagraph(lr.RevisionCode);
            row.Cells[1].AddParagraph(lr.ChangeSummary ?? "—");
            row.Cells[2].AddParagraph(lr.CreatedAt.ToString("yyyy-MM-dd"));
            row.Cells[3].AddParagraph(lr.CreatedBy ?? "—");
        }
    }

    private static void AppendApprovalSignatures(Section section, SpecDetailDto d, DetailLayout layout)
    {
        AppendSectionTitle(section, "Approval Signatures · Chữ ký phê duyệt", layout);
        var t = section.AddTable();
        ApplyTableBorders(t, layout);
        t.Format.Font.Size = layout.TableBodyPt;
        for (int i = 0; i < 4; i++) t.AddColumn(Unit.FromCentimeter(UsableWidthCm / 4));
        var hRow = t.AddRow();
        hRow.HeadingFormat = true;
        hRow.Format.Font.Bold = true;
        hRow.Shading.Color = Color.Parse(StyleConstants.HeaderBgHex);
        hRow.Cells[0].AddParagraph("R&D Issued");
        hRow.Cells[1].AddParagraph("R&D Confirmed");
        hRow.Cells[2].AddParagraph("PD Confirmed");
        hRow.Cells[3].AddParagraph("QA Confirmed");
        var nameRow = t.AddRow();
        nameRow.Format.Font.Bold = true;
        nameRow.Cells[0].AddParagraph(d.CreatedBy ?? "—");
        nameRow.Cells[1].AddParagraph(d.ApprovedBy ?? "—");
        nameRow.Cells[2].AddParagraph("—");
        nameRow.Cells[3].AddParagraph("—");
        var dateRow = t.AddRow();
        dateRow.Format.Font.Size = 6;
        dateRow.Format.Font.Color = Color.Parse(StyleConstants.MutedColorHex);
        dateRow.Cells[0].AddParagraph($"Date: {d.CreatedAt:yyyy-MM-dd}");
        dateRow.Cells[1].AddParagraph($"Date: {d.ApprovedAt?.ToString("yyyy-MM-dd") ?? "—"}");
        dateRow.Cells[2].AddParagraph("Date: —");
        dateRow.Cells[3].AddParagraph("Date: —");
        var note = section.AddParagraph("Note: PD + QA signature workflow ships in a later PR (approval-chain).");
        note.Format.Font.Size = 6;
        note.Format.Font.Italic = true;
        note.Format.Font.Color = Color.Parse(StyleConstants.MutedColorHex);
        note.Format.SpaceBefore = 2;
    }

    private static void AppendChangeLog(Section section, SpecDetailDto d, DetailLayout layout)
    {
        AppendSectionTitle(section, "Change Log · Audit timeline", layout, "#FEF3C7");
        if (d.AuditEntries.Count == 0)
        {
            var p = section.AddParagraph("— No audit entries —");
            p.Format.Font.Italic = true;
            p.Format.Font.Color = Color.Parse(StyleConstants.MutedColorHex);
            return;
        }
        foreach (var a in d.AuditEntries.Take(15))
        {
            var p = section.AddParagraph();
            p.Format.Font.Size = layout.TableBodyPt;
            var ts = p.AddFormattedText($"{a.Timestamp:yyyy-MM-dd HH:mm} ", TextFormat.NotBold);
            ts.Color = Color.Parse(StyleConstants.MutedColorHex);
            var act = p.AddFormattedText(a.Action, TextFormat.Bold);
            p.AddText($" · {a.ActorUsername}");
            if (!string.IsNullOrWhiteSpace(a.ActorRole)) p.AddText($" ({a.ActorRole})");
        }
    }

    private static void AppendEmpty(Section section)
    {
        var p = section.AddParagraph("— No rows —");
        p.Format.Font.Size = 8;
        p.Format.Font.Italic = true;
        p.Format.Font.Color = Color.Parse(StyleConstants.MutedColorHex);
    }

    /// <summary>Add columns whose RELATIVE weights are scaled to fill the
    /// usable page width — so a table never strands at ~60% width with a big
    /// right-hand gap (the "vỡ" look). Long-content columns just carry a
    /// bigger weight.</summary>
    private static void AddScaledColumns(Table t, double[] weights)
    {
        double sum = 0; foreach (var w in weights) sum += w;
        double scale = UsableWidthCm / sum;
        foreach (var w in weights) t.AddColumn(Unit.FromCentimeter(w * scale));
    }

    /// <summary>Give a fillable data row its COMFORTABLE per-tier minimum
    /// (<see cref="DetailLayout.RowMinCm"/>) PLUS the small bounded auto-fill
    /// (<see cref="DetailLayout.RowFillCm"/>), using <see cref="RowHeightRule.AtLeast"/>
    /// so a taller cell still expands (never clips). Content is vertically
    /// centred so it sits mid-row, not floating at the top. The fill is capped
    /// low so rows stay a clean, uniform band — never a grotesque tall box.</summary>
    private static void SetFillRow(Row row, DetailLayout layout)
    {
        row.HeightRule = RowHeightRule.AtLeast;
        row.Height = Unit.FromCentimeter(layout.RowMinCm + layout.RowFillCm);
        row.VerticalAlignment = VerticalAlignment.Center;
    }

    private static void ApplyTableBorders(Table t, DetailLayout layout)
    {
        t.Borders.Color = Color.Parse(StyleConstants.BorderColorHex);
        t.Borders.Width = layout.BorderWidth;   // hairline (0.25pt)
        t.LeftPadding = layout.PadH;
        t.RightPadding = layout.PadH;
        t.TopPadding = layout.PadV;
        t.BottomPadding = layout.PadV;
    }
}
