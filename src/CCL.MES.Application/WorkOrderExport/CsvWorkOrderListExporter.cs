using System.Text;
using CCL.MES.Application.Services;

namespace CCL.MES.Application.WorkOrderExport;

/// <summary>
/// Phase 8 PR #32c — CSV list exporter for Work Orders. Pure .NET, no
/// extra deps. Port of <c>CsvSpecListExporter</c> from PR #31c.
///
/// Spec:
///   - RFC 4180 escaping (`,` / `"` / `\n` / `\r` → wrap with `"`;
///     embedded `"` → `""`)
///   - UTF-8 BOM (EF BB BF) prepended so Excel (Vietnam locale) auto-
///     detects UTF-8 and decodes ký tự non-ASCII correctly
///   - Line endings `\r\n` (RFC 4180 + Windows Excel default)
///   - Header row = canonical column labels; "#" sequence column prefixed
///   - Active + Closed concatenated (Section column = "Active" / "Closed"
///     so operator can filter/sort in Excel)
/// </summary>
public class CsvWorkOrderListExporter : IWorkOrderListExporter
{
    public string Format => "csv";
    public string ContentType => "text/csv; charset=utf-8";
    public string FileExtension => "csv";

    public byte[] Export(
        IReadOnlyList<WorkOrderCardItem> active,
        IReadOnlyList<WorkOrderCardItem> closed,
        WoExportContext context)
    {
        var sb = new StringBuilder();
        var cols = WoListColumns.All;

        // Header — "#" + 12 data cols
        sb.Append("\"#\"");
        foreach (var c in cols)
        {
            sb.Append(',');
            sb.Append(CsvEscape(c.Label));
        }
        sb.Append("\r\n");

        // Data rows — Active first, then Closed; sequence "#" starts at 1
        int index = 0;
        foreach (var row in active)
        {
            index++;
            AppendRow(sb, index, row, "Active", context);
        }
        foreach (var row in closed)
        {
            index++;
            AppendRow(sb, index, row, "Closed", context);
        }

        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var body = Encoding.UTF8.GetBytes(sb.ToString());
        var output = new byte[bom.Length + body.Length];
        Buffer.BlockCopy(bom, 0, output, 0, bom.Length);
        Buffer.BlockCopy(body, 0, output, bom.Length, body.Length);
        return output;
    }

    private static void AppendRow(StringBuilder sb, int index, WorkOrderCardItem row, string section, WoExportContext context)
    {
        sb.Append(index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var cells = WoListColumns.ToDisplayCells(row, section, context.Culture);
        foreach (var cell in cells)
        {
            sb.Append(',');
            sb.Append(CsvEscape(cell));
        }
        sb.Append("\r\n");
    }

    public static string CsvEscape(string? value)
    {
        if (value is null) return "\"\"";
        bool needsQuoting = value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
        if (!needsQuoting) return value;
        var escaped = value.Replace("\"", "\"\"");
        return "\"" + escaped + "\"";
    }
}
