using System.Text;

namespace CCL.MES.Application.SpecExport;

/// <summary>
/// Phase 8 PR #31c — CSV list exporter. Pure .NET (KHÔNG dep mới).
///
/// Spec:
///   - RFC 4180-compliant escaping (`,` / `"` / `\n` / `\r` → wrap with `"`;
///     embedded `"` → `""`)
///   - UTF-8 BOM (bytes EF BB BF) prepended để Excel Vietnam locale auto-
///     detect UTF-8 + decode ký tự non-ASCII đúng (bài học Ops Control v1.2
///     Sprint S-D15-COSTING-CUTOVER PR #90 — without BOM Excel hiển thị
///     `220√ó395` thay vì `220×395`).
///   - Line endings `\r\n` (RFC 4180 + Windows Excel default).
///   - Header row = canonical column labels (`SpecListColumns.All`); cột "#"
///     row index thêm ở vị trí đầu (mirror grid).
///   - Empty result → file chỉ có header + 0 data rows (KHÔNG error).
/// </summary>
public class CsvSpecListExporter : ISpecListExporter
{
    public string Format => "csv";
    public string ContentType => "text/csv; charset=utf-8";
    public string FileExtension => "csv";

    public byte[] Export(IReadOnlyList<ProductRevisionListItem> rows, SpecExportContext context)
    {
        var sb = new StringBuilder();
        var cols = SpecListColumns.All;

        // Header — cột "#" + 13 data cols
        sb.Append("\"#\"");
        foreach (var c in cols)
        {
            sb.Append(',');
            sb.Append(CsvEscape(c.Label));
        }
        sb.Append("\r\n");

        // Data rows — pre-flatten via SpecListColumns.ToDisplayCells (mirror grid)
        for (int i = 0; i < rows.Count; i++)
        {
            sb.Append((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            var cells = SpecListColumns.ToDisplayCells(rows[i], context.Culture);
            foreach (var cell in cells)
            {
                sb.Append(',');
                sb.Append(CsvEscape(cell));
            }
            sb.Append("\r\n");
        }

        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var body = Encoding.UTF8.GetBytes(sb.ToString());
        var output = new byte[bom.Length + body.Length];
        Buffer.BlockCopy(bom, 0, output, 0, bom.Length);
        Buffer.BlockCopy(body, 0, output, bom.Length, body.Length);
        return output;
    }

    /// <summary>
    /// RFC 4180 escape — wrap trong `"` nếu value chứa `,` / `"` / `\n` / `\r`;
    /// embedded `"` → `""`. Null → empty quoted string.
    /// </summary>
    public static string CsvEscape(string? value)
    {
        if (value is null) return "\"\"";
        bool needsQuoting = value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
        if (!needsQuoting) return value;
        var escaped = value.Replace("\"", "\"\"");
        return "\"" + escaped + "\"";
    }
}
