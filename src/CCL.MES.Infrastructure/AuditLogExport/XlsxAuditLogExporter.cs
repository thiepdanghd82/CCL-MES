using CCL.MES.Application.AuditLogExport;
using CCL.MES.Domain.Entities;
using ClosedXML.Excel;

namespace CCL.MES.Infrastructure.AuditLogExport;

/// <summary>
/// Phase 9 audit-export — XLSX exporter (ClosedXML 0.104.2 reuse from
/// PR #31a — no new dep). Mirrors
/// <see cref="CCL.MES.Infrastructure.SpecExport.XlsxSpecListExporter"/>
/// shape: single worksheet, bold + colored header, freeze row 1,
/// auto-filter on the data range, ISO-8601 date formatting on column 1.
///
/// <para>
/// 9 columns match <see cref="CsvAuditLogExporter"/> for round-trip
/// consistency: Timestamp_UTC / Actor / Role / Action / Target_Type /
/// Target_Id / Detail / IP / Source.
/// </para>
/// </summary>
public class XlsxAuditLogExporter : IAuditLogExporter
{
    public string Format => "xlsx";
    public string ContentType => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public string FileExtension => "xlsx";

    private static readonly string[] HeaderRow =
    {
        "Timestamp (UTC)",
        "Actor",
        "Role",
        "Action",
        "Target Type",
        "Target Id",
        "Detail (JSON)",
        "IP",
        "Source",
    };

    // Per-column display widths (chars). Detail is wide so the JSON
    // payload is at least partially visible without manual resize.
    private static readonly int[] ColWidths =
    {
        22, 16, 12, 22, 18, 18, 60, 14, 10,
    };

    public byte[] Export(IReadOnlyList<AuditLog> rows, AuditLogExportContext context)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(SafeSheetName(context.Title));

        // Header row.
        for (int c = 0; c < HeaderRow.Length; c++)
            ws.Cell(1, c + 1).Value = HeaderRow[c];

        var headerRange = ws.Range(1, 1, 1, HeaderRow.Length);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#E5E7EB");
        headerRange.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        // Data rows.
        for (int r = 0; r < rows.Count; r++)
        {
            var a = rows[r];
            int excelRow = r + 2;

            // Col 1 — Timestamp UTC (typed DateTime so Excel sorts it numerically).
            ws.Cell(excelRow, 1).Value = a.Timestamp.ToUniversalTime();
            ws.Cell(excelRow, 1).Style.DateFormat.Format = "yyyy-mm-ddThh:mm:ss";

            ws.Cell(excelRow, 2).Value = a.ActorUsername ?? "";
            ws.Cell(excelRow, 3).Value = a.ActorRole ?? "";
            ws.Cell(excelRow, 4).Value = a.Action ?? "";
            ws.Cell(excelRow, 5).Value = a.TargetType ?? "";
            ws.Cell(excelRow, 6).Value = a.TargetId ?? "";

            // Detail JSON — text format, wrap so very long payloads stay readable.
            ws.Cell(excelRow, 7).Value = a.Detail ?? "";
            ws.Cell(excelRow, 7).Style.Alignment.WrapText = true;

            ws.Cell(excelRow, 8).Value = a.IpAddress ?? "";
            ws.Cell(excelRow, 9).Value = a.Source ?? "";
        }

        for (int c = 0; c < ColWidths.Length; c++)
            ws.Column(c + 1).Width = ColWidths[c];

        ws.SheetView.FreezeRows(1);

        if (rows.Count > 0)
            ws.RangeUsed()!.SetAutoFilter();
        else
            ws.Range(1, 1, 1, HeaderRow.Length).SetAutoFilter();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Excel sheet name max 31 chars + cấm <c>\/?*[]:</c> — sanitize.
    /// </summary>
    private static string SafeSheetName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Sheet1";
        var bad = new[] { '\\', '/', '?', '*', '[', ']', ':' };
        var cleaned = new string(raw.Where(c => !bad.Contains(c)).ToArray()).Trim();
        if (cleaned.Length > 31) cleaned = cleaned.Substring(0, 31);
        return string.IsNullOrEmpty(cleaned) ? "Sheet1" : cleaned;
    }
}
