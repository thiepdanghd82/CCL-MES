using System.Globalization;
using System.Text;
using CCL.MES.Domain.Entities;

namespace CCL.MES.Application.AuditLogExport;

/// <summary>
/// Phase 9 audit-export — CSV list exporter. Pure .NET (no new dep).
/// Mirrors <see cref="CCL.MES.Application.SpecExport.CsvSpecListExporter"/>
/// shape: RFC 4180 escape + UTF-8 BOM + CRLF line endings.
///
/// <para>
/// 9 columns: Timestamp_UTC (ISO 8601) / Actor / Role / Action /
/// Target_Type / Target_Id / Detail (raw JSON) / IP / Source.
/// </para>
///
/// <para>
/// Detail column carries the action-specific JSON verbatim. RFC 4180
/// escape wraps the cell in double-quotes and doubles embedded quotes
/// — never strip or pretty-print the payload so downstream SIEM /
/// Excel can round-trip the bytes byte-for-byte.
/// </para>
/// </summary>
public class CsvAuditLogExporter : IAuditLogExporter
{
    public string Format => "csv";
    public string ContentType => "text/csv; charset=utf-8";
    public string FileExtension => "csv";

    private static readonly string[] HeaderRow =
    {
        "Timestamp_UTC",
        "Actor",
        "Role",
        "Action",
        "Target_Type",
        "Target_Id",
        "Detail",
        "IP",
        "Source",
    };

    public byte[] Export(IReadOnlyList<AuditLog> rows, AuditLogExportContext context)
    {
        var sb = new StringBuilder();

        // Header row.
        for (int i = 0; i < HeaderRow.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(CsvEscape(HeaderRow[i]));
        }
        sb.Append("\r\n");

        // Data rows — order: caller passes rows already in display order.
        foreach (var a in rows)
        {
            sb.Append(CsvEscape(a.Timestamp.ToUniversalTime()
                .ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)));
            sb.Append(',');
            sb.Append(CsvEscape(a.ActorUsername));
            sb.Append(',');
            sb.Append(CsvEscape(a.ActorRole));
            sb.Append(',');
            sb.Append(CsvEscape(a.Action));
            sb.Append(',');
            sb.Append(CsvEscape(a.TargetType ?? ""));
            sb.Append(',');
            sb.Append(CsvEscape(a.TargetId ?? ""));
            sb.Append(',');
            sb.Append(CsvEscape(a.Detail ?? ""));
            sb.Append(',');
            sb.Append(CsvEscape(a.IpAddress ?? ""));
            sb.Append(',');
            sb.Append(CsvEscape(a.Source));
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
    /// RFC 4180 escape — wrap in quotes when the value contains
    /// <c>,</c> / <c>"</c> / <c>\n</c> / <c>\r</c>; embedded <c>"</c>
    /// doubled.
    /// </summary>
    public static string CsvEscape(string value)
    {
        bool needsQuoting = value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
        if (!needsQuoting) return value;
        var escaped = value.Replace("\"", "\"\"");
        return "\"" + escaped + "\"";
    }
}
