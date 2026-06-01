using System.Globalization;
using System.Text.RegularExpressions;
using CCL.MES.Application.SpecImport;
using ClosedXML.Excel;

namespace CCL.MES.Infrastructure.SpecImport;

/// <summary>
/// Phase 8 PR #31a — Port `parseXlsxSilkscreen` từ SpecHub HTML:11434-11645.
///
/// Tham chiếu READ-ONLY: `/Volumes/.../SpecHub/spechub-prototype.html`. Logic giữ
/// nguyên 1:1 (cell layout fixed + dynamic header scan fallback) để xử lý mọi
/// silkscreen template SpecHub đã chứng minh parse được. ClosedXML đọc xlsx →
/// convert worksheet ra `string[][]` aoa (Array of Arrays) — mirror SheetJS
/// `XLSX.utils.sheet_to_json(ws, {header: 1, defval: '', raw: false})`.
///
/// Per Q11: planner ≠ silkscreen runtime fallback silkscreen layout (giống
/// SpecHub `parseXlsxToSpec`); warning gắn vào ParsedSpecDto.Warnings.
/// </summary>
public class SilkscreenXlsxParser : ISpecXlsxParser
{
    /// <summary>Scan range mặc định cho header label-anchored helpers.</summary>
    private static readonly int[] HeaderScan = { 1, 2, 3, 4, 5 };

    public ParsedSpecDto Parse(Stream xlsxStream, string forcedCategory)
    {
        // PR #31a — normalize trước ClosedXML để xử lý SpecHub samples xuất từ
        // SheetJS có case mismatch trong Content_Types Override PartName.
        using var normalized = XlsxNormalizer.Normalize(xlsxStream);
        using var wb = new XLWorkbook(normalized);
        var ws = wb.Worksheet(1);
        var aoa = WorksheetToAoa(ws);

        var detected = DetectSpecCategory(aoa);
        var category = string.IsNullOrWhiteSpace(forcedCategory) ? detected : forcedCategory;

        var spec = ParseSilkscreen(aoa);
        spec.Category = category;

        if (!string.Equals(category, "silkscreen", StringComparison.OrdinalIgnoreCase))
        {
            spec.Warnings.Add(
                $"Planner '{category}' chưa có parser riêng — dùng silkscreen layout fallback. " +
                $"Có thể parse không chính xác cell cố định.");
        }
        if (string.Equals(forcedCategory, "silkscreen", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(detected, "silkscreen", StringComparison.OrdinalIgnoreCase))
        {
            spec.Warnings.Add($"File header gợi ý category='{detected}' nhưng operator chọn silkscreen — dùng lựa chọn operator.");
        }

        return spec;
    }

    // ── aoa builder ──────────────────────────────────────────────────────────
    // Cell value → string. ClosedXML returns boxed obj; Try to render dates +
    // numbers như SheetJS raw=false (preserve formatted text); fallback to
    // FormattedString cho ô có percent/date/number format.
    private static string[][] WorksheetToAoa(IXLWorksheet ws)
    {
        var range = ws.RangeUsed();
        if (range is null) return Array.Empty<string[]>();
        var rowCount = range.RowCount();
        var colCount = range.ColumnCount();
        var aoa = new string[rowCount][];
        for (int r = 0; r < rowCount; r++)
        {
            var row = new string[colCount];
            for (int c = 0; c < colCount; c++)
            {
                var cell = range.Cell(r + 1, c + 1);
                row[c] = CellToString(cell);
            }
            aoa[r] = row;
        }
        return aoa;
    }

    private static string CellToString(IXLCell cell)
    {
        if (cell is null || cell.IsEmpty()) return "";
        try
        {
            // GetFormattedString respects locale + percent/date format; mirror
            // SheetJS raw=false (returns formatted text instead of raw number).
            var s = cell.GetFormattedString() ?? "";
            return s.Trim();
        }
        catch
        {
            return (cell.Value.ToString() ?? "").Trim();
        }
    }

    // ── Helpers ported từ SpecHub HTML:11321-11432 ───────────────────────────

    private static string DetectSpecCategory(string[][] aoa)
    {
        var titles = string.Join(" ",
            new[] { GetCell(aoa, 1, 1), GetCell(aoa, 2, 1), GetCell(aoa, 1, 2) })
            .ToUpperInvariant();

        if (Regex.IsMatch(titles, @"FLEXO|BROTECH|GALLUS")) return "flexo";
        if (Regex.IsMatch(titles, @"LETTER\s?PRESS")) return "letterpress";
        if (Regex.IsMatch(titles, @"INDIGO")) return "indigo";
        if (Regex.IsMatch(titles, @"DIE\s?CUT|RDC|POWERPUNCH|CNC")) return "diecut";
        if (Regex.IsMatch(titles, @"SILK")) return "silkscreen";
        // Default — safe to silkscreen (SpecHub convention).
        return "silkscreen";
    }

    /// <summary>1-based cell accessor mirror SheetJS aoa[r-1][c-1].</summary>
    private static string GetCell(string[][] aoa, int r, int c)
    {
        if (r < 1 || c < 1) return "";
        if (r - 1 >= aoa.Length) return "";
        var row = aoa[r - 1];
        if (row is null || c - 1 >= row.Length) return "";
        var v = row[c - 1];
        return v?.Trim() ?? "";
    }

    private static int FindRowByC1(string[][] aoa, Regex regex, int startRow = 1, int endRow = 60)
    {
        for (int r = startRow; r <= endRow; r++)
        {
            var c1 = GetCell(aoa, r, 1);
            if (!string.IsNullOrEmpty(c1) && regex.IsMatch(c1)) return r;
        }
        return -1;
    }

    private static string FindValueAfterLabel(string[][] aoa, Regex labelRegex, int[]? scanRows = null, int maxCol = 60)
    {
        scanRows ??= HeaderScan;
        foreach (var r in scanRows)
        {
            for (int c = 1; c <= maxCol; c++)
            {
                var cell = GetCell(aoa, r, c);
                if (!string.IsNullOrEmpty(cell) && labelRegex.IsMatch(cell))
                {
                    for (int c2 = c + 1; c2 <= maxCol; c2++)
                    {
                        var v = GetCell(aoa, r, c2);
                        if (!string.IsNullOrEmpty(v)) return v;
                    }
                }
            }
        }
        return "";
    }

    private static string FindCellMatching(string[][] aoa, Regex regex, int[]? scanRows = null, int maxCol = 60)
    {
        scanRows ??= HeaderScan;
        foreach (var r in scanRows)
        {
            for (int c = 1; c <= maxCol; c++)
            {
                var cell = GetCell(aoa, r, c);
                if (!string.IsNullOrEmpty(cell) && regex.IsMatch(cell)) return cell;
            }
        }
        return "";
    }

    private static Dictionary<string, int> BuildColumnMap(
        string[][] aoa, int[] scanRows, Dictionary<string, Regex> fieldRegexes, int maxCol = 60)
    {
        var map = new Dictionary<string, int>();
        foreach (var (field, regex) in fieldRegexes)
        {
            foreach (var r in scanRows)
            {
                bool found = false;
                for (int c = 1; c <= maxCol; c++)
                {
                    var cell = GetCell(aoa, r, c);
                    if (!string.IsNullOrEmpty(cell) && regex.IsMatch(cell))
                    {
                        map[field] = c;
                        found = true;
                        break;
                    }
                }
                if (found) break;
            }
        }
        return map;
    }

    private static int Col(Dictionary<string, int> map, string field, int fallback)
        => map.TryGetValue(field, out var c) ? c : fallback;

    private static double? ParseDouble(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var clean = Regex.Replace(s, @"[^\d\.\-,]", "").Replace(",", ".");
        if (string.IsNullOrEmpty(clean)) return null;
        return double.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private static int? ParseInt(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var clean = Regex.Replace(s, @"[^\d\-]", "");
        if (string.IsNullOrEmpty(clean)) return null;
        return int.TryParse(clean, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;
    }

    // ── Silkscreen parser ────────────────────────────────────────────────────
    private static ParsedSpecDto ParseSilkscreen(string[][] aoa)
    {
        var spec = new ParsedSpecDto { Category = "silkscreen" };

        // HEADER label-anchored scan
        var refNoLabel = FindValueAfterLabel(aoa, new Regex(@"số tham chiếu|ref\s*no", RegexOptions.IgnoreCase));
        spec.RefNo = !string.IsNullOrEmpty(refNoLabel)
            ? refNoLabel
            : FindCellMatching(aoa, new Regex(@"^CCL-(Silk|Seal|Flexo|Indigo|Letter)-", RegexOptions.IgnoreCase));

        var inspRaw = FindCellMatching(aoa, new Regex(@"^Spec\s+[A-Z]?\d+\w*$", RegexOptions.IgnoreCase));
        if (string.IsNullOrEmpty(inspRaw))
            inspRaw = FindValueAfterLabel(aoa, new Regex(@"mức độ kiểm tra|inspection level", RegexOptions.IgnoreCase));
        spec.InspectionLevel = string.IsNullOrEmpty(inspRaw)
            ? "A"
            : Regex.Replace(inspRaw, @"^Spec\s*", "", RegexOptions.IgnoreCase).Trim();
        if (string.IsNullOrWhiteSpace(spec.InspectionLevel)) spec.InspectionLevel = "A";

        var stamp = FindCellMatching(aoa, new Regex(@"^(approved|draft|in[\s_-]?review)$", RegexOptions.IgnoreCase));
        spec.ApprovalStamp = string.IsNullOrEmpty(stamp) ? "Approved" : stamp;

        var comp = FindCellMatching(aoa, new Regex(@"\brohs\b|\bcompliance\b|\bcompliant\b", RegexOptions.IgnoreCase));
        spec.Compliance = string.IsNullOrEmpty(comp) ? "RoHS Compliance" : comp;

        // PRODUCT INFO — find "Khách hàng/Customer" header row → data row +1
        var piHeaderRow = FindRowByC1(aoa, new Regex(@"khách hàng|customer", RegexOptions.IgnoreCase));
        var piDataRow = piHeaderRow > 0 ? piHeaderRow + 1 : 4;
        var piMap = piHeaderRow > 0
            ? BuildColumnMap(aoa, new[] { piHeaderRow }, new Dictionary<string, Regex>
            {
                ["customer"]         = new(@"khách hàng|customer", RegexOptions.IgnoreCase),
                ["partNo"]           = new(@"mã sp|part\s*no", RegexOptions.IgnoreCase),
                ["partName"]         = new(@"tên sp|part\s*name", RegexOptions.IgnoreCase),
                ["materialType"]     = new(@"tên nguyên liệu|material\s*type", RegexOptions.IgnoreCase),
                ["materialSize"]     = new(@"cỡ nguyên liệu|material\s*size", RegexOptions.IgnoreCase),
                ["laminationTape"]   = new(@"tên ép dán|lamination.*tape\b(?!.*size)", RegexOptions.IgnoreCase),
                ["laminationSize"]   = new(@"cỡ ép dán|lamination.*size|tape\s*size", RegexOptions.IgnoreCase),
                ["laminationCavity"] = new(@"cavity ép dán|lamination\s*cavity", RegexOptions.IgnoreCase),
            })
            : new Dictionary<string, int>
            {
                ["customer"] = 1, ["partNo"] = 6, ["partName"] = 14,
                ["materialType"] = 24, ["materialSize"] = 33,
                ["laminationTape"] = 43, ["laminationSize"] = 49, ["laminationCavity"] = 53,
            };

        spec.Customer        = GetCell(aoa, piDataRow, Col(piMap, "customer", 1));
        spec.PartNo          = GetCell(aoa, piDataRow, Col(piMap, "partNo", 6));
        spec.PartName        = GetCell(aoa, piDataRow, Col(piMap, "partName", 14));
        spec.MaterialType    = GetCell(aoa, piDataRow, Col(piMap, "materialType", 24));
        spec.MaterialSize    = GetCell(aoa, piDataRow, Col(piMap, "materialSize", 33));
        spec.LaminationTape  = GetCell(aoa, piDataRow, Col(piMap, "laminationTape", 43));
        if (string.IsNullOrEmpty(spec.LaminationTape)) spec.LaminationTape = "— Không có";
        spec.LaminationSize  = GetCell(aoa, piDataRow, Col(piMap, "laminationSize", 49));
        if (string.IsNullOrEmpty(spec.LaminationSize)) spec.LaminationSize = "—";
        spec.LaminationCavity = GetCell(aoa, piDataRow, Col(piMap, "laminationCavity", 53));
        if (string.IsNullOrEmpty(spec.LaminationCavity)) spec.LaminationCavity = "—";

        // PRINT PARAMS
        var ppHeaderRow = FindRowByC1(aoa, new Regex(@"số lượng.*lần in|printing cavity", RegexOptions.IgnoreCase));
        var ppRow = ppHeaderRow > 0 ? ppHeaderRow + 1 : 8;
        var ppMap = ppHeaderRow > 0
            ? BuildColumnMap(aoa, new[] { ppHeaderRow }, new Dictionary<string, Regex>
            {
                ["cavity"]   = new(@"số lượng.*lần in|printing cavity", RegexOptions.IgnoreCase),
                ["pitch"]    = new(@"bước nhảy|length pitch", RegexOptions.IgnoreCase),
                ["sizeW"]    = new(@"kích thước sp|product size", RegexOptions.IgnoreCase),
                ["diameter"] = new(@"đường kính|diameter", RegexOptions.IgnoreCase),
            })
            : new Dictionary<string, int> { ["cavity"] = 1, ["pitch"] = 6, ["sizeW"] = 14, ["diameter"] = 24 };

        spec.PrintingCavity = ParseInt(GetCell(aoa, ppRow, Col(ppMap, "cavity", 1)));
        spec.LengthPitchMm  = ParseDouble(GetCell(aoa, ppRow, Col(ppMap, "pitch", 6)));
        spec.ProductSizeW   = ParseDouble(GetCell(aoa, ppRow, Col(ppMap, "sizeW", 14)));
        // Height H usually 6 cols to right of W (separator at sizeW+5)
        spec.ProductSizeH   = ParseDouble(GetCell(aoa, ppRow, Col(ppMap, "sizeW", 14) + 6));
        spec.Diameter       = ParseDouble(GetCell(aoa, ppRow, Col(ppMap, "diameter", 24)));

        // PRINT ROWS — find "Stt/No." main header → 3 rows below (2 sub-header rows in between)
        var sttHeaderRow = FindRowByC1(aoa, new Regex(@"^stt$|^stt\s|^no\.?$|^no\.?\s", RegexOptions.IgnoreCase));
        var prMap = sttHeaderRow > 0
            ? BuildColumnMap(aoa, new[] { sttHeaderRow + 1, sttHeaderRow + 2 }, new Dictionary<string, Regex>
            {
                ["surface"]   = new(@"mặt in|printing surface", RegexOptions.IgnoreCase),
                ["color"]     = new(@"^mầu\b|^color$", RegexOptions.IgnoreCase),
                ["inkName"]   = new(@"tên mực|ink\s*name", RegexOptions.IgnoreCase),
                ["inkCode"]   = new(@"mã mực|ink\s*code", RegexOptions.IgnoreCase),
                ["maker"]     = new(@"^hãng$|^maker$", RegexOptions.IgnoreCase),
                ["retarder"]  = new(@"chất hãm|^retarder$", RegexOptions.IgnoreCase),
                ["visc"]      = new(@"độ nhớt|viscosity", RegexOptions.IgnoreCase),
                ["speed"]     = new(@"printing speed|shoot.*min", RegexOptions.IgnoreCase),
                ["squeegee"]  = new(@"điều kiện ống lăn|squee.*condition", RegexOptions.IgnoreCase),
                ["dry"]       = new(@"khô tự nhiên|nd\s*\/\s*dr|^method$", RegexOptions.IgnoreCase),
                ["temp"]      = new(@"nhiệt độ|^temp\b", RegexOptions.IgnoreCase),
                ["time"]      = new(@"thời gian sấy|dur\.?\s*min", RegexOptions.IgnoreCase),
                ["uv"]        = new(@"^uv\b|tia cực tím", RegexOptions.IgnoreCase),
                ["emulsion"]  = new(@"độ dày khuôn|emulsion thickness", RegexOptions.IgnoreCase),
                ["size"]      = new(@"^cỡ\b|^size\s*\(?mm", RegexOptions.IgnoreCase),
                ["mesh"]      = new(@"^lưới$|^#?\s*mesh$", RegexOptions.IgnoreCase),
                ["angle"]     = new(@"^góc\b|^angle\b", RegexOptions.IgnoreCase),
                ["plateCode"] = new(@"mã khuôn|plate\s*code", RegexOptions.IgnoreCase),
                ["control"]   = new(@"quản lí khuôn|control\s*number", RegexOptions.IgnoreCase),
                ["remark"]    = new(@"^ghi chú|^remark$", RegexOptions.IgnoreCase),
            })
            : new Dictionary<string, int>
            {
                ["surface"] = 2, ["color"] = 3, ["inkName"] = 7, ["inkCode"] = 11,
                ["maker"] = 14, ["retarder"] = 17, ["visc"] = 20, ["speed"] = 23,
                ["squeegee"] = 27, ["dry"] = 30, ["temp"] = 32, ["time"] = 34,
                ["uv"] = 36, ["emulsion"] = 38, ["size"] = 41, ["mesh"] = 46,
                ["angle"] = 49, ["plateCode"] = 51, ["control"] = 53, ["remark"] = 54,
            };

        var printRowStart = sttHeaderRow > 0 ? sttHeaderRow + 3 : 13;
        var rowsRead = 0;
        for (int r = printRowStart; r < printRowStart + 30; r++)
        {
            var no = GetCell(aoa, r, 1);
            var c1 = no.ToLowerInvariant();
            if (string.IsNullOrEmpty(no)
                || c1.Contains("ghi ch") || c1.Contains("remark")
                || c1.Contains("lại") || c1.Contains("rev")) break;
            if (!Regex.IsMatch(no, @"^\d")) break;

            if (!int.TryParse(Regex.Match(no, @"\d+").Value, out var seq)) seq = rowsRead + 1;

            spec.PrintRows.Add(new ParsedPrintColorDto
            {
                Seq          = seq,
                Surface      = NullIfEmpty(GetCell(aoa, r, Col(prMap, "surface", 2))) ?? "R",
                Color        = NullIfEmpty(GetCell(aoa, r, Col(prMap, "color", 3))),
                InkName      = NullIfEmpty(GetCell(aoa, r, Col(prMap, "inkName", 7))),
                InkCode      = NullIfEmpty(GetCell(aoa, r, Col(prMap, "inkCode", 11))),
                Maker        = NullIfEmpty(GetCell(aoa, r, Col(prMap, "maker", 14))),
                Retarder     = NullIfEmpty(GetCell(aoa, r, Col(prMap, "retarder", 17))),
                Viscosity    = ParseDouble(GetCell(aoa, r, Col(prMap, "visc", 20))),
                Speed        = ParseDouble(GetCell(aoa, r, Col(prMap, "speed", 23))),
                Squeegee     = NullIfEmpty(GetCell(aoa, r, Col(prMap, "squeegee", 27))),
                Dry          = NullIfEmpty(GetCell(aoa, r, Col(prMap, "dry", 30))),
                TemperatureC = ParseDouble(GetCell(aoa, r, Col(prMap, "temp", 32))),
                TimeMin      = ParseInt(GetCell(aoa, r, Col(prMap, "time", 34))),
                Uv           = NullIfEmpty(GetCell(aoa, r, Col(prMap, "uv", 36))),
                EmulsionUm   = ParseDouble(GetCell(aoa, r, Col(prMap, "emulsion", 38))),
                PlateSize    = NullIfEmpty(GetCell(aoa, r, Col(prMap, "size", 41))),
                Mesh         = NullIfEmpty(GetCell(aoa, r, Col(prMap, "mesh", 46))),
                AngleDeg     = ParseDouble(GetCell(aoa, r, Col(prMap, "angle", 49))),
                PlateCode    = NullIfEmpty(GetCell(aoa, r, Col(prMap, "plateCode", 51))),
                ControlNo    = ParseInt(GetCell(aoa, r, Col(prMap, "control", 53))),
                Remark       = NullIfEmpty(GetCell(aoa, r, Col(prMap, "remark", 54))),
            });
            rowsRead++;
        }

        // Remarks blob — find "Ghi chú / Remarks" header row, value at C3
        for (int r = 5; r < 60; r++)
        {
            var c1 = GetCell(aoa, r, 1).ToLowerInvariant();
            if (c1.Contains("ghi ch") || c1.Contains("remark"))
            {
                spec.Remarks = GetCell(aoa, r, 3);
                break;
            }
        }

        // Validations Q9
        if (string.IsNullOrWhiteSpace(spec.Customer)) spec.Warnings.Add("Customer field missing — required.");
        if (string.IsNullOrWhiteSpace(spec.PartNo)) spec.Warnings.Add("Part No field missing — required.");
        if (string.IsNullOrWhiteSpace(spec.RefNo))
        {
            // Fallback synth ref no — semantic gắn liền với spec code per SpecHub
            // line 11598. Caller có thể accept hoặc reject; vẫn surface warning.
            spec.RefNo = $"CCL-Silk-{DateTime.UtcNow:yyyyMMddHHmmss}";
            spec.Warnings.Add($"RefNo missing in xlsx — synthesized as '{spec.RefNo}'.");
        }
        if (spec.PrintRows.Count == 0)
            spec.Warnings.Add("Không parse được print row nào — kiểm tra layout xlsx có khớp template silkscreen?");

        return spec;
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
