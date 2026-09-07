using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using CCL.MES.Application.Services;

namespace CCL.MES.Infrastructure.IqcMaster;

/// <summary>
/// Đọc 4 sheet ledger của "IQC report 2026": Roll · PCS · Chem · Tool.
/// Map cột theo VỊ TRÍ đã đối chiếu tay (2 dòng tiêu đề; data từ dòng 3).
/// Culture invariant quanh số/ngày (L44).
/// </summary>
public static class IqcHistoryLedgerReader
{
    private const int FirstDataRow = 3;

    private static readonly string[] RollVisualKeys =
    [
        "RD-01", "RD-02", "RD-03", "RD-04", "RD-05", "RD-06", "RD-07",
        "RD-08", "RD-09", "RD-10", "RD-11", "RD-12", "RD-13",
    ];

    private static readonly string[] PcsVisualKeys =
    [
        "PD-01", "PD-02", "PD-03", "PD-04", "PD-05",
        "PD-06", "PD-07", "PD-08", "PD-09",
    ];

    public static List<IqcHistoryLedgerRow> Read(Stream xlsx)
    {
        using var wb = new XLWorkbook(xlsx);
        var rows = new List<IqcHistoryLedgerRow>();
        rows.AddRange(ReadRoll(wb));
        rows.AddRange(ReadPcs(wb));
        rows.AddRange(ReadChem(wb));
        rows.AddRange(ReadTool(wb));
        return rows;
    }

    private static IEnumerable<IqcHistoryLedgerRow> ReadRoll(XLWorkbook wb)
    {
        if (!TrySheet(wb, "Roll", out var ws)) yield break;
        var last = ws.LastRowUsed()?.RowNumber() ?? 0;
        for (var r = FirstDataRow; r <= last; r++)
        {
            if (IsBlank(ws, r, 1, 6, 8)) continue;
            var qtyRoll = Num(ws, r, 12);
            var qtyM2 = Num(ws, r, 10);
            var qty = qtyRoll > 0 ? qtyRoll : qtyM2;
            var uom = qtyRoll > 0 ? "rolls" : (qtyM2 > 0 ? "m2" : null);

            var defects = new List<IqcLedgerDefectCell>(13);
            for (var i = 0; i < 13; i++)
            {
                var col = 20 + i; // T=20 … AF=32
                var raw = Cell(ws, r, col);
                defects.Add(new IqcLedgerDefectCell(RollVisualKeys[i], ParseCount(raw)));
            }

            var checks = new IqcHistoryLedgerChecks(
                WarehouseInDate: Cell(ws, r, 13),
                ExpiryText: Cell(ws, r, 14),
                Pefc: Cell(ws, r, 15),
                PefcLevel: Cell(ws, r, 16),
                PackagingSpec: null,
                PackagingPass: ParsePass(Cell(ws, r, 17)),
                PackagingInspector: Cell(ws, r, 18),
                VisualSampleQty: Int(ws, r, 19),
                VisualDefects: defects,
                VisualPass: ParsePass(Cell(ws, r, 33)),
                VisualInspector: Cell(ws, r, 34),
                WidthNominal: NumOrNull(ws, r, 35),
                WidthLow: NumOrNull(ws, r, 36),
                WidthUp: NumOrNull(ws, r, 37),
                WidthSamples: Samples5(ws, r, 38),
                WidthSampleTexts: Array.Empty<string?>(),
                WidthPass: ParsePass(Cell(ws, r, 43)),
                ThicknessSpec: Cell(ws, r, 45),
                ThicknessSamples: Samples5(ws, r, 46),
                ThicknessPass: ParsePass(Cell(ws, r, 51)),
                DimensionInspector: FirstNonEmpty(Cell(ws, r, 44), Cell(ws, r, 52)),
                FuncSpec: Cell(ws, r, 53),
                FuncPass: ParsePass(Cell(ws, r, 54)),
                FuncInspector: Cell(ws, r, 55),
                LabSpec: Cell(ws, r, 59),
                LabSheets: Samples5(ws, r, 60),
                LabPass: ParsePass(Cell(ws, r, 65)),
                LabInspector: Cell(ws, r, 66));

            yield return new IqcHistoryLedgerRow(
                Sheet: "Roll",
                ExcelRow: r,
                Stt: Int(ws, r, 1),
                InspectedAt: Date(ws, r, 2) ?? Date(ws, r, 13) ?? DateTime.MinValue,
                SupplierName: Cell(ws, r, 5),
                CodeIfs: Cell(ws, r, 6),
                MotherCode: Cell(ws, r, 7),
                MaterialName: Cell(ws, r, 8),
                PoNumber: Cell(ws, r, 9),
                Quantity: qty,
                Uom: uom,
                FinalJudgment: Cell(ws, r, 68),
                Inspector: Cell(ws, r, 69),
                Checks: checks);
        }
    }

    private static IEnumerable<IqcHistoryLedgerRow> ReadPcs(XLWorkbook wb)
    {
        if (!TrySheet(wb, "PCS", out var ws)) yield break;
        var last = ws.LastRowUsed()?.RowNumber() ?? 0;
        for (var r = FirstDataRow; r <= last; r++)
        {
            if (IsBlank(ws, r, 1, 6, 8)) continue;

            var defects = new List<IqcLedgerDefectCell>(9);
            for (var i = 0; i < 9; i++)
            {
                var col = 17 + i; // Q=17 … Y=25
                defects.Add(new IqcLedgerDefectCell(PcsVisualKeys[i], ParseCount(Cell(ws, r, col))));
            }

            var widthTexts = new string?[5];
            var widthNums = new double?[5];
            for (var i = 0; i < 5; i++)
            {
                var t = Cell(ws, r, 29 + i); // AC=29 … AG=33
                widthTexts[i] = t;
                widthNums[i] = ParseLeadingDouble(t);
            }

            var checks = new IqcHistoryLedgerChecks(
                WarehouseInDate: Cell(ws, r, 12),
                ExpiryText: Cell(ws, r, 13),
                Pefc: null,
                PefcLevel: null,
                PackagingSpec: Cell(ws, r, 10),
                PackagingPass: ParsePass(Cell(ws, r, 14)),
                PackagingInspector: Cell(ws, r, 15),
                VisualSampleQty: Int(ws, r, 16),
                VisualDefects: defects,
                VisualPass: ParsePass(Cell(ws, r, 26)),
                VisualInspector: Cell(ws, r, 27),
                WidthNominal: null,
                WidthLow: null,
                WidthUp: null,
                WidthSamples: widthNums,
                WidthSampleTexts: widthTexts,
                WidthPass: ParsePass(Cell(ws, r, 34)),
                ThicknessSpec: Cell(ws, r, 35),
                ThicknessSamples: Samples5(ws, r, 36),
                ThicknessPass: ParsePass(Cell(ws, r, 41)),
                DimensionInspector: Cell(ws, r, 42),
                FuncSpec: null,
                FuncPass: null,
                FuncInspector: null,
                LabSpec: null,
                LabSheets: Array.Empty<double?>(),
                LabPass: null,
                LabInspector: null);

            yield return new IqcHistoryLedgerRow(
                Sheet: "PCS",
                ExcelRow: r,
                Stt: Int(ws, r, 1),
                InspectedAt: Date(ws, r, 2) ?? Date(ws, r, 12) ?? DateTime.MinValue,
                SupplierName: Cell(ws, r, 5),
                CodeIfs: Cell(ws, r, 6),
                MotherCode: null,
                MaterialName: Cell(ws, r, 7) ?? Cell(ws, r, 8),
                PoNumber: Cell(ws, r, 9),
                Quantity: Num(ws, r, 11),
                Uom: "pcs",
                FinalJudgment: Cell(ws, r, 46),
                Inspector: Cell(ws, r, 47),
                Checks: checks);
        }
    }

    private static IEnumerable<IqcHistoryLedgerRow> ReadChem(XLWorkbook wb)
    {
        if (!TrySheet(wb, "Chem", out var ws)) yield break;
        var last = ws.LastRowUsed()?.RowNumber() ?? 0;
        for (var r = FirstDataRow; r <= last; r++)
        {
            if (IsBlank(ws, r, 1, 6, 7)) continue;
            var qtyKg = Num(ws, r, 10);
            yield return new IqcHistoryLedgerRow(
                Sheet: "Chem",
                ExcelRow: r,
                Stt: Int(ws, r, 1),
                InspectedAt: Date(ws, r, 2) ?? Date(ws, r, 13) ?? DateTime.MinValue,
                SupplierName: Cell(ws, r, 5),
                CodeIfs: Cell(ws, r, 6),
                MotherCode: null,
                MaterialName: Cell(ws, r, 7),
                PoNumber: Cell(ws, r, 8),
                Quantity: qtyKg,
                Uom: qtyKg > 0 ? "kg" : null,
                FinalJudgment: Cell(ws, r, 22),
                Inspector: Cell(ws, r, 23));
        }
    }

    private static IEnumerable<IqcHistoryLedgerRow> ReadTool(XLWorkbook wb)
    {
        if (!TrySheet(wb, "Tool", out var ws)) yield break;
        var last = ws.LastRowUsed()?.RowNumber() ?? 0;
        for (var r = FirstDataRow; r <= last; r++)
        {
            if (IsBlank(ws, r, 1, 6, 7)) continue;
            yield return new IqcHistoryLedgerRow(
                Sheet: "Tool",
                ExcelRow: r,
                Stt: Int(ws, r, 1),
                InspectedAt: Date(ws, r, 2) ?? Date(ws, r, 9) ?? DateTime.MinValue,
                SupplierName: Cell(ws, r, 5),
                CodeIfs: Cell(ws, r, 7),
                MotherCode: null,
                MaterialName: Cell(ws, r, 6),
                PoNumber: Cell(ws, r, 8),
                Quantity: Num(ws, r, 10),
                Uom: "ea",
                FinalJudgment: Cell(ws, r, 20),
                Inspector: Cell(ws, r, 21));
        }
    }

    private static IReadOnlyList<double?> Samples5(IXLWorksheet ws, int row, int startCol)
    {
        var a = new double?[5];
        for (var i = 0; i < 5; i++)
            a[i] = NumOrNull(ws, row, startCol + i);
        return a;
    }

    public static bool? ParsePass(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim().ToUpperInvariant();
        if (s is "OK" or "PASS" or "ĐẠT" or "DAT") return true;
        if (s is "NG" or "FAIL" or "N.G" or "KHÔNG ĐẠT" or "KHONG DAT") return false;
        return null;
    }

    private static int? ParseCount(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
            return i;
        if (double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return (int)d;
        return null;
    }

    private static double? ParseLeadingDouble(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var m = Regex.Match(raw, @"-?\d+(?:[.,]\d+)?");
        if (!m.Success) return null;
        var s = m.Value.Replace(',', '.');
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    private static string? FirstNonEmpty(params string?[] vals)
    {
        foreach (var v in vals)
            if (!string.IsNullOrWhiteSpace(v)) return v;
        return null;
    }

    private static bool TrySheet(XLWorkbook wb, string name, out IXLWorksheet ws)
    {
        if (wb.Worksheets.TryGetWorksheet(name, out ws!)) return true;
        ws = null!;
        return false;
    }

    private static bool IsBlank(IXLWorksheet ws, int row, params int[] cols)
    {
        foreach (var c in cols)
            if (!string.IsNullOrWhiteSpace(Cell(ws, row, c))) return false;
        return true;
    }

    private static string? Cell(IXLWorksheet ws, int row, int col)
    {
        var prev = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var v = ws.Cell(row, col).GetFormattedString()?.Trim();
            if (string.IsNullOrEmpty(v) || v is "-" or "--") return null;
            return v;
        }
        finally { CultureInfo.CurrentCulture = prev; }
    }

    private static int? Int(IXLWorksheet ws, int row, int col)
    {
        var cell = ws.Cell(row, col);
        if (cell.TryGetValue(out double d)) return (int)d;
        var s = Cell(ws, row, col);
        return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;
    }

    private static double Num(IXLWorksheet ws, int row, int col)
        => NumOrNull(ws, row, col) ?? 0;

    private static double? NumOrNull(IXLWorksheet ws, int row, int col)
    {
        var cell = ws.Cell(row, col);
        if (cell.IsEmpty()) return null;
        if (cell.TryGetValue(out double d)) return d;
        var s = Cell(ws, row, col);
        if (string.IsNullOrWhiteSpace(s)) return null;
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    private static DateTime? Date(IXLWorksheet ws, int row, int col)
    {
        var cell = ws.Cell(row, col);
        if (cell.TryGetValue(out DateTime dt)) return dt.Date;
        var s = Cell(ws, row, col);
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return parsed.Date;
        return null;
    }
}
