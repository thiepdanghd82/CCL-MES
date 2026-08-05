using System.Globalization;
using CCL.MES.Application.SpecDetail;
using CCL.MES.Application.SpecExport;
using ClosedXML.Excel;

namespace CCL.MES.Infrastructure.SpecExport;

/// <summary>
/// Single-spec DETAIL sheet → Excel (.xlsx). Companion to
/// <see cref="PdfSpecSheetExporter"/>: same sections (header · product info ·
/// print params · print process · remarks · revision history · approval), laid
/// out to mirror the on-screen showcard + the MigraDoc PDF. Uses the same navy
/// palette (<see cref="SpecPdfDocumentBuilder.StyleConstants"/>).
///
/// This is a DOCUMENT, not a data-grid:
///   • no auto-filter (headers stay clean, never clipped by a dropdown arrow);
///   • numeric cells carry real numbers (+ number-format), NOT text — so Excel
///     shows no green "number stored as text" triangle and columns right/centre
///     align naturally. Codes with leading zeros / letters / "*" stay TEXT;
///   • column widths are measured from the actual header + cell content so a
///     long header ("Retarder" / "Plate Code" / "Remark") is never cut;
///   • section titles are single-language (export language = English by default).
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
    private static readonly XLColor BorderClr  = XLColor.FromHtml(SpecPdfDocumentBuilder.StyleConstants.BorderColorHex);

    private const int Width = 21;   // widest section (silk print process) → sheet width

    // Column-width auto-measure bounds (Excel width units ≈ characters).
    private const double MinColWidth = 6.0;
    private const double MaxColWidth = 44.0;
    private const double ColWidthPad = 2.4;

    /// <summary>Numeric intent for a column — drives cell DataType + alignment.</summary>
    private enum Kind { Text, Int, Dec1 }

    private sealed record Col(string Header, Kind Kind = Kind.Text);

    public byte[] Export(SpecDetailDto d, SpecExportContext context)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Spec Sheet");
        var template = ResolveTemplate(d.Planner);

        // Per-column max content length (header + values), seeded 0 → measured
        // as Table() writes. Merged banners (title/meta/section/remarks) do NOT
        // contribute, so a long banner never inflates a column.
        var w = new double[Width];
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
        Section(ws, ref r, "Product Information");
        if (d.IsFlexo)
            Table(ws, ref r, w,
                new[] { new Col("Customer"), new Col("Part No"), new Col("Part Name"),
                        new Col("Version"), new Col("Size"), new Col("Substrate") },
                new[] { new object?[] { d.CustomerName, d.ProductCode, d.ProductName, "—", d.ProductSizeDisplay, d.SubstrateType } });
        else
        {
            var ex = ParseSilkMaterialExtra(d.MaterialExtraJson);
            Table(ws, ref r, w,
                new[] { new Col("Customer"), new Col("Part No"), new Col("Part Name"), new Col("Material"),
                        new Col("Mat Size"), new Col("Lamination"), new Col("Lam Size"), new Col("Lam Cav") },
                new[] { new object?[] { d.CustomerName, d.ProductCode, d.ProductName, d.SubstrateType,
                                        ex.MaterialSize, ex.LaminationTape, ex.LaminationSize, ex.LaminationCavity } });
        }
        r++;

        // ── Print Parameters (silk) ──────────────────────────────────────
        if (template == "SILK")
        {
            Section(ws, ref r, "Print Parameters");
            Table(ws, ref r, w,
                new[] { new Col("Printing Cavity", Kind.Int), new Col("Length Pitch (mm)", Kind.Dec1),
                        new Col("Product Size"), new Col("Adhesive") },
                new[] { new object?[] { d.PrintingCavity, d.LengthPitchMm, d.ProductSizeDisplay, d.AdhesiveType } });
            r++;
        }

        // ── Print Process ────────────────────────────────────────────────
        if (template == "SILK" && d.PrintColors.Count > 0)
        {
            Section(ws, ref r, $"Print Process · Drying · Plate Parameter — {d.PrintColors.Count} colors");
            var cols = new[]
            {
                new Col("No", Kind.Int), new Col("Surf"), new Col("Color"), new Col("Ink Name"),
                new Col("Ink Code"), new Col("Maker"), new Col("Retarder"), new Col("Visc", Kind.Int),
                new Col("Speed", Kind.Int), new Col("Squee"), new Col("Dry"), new Col("°C", Kind.Int),
                new Col("min", Kind.Int), new Col("UV", Kind.Int), new Col("Emul", Kind.Int),
                new Col("Plate Size"), new Col("Mesh"), new Col("Angle", Kind.Dec1),
                new Col("Plate Code"), new Col("Ctrl#", Kind.Int), new Col("Remark"),
            };
            var rows = d.PrintColors.Select(c => new object?[]
            {
                c.Seq, c.Surface, c.Color, c.InkName, c.InkCode, c.Maker, c.Retarder,
                c.Viscosity, c.Speed, c.Squeegee, c.Dry, c.TemperatureC, c.TimeMin,
                c.Uv, c.EmulsionUm, c.PlateSize, c.Mesh, c.AngleDeg, c.PlateCode, c.ControlNo, c.Remark,
            }).ToArray();
            Table(ws, ref r, w, cols, rows);
            r++;
        }
        else if (template == "FLEXO")
        {
            if (d.FlexoPrintRows.Count > 0)
            {
                Section(ws, ref r, $"Printing Information — {d.FlexoPrintRows.Count} processes");
                Table(ws, ref r, w,
                    new[] { new Col("Process"), new Col("Material"), new Col("Size"), new Col("Cyl"),
                            new Col("Pitch", Kind.Dec1), new Col("Speed", Kind.Int), new Col("Plt Cav") },
                    d.FlexoPrintRows.Select(p => new object?[] { p.Process, p.Material, p.Size,
                        p.Cylinders, p.PitchMm, p.Speed, p.PlateCavity }).ToArray());
                r++;
            }
            if (d.FlexoCuttingRows.Count > 0)
            {
                Section(ws, ref r, $"Cutting Information — {d.FlexoCuttingRows.Count} processes");
                Table(ws, ref r, w,
                    new[] { new Col("Process"), new Col("Lamination"), new Col("Cutter Name"),
                            new Col("Pcs/Sh", Kind.Int), new Col("Cavity", Kind.Int), new Col("Pitch", Kind.Dec1), new Col("Packing") },
                    d.FlexoCuttingRows.Select(c => new object?[] { c.Process, c.Lamination, c.CutterName,
                        c.PcsPerSheet, c.CuttingCavity, c.PitchMm, c.Packing }).ToArray());
                r++;
            }
            if (d.FlexoInkRows.Count > 0)
            {
                Section(ws, ref r, $"Ink Information — {d.FlexoInkRows.Count} inks");
                Table(ws, ref r, w,
                    new[] { new Col("No", Kind.Int), new Col("Color"), new Col("Ink Code"), new Col("Description"),
                            new Col("Brand"), new Col("Anilox"), new Col("Plate"), new Col("UV", Kind.Int) },
                    d.FlexoInkRows.Select(i => new object?[] { i.Seq, i.Color, i.InkCode,
                        i.InkDescription, i.Brand, i.Anilox, i.PlateCode, i.UvPowerW }).ToArray());
                r++;
            }
        }

        // ── Remarks ──────────────────────────────────────────────────────
        Section(ws, ref r, "Remarks");
        var rm = ws.Range(r, 1, r, Width).Merge();
        rm.Value = string.IsNullOrEmpty(d.RemarksText) ? "—" : d.RemarksText;
        rm.Style.Alignment.WrapText = true;
        r += 2;

        // ── Revision History (flexible — one row per lineage) ────────────
        Section(ws, ref r, "Revision History");
        Table(ws, ref r, w,
            new[] { new Col("Rev"), new Col("Contents"), new Col("Date"), new Col("By") },
            d.Lineage.Count == 0
                ? new[] { new object?[] { "—", "No revision history", "—", "—" } }
                : d.Lineage.Select(l => new object?[] { l.RevisionCode, l.ChangeSummary,
                    l.CreatedAt.ToString("yyyy-MM-dd"), l.CreatedBy }).ToArray());
        r++;

        // ── Approval Signatures ──────────────────────────────────────────
        Section(ws, ref r, "Approval Signatures");
        Table(ws, ref r, w,
            new[] { new Col("R&D Issued"), new Col("R&D Confirmed"), new Col("PD Confirmed"), new Col("QA Confirmed") },
            new[]
            {
                new object?[] { d.CreatedBy, d.ApprovedBy, "—", "—" },
                new object?[] { $"Date: {d.CreatedAt:yyyy-MM-dd}", $"Date: {d.ApprovedAt?.ToString("yyyy-MM-dd") ?? "—"}", "Date: —", "Date: —" },
            });

        // ── Sheet finish ─────────────────────────────────────────────────
        // Explicit widths measured from real content (header + cells) — no
        // AdjustToContents guessing, no auto-filter arrow eating header space.
        for (int c = 0; c < Width; c++)
        {
            double want = (w[c] <= 0 ? MinColWidth : w[c] + ColWidthPad);
            ws.Column(c + 1).Width = Math.Clamp(want, MinColWidth, MaxColWidth);
        }
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

    /// <summary>
    /// Header row (grey fill, bold, centred) + typed data rows (alt shading,
    /// numeric = real number + format + centre, text = left), thin borders.
    /// Records each column's widest content into <paramref name="w"/> so the
    /// sheet-finish step can size columns without clipping any header.
    /// </summary>
    private static void Table(IXLWorksheet ws, ref int r, double[] w, Col[] cols, object?[][] rows)
    {
        int startRow = r;

        // Header row.
        for (int c = 0; c < cols.Length; c++)
        {
            var cell = ws.Cell(r, c + 1);
            cell.Value = cols[c].Header;
            Measure(w, c, cols[c].Header);
        }
        var hRange = ws.Range(r, 1, r, cols.Length);
        hRange.Style.Font.Bold = true;
        hRange.Style.Fill.BackgroundColor = HeaderBg;
        hRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        r++;

        // Data rows.
        bool alt = false;
        foreach (var row in rows)
        {
            for (int c = 0; c < cols.Length; c++)
                WriteCell(ws.Cell(r, c + 1), c < row.Length ? row[c] : null, cols[c].Kind, w, c);
            if (alt) ws.Range(r, 1, r, cols.Length).Style.Fill.BackgroundColor = AltBg;
            alt = !alt;
            r++;
        }

        var full = ws.Range(startRow, 1, r - 1, cols.Length);
        full.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        full.Style.Border.InsideBorder = XLBorderStyleValues.Hair;
        full.Style.Border.OutsideBorderColor = BorderClr;
        full.Style.Border.InsideBorderColor = BorderClr;
    }

    /// <summary>
    /// Write one cell honouring its <see cref="Kind"/>. Numeric kinds parse the
    /// value to a real number (int/double/decimal or a numeric string) and set
    /// the cell as a NUMBER with a format + centre alignment — killing the
    /// "number stored as text" triangle. Non-numeric / blank values, and any
    /// value on a <see cref="Kind.Text"/> column (codes, "700*950", leading-zero
    /// part numbers), stay left-aligned text with an em-dash placeholder.
    /// </summary>
    private static void WriteCell(IXLCell cell, object? v, Kind kind, double[] w, int col)
    {
        double? num = kind == Kind.Text ? null : ToNumber(v);
        if (num.HasValue)
        {
            cell.Value = num.Value;
            cell.Style.NumberFormat.Format = kind == Kind.Int ? "0" : "0.0";
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            Measure(w, col, num.Value.ToString(kind == Kind.Int ? "0" : "0.0", CultureInfo.InvariantCulture));
            return;
        }

        var s = v?.ToString();
        if (string.IsNullOrWhiteSpace(s)) s = "—";
        cell.Value = s;
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        Measure(w, col, s);
    }

    private static double? ToNumber(object? v) => v switch
    {
        null            => null,
        double dd       => dd,
        float ff        => ff,
        int ii          => ii,
        long ll         => ll,
        decimal mm      => (double)mm,
        string ss when double.TryParse(ss, NumberStyles.Any, CultureInfo.InvariantCulture, out var p) => p,
        _               => null,
    };

    private static void Measure(double[] w, int col, string text)
    {
        if (col >= 0 && col < w.Length && text.Length > w[col]) w[col] = text.Length;
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
