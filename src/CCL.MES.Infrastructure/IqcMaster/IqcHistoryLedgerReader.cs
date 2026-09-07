using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using CCL.MES.Application.Services;

namespace CCL.MES.Infrastructure.IqcMaster;

/// <summary>
/// Đọc 4 sheet ledger của "IQC report 2026": Roll · PCS · Chem · Tool.
/// Map cột theo VỊ TRÍ đã đối chiếu tay (2 dòng tiêu đề; data từ dòng 3).
/// Culture invariant quanh số/ngày (L44).
///
/// PCS kích thước: (1) ô <c>290x301</c> → rộng=290, dài=301;
/// (2) cặp 2 dòng (dòng 2 thường <c>Stt</c> trống hoặc cùng ngày/cùng Code IFS)
/// → dòng 1 rộng, dòng 2 dài.
/// </summary>
public static class IqcHistoryLedgerReader
{
    private const int FirstDataRow = 3;

    private static readonly Regex WxLRegex = new(
        @"^(-?\d+(?:[.,]\d+)?)\s*[x×X]\s*(-?\d+(?:[.,]\d+)?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TolRegex = new(
        @"^(-?\d+(?:[.,]\d+)?)\s*\+\s*(-?\d+(?:[.,]\d+)?)\s*-\s*(-?\d+(?:[.,]\d+)?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

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

    /// <summary>
    /// Đọc workbook. <paramref name="absorbedPcsPairs"/> (optional) nhận
    /// (primaryExcelRow, absorbedExcelRow) — dòng PCS đã gộp vào phiếu primary.
    /// </summary>
    public static List<IqcHistoryLedgerRow> Read(
        Stream xlsx, List<(int PrimaryExcelRow, int AbsorbedExcelRow)>? absorbedPcsPairs = null)
    {
        using var wb = new XLWorkbook(xlsx);
        var rows = new List<IqcHistoryLedgerRow>();
        rows.AddRange(ReadRoll(wb));
        rows.AddRange(ReadPcs(wb, absorbedPcsPairs));
        rows.AddRange(ReadChem(wb));
        rows.AddRange(ReadTool(wb));
        return rows;
    }

    /// <summary>Tách ô <c>290x301</c> / số đơn → (rộng, dài).</summary>
    public static (double? Width, double? Length) ParseWidthLengthCell(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (null, null);
        var m = WxLRegex.Match(raw.Trim());
        if (m.Success)
            return (ParseDoubleToken(m.Groups[1].Value), ParseDoubleToken(m.Groups[2].Value));
        return (ParseLeadingDouble(raw), null);
    }

    /// <summary>Parse TC dạng <c>97.5+0.5-0.2</c> hoặc <c>290x300</c>.</summary>
    public static (double? Nominal, double? Low, double? Up, double? LengthNominal) ParseSizeSpec(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (null, null, null, null);
        var t = raw.Trim();
        var wxl = WxLRegex.Match(t);
        if (wxl.Success)
        {
            var w = ParseDoubleToken(wxl.Groups[1].Value);
            var l = ParseDoubleToken(wxl.Groups[2].Value);
            return (w, null, null, l);
        }
        var tol = TolRegex.Match(t);
        if (tol.Success)
        {
            var nom = ParseDoubleToken(tol.Groups[1].Value);
            var up = ParseDoubleToken(tol.Groups[2].Value);
            var lowDelta = ParseDoubleToken(tol.Groups[3].Value);
            return (nom, nom is null || lowDelta is null ? null : nom - lowDelta,
                nom is null || up is null ? null : nom + up, null);
        }
        var lead = ParseLeadingDouble(t);
        return (lead, null, null, null);
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
                LengthNominal: null,
                LengthLow: null,
                LengthUp: null,
                LengthSamples: Array.Empty<double?>(),
                LengthSampleTexts: Array.Empty<string?>(),
                LengthPass: null,
                LengthSpec: null,
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

    private static IEnumerable<IqcHistoryLedgerRow> ReadPcs(
        XLWorkbook wb, List<(int PrimaryExcelRow, int AbsorbedExcelRow)>? absorbedPairs)
    {
        if (!TrySheet(wb, "PCS", out var ws)) yield break;
        var last = ws.LastRowUsed()?.RowNumber() ?? 0;
        IqcHistoryLedgerRow? open = null;
        var openComplete = false;

        for (var r = FirstDataRow; r <= last; r++)
        {
            if (IsBlank(ws, r, 1, 6, 8)) continue;
            var built = BuildPcsRow(ws, r);

            if (open is not null && !openComplete && IsPcsLengthCompanion(open, built))
            {
                open = MergePcsLength(open, built);
                openComplete = true;
                absorbedPairs?.Add((open.ExcelRow, r));
                continue;
            }

            if (open is not null)
                yield return open;

            open = built;
            openComplete = PcsHasLength(built.Checks);
        }

        if (open is not null)
            yield return open;
    }

    private static IqcHistoryLedgerRow BuildPcsRow(IXLWorksheet ws, int r)
    {
        var defects = new List<IqcLedgerDefectCell>(9);
        for (var i = 0; i < 9; i++)
        {
            var col = 17 + i; // Q=17 … Y=25
            defects.Add(new IqcLedgerDefectCell(PcsVisualKeys[i], ParseCount(Cell(ws, r, col))));
        }

        var widthTexts = new string?[5];
        var widthNums = new double?[5];
        var lengthTexts = new string?[5];
        var lengthNums = new double?[5];
        var anyWxL = false;
        for (var i = 0; i < 5; i++)
        {
            var t = Cell(ws, r, 29 + i); // AC=29 … AG=33
            var (w, l) = ParseWidthLengthCell(t);
            if (l.HasValue) anyWxL = true;
            widthNums[i] = w;
            lengthNums[i] = l;
            if (l.HasValue)
            {
                widthTexts[i] = w?.ToString(CultureInfo.InvariantCulture);
                lengthTexts[i] = l?.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                widthTexts[i] = t;
            }
        }

        var ab = Cell(ws, r, 28); // AB — TC rộng x dài
        var (nom, low, up, lenNomFromAb) = ParseSizeSpec(ab);
        if (lenNomFromAb.HasValue) anyWxL = true;

        var widthPass = ParsePass(Cell(ws, r, 34));
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
            WidthNominal: nom,
            WidthLow: low,
            WidthUp: up,
            WidthSamples: widthNums,
            WidthSampleTexts: widthTexts,
            WidthPass: widthPass,
            LengthNominal: lenNomFromAb,
            LengthLow: null,
            LengthUp: null,
            LengthSamples: anyWxL ? lengthNums : Array.Empty<double?>(),
            LengthSampleTexts: anyWxL ? lengthTexts : Array.Empty<string?>(),
            LengthPass: anyWxL ? widthPass : null,
            LengthSpec: anyWxL ? ab : null,
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

        return new IqcHistoryLedgerRow(
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

    /// <summary>
    /// Dòng sau là nửa dài của phiếu đang mở: cùng Code IFS, chưa có dài,
    /// và (Stt trống · hoặc không có ngày · hoặc cùng ngày nhập).
    /// </summary>
    public static bool IsPcsLengthCompanion(IqcHistoryLedgerRow open, IqcHistoryLedgerRow next)
    {
        if (PcsHasLength(open.Checks)) return false;
        if (!string.Equals(open.CodeIfs?.Trim(), next.CodeIfs?.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;
        if (!HasAnyDimSample(next.Checks)) return false;

        if (next.Stt is null) return true;
        if (next.InspectedAt == DateTime.MinValue) return true;
        if (open.InspectedAt != DateTime.MinValue
            && open.InspectedAt.Date == next.InspectedAt.Date)
            return true;
        return false;
    }

    private static bool PcsHasLength(IqcHistoryLedgerChecks? c)
        => c is not null && (
            (c.LengthSamples?.Any(v => v.HasValue) ?? false)
            || (c.LengthSampleTexts?.Any(t => !string.IsNullOrWhiteSpace(t)) ?? false)
            || c.LengthNominal.HasValue
            || c.LengthPass.HasValue);

    private static bool HasAnyDimSample(IqcHistoryLedgerChecks? c)
        => c is not null && (
            (c.WidthSamples?.Any(v => v.HasValue) ?? false)
            || (c.WidthSampleTexts?.Any(t => !string.IsNullOrWhiteSpace(t)) ?? false)
            || (c.LengthSamples?.Any(v => v.HasValue) ?? false));

    private static IqcHistoryLedgerRow MergePcsLength(IqcHistoryLedgerRow open, IqcHistoryLedgerRow cont)
    {
        var o = open.Checks!;
        var c = cont.Checks!;
        // Continuation row's "width" cells are actually length measurements.
        var lengthNom = c.WidthNominal;
        var lengthLow = c.WidthLow;
        var lengthUp = c.WidthUp;
        var lengthSpec = FormatSpec(lengthNom, lengthLow, lengthUp)
                         ?? lengthNom?.ToString(CultureInfo.InvariantCulture);

        var merged = o with
        {
            LengthNominal = lengthNom,
            LengthLow = lengthLow,
            LengthUp = lengthUp,
            LengthSamples = c.WidthSamples,
            LengthSampleTexts = c.WidthSampleTexts,
            LengthPass = c.WidthPass ?? o.LengthPass,
            LengthSpec = lengthSpec,
            DimensionInspector = FirstNonEmpty(o.DimensionInspector, c.DimensionInspector),
        };

        return open with { Checks = merged };
    }

    private static string? FormatSpec(double? nom, double? low, double? up)
    {
        if (nom is null) return null;
        if (low is null || up is null)
            return nom.Value.ToString(CultureInfo.InvariantCulture);
        var plus = up.Value - nom.Value;
        var minus = nom.Value - low.Value;
        return string.Create(CultureInfo.InvariantCulture,
            $"{nom.Value}+{plus}-{minus}");
    }

    private static readonly string[] ChemVisualKeys = ["CD-01", "CD-02", "CD-03"];

    private static IEnumerable<IqcHistoryLedgerRow> ReadChem(XLWorkbook wb)
    {
        if (!TrySheet(wb, "Chem", out var ws)) yield break;
        var last = ws.LastRowUsed()?.RowNumber() ?? 0;
        for (var r = FirstDataRow; r <= last; r++)
        {
            if (IsBlank(ws, r, 1, 6, 7)) continue;
            var qtyKg = Num(ws, r, 10);

            // Cột 16–18 = OK/NG từng hạng mục (không phải đếm lỗi) → Count 0=OK, 1=NG.
            var defects = new List<IqcLedgerDefectCell>(3);
            for (var i = 0; i < 3; i++)
            {
                var pass = ParsePass(Cell(ws, r, 16 + i));
                int? count = pass is null ? null : (pass.Value ? 0 : 1);
                defects.Add(new IqcLedgerDefectCell(ChemVisualKeys[i], count));
            }

            var checks = new IqcHistoryLedgerChecks(
                WarehouseInDate: Cell(ws, r, 13),
                ExpiryText: Cell(ws, r, 14),
                Pefc: null,
                PefcLevel: null,
                PackagingSpec: Cell(ws, r, 9),
                PackagingPass: ParsePass(Cell(ws, r, 15)),
                PackagingInspector: null,
                VisualSampleQty: Int(ws, r, 12),
                VisualDefects: defects,
                VisualPass: ParsePass(Cell(ws, r, 19)),
                VisualInspector: Cell(ws, r, 23),
                WidthNominal: null,
                WidthLow: null,
                WidthUp: null,
                WidthSamples: Array.Empty<double?>(),
                WidthSampleTexts: Array.Empty<string?>(),
                WidthPass: null,
                LengthNominal: null,
                LengthLow: null,
                LengthUp: null,
                LengthSamples: Array.Empty<double?>(),
                LengthSampleTexts: Array.Empty<string?>(),
                LengthPass: null,
                LengthSpec: null,
                ThicknessSpec: null,
                ThicknessSamples: Array.Empty<double?>(),
                ThicknessPass: null,
                DimensionInspector: null,
                FuncSpec: null,
                FuncPass: null,
                FuncInspector: null,
                LabSpec: null,
                LabSheets: Array.Empty<double?>(),
                LabPass: null,
                LabInspector: null,
                HsfPass: ParsePass(Cell(ws, r, 20)),
                CoaPass: ParsePass(Cell(ws, r, 21)));

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
                Inspector: Cell(ws, r, 23),
                Checks: checks);
        }
    }

    private static readonly string[] ToolVisualKeys =
        ["TD-01", "TD-02", "TD-03", "TD-04", "TD-05"];

    private static IEnumerable<IqcHistoryLedgerRow> ReadTool(XLWorkbook wb)
    {
        if (!TrySheet(wb, "Tool", out var ws)) yield break;
        var last = ws.LastRowUsed()?.RowNumber() ?? 0;
        for (var r = FirstDataRow; r <= last; r++)
        {
            if (IsBlank(ws, r, 1, 6, 7)) continue;

            // Cột 13–17 = đếm lỗi TD-01..05 (Ac=0), không phải OK/NG kiểu Chem.
            var defects = new List<IqcLedgerDefectCell>(5);
            for (var i = 0; i < 5; i++)
                defects.Add(new IqcLedgerDefectCell(ToolVisualKeys[i], ParseCount(Cell(ws, r, 13 + i))));

            var checks = new IqcHistoryLedgerChecks(
                WarehouseInDate: Cell(ws, r, 9), // Ngày sản xuất
                ExpiryText: null,
                Pefc: null,
                PefcLevel: null,
                PackagingSpec: null,
                PackagingPass: ParsePass(Cell(ws, r, 12)), // Tem nhãn
                PackagingInspector: null,
                VisualSampleQty: Int(ws, r, 11),
                VisualDefects: defects,
                VisualPass: ParsePass(Cell(ws, r, 18)),
                VisualInspector: Cell(ws, r, 21),
                WidthNominal: null,
                WidthLow: null,
                WidthUp: null,
                WidthSamples: Array.Empty<double?>(),
                WidthSampleTexts: Array.Empty<string?>(),
                WidthPass: null,
                LengthNominal: null,
                LengthLow: null,
                LengthUp: null,
                LengthSamples: Array.Empty<double?>(),
                LengthSampleTexts: Array.Empty<string?>(),
                LengthPass: null,
                LengthSpec: null,
                ThicknessSpec: null,
                ThicknessSamples: Array.Empty<double?>(),
                ThicknessPass: null,
                DimensionInspector: null,
                FuncSpec: null,
                FuncPass: null,
                FuncInspector: null,
                LabSpec: null,
                LabSheets: Array.Empty<double?>(),
                LabPass: null,
                LabInspector: null,
                HsfPass: ParsePass(Cell(ws, r, 19)),
                CoaPass: null);

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
                Inspector: Cell(ws, r, 21),
                Checks: checks);
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

    public static double? ParseLeadingDouble(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var m = Regex.Match(raw, @"-?\d+(?:[.,]\d+)?");
        if (!m.Success) return null;
        return ParseDoubleToken(m.Value);
    }

    private static double? ParseDoubleToken(string s)
    {
        s = s.Replace(',', '.');
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
        var s = Cell(ws, row, col);
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return i;
        if (double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return (int)d;
        return null;
    }

    private static double Num(IXLWorksheet ws, int row, int col)
        => NumOrNull(ws, row, col) ?? 0;

    private static double? NumOrNull(IXLWorksheet ws, int row, int col)
    {
        var s = Cell(ws, row, col);
        return ParseLeadingDouble(s);
    }

    private static DateTime? Date(IXLWorksheet ws, int row, int col)
    {
        var cell = ws.Cell(row, col);
        if (cell.TryGetValue(out DateTime dt)) return dt;
        var s = Cell(ws, row, col);
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (DateTime.TryParse(s, CultureInfo.GetCultureInfo("vi-VN"), DateTimeStyles.None, out var d1))
            return d1;
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d2))
            return d2;
        return null;
    }
}
