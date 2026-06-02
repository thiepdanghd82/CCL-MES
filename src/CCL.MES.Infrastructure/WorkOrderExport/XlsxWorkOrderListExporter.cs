using CCL.MES.Application.Services;
using CCL.MES.Application.WorkOrderExport;
using ClosedXML.Excel;

namespace CCL.MES.Infrastructure.WorkOrderExport;

/// <summary>
/// Phase 8 PR #32c — Excel list exporter for Work Orders. Mirrors
/// <c>XlsxSpecListExporter</c> from PR #31c. ClosedXML 0.104.2 reused
/// from existing Infrastructure deps — NO new package.
///
/// Layout:
///   - Single worksheet "Work Orders"
///   - Header row 1: bold + gray fill + center align + freeze pane
///   - Auto-filter on full data range so operators can filter Section
///     column to split Active vs Closed in Excel
///   - Int columns (TargetQty, ProducedQty) stay native int with "0"
///     number format + center alignment
/// </summary>
public class XlsxWorkOrderListExporter : IWorkOrderListExporter
{
    public string Format => "xlsx";
    public string ContentType => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public string FileExtension => "xlsx";

    public byte[] Export(
        IReadOnlyList<WorkOrderCardItem> active,
        IReadOnlyList<WorkOrderCardItem> closed,
        WoExportContext context)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(SafeSheetName(context.Title));
        var cols = WoListColumns.All;

        // Header row 1: "#" + 12 data cols
        ws.Cell(1, 1).Value = "#";
        for (int c = 0; c < cols.Count; c++)
            ws.Cell(1, c + 2).Value = cols[c].Label;

        var headerRange = ws.Range(1, 1, 1, cols.Count + 1);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#E5E7EB");
        headerRange.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        // Data rows — Active first, then Closed (matches CSV ordering)
        int rowIndex = 0;
        int excelRow = 2;
        foreach (var row in active)
        {
            rowIndex++;
            WriteRow(ws, excelRow, rowIndex, row, "Active", cols);
            excelRow++;
        }
        foreach (var row in closed)
        {
            rowIndex++;
            WriteRow(ws, excelRow, rowIndex, row, "Closed", cols);
            excelRow++;
        }

        // Column widths
        ws.Column(1).Width = 6;  // # column
        for (int c = 0; c < cols.Count; c++)
            ws.Column(c + 2).Width = Math.Max(8, cols[c].WidthCh + 2);

        ws.SheetView.FreezeRows(1);

        if (rowIndex > 0)
        {
            ws.RangeUsed()!.SetAutoFilter();
        }
        else
        {
            ws.Range(1, 1, 1, cols.Count + 1).SetAutoFilter();
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void WriteRow(IXLWorksheet ws, int excelRow, int sequence,
        WorkOrderCardItem row, string section, IReadOnlyList<WoListColumn> cols)
    {
        ws.Cell(excelRow, 1).Value = sequence;
        var cells = WoListColumns.ToTypedCells(row, section);
        for (int c = 0; c < cells.Length; c++)
        {
            int excelCol = c + 2;
            SetCellValue(ws.Cell(excelRow, excelCol), cells[c], cols[c]);
        }
    }

    private static void SetCellValue(IXLCell cell, object? value, WoListColumn col)
    {
        if (value is null) return;
        switch (col.Type)
        {
            case WoColumnType.Int:
                if (value is int i) { cell.Value = i; cell.Style.NumberFormat.Format = "0"; }
                else cell.SetValue(value.ToString());
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                break;
            case WoColumnType.Text:
            default:
                cell.SetValue(value.ToString());
                break;
        }
    }

    private static string SafeSheetName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Sheet1";
        var bad = new[] { '\\', '/', '?', '*', '[', ']', ':' };
        var cleaned = new string(raw.Where(c => !bad.Contains(c)).ToArray()).Trim();
        if (cleaned.Length > 31) cleaned = cleaned.Substring(0, 31);
        return string.IsNullOrEmpty(cleaned) ? "Sheet1" : cleaned;
    }
}
