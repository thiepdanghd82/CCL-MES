using ClosedXML.Excel;
using CCL.MES.Application.Services;
using CCL.MES.Infrastructure.SpecImport;

namespace CCL.MES.Infrastructure.QcLibrary;

/// <summary>
/// Parser cho thư viện hạng mục kiểm v5 — sheet <c>IPQC_FQC_OQC_MAP</c> của
/// <c>IPQC_Library_CMES_v5.xlsx</c> (59 item × 33 cột). Đọc ma trận TICK-BOX
/// (● / ✓ / x / 1 = true; · / rỗng = false) cho 16 cột C~R + đủ field A..AG,
/// trả về <see cref="QcCheckLibraryRow"/> (mirror <see cref="QcCheckLibraryCsv"/>).
/// Chạy qua <see cref="XlsxNormalizer"/> để chịu được file openpyxl (sharedStrings
/// PascalCase). Bỏ dòng ItemID rỗng.
/// </summary>
public static class QcLibraryV5Parser
{
    public const string SheetName = "IPQC_FQC_OQC_MAP";

    // Cột 1-based (ClosedXML). A=1 … AG=33.
    private const int CItemId = 1, CLine = 2;
    private const int CBlankLabel = 3, CFlexo = 4, CLetterPress = 5, CHpIndigo = 6,
        CSilkScreen = 7, CFlatbed = 8, CRdc = 9, CLaminate = 10, CZebra = 11,
        CSheetCut = 12, CPunchHole = 13, CDrillHole = 14, CSlit = 15,
        CIpqc = 16, CFqc = 17, COqc = 18;
    private const int CGroup = 19, CCode = 20, CItemVi = 21, CItemEn = 22,
        CAcceptVi = 23, CAcceptEn = 24, CMethod = 25, CSeverity = 26, CAql = 27,
        CSampling = 28, CCheckType = 29, CDefect = 30, CIsoRef = 31, CCondition = 32, CNote = 33;

    private static readonly HashSet<string> TruthyMarks =
        new(StringComparer.OrdinalIgnoreCase) { "●", "✓", "✔", "x", "1", "true", "yes", "y", "có" };

    public static IReadOnlyList<QcCheckLibraryRow> Parse(Stream xlsxStream)
    {
        using var normalized = XlsxNormalizer.Normalize(xlsxStream);
        using var wb = new XLWorkbook(normalized);
        var ws = wb.Worksheet(SheetName);

        var rows = new List<QcCheckLibraryRow>();
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
        for (var r = 2; r <= lastRow; r++)   // row 1 = header
        {
            var row = ws.Row(r);
            var itemId = S(row, CItemId);
            if (itemId.Length == 0) continue;

            rows.Add(new QcCheckLibraryRow
            {
                ItemId = itemId,
                ProcessLine = S(row, CLine),
                GroupLabel = S(row, CGroup),
                Code = S(row, CCode),
                BlankLabel = Tick(row, CBlankLabel),
                Flexo = Tick(row, CFlexo),
                LetterPress = Tick(row, CLetterPress),
                HpIndigo = Tick(row, CHpIndigo),
                SilkScreen = Tick(row, CSilkScreen),
                Flatbed = Tick(row, CFlatbed),
                Rdc = Tick(row, CRdc),
                Laminate = Tick(row, CLaminate),
                Zebra = Tick(row, CZebra),
                SheetCut = Tick(row, CSheetCut),
                PunchHole = Tick(row, CPunchHole),
                DrillHole = Tick(row, CDrillHole),
                Slit = Tick(row, CSlit),
                Ipqc = Tick(row, CIpqc),
                Fqc = Tick(row, CFqc),
                Oqc = Tick(row, COqc),
                ItemVi = S(row, CItemVi),
                ItemEn = S(row, CItemEn),
                AcceptanceVi = S(row, CAcceptVi),
                AcceptanceEn = S(row, CAcceptEn),
                Method = N(row, CMethod),
                Severity = N(row, CSeverity),
                Aql = N(row, CAql),
                Sampling = N(row, CSampling),
                CheckType = N(row, CCheckType),
                DefectCode = N(row, CDefect),
                IsoRef = N(row, CIsoRef),
                AppliesWhen = N(row, CCondition),
                Note = N(row, CNote),
            });
        }
        return rows;
    }

    private static string S(IXLRow row, int col) => row.Cell(col).GetFormattedString().Trim();
    private static string? N(IXLRow row, int col) { var s = S(row, col); return s.Length == 0 ? null : s; }
    private static bool Tick(IXLRow row, int col) => TruthyMarks.Contains(S(row, col));
}
