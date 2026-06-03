using System.Globalization;
using System.Text.Json;
using CCL.MES.Application.AuditLogExport;
using CCL.MES.Application.Audit;
using CCL.MES.Domain.Audit;
using CCL.MES.Infrastructure.AuditLogExport;
using CCL.MES.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CCL.MES.Web.Controllers;

/// <summary>
/// Phase 9 audit-export — Export audit log to CSV / XLSX. Endpoints:
///   GET /api/audit-log/export/csv?search=&amp;action=&amp;actor=&amp;from=&amp;to=
///   GET /api/audit-log/export/xlsx?search=&amp;action=&amp;actor=&amp;from=&amp;to=
///
/// RBAC: <c>Admin</c> only. Audit trail carries login-fail attempts +
/// IP + target ids — surface is admin-grade sensitive. Per Henry's
/// Q1 (PHASE9-AUDIT-RETENTION-PLAN §3): AdminOnly default.
///
/// <para>
/// Pattern reuse PR #31c (SpecsExportController): path-segment route
/// (no dot-extension — bài học #33 static-file middleware claim);
/// format-keyed exporter dispatch; try-catch wrap → 500 with detail
/// message (no stack leak); audit emit AFTER successful export so the
/// audit trail captures the export event even if the byte stream
/// happens to fail mid-response.
/// </para>
///
/// <para>
/// <b>Hard cap</b>: 100k rows per export (PHASE9 plan §3 Q3). Over-cap
/// returns 400 with the matched-row count so the operator can narrow
/// the filter. Prevents a multi-GB workbook OOM on the prod box when
/// the audit table has accumulated millions of rows.
/// </para>
///
/// <para>
/// <b>Audit emit</b>: <c>AUDIT_EXPORT</c> row written via
/// <see cref="IAuditWriter"/> on every successful export. Detail JSON:
/// <c>{ format, search, action_filter, actor_filter, from, to, rows,
/// filename, content_length }</c>. This is the "audit-the-audit-export"
/// hook — admin cannot lift trail data without leaving a trail event.
/// </para>
/// </summary>
[ApiController]
[Route("api/audit-log/export")]
[Authorize(Roles = "Admin")]
public class AuditLogExportController : ControllerBase
{
    private const int HardCapRows = 100_000;

    private readonly AuditLogService _audits;
    private readonly CsvAuditLogExporter _csv;
    private readonly XlsxAuditLogExporter _xlsx;
    private readonly IAuditWriter _auditWriter;

    public AuditLogExportController(
        AuditLogService audits,
        CsvAuditLogExporter csv,
        XlsxAuditLogExporter xlsx,
        IAuditWriter auditWriter)
    {
        _audits = audits;
        _csv = csv;
        _xlsx = xlsx;
        _auditWriter = auditWriter;
    }

    [HttpGet("csv")]
    public Task<IActionResult> ExportCsv(
        [FromQuery] string? search,
        [FromQuery] string? action,
        [FromQuery] string? actor,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to)
        => ExportAsync(_csv, search, action, actor, from, to);

    [HttpGet("xlsx")]
    public Task<IActionResult> ExportXlsx(
        [FromQuery] string? search,
        [FromQuery] string? action,
        [FromQuery] string? actor,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to)
        => ExportAsync(_xlsx, search, action, actor, from, to);

    private async Task<IActionResult> ExportAsync(
        IAuditLogExporter exporter,
        string? search,
        string? action,
        string? actor,
        DateTime? from,
        DateTime? to)
    {
        try
        {
            var list = await _audits.ListForExportAsync(search, action, actor, from, to, HardCapRows);
            if (list.Exceeded)
            {
                return Problem(
                    title: "Audit log export refused — result set too large",
                    detail: $"Filter matches {list.MatchCount:N0} rows; hard cap is {HardCapRows:N0}. " +
                            $"Narrow the date range or add an action/actor filter.",
                    statusCode: 400);
            }

            var filterDescription = BuildFilterDescription(search, action, actor, from, to);
            var ctx = new AuditLogExportContext(
                Title:             "Audit Log",
                FilterDescription: filterDescription,
                GeneratedAt:       DateTime.UtcNow.ToLocalTime(),
                GeneratedBy:       User?.Identity?.Name ?? "anonymous",
                Culture:           CultureInfo.InvariantCulture);

            var bytes = exporter.Export(list.Items, ctx);
            var ts = DateTime.UtcNow.ToLocalTime()
                .ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var filename = $"AuditLog_{ts}.{exporter.FileExtension}";

            // Audit-the-audit-export. Emit AFTER bytes are built (so a render
            // failure does NOT leave a misleading "exported" trail event).
            await _auditWriter.EmitAsync(
                AuditAction.AuditExport,
                User?.Identity?.Name ?? "anonymous",
                actorRole: "Admin",
                targetType: "AuditLog",
                targetId: "(batch)",
                detail: JsonSerializer.Serialize(new
                {
                    format         = exporter.Format,
                    search         = search ?? "",
                    action_filter  = action ?? "",
                    actor_filter   = actor ?? "",
                    from           = from?.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                    to             = to?.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                    rows           = list.Items.Count,
                    filename,
                    content_length = bytes.Length,
                }));

            return File(bytes, exporter.ContentType, filename);
        }
        catch (Exception ex)
        {
            // Bài học PR #27 — never leak stack trace in response body.
            return Problem(
                title: "Audit log export failed",
                detail: ex.Message,
                statusCode: 500);
        }
    }

    private static string? BuildFilterDescription(
        string? search, string? action, string? actor, DateTime? from, DateTime? to)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(search)) parts.Add($"search=\"{search}\"");
        if (!string.IsNullOrWhiteSpace(action)) parts.Add($"action={action}");
        if (!string.IsNullOrWhiteSpace(actor))  parts.Add($"actor=\"{actor}\"");
        if (from.HasValue)
            parts.Add($"from={from.Value.ToUniversalTime():yyyy-MM-dd}");
        if (to.HasValue)
            parts.Add($"to={to.Value.ToUniversalTime():yyyy-MM-dd}");
        return parts.Count == 0 ? null : string.Join("; ", parts);
    }
}
