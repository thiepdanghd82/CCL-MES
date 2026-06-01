using CCL.MES.Application;
using CCL.MES.Application.SpecExport;
using ClosedXML.Excel;

namespace CCL.MES.Infrastructure.SpecExport;

/// <summary>
/// Phase 8 PR #31c — Excel list exporter (ClosedXML 0.104.2 reuse từ PR #31a).
///
/// Features:
///   - Single worksheet "NPI Spec Library"
///   - Header row 1: bold + colored fill + freeze pane
///   - Auto-filter on column range (operator có thể filter/sort trong Excel)
///   - Per-column number formats: Int / Decimal1 / Date / Text (mirror grid types)
///   - Column widths theo <see cref="SpecListColumn.WidthCh"/>
///   - Metadata row trên header (export date + filter desc) — optional, render
///     khi context.FilterDescription non-null
/// </summary>
public class XlsxSpecListExporter : ISpecListExporter
{
    public string Format => "xlsx";
    public string ContentType => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public string FileExtension => "xlsx";

    public byte[] Export(IReadOnlyList<ProductRevisionListItem> rows, SpecExportContext context)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(SafeSheetName(context.Title));
        var cols = SpecListColumns.All;

        // Header row 1: "#" + 13 data cols
        ws.Cell(1, 1).Value = "#";
        for (int c = 0; c < cols.Count; c++)
            ws.Cell(1, c + 2).Value = cols[c].Label;

        // Header style
        var headerRange = ws.Range(1, 1, 1, cols.Count + 1);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#E5E7EB");
        headerRange.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        // Data rows — typed projection for proper Excel cell types
        for (int r = 0; r < rows.Count; r++)
        {
            int excelRow = r + 2;
            ws.Cell(excelRow, 1).Value = r + 1;  // # column
            var cells = SpecListColumns.ToTypedCells(rows[r]);
            for (int c = 0; c < cells.Length; c++)
            {
                int excelCol = c + 2;
                SetCellValue(ws.Cell(excelRow, excelCol), cells[c], cols[c]);
            }
        }

        // Column widths
        ws.Column(1).Width = 6;  // # column
        for (int c = 0; c < cols.Count; c++)
            ws.Column(c + 2).Width = Math.Max(8, cols[c].WidthCh + 2);

        // Freeze pane (row 1)
        ws.SheetView.FreezeRows(1);

        // Auto-filter on full range
        if (rows.Count > 0)
        {
            ws.RangeUsed()!.SetAutoFilter();
        }
        else
        {
            // Empty result — auto-filter chỉ trên header để Excel mở vẫn OK
            ws.Range(1, 1, 1, cols.Count + 1).SetAutoFilter();
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Render typed value sang ClosedXML cell với format đúng theo column type.
    /// Null → cell stays empty (default formatting). ClosedXML `IXLCell.Value`
    /// accepts XLCellValue — convert via helpers tránh boxing-only path.
    /// </summary>
    private static void SetCellValue(IXLCell cell, object? value, SpecListColumn col)
    {
        if (value is null) return;
        switch (col.Type)
        {
            case ColumnType.Int:
                if (value is int i) { cell.Value = i; cell.Style.NumberFormat.Format = "0"; }
                else cell.SetValue(value.ToString());
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                break;
            case ColumnType.Decimal1:
                if (value is double d) { cell.Value = d; cell.Style.NumberFormat.Format = "0.0"; }
                else cell.SetValue(value.ToString());
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                break;
            case ColumnType.Date:
                if (value is DateTime dt) { cell.Value = dt; cell.Style.DateFormat.Format = "yyyy-MM-dd"; }
                else cell.SetValue(value.ToString());
                break;
            case ColumnType.Text:
            default:
                cell.SetValue(value.ToString());
                break;
        }
    }

    /// <summary>
    /// Excel sheet name max 31 chars + cấm `\/?*[]:` — sanitize input safely.
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
