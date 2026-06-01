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
    public static Document BuildDetailSheet(SpecDetailDto detail, SpecExportContext context)
    {
        var doc = BuildEmpty(
            title: $"Spec Sheet {detail.RefNo ?? detail.SpecCode} Rev {detail.RevisionCode}",
            orientation: MigraDocOrientation.Portrait);
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

        // 1. Doc header
        AppendDocHeader(section, detail);
        // 2. Compliance strip
        AppendComplianceStrip(section, detail);
        // 3. Product Information
        AppendProductInfo(section, detail);
        // 4. Print Parameters (silk only)
        if (detail.IsSilkscreen)
            AppendPrintParams(section, detail);
        // 5/5b. Silk colors OR Flexo 3 sub-tables
        if (detail.IsSilkscreen)
        {
            AppendSilkPrintProcess(section, detail);
        }
        else if (detail.IsFlexo)
        {
            AppendFlexoPrinting(section, detail);
            AppendFlexoCutting(section, detail);
            AppendFlexoInk(section, detail);
        }
        // 6. Remarks
        AppendRemarks(section, detail);
        // 7. Revision History
        AppendRevisionHistory(section, detail);
        // 8. Approval Signatures (Option A render-only)
        AppendApprovalSignatures(section, detail);
        // 9. Change Log (audit timeline) — chỉ render top 10 entries cho PDF
        AppendChangeLog(section, detail);

        return doc;
    }

    private static void AppendDocHeader(Section section, SpecDetailDto d)
    {
        // 3-col table layout: company / center title / right block (REF NO + stamp)
        var t = section.AddTable();
        t.Borders.Color = Color.Parse(StyleConstants.BorderColorHex);
        t.Borders.Width = 0;
        t.Borders.Bottom.Width = 1.5;
        // PR-A: navy header band bottom border (đồng nhất app theme)
        t.Borders.Bottom.Color = Color.Parse(StyleConstants.PrimaryColorHex);
        t.AddColumn(Unit.FromCentimeter(6));
        t.AddColumn(Unit.FromCentimeter(7));
        t.AddColumn(Unit.FromCentimeter(5));
        var row = t.AddRow();
        // Left
        var pCo = row.Cells[0].AddParagraph("Công ty TNHH CCL Design Việt Nam");
        pCo.Format.Font.Bold = true;
        pCo.Format.Font.Size = 9;
        var pCoSub = row.Cells[0].AddParagraph("CCL Design Vietnam Co. Ltd");
        pCoSub.Format.Font.Size = 7;
        pCoSub.Format.Font.Color = Color.Parse(StyleConstants.MutedColorHex);
        // Center
        var pCenter = row.Cells[1].AddParagraph(d.IsFlexo ? "SEAL" : "SILK");
        pCenter.Format.Alignment = ParagraphAlignment.Center;
        pCenter.Format.Font.Size = 16;
        pCenter.Format.Font.Bold = true;
        pCenter.Format.Font.Color = // PR-A: unified navy — silk + flexo cùng PrimaryColorHex (KHÔNG còn AccentColorHex split silk-red)
            Color.Parse(StyleConstants.PrimaryColorHex);
        var pSub = row.Cells[1].AddParagraph(d.IsFlexo
            ? "Thông số kỹ thuật sản phẩm tiêu chuẩn In Nhãn Flexo"
            : "Thông số kỹ thuật sản phẩm tiêu chuẩn In Lụa");
        pSub.Format.Alignment = ParagraphAlignment.Center;
        pSub.Format.Font.Size = 7;
        pSub.Format.Font.Color = Color.Parse(StyleConstants.MutedColorHex);
        // Right
        var pRef = row.Cells[2].AddParagraph($"REF NO: {d.RefNo ?? "—"}");
        pRef.Format.Alignment = ParagraphAlignment.Right;
        pRef.Format.Font.Size = 9;
        pRef.Format.Font.Bold = true;
        pRef.Format.Font.Color = // PR-A: unified navy — silk + flexo cùng PrimaryColorHex (KHÔNG còn AccentColorHex split silk-red)
            Color.Parse(StyleConstants.PrimaryColorHex);
        var pStamp = row.Cells[2].AddParagraph($"Inspection: {d.InspectionLevel ?? "—"}  [{d.StatusDisplay}]");
        pStamp.Format.Alignment = ParagraphAlignment.Right;
        pStamp.Format.Font.Size = 8;
    }

    private static void AppendComplianceStrip(Section section, SpecDetailDto d)
    {
        var p = section.AddParagraph($"Compliance: {string.Join(" · ", d.ComplianceChips)}");
        p.Format.SpaceBefore = 6;
        p.Format.SpaceAfter = 6;
        p.Format.Font.Size = 8;
        p.Format.Font.Color = Color.Parse(StyleConstants.MutedColorHex);
    }

    private static void AppendSectionTitle(Section section, string title, string? bgHex = null)
    {
        var p = section.AddParagraph(title);
        p.Format.SpaceBefore = 6;
        p.Format.Font.Bold = true;
        p.Format.Font.Size = StyleConstants.HeaderFontPt;
        p.Format.Shading.Color = Color.Parse(bgHex ?? StyleConstants.HeaderBgHex);
        p.Format.Borders.Color = Color.Parse(StyleConstants.BorderColorHex);
        p.Format.Borders.Width = 0.4;
        p.Format.LeftIndent = "0.1cm";
    }

    private static void AppendProductInfo(Section section, SpecDetailDto d)
    {
        AppendSectionTitle(section, "Product Information · Thông tin sản phẩm");
        var t = section.AddTable();
        ApplyTableBorders(t);
        t.Format.Font.Size = 7.5;
        var headers = d.IsFlexo
            ? new[] { "Customer", "Part No", "Part Name", "Version", "Size", "Substrate" }
            : new[] { "Customer", "Part No", "Part Name", "Material", "Mat Size", "Lamination", "Lam Size", "Lam Cav" };
        var values = d.IsFlexo
            ? new[] { d.CustomerName ?? "—", d.ProductCode, d.ProductName, "—",
                      d.ProductSizeDisplay, d.SubstrateType ?? "—" }
            : SilkProductValues(d);
        foreach (var _ in headers) t.AddColumn(Unit.FromCentimeter(2.8));
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

    private static void AppendPrintParams(Section section, SpecDetailDto d)
    {
        AppendSectionTitle(section, "Print Parameters · Thông số in");
        var t = section.AddTable();
        ApplyTableBorders(t);
        t.Format.Font.Size = 8;
        for (int i = 0; i < 4; i++) t.AddColumn(Unit.FromCentimeter(4.5));
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

    private static void AppendSilkPrintProcess(Section section, SpecDetailDto d)
    {
        AppendSectionTitle(section, $"Print Process — {d.PrintColors.Count} colors");
        if (d.PrintColors.Count == 0)
        {
            var p = section.AddParagraph("— No print rows —");
            p.Format.Font.Italic = true;
            p.Format.Font.Color = Color.Parse(StyleConstants.MutedColorHex);
            return;
        }
        var t = section.AddTable();
        ApplyTableBorders(t);
        t.Format.Font.Size = 6.5;
        // 8 essential cols (rest fit in landscape would be needed) — portrait constraint
        var hdr = new[] { "No", "Sur", "Color", "Ink Code", "Maker", "Mesh", "Plate Code", "Remark" };
        var widths = new[] { 0.7, 0.8, 3.2, 1.8, 1.5, 1.5, 2.0, 6.5 };
        for (int i = 0; i < hdr.Length; i++) t.AddColumn(Unit.FromCentimeter(widths[i]));
        var hRow = t.AddRow();
        hRow.HeadingFormat = true;
        hRow.Format.Font.Bold = true;
        hRow.Shading.Color = Color.Parse(StyleConstants.HeaderBgHex);
        for (int i = 0; i < hdr.Length; i++) hRow.Cells[i].AddParagraph(hdr[i]);
        foreach (var c in d.PrintColors)
        {
            var row = t.AddRow();
            row.Cells[0].AddParagraph(c.Seq.ToString());
            row.Cells[1].AddParagraph(c.Surface ?? "—");
            row.Cells[2].AddParagraph(c.Color ?? "—");
            row.Cells[3].AddParagraph(c.InkCode ?? "—");
            row.Cells[4].AddParagraph(c.Maker ?? "—");
            row.Cells[5].AddParagraph(c.Mesh ?? "—");
            row.Cells[6].AddParagraph(c.PlateCode ?? "—");
            row.Cells[7].AddParagraph(c.Remark ?? "—");
        }
    }

    private static void AppendFlexoPrinting(Section section, SpecDetailDto d)
    {
        AppendSectionTitle(section, $"Printing Information — {d.FlexoPrintRows.Count} processes", "#CEE0FA");
        if (d.FlexoPrintRows.Count == 0) { AppendEmpty(section); return; }
        var t = section.AddTable();
        ApplyTableBorders(t);
        t.Format.Font.Size = 7;
        var hdr = new[] { "Process", "Material", "Size", "Cyl", "Pitch", "Speed", "Plt Cav" };
        var widths = new[] { 3.5, 3.5, 2.0, 1.5, 2.0, 2.0, 1.5 };
        for (int i = 0; i < hdr.Length; i++) t.AddColumn(Unit.FromCentimeter(widths[i]));
        var hRow = t.AddRow();
        hRow.HeadingFormat = true;
        hRow.Format.Font.Bold = true;
        hRow.Shading.Color = Color.Parse(StyleConstants.HeaderBgHex);
        for (int i = 0; i < hdr.Length; i++) hRow.Cells[i].AddParagraph(hdr[i]);
        foreach (var r in d.FlexoPrintRows)
        {
            var row = t.AddRow();
            row.Cells[0].AddParagraph(r.Process ?? "—");
            row.Cells[1].AddParagraph(r.Material ?? "—");
            row.Cells[2].AddParagraph(r.Size ?? "—");
            row.Cells[3].AddParagraph(r.Cylinders ?? "—");
            row.Cells[4].AddParagraph(r.PitchMm ?? "—");
            row.Cells[5].AddParagraph(r.Speed ?? "—");
            row.Cells[6].AddParagraph(r.PlateCavity ?? "—");
        }
    }

    private static void AppendFlexoCutting(Section section, SpecDetailDto d)
    {
        AppendSectionTitle(section, $"Cutting Information — {d.FlexoCuttingRows.Count} processes", "#FFEACC");
        if (d.FlexoCuttingRows.Count == 0) { AppendEmpty(section); return; }
        var t = section.AddTable();
        ApplyTableBorders(t);
        t.Format.Font.Size = 7;
        var hdr = new[] { "Process", "Lamination", "Cutter Name", "Pcs/Sh", "Cavity", "Pitch", "Packing" };
        var widths = new[] { 3.5, 2.5, 3.0, 1.3, 1.3, 1.5, 2.9 };
        for (int i = 0; i < hdr.Length; i++) t.AddColumn(Unit.FromCentimeter(widths[i]));
        var hRow = t.AddRow();
        hRow.HeadingFormat = true;
        hRow.Format.Font.Bold = true;
        hRow.Shading.Color = Color.Parse(StyleConstants.HeaderBgHex);
        for (int i = 0; i < hdr.Length; i++) hRow.Cells[i].AddParagraph(hdr[i]);
        foreach (var c in d.FlexoCuttingRows)
        {
            var row = t.AddRow();
            row.Cells[0].AddParagraph(c.Process ?? "—");
            row.Cells[1].AddParagraph(c.Lamination ?? "—");
            row.Cells[2].AddParagraph(c.CutterName ?? "—");
            row.Cells[3].AddParagraph(c.PcsPerSheet?.ToString(CultureInfo.InvariantCulture) ?? "—");
            row.Cells[4].AddParagraph(c.CuttingCavity?.ToString(CultureInfo.InvariantCulture) ?? "—");
            row.Cells[5].AddParagraph(c.PitchMm?.ToString("N1", CultureInfo.InvariantCulture) ?? "—");
            row.Cells[6].AddParagraph(c.Packing ?? "—");
        }
    }

    private static void AppendFlexoInk(Section section, SpecDetailDto d)
    {
        AppendSectionTitle(section, $"Ink Information — {d.FlexoInkRows.Count} inks", "#D8F0D6");
        if (d.FlexoInkRows.Count == 0) { AppendEmpty(section); return; }
        var t = section.AddTable();
        ApplyTableBorders(t);
        t.Format.Font.Size = 7;
        var hdr = new[] { "No", "Color", "Ink Code", "Description", "Brand", "Anilox", "Plate", "UV" };
        var widths = new[] { 0.7, 2.5, 1.8, 4.0, 1.8, 1.8, 1.8, 1.6 };
        for (int i = 0; i < hdr.Length; i++) t.AddColumn(Unit.FromCentimeter(widths[i]));
        var hRow = t.AddRow();
        hRow.HeadingFormat = true;
        hRow.Format.Font.Bold = true;
        hRow.Shading.Color = Color.Parse(StyleConstants.HeaderBgHex);
        for (int i = 0; i < hdr.Length; i++) hRow.Cells[i].AddParagraph(hdr[i]);
        foreach (var i in d.FlexoInkRows)
        {
            var row = t.AddRow();
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

    private static void AppendRemarks(Section section, SpecDetailDto d)
    {
        AppendSectionTitle(section, "Remarks · Ghi chú");
        if (d.IsFlexo)
        {
            var t = section.AddTable();
            ApplyTableBorders(t);
            t.Format.Font.Size = 8;
            t.AddColumn(Unit.FromCentimeter(9));
            t.AddColumn(Unit.FromCentimeter(9));
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
            p.Format.Font.Size = 8;
            p.Format.SpaceBefore = 2;
        }
    }

    private static void AppendRevisionHistory(Section section, SpecDetailDto d)
    {
        AppendSectionTitle(section, "Revision History · Lịch sử thay đổi");
        var t = section.AddTable();
        ApplyTableBorders(t);
        t.Format.Font.Size = 8;
        t.AddColumn(Unit.FromCentimeter(1.5));
        t.AddColumn(Unit.FromCentimeter(11.5));
        t.AddColumn(Unit.FromCentimeter(2.5));
        t.AddColumn(Unit.FromCentimeter(2.5));
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
            var cell0 = row.Cells[0]; cell0.MergeRight = 3;
            var p = cell0.AddParagraph("— No revision history —");
            p.Format.Font.Italic = true;
            p.Format.Alignment = ParagraphAlignment.Center;
        }
        foreach (var lr in d.Lineage)
        {
            var row = t.AddRow();
            row.Cells[0].AddParagraph(lr.RevisionCode);
            row.Cells[1].AddParagraph(lr.ChangeSummary ?? "—");
            row.Cells[2].AddParagraph(lr.CreatedAt.ToString("yyyy-MM-dd"));
            row.Cells[3].AddParagraph(lr.CreatedBy ?? "—");
        }
    }

    private static void AppendApprovalSignatures(Section section, SpecDetailDto d)
    {
        AppendSectionTitle(section, "Approval Signatures · Chữ ký phê duyệt");
        var t = section.AddTable();
        ApplyTableBorders(t);
        t.Format.Font.Size = 7;
        for (int i = 0; i < 4; i++) t.AddColumn(Unit.FromCentimeter(4.5));
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

    private static void AppendChangeLog(Section section, SpecDetailDto d)
    {
        AppendSectionTitle(section, "Change Log · Audit timeline", "#FEF3C7");
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
            p.Format.Font.Size = 7;
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

    private static void ApplyTableBorders(Table t)
    {
        t.Borders.Color = Color.Parse(StyleConstants.BorderColorHex);
        t.Borders.Width = 0.4;
        t.LeftPadding = 2;
        t.RightPadding = 2;
        t.TopPadding = 1.5;
        t.BottomPadding = 1.5;
    }
}
