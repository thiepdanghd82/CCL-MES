using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace CCL.MES.Infrastructure.SpecImport;

/// <summary>
/// Phase 8 PR #31b — Shared parser primitives (aoa builder + cell helpers +
/// dynamic header scan). Tách ra khỏi <see cref="SilkscreenXlsxParser"/> để
/// <c>FlexoXlsxParser</c> reuse mà KHÔNG copy-paste. 1:1 với SpecHub HTML
/// helpers (`getCell`, `findRowByC1`, `findValueAfterLabel`, `findCellMatching`,
/// `buildColumnMap`).
/// </summary>
internal static class XlsxParserHelpers
{
    /// <summary>Scan range mặc định cho header label-anchored helpers.</summary>
    public static readonly int[] HeaderScan = { 1, 2, 3, 4, 5 };

    public static string[][] WorksheetToAoa(IXLWorksheet ws)
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
                row[c] = CellToString(range.Cell(r + 1, c + 1));
            }
            aoa[r] = row;
        }
        return aoa;
    }

    public static string CellToString(IXLCell cell)
    {
        if (cell is null || cell.IsEmpty()) return "";
        try
        {
            var s = cell.GetFormattedString() ?? "";
            return s.Trim();
        }
        catch
        {
            return (cell.Value.ToString() ?? "").Trim();
        }
    }

    /// <summary>Detect category từ R1/R2 title text — mirror SpecHub HTML:11321.</summary>
    public static string DetectSpecCategory(string[][] aoa)
    {
        var titles = string.Join(" ",
            new[] { GetCell(aoa, 1, 1), GetCell(aoa, 2, 1), GetCell(aoa, 1, 2) })
            .ToUpperInvariant();

        if (Regex.IsMatch(titles, @"FLEXO|BROTECH|GALLUS")) return "flexo";
        if (Regex.IsMatch(titles, @"LETTER\s?PRESS")) return "letterpress";
        if (Regex.IsMatch(titles, @"INDIGO")) return "indigo";
        if (Regex.IsMatch(titles, @"DIE\s?CUT|RDC|POWERPUNCH|CNC")) return "diecut";
        if (Regex.IsMatch(titles, @"SILK")) return "silkscreen";
        return "silkscreen";
    }

    /// <summary>1-based cell accessor mirror SheetJS aoa[r-1][c-1].</summary>
    public static string GetCell(string[][] aoa, int r, int c)
    {
        if (r < 1 || c < 1) return "";
        if (r - 1 >= aoa.Length) return "";
        var row = aoa[r - 1];
        if (row is null || c - 1 >= row.Length) return "";
        var v = row[c - 1];
        return v?.Trim() ?? "";
    }

    public static int FindRowByC1(string[][] aoa, Regex regex, int startRow = 1, int endRow = 60)
    {
        for (int r = startRow; r <= endRow; r++)
        {
            var c1 = GetCell(aoa, r, 1);
            if (!string.IsNullOrEmpty(c1) && regex.IsMatch(c1)) return r;
        }
        return -1;
    }

    public static string FindValueAfterLabel(string[][] aoa, Regex labelRegex, int[]? scanRows = null, int maxCol = 60)
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

    public static string FindCellMatching(string[][] aoa, Regex regex, int[]? scanRows = null, int maxCol = 60)
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

    public static Dictionary<string, int> BuildColumnMap(
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

    public static int Col(Dictionary<string, int> map, string field, int fallback)
        => map.TryGetValue(field, out var c) ? c : fallback;

    /// <summary>
    /// VN locale tolerant — strip non-digit/non-decimal char, replace comma→dot,
    /// parse with InvariantCulture. Trả null nếu rỗng / không parse được.
    /// </summary>
    public static double? ParseDouble(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var clean = Regex.Replace(s, @"[^\d\.\-,]", "").Replace(",", ".");
        if (string.IsNullOrEmpty(clean)) return null;
        return double.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    public static int? ParseInt(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var clean = Regex.Replace(s, @"[^\d\-]", "");
        if (string.IsNullOrEmpty(clean)) return null;
        return int.TryParse(clean, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;
    }

    public static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
