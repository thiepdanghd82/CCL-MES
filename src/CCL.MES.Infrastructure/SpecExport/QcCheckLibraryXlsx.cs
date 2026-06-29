using CCL.MES.Application.Services;
using ClosedXML.Excel;

namespace CCL.MES.Infrastructure.SpecExport;

/// <summary>
/// QC Library admin (Bước 1) — .xlsx parser for the 19-column check-item
/// library template. Mirrors <see cref="QcCheckLibraryCsv.ParseDetailed"/>
/// EXACTLY (same column order 0..18, same required-field rules, same skip
/// diagnostics, same <see cref="QcCheckLibraryCsv.ParseResult"/> shape) so
/// CSV-file seed and UI xlsx-import share one downstream importer + one set
/// of validation rules. ClosedXML is already a dependency (Infrastructure,
/// MIT, no NU1903) — reused here, no new package.
/// </summary>
public static class QcCheckLibraryXlsx
{
    /// <summary>Column headers (row 1) of the import/export template, in order.</summary>
    public static readonly string[] TemplateHeaders =
    {
        "ItemId", "ProcessLine", "GroupLabel", "Code", "ItemVi", "ItemEn",
        "AcceptanceVi", "AcceptanceEn", "Method", "Severity", "Aql", "Sampling",
        "CheckType", "DefectCode", "ParetoPct", "ShortForm", "IsoRef",
        "AppliesWhen", "Note",
    };

    // Same required (non-empty) columns as QcCheckLibraryCsv (0-indexed).
    private static readonly (int Idx, string Name)[] RequiredFields =
    {
        (1, "ProcessLine"), (2, "GroupLabel"), (3, "Code"),
        (4, "ItemVi"), (5, "ItemEn"), (6, "AcceptanceVi"), (7, "AcceptanceEn"),
    };

    private const int ExpectedColumns = 19;

    /// <summary>
    /// Parse the first worksheet. Row 1 = header (skipped). Data rows with an
    /// empty ItemId are skipped silently; rows missing a required field are
    /// dropped + recorded in <see cref="QcCheckLibraryCsv.ParseResult.Skipped"/>.
    /// </summary>
    public static QcCheckLibraryCsv.ParseResult ParseDetailed(Stream xlsx)
    {
        using var wb = new XLWorkbook(xlsx);
        var ws = wb.Worksheets.FirstOrDefault();
        var rows = new List<QcCheckLibraryRow>();
        var skipped = new List<string>();
        if (ws is null) return new QcCheckLibraryCsv.ParseResult(rows, skipped);

        var used = ws.RangeUsed();
        if (used is null) return new QcCheckLibraryCsv.ParseResult(rows, skipped);

        int lastRow = used.LastRow().RowNumber();
        for (int r = 2; r <= lastRow; r++) // row 1 = header
        {
            var row = ws.Row(r);
            // 1-based cells; G(i) reads 0-indexed column i → cell i+1.
            string G(int idx) => row.Cell(idx + 1).GetString().Trim();

            var itemId = G(0);
            if (itemId.Length == 0) continue; // blank row — silent skip

            var missing = RequiredFields.FirstOrDefault(rf => G(rf.Idx).Length == 0);
            if (missing.Name is not null)
            {
                skipped.Add($"row {r} (ItemId='{itemId}'): rỗng field bắt buộc '{missing.Name}'");
                continue;
            }

            rows.Add(new QcCheckLibraryRow
            {
                ItemId = itemId,
                ProcessLine = G(1),
                GroupLabel = G(2),
                Code = G(3),
                ItemVi = G(4),
                ItemEn = G(5),
                AcceptanceVi = G(6),
                AcceptanceEn = G(7),
                Method = NullIfEmpty(G(8)),
                Severity = NullIfEmpty(G(9)),
                Aql = NullIfEmpty(G(10)),
                Sampling = NullIfEmpty(G(11)),
                CheckType = NullIfEmpty(G(12)),
                DefectCode = NullIfEmpty(G(13)),
                ParetoPct = NullIfEmpty(G(14)),
                ShortForm = NullIfEmpty(G(15)),
                IsoRef = NullIfEmpty(G(16)),
                AppliesWhen = NullIfEmpty(G(17)),
                Note = NullIfEmpty(G(18)),
            });
        }
        return new QcCheckLibraryCsv.ParseResult(rows, skipped);
    }

    /// <summary>Build a blank template .xlsx (header row only) for operators to fill.</summary>
    public static byte[] BuildTemplate()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("CheckItemLibrary");
        for (int c = 0; c < TemplateHeaders.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = TemplateHeaders[c];
            cell.Style.Font.Bold = true;
        }
        // One example row so the operator sees the expected shape.
        string[] sample =
        {
            "LBL-A1", "LABEL", "A·Ngoại quan", "A1", "Đúng nội dung in",
            "Print content correct", "Khớp file", "Matches artwork", "Visual",
            "◆ Critical", "", "", "Visual", "CONTENT", "", "", "", "", "",
        };
        for (int c = 0; c < sample.Length; c++) ws.Cell(2, c + 1).Value = sample[c];
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;
}
