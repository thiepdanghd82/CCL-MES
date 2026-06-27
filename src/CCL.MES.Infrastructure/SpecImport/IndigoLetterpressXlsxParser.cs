using System.Text.RegularExpressions;
using CCL.MES.Application.SpecImport;
using ClosedXML.Excel;
using static CCL.MES.Infrastructure.SpecImport.XlsxParserHelpers;

namespace CCL.MES.Infrastructure.SpecImport;

/// <summary>
/// P10.10 — dedicated parser for the Indigo (HP) and Letterpress (LP) seal
/// templates, replacing the silkscreen-layout fallback (the
/// "Planner 'indigo' chưa có parser riêng" warning).
///
/// Reference samples (READ-ONLY): <c>Data/Specs/HP_*.xlsx</c> (Indigo) +
/// <c>Data/Specs/LP_*.xlsx</c> (Letterpress). Both share the same conceptual
/// shape — header → product info → one PRINT process row → one CUT process row
/// → an ink-rows table (Color / Ink code / Ink description / Brand / UV power)
/// → remarks — but the column positions differ wildly between the two
/// templates (e.g. Part No sits at C4 for Indigo, C9 for Letterpress). So this
/// parser is purely LABEL-ANCHORED (<see cref="BuildColumnMap"/> over the
/// detected header rows) with no fixed-cell fallbacks, which lets the one
/// class serve both planners. The ink rows reuse the flexo ink-row entity
/// shape (<see cref="ParsedFlexoInkRowDto"/>) so persistence + the showcard
/// render path are shared with flexo.
/// </summary>
public class IndigoLetterpressXlsxParser : ISpecXlsxParser
{
    public ParsedSpecDto Parse(Stream xlsxStream, string forcedCategory)
    {
        using var normalized = XlsxNormalizer.Normalize(xlsxStream);
        using var wb = new XLWorkbook(normalized);
        var ws = wb.Worksheet(1);
        var aoa = WorksheetToAoa(ws);

        var detected = DetectSpecCategory(aoa);
        var category = string.IsNullOrWhiteSpace(forcedCategory) ? detected : forcedCategory;
        // Only indigo / letterpress belong here; normalise anything else onto
        // the detected value so the warning below is meaningful.
        if (!IsIndigoOrLetter(category)) category = IsIndigoOrLetter(detected) ? detected : "indigo";

        var spec = ParseInkBased(aoa);
        spec.Category = category;

        if (!IsIndigoOrLetter(detected) && IsIndigoOrLetter(forcedCategory))
        {
            spec.Warnings.Add($"File header gợi ý category='{detected}' nhưng operator chọn '{forcedCategory}' — dùng lựa chọn operator.");
        }

        return spec;
    }

    private static bool IsIndigoOrLetter(string? c)
        => string.Equals(c, "indigo", StringComparison.OrdinalIgnoreCase)
        || string.Equals(c, "letterpress", StringComparison.OrdinalIgnoreCase);

    private static ParsedSpecDto ParseInkBased(string[][] aoa)
    {
        var spec = new ParsedSpecDto { Category = "indigo" };

        // ── HEADER (label-anchored, shared with silk/flexo) ─────────────────
        var refNoLabel = FindValueAfterLabel(aoa, new Regex(@"số tham chiếu|ref\s*no", RegexOptions.IgnoreCase));
        spec.RefNo = !string.IsNullOrEmpty(refNoLabel)
            ? refNoLabel
            : FindCellMatching(aoa, new Regex(@"^CCL-(Silk|Seal|Flexo|Indigo|Letter)-", RegexOptions.IgnoreCase));

        // Prefer the explicit "Spec NN" stamp (LP carries one, e.g. "Spec 94").
        // The generic "value-after-label" scan is unsafe on the Indigo/LP header
        // because the Inspection-Level label sits immediately left of the REF NO
        // label, so it would grab "Số tham chiếu REF NO:" — reject any value that
        // looks like another label.
        var inspRaw = FindCellMatching(aoa, new Regex(@"^Spec\s+[A-Z]?\d+\w*$", RegexOptions.IgnoreCase));
        if (string.IsNullOrEmpty(inspRaw))
        {
            var afterLabel = FindValueAfterLabel(aoa, new Regex(@"mức độ kiểm tra|inspection level", RegexOptions.IgnoreCase));
            if (!Regex.IsMatch(afterLabel, @"tham chiếu|ref\s*no|[:]", RegexOptions.IgnoreCase))
                inspRaw = afterLabel;
        }
        spec.InspectionLevel = string.IsNullOrEmpty(inspRaw)
            ? "A"
            : Regex.Replace(inspRaw, @"^Spec\s*", "", RegexOptions.IgnoreCase).Trim();
        if (string.IsNullOrWhiteSpace(spec.InspectionLevel)) spec.InspectionLevel = "A";

        var stamp = FindCellMatching(aoa, new Regex(@"^(approved|draft|sample|in[\s_-]?review)$", RegexOptions.IgnoreCase));
        spec.ApprovalStamp = string.IsNullOrEmpty(stamp) ? "Approved" : stamp;

        var comp = FindCellMatching(aoa, new Regex(@"\brohs\b|\bcompliance\b|\bcompliant\b", RegexOptions.IgnoreCase));
        spec.Compliance = string.IsNullOrEmpty(comp) ? "RoHS Compliance" : comp;

        // ── PRODUCT INFO (label-anchored header row → data row +1) ───────────
        var piHeaderRow = FindRowByC1(aoa, new Regex(@"khách hàng|custom?mer", RegexOptions.IgnoreCase));
        if (piHeaderRow > 0)
        {
            var piMap = BuildColumnMap(aoa, new[] { piHeaderRow }, new Dictionary<string, Regex>
            {
                ["customer"] = new(@"khách hàng|custom?mer", RegexOptions.IgnoreCase),
                ["partNo"]   = new(@"mã sản phẩm|mã sp|part\s*no", RegexOptions.IgnoreCase),
                ["partName"] = new(@"tên linh kiện|tên sp|part\s*name", RegexOptions.IgnoreCase),
                ["version"]  = new(@"phiên bản|version", RegexOptions.IgnoreCase),
                ["size"]     = new(@"kích thước sp|product size", RegexOptions.IgnoreCase),
                ["diameter"] = new(@"đường kính|diameter", RegexOptions.IgnoreCase),
            }, WideCol);  // LP product columns reach C62 (> default maxCol 60)
            var d = piHeaderRow + 1;
            spec.Customer = GetCell(aoa, d, Col(piMap, "customer", 1));
            spec.PartNo   = GetCell(aoa, d, Col(piMap, "partNo", 4));
            spec.PartName = GetCell(aoa, d, Col(piMap, "partName", 10));
            spec.Version  = NullIfEmpty(GetCell(aoa, d, Col(piMap, "version", 18)));
            var sizeCol = Col(piMap, "size", 21);
            spec.ProductSizeW = ParseDouble(GetCell(aoa, d, sizeCol));
            spec.ProductSizeH = NextNumericAfter(aoa, d, sizeCol);  // "W x H" — H is the next number right of W
            spec.Diameter = ParseDouble(GetCell(aoa, d, Col(piMap, "diameter", 27)));
        }

        // ── PRINT process row (single) — header below "THÔNG TIN IN" ─────────
        var printHeaderRow = FindHeaderUnderSection(aoa,
            new Regex(@"thông tin in|printing information", RegexOptions.IgnoreCase),
            new Regex(@"quy trình|process", RegexOptions.IgnoreCase));
        if (printHeaderRow > 0)
        {
            var ppMap = BuildColumnMap(aoa, new[] { printHeaderRow }, new Dictionary<string, Regex>
            {
                ["process"]   = new(@"quy trình|process", RegexOptions.IgnoreCase),
                ["material"]  = new(@"nguyên liệu|material\s*code", RegexOptions.IgnoreCase),
                ["thickness"] = new(@"độ dày|thickne", RegexOptions.IgnoreCase),
                ["size"]      = new(@"cỡ nguyên liệu|material\s*size", RegexOptions.IgnoreCase),
                ["pitch"]     = new(@"mức độ|length pitch", RegexOptions.IgnoreCase),
                ["speed"]     = new(@"tốc độ|speed|motion", RegexOptions.IgnoreCase),
                ["cavity"]    = new(@"plate cavity|bản in", RegexOptions.IgnoreCase),
            });
            var d = printHeaderRow + 1;
            var pProcess = GetCell(aoa, d, Col(ppMap, "process", 1));
            if (!string.IsNullOrWhiteSpace(pProcess) || !string.IsNullOrWhiteSpace(GetCell(aoa, d, Col(ppMap, "material", 4))))
            {
                spec.FlexoPrintRows.Add(new ParsedFlexoPrintRowDto
                {
                    Seq         = 1,
                    Process     = NullIfEmpty(pProcess),
                    Material    = NullIfEmpty(GetCell(aoa, d, Col(ppMap, "material", 4))),
                    Thickness   = NullIfEmpty(GetCell(aoa, d, Col(ppMap, "thickness", 8))),
                    Size        = NullIfEmpty(GetCell(aoa, d, Col(ppMap, "size", 9))),
                    PitchMm     = NullIfEmpty(GetCell(aoa, d, Col(ppMap, "pitch", 13))),
                    Speed       = NullIfEmpty(GetCell(aoa, d, Col(ppMap, "speed", 19))),
                    PlateCavity = NullIfEmpty(GetCell(aoa, d, Col(ppMap, "cavity", 24))),
                });
            }
        }

        // ── CUT process row (single) ────────────────────────────────────────
        // The LP template puts the cut section to the RIGHT of the print
        // section on the SAME rows, and the cut columns reach C64 (past the
        // default maxCol 60). So locate the cut SECTION cell, then build the
        // column map scanning ONLY from that column rightward — this both
        // disambiguates the repeated Process / Material Size / Length pitch
        // labels (they appear in both halves) and reaches the far columns.
        var cutSection = FindSection(aoa, new Regex(@"thông tin cắt|cutting information", RegexOptions.IgnoreCase));
        if (cutSection.Row > 0)
        {
            var cutHeaderRow = FindHeaderRowFrom(aoa, cutSection.Row, cutSection.Col,
                new Regex(@"tên dao cắt|cutter name|ép dán|lamination|pcs/lần cắt|cutting cavity", RegexOptions.IgnoreCase));
            if (cutHeaderRow > 0)
            {
                var cutMap = ColMapFrom(aoa, cutHeaderRow, cutSection.Col, WideCol, new Dictionary<string, Regex>
                {
                    ["process"]    = new(@"quy trình|process", RegexOptions.IgnoreCase),
                    ["lamination"] = new(@"ép dán|lamination", RegexOptions.IgnoreCase),
                    ["size"]       = new(@"cỡ nguyên liệu|material\s*size", RegexOptions.IgnoreCase),
                    ["pitch"]      = new(@"mức độ|length pitch", RegexOptions.IgnoreCase),
                    ["cutter"]     = new(@"tên dao cắt|cutter name", RegexOptions.IgnoreCase),
                    ["pcsSheet"]   = new(@"cái\s*/\s*tờ|pcs\s*/\s*sheet", RegexOptions.IgnoreCase),
                    ["cutCavity"]  = new(@"pcs/lần cắt|cutting cavity", RegexOptions.IgnoreCase),
                    ["packing"]    = new(@"quy cách đóng gói|packing", RegexOptions.IgnoreCase),
                });
                var d = cutHeaderRow + 1;
                var process = GetCell(aoa, d, Col(cutMap, "process", 0));
                var cutter = GetCell(aoa, d, Col(cutMap, "cutter", 0));
                if (!string.IsNullOrWhiteSpace(process) || !string.IsNullOrWhiteSpace(cutter))
                {
                    spec.FlexoCuttingRows.Add(new ParsedFlexoCuttingRowDto
                    {
                        Seq           = 1,
                        Process       = NullIfEmpty(process),
                        Lamination    = NullIfEmpty(GetCell(aoa, d, Col(cutMap, "lamination", 0))),
                        Size          = NullIfEmpty(GetCell(aoa, d, Col(cutMap, "size", 0))),
                        PitchMm       = ParseDouble(GetCell(aoa, d, Col(cutMap, "pitch", 0))),
                        CutterName    = NullIfEmpty(cutter),
                        PcsPerSheet   = ParseInt(GetCell(aoa, d, Col(cutMap, "pcsSheet", 0))),
                        CuttingCavity = ParseInt(GetCell(aoa, d, Col(cutMap, "cutCavity", 0))),
                        Packing       = NullIfEmpty(GetCell(aoa, d, Col(cutMap, "packing", 0))),
                    });
                }
            }
        }

        // ── INK ROWS — header by "STT/No" near "Thông tin mực" ───────────────
        var inkHeaderRow = FindHeaderUnderSection(aoa,
            new Regex(@"thông tin mực|ink information", RegexOptions.IgnoreCase),
            new Regex(@"^stt\b|^no\b", RegexOptions.IgnoreCase),
            allowSameRow: false);
        if (inkHeaderRow > 0)
        {
            var inkMap = BuildColumnMap(aoa, new[] { inkHeaderRow }, new Dictionary<string, Regex>
            {
                ["color"]   = new(@"^màu\b|color", RegexOptions.IgnoreCase),
                ["inkCode"] = new(@"mã mực|ink\s*code", RegexOptions.IgnoreCase),
                ["inkDesc"] = new(@"tên mực|ink\s*description", RegexOptions.IgnoreCase),
                ["brand"]   = new(@"^hãng\b|brand", RegexOptions.IgnoreCase),
                ["uvPower"] = new(@"công suất uv|uv\s*power", RegexOptions.IgnoreCase),
            }, WideCol);
            for (int r = inkHeaderRow + 1; r < inkHeaderRow + 40; r++)
            {
                var no = GetCell(aoa, r, 1);
                var c1l = no.ToLowerInvariant();
                if (string.IsNullOrEmpty(no)) continue;
                if (c1l.Contains("ghi ch") || c1l.Contains("remark") || c1l.Contains("lại") || c1l == "rev") break;
                if (!Regex.IsMatch(no, @"^\d")) break;
                if (!int.TryParse(Regex.Match(no, @"\d+").Value, out var seq)) seq = spec.FlexoInkRows.Count + 1;

                var color   = NullIfEmpty(GetCell(aoa, r, Col(inkMap, "color", 2)));
                var inkCode = NullIfEmpty(GetCell(aoa, r, Col(inkMap, "inkCode", 4)));
                var inkDesc = NullIfEmpty(GetCell(aoa, r, Col(inkMap, "inkDesc", 7)));
                // Skip fully-empty placeholder rows (Indigo/LP pre-print blank rows).
                if (color is null && inkCode is null && inkDesc is null) continue;

                spec.FlexoInkRows.Add(new ParsedFlexoInkRowDto
                {
                    Seq            = seq,
                    Color          = color,
                    InkCode        = inkCode,
                    InkDescription = inkDesc,
                    Brand          = NullIfEmpty(GetCell(aoa, r, Col(inkMap, "brand", 14))),
                    UvPowerW       = ParseDouble(GetCell(aoa, r, Col(inkMap, "uvPower", 0))),
                });
            }
        }

        // ── REMARKS — first "Ghi chú / Remark" row, value one column right ───
        for (int r = 5; r < 60; r++)
        {
            var c1l = GetCell(aoa, r, 1).ToLowerInvariant();
            if (c1l.Contains("ghi ch") || c1l.Contains("remark"))
            {
                var left = GetCell(aoa, r, 2);
                if (string.IsNullOrWhiteSpace(left)) left = GetCell(aoa, r, 3);
                spec.Remarks = left;
                spec.RemarksLeft = NullIfEmpty(left);
                break;
            }
        }

        // ── Validations (mirror silk/flexo) ─────────────────────────────────
        if (string.IsNullOrWhiteSpace(spec.Customer)) spec.Warnings.Add("Customer field missing — required.");
        if (string.IsNullOrWhiteSpace(spec.PartNo)) spec.Warnings.Add("Part No field missing — required.");
        if (string.IsNullOrWhiteSpace(spec.RefNo))
        {
            spec.RefNo = $"CCL-Seal-{DateTime.UtcNow:yyyyMMddHHmmss}";
            spec.Warnings.Add($"RefNo missing in xlsx — synthesized as '{spec.RefNo}'.");
        }
        if (spec.FlexoInkRows.Count == 0)
            spec.Warnings.Add("Không parse được ink row nào — kiểm tra layout xlsx có khớp template Indigo/Letterpress?");

        return spec;
    }

    /// <summary>Find a header row whose C1 (or any cell when
    /// <paramref name="allowSameRow"/>) matches <paramref name="headerLabel"/>,
    /// at or below the row that holds <paramref name="sectionLabel"/>. Returns
    /// -1 when the section is absent.</summary>
    private static int FindHeaderUnderSection(string[][] aoa, Regex sectionLabel, Regex headerLabel, bool allowSameRow = false)
    {
        int sectionRow = -1;
        for (int r = 1; r < 60; r++)
        {
            for (int c = 1; c <= 70; c++)
            {
                var cell = GetCell(aoa, r, c);
                if (!string.IsNullOrEmpty(cell) && sectionLabel.IsMatch(cell)) { sectionRow = r; break; }
            }
            if (sectionRow > 0) break;
        }
        if (sectionRow < 0) return -1;

        var start = allowSameRow ? sectionRow : sectionRow + 1;
        for (int r = start; r < sectionRow + 6 && r < 60; r++)
        {
            for (int c = 1; c <= 70; c++)
            {
                var cell = GetCell(aoa, r, c);
                if (!string.IsNullOrEmpty(cell) && headerLabel.IsMatch(cell)) return r;
            }
        }
        return -1;
    }

    /// <summary>Next numeric cell to the right of <paramref name="fromCol"/> on
    /// row <paramref name="r"/> — the "H" in a "W x H" product-size pair.</summary>
    private static double? NextNumericAfter(string[][] aoa, int r, int fromCol)
    {
        for (int c = fromCol + 1; c <= fromCol + 10; c++)
        {
            var v = GetCell(aoa, r, c);
            if (string.IsNullOrWhiteSpace(v)) continue;
            var d = ParseDouble(v);
            if (d is not null) return d;
        }
        return null;
    }

    /// <summary>The LP template extends past the default 60-column scan window
    /// (cut Cutting-cavity = C61, Packing = C64, Diameter = C62).</summary>
    private const int WideCol = 90;

    /// <summary>Locate the (row, col) of the first cell matching a section
    /// label. Returns (-1, -1) when absent.</summary>
    private static (int Row, int Col) FindSection(string[][] aoa, Regex sectionLabel)
    {
        for (int r = 1; r < 60; r++)
            for (int c = 1; c <= WideCol; c++)
            {
                var cell = GetCell(aoa, r, c);
                if (!string.IsNullOrEmpty(cell) && sectionLabel.IsMatch(cell)) return (r, c);
            }
        return (-1, -1);
    }

    /// <summary>Find a header row at/below <paramref name="fromRow"/> that
    /// carries <paramref name="headerLabel"/> at or right of
    /// <paramref name="fromCol"/> (used to find the cut header in the right
    /// half of a side-by-side print|cut layout). -1 when not found.</summary>
    private static int FindHeaderRowFrom(string[][] aoa, int fromRow, int fromCol, Regex headerLabel)
    {
        for (int r = fromRow; r < fromRow + 5 && r < 60; r++)
            for (int c = fromCol; c <= WideCol; c++)
            {
                var cell = GetCell(aoa, r, c);
                if (!string.IsNullOrEmpty(cell) && headerLabel.IsMatch(cell)) return r;
            }
        return -1;
    }

    /// <summary>Column map restricted to [minCol, maxCol] on a single header
    /// row — disambiguates labels that repeat across a side-by-side layout by
    /// scanning only the relevant half.</summary>
    private static Dictionary<string, int> ColMapFrom(
        string[][] aoa, int headerRow, int minCol, int maxCol, Dictionary<string, Regex> fields)
    {
        var map = new Dictionary<string, int>();
        foreach (var (field, rx) in fields)
            for (int c = minCol; c <= maxCol; c++)
            {
                var cell = GetCell(aoa, headerRow, c);
                if (!string.IsNullOrEmpty(cell) && rx.IsMatch(cell)) { map[field] = c; break; }
            }
        return map;
    }
}
