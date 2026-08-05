using System.Globalization;
using CCL.MES.Application.SpecDetail;
using CCL.MES.Application.SpecExport;
using ClosedXML.Excel;

namespace CCL.MES.Infrastructure.SpecExport;

/// <summary>
/// Single-spec DETAIL sheet → Excel (.xlsx). Companion to
/// <see cref="PdfSpecSheetExporter"/>: same sections (header · product info ·
/// print params · print process · remarks · revision history · approval), but
/// as a structured, editable worksheet — one section per band, the wide Print
/// Process table as a proper filterable grid. Uses the same navy palette as the
/// PDF (<see cref="SpecPdfDocumentBuilder.StyleConstants"/>).
///
/// Change Log is intentionally omitted (on-screen only), matching the PDF.
/// </summary>
public class XlsxSpecSheetExporter
{
    public string ContentType => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static readonly XLColor Navy      = XLColor.FromHtml(SpecPdfDocumentBuilder.StyleConstants.PrimaryColorHex);
    private static readonly XLColor SectionBg = XLColor.FromHtml(SpecPdfDocumentBuilder.StyleConstants.HeaderBgHex);
    private static readonly XLColor HeaderBg  = XLColor.FromHtml(SpecPdfDocumentBuilder.StyleConstants.ColHeaderBgHex);
    private static readonly XLColor AltBg      = XLColor.FromHtml(SpecPdfDocumentBuilder.StyleConstants.AltRowBgHex);
    private const int Width = 21;   // widest section (silk print process) → sheet width

    public byte[] Export(SpecDetailDto d, SpecExportContext context)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Spec Sheet");
        var template = ResolveTemplate(d.Planner);
        int r = 1;

        // ── Title band (navy) ────────────────────────────────────────────
        var title = template switch
        {
            "FLEXO" => "FLEXO LABEL SPECIFICATION",
            "SILK"  => "SILKSCREEN SPECIFICATION",
            _       => "PRODUCT SPECIFICATION",
        };
        var titleRange = ws.Range(r, 1, r, Width).Merge();
        titleRange.Value = title;
        titleRange.Style.Fill.BackgroundColor = Navy;
        titleRange.Style.Font.FontColor = XLColor.White;
        titleRange.Style.Font.Bold = true;
        titleRange.Style.Font.FontSize = 14;
        titleRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Row(r).Height = 22;
        r++;

        // Meta row: company · REF · spec/rev/status
        var metaRange = ws.Range(r, 1, r, Width).Merge();
        var specBits = new List<string>();
        if (!string.IsNullOrWhiteSpace(d.SpecCode)) specBits.Add($"Spec {d.SpecCode}");
        specBits.Add($"Rev {d.RevisionCode}");
        if (!string.IsNullOrWhiteSpace(d.InspectionLevel)) specBits.Add($"Insp {d.InspectionLevel}");
        metaRange.Value =
            $"CCL Design Vietnam Co. Ltd      REF NO: {d.RefNo ?? "—"}      "
            + string.Join(" · ", specBits) + $"   [{d.StatusDisplay}]";
        metaRange.Style.Font.FontColor = Navy;
        metaRange.Style.Font.Bold = true;
        metaRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        r += 2;

        // Compliance
        if (d.ComplianceChips.Count > 0)
        {
            var c = ws.Range(r, 1, r, Width).Merge();
            c.Value = "Compliance: " + string.Join(" · ", d.ComplianceChips);
            c.Style.Font.Italic = true;
            r += 2;
        }

        // ── Product Information ──────────────────────────────────────────
        Section(ws, ref r, "Product Information · Thông tin sản phẩm");
        if (d.IsFlexo)
            Table(ws, ref r,
                new[] { "Customer", "Part No", "Part Name", "Version", "Size", "Substrate" },
                new[] { new[] { d.CustomerName ?? "—", d.ProductCode, d.ProductName, "—", d.ProductSizeDisplay, d.SubstrateType ?? "—" } });
        else
        {
            var ex = ParseSilkMaterialExtra(d.MaterialExtraJson);
            Table(ws, ref r,
                new[] { "Customer", "Part No", "Part Name", "Material", "Mat Size", "Lamination", "Lam Size", "Lam Cav" },
                new[] { new[] { d.CustomerName ?? "—", d.ProductCode, d.ProductName, d.SubstrateType ?? "—",
                                ex.MaterialSize ?? "—", ex.LaminationTape ?? "—", ex.LaminationSize ?? "—", ex.LaminationCavity ?? "—" } });
        }
        r++;

        // ── Print Parameters (silk) ──────────────────────────────────────
        if (template == "SILK")
        {
            Section(ws, ref r, "Print Parameters · Thông số in");
            Table(ws, ref r,
                new[] { "Printing Cavity", "Length Pitch (mm)", "Product Size", "Adhesive" },
                new[] { new[] {
                    d.PrintingCavity?.ToString(CultureInfo.InvariantCulture) ?? "—",
                    d.LengthPitchMm?.ToString("N1", CultureInfo.InvariantCulture) ?? "—",
                    d.ProductSizeDisplay, d.AdhesiveType ?? "—" } });
            r++;
        }

        // ── Print Process ────────────────────────────────────────────────
        if (template == "SILK" && d.PrintColors.Count > 0)
        {
            Section(ws, ref r, $"Print Process · Drying · Plate Parameter — {d.PrintColors.Count} colors");
            var hdr = new[] { "No", "Surf", "Color", "Ink Name", "Ink Code", "Maker", "Retarder",
                "Visc", "Speed", "Squee", "Dry", "°C", "min", "UV", "Emul",
                "Plate Size", "Mesh", "Angle", "Plate Code", "Ctrl#", "Remark" };
            string N(double? v, string f) => v?.ToString(f, CultureInfo.InvariantCulture) ?? "—";
            var rows = d.PrintColors.Select(c => new[] {
                c.Seq.ToString(CultureInfo.InvariantCulture), c.Surface ?? "—", c.Color ?? "—", c.InkName ?? "—",
                c.InkCode ?? "—", c.Maker ?? "—", c.Retarder ?? "—", N(c.Viscosity, "N0"), N(c.Speed, "N0"),
                c.Squeegee ?? "—", c.Dry ?? "—", N(c.TemperatureC, "N0"), c.TimeMin?.ToString(CultureInfo.InvariantCulture) ?? "—",
                c.Uv ?? "—", N(c.EmulsionUm, "N0"), c.PlateSize ?? "—", c.Mesh ?? "—", N(c.AngleDeg, "N1"),
                c.PlateCode ?? "—", c.ControlNo?.ToString(CultureInfo.InvariantCulture) ?? "—", c.Remark ?? "—" }).ToArray();
            Table(ws, ref r, hdr, rows, filter: true);
            r++;
        }
        else if (template == "FLEXO")
        {
            if (d.FlexoPrintRows.Count > 0)
            {
                Section(ws, ref r, $"Printing Information — {d.FlexoPrintRows.Count} processes");
                Table(ws, ref r,
                    new[] { "Process", "Material", "Size", "Cyl", "Pitch", "Speed", "Plt Cav" },
                    d.FlexoPrintRows.Select(p => new[] { p.Process ?? "—", p.Material ?? "—", p.Size ?? "—",
                        p.Cylinders ?? "—", p.PitchMm ?? "—", p.Speed ?? "—", p.PlateCavity ?? "—" }).ToArray());
                r++;
            }
            if (d.FlexoCuttingRows.Count > 0)
            {
                Section(ws, ref r, $"Cutting Information — {d.FlexoCuttingRows.Count} processes");
                Table(ws, ref r,
                    new[] { "Process", "Lamination", "Cutter Name", "Pcs/Sh", "Cavity", "Pitch", "Packing" },
                    d.FlexoCuttingRows.Select(c => new[] { c.Process ?? "—", c.Lamination ?? "—", c.CutterName ?? "—",
                        c.PcsPerSheet?.ToString(CultureInfo.InvariantCulture) ?? "—", c.CuttingCavity?.ToString(CultureInfo.InvariantCulture) ?? "—",
                        c.PitchMm?.ToString("N1", CultureInfo.InvariantCulture) ?? "—", c.Packing ?? "—" }).ToArray());
                r++;
            }
            if (d.FlexoInkRows.Count > 0)
            {
                Section(ws, ref r, $"Ink Information — {d.FlexoInkRows.Count} inks");
                Table(ws, ref r,
                    new[] { "No", "Color", "Ink Code", "Description", "Brand", "Anilox", "Plate", "UV" },
                    d.FlexoInkRows.Select(i => new[] { i.Seq.ToString(CultureInfo.InvariantCulture), i.Color ?? "—", i.InkCode ?? "—",
                        i.InkDescription ?? "—", i.Brand ?? "—", i.Anilox ?? "—", i.PlateCode ?? "—",
                        i.UvPowerW?.ToString("N0", CultureInfo.InvariantCulture) ?? "—" }).ToArray());
                r++;
            }
        }

        // ── Remarks ──────────────────────────────────────────────────────
        Section(ws, ref r, "Remarks · Ghi chú");
        var rm = ws.Range(r, 1, r, Width).Merge();
        rm.Value = string.IsNullOrEmpty(d.RemarksText) ? "—" : d.RemarksText;
        r += 2;

        // ── Revision History (flexible — one row per lineage) ────────────
        Section(ws, ref r, "Revision History · Lịch sử thay đổi");
        Table(ws, ref r, new[] { "Rev", "Contents", "Date", "By" },
            d.Lineage.Count == 0
                ? new[] { new[] { "—", "No revision history", "—", "—" } }
                : d.Lineage.Select(l => new[] { l.RevisionCode, l.ChangeSummary ?? "—",
                    l.CreatedAt.ToString("yyyy-MM-dd"), l.CreatedBy ?? "—" }).ToArray());
        r++;

        // ── Approval Signatures ──────────────────────────────────────────
        Section(ws, ref r, "Approval Signatures · Chữ ký phê duyệt");
        Table(ws, ref r,
            new[] { "R&D Issued", "R&D Confirmed", "PD Confirmed", "QA Confirmed" },
            new[]
            {
                new[] { d.CreatedBy ?? "—", d.ApprovedBy ?? "—", "—", "—" },
                new[] { $"Date: {d.CreatedAt:yyyy-MM-dd}", $"Date: {d.ApprovedAt?.ToString("yyyy-MM-dd") ?? "—"}", "Date: —", "Date: —" },
            });

        // ── Sheet finish ─────────────────────────────────────────────────
        ws.Columns(1, Width).AdjustToContents(8.0, 40.0);
        ws.SheetView.FreezeRows(2);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    /// <summary>Full-width section title band (navy-light fill, navy bold).</summary>
    private static void Section(IXLWorksheet ws, ref int r, string title)
    {
        var band = ws.Range(r, 1, r, Width).Merge();
        band.Value = title;
        band.Style.Fill.BackgroundColor = SectionBg;
        band.Style.Font.Bold = true;
        band.Style.Font.FontColor = Navy;
        r++;
    }

    /// <summary>Header row (grey fill, bold, centred) + data rows (alt shading),
    /// thin borders. Optionally attach an Excel auto-filter to the range.</summary>
    private static void Table(IXLWorksheet ws, ref int r, string[] headers, string[][] rows, bool filter = false)
    {
        int startRow = r;
        for (int c = 0; c < headers.Length; c++) ws.Cell(r, c + 1).Value = headers[c];
        var hRange = ws.Range(r, 1, r, headers.Length);
        hRange.Style.Font.Bold = true;
        hRange.Style.Fill.BackgroundColor = HeaderBg;
        hRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        r++;

        bool alt = false;
        foreach (var row in rows)
        {
            for (int c = 0; c < row.Length; c++) ws.Cell(r, c + 1).Value = row[c];
            if (alt) ws.Range(r, 1, r, headers.Length).Style.Fill.BackgroundColor = AltBg;
            alt = !alt;
            r++;
        }

        var full = ws.Range(startRow, 1, r - 1, headers.Length);
        full.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        full.Style.Border.InsideBorder = XLBorderStyleValues.Hair;
        full.Style.Border.OutsideBorderColor = XLColor.FromHtml(SpecPdfDocumentBuilder.StyleConstants.BorderColorHex);
        full.Style.Border.InsideBorderColor = XLColor.FromHtml(SpecPdfDocumentBuilder.StyleConstants.BorderColorHex);
        if (filter) ws.Range(startRow, 1, r - 1, headers.Length).SetAutoFilter();
    }

    private static string ResolveTemplate(string planner) => (planner ?? "UNKNOWN").Trim().ToUpperInvariant() switch
    {
        "SILK"  => "SILK",
        "FLEXO" => "FLEXO",
        _       => "GENERIC",
    };

    private static (string? MaterialSize, string? LaminationTape, string? LaminationSize, string? LaminationCavity)
        ParseSilkMaterialExtra(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return (null, null, null, null);
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            string? Get(string k) => root.TryGetProperty(k, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : null;
            return (Get("material_size"), Get("lamination_tape"), Get("lamination_size"), Get("lamination_cavity"));
        }
        catch { return (null, null, null, null); }
    }
}
