using CCL.MES.Application;
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
    /// <summary>Shared style constants — reuse cho list + detail sheet PR #33.</summary>
    public static class StyleConstants
    {
        public const string PrimaryColorHex = "#0033A0";   // CCL brand blue
        public const string MutedColorHex   = "#6B7280";   // tailwind gray-500
        public const string HeaderBgHex     = "#E5E7EB";   // tailwind gray-200
        public const string BorderColorHex  = "#D1D5DB";   // tailwind gray-300
        public const string AccentColorHex  = "#C8102E";   // CCL accent red (silk planner)

        public const double TitleFontPt    = 14;
        public const double SubtitleFontPt = 9;
        public const double TableFontPt    = 8;
        public const double HeaderFontPt   = 8.5;
        public const double FooterFontPt   = 7.5;
    }

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
                dataRow.Shading.Color = Color.Parse("#F9FAFB");
        }

        return doc;
    }
}
