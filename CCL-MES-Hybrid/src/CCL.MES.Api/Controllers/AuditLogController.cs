using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using CCL.MES.Api.Services;
using CCL.MES.Application.Audit;
using CCL.MES.Application.AuditLogExport;
using CCL.MES.Domain.Audit;
using CCL.MES.Shared;
using CCL.MES.Shared.Envelopes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// P10.6e — Admin Audit Log viewer + CSV/XLSX export.
///
/// Surfaces:
///   GET /api/v2/audit/log              Paged JSON list (server-side
///                                      filter on search/action/actor/
///                                      from/to + page + pageSize).
///   GET /api/v2/audit/actions          Distinct action codes for the
///                                      filter dropdown.
///   GET /api/v2/audit/export/csv       CSV stream (RFC 4180 + UTF-8 BOM).
///   GET /api/v2/audit/export/xlsx      XLSX stream (ClosedXML, mirrors
///                                      legacy 9-column shape).
///
/// Every endpoint requires AdminOnly. Anon → 401 (FallbackPolicy);
/// authenticated non-Admin → 403. Both states are covered by
/// <see cref="RouteDiscoveryCanaryTests"/> + <see cref="AuditLogControllerTests"/>
/// so a future PR that drops the policy attribute fails CI.
///
/// Export hard-cap = 100k rows
/// (<see cref="AuditLogQueryService.ExportHardCap"/>). Over-cap returns
/// 422 with code <c>audit.export_too_large</c> + the matched-row count
/// so the operator can narrow the filter.
///
/// Audit emit on every successful export — AUDIT_EXPORT, detail JSON
/// matches the legacy AuditLogExportController shape so SIEM rules
/// keyed off the Phase 9 schema work unchanged. Emit AFTER the bytes
/// are built so a render failure does NOT leave a misleading
/// "exported" trail event.
/// </summary>
[ApiController]
[Route(ApiVersion.Prefix + "/audit")]
[Authorize(Policy = "AdminOnly")]
public sealed class AuditLogController : ControllerBase
{
    private readonly AuditLogQueryService _svc;
    private readonly IEnumerable<IAuditLogExporter> _exporters;
    private readonly IAuditWriter _audit;

    public AuditLogController(
        AuditLogQueryService svc,
        IEnumerable<IAuditLogExporter> exporters,
        IAuditWriter audit)
    {
        _svc = svc;
        _exporters = exporters;
        _audit = audit;
    }

    [HttpGet("log")]
    public async Task<IActionResult> GetLog(
        [FromQuery] string? search,
        [FromQuery] string? action,
        [FromQuery] string? actor,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = AuditLogQueryService.DefaultPageSize,
        CancellationToken ct = default)
    {
        var result = await _svc.ListAsync(search, action, actor, from, to, page, pageSize, ct);
        return Ok(result);
    }

    [HttpGet("actions")]
    public async Task<IActionResult> GetActions(CancellationToken ct)
    {
        var actions = await _svc.DistinctActionsAsync(ct);
        return Ok(actions);
    }

    [HttpGet("export/csv")]
    public Task<IActionResult> ExportCsv(
        [FromQuery] string? search,
        [FromQuery] string? action,
        [FromQuery] string? actor,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct) =>
        ExportAsync("csv", search, action, actor, from, to, ct);

    [HttpGet("export/xlsx")]
    public Task<IActionResult> ExportXlsx(
        [FromQuery] string? search,
        [FromQuery] string? action,
        [FromQuery] string? actor,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct) =>
        ExportAsync("xlsx", search, action, actor, from, to, ct);

    private async Task<IActionResult> ExportAsync(
        string format,
        string? search, string? action, string? actor,
        DateTime? from, DateTime? to,
        CancellationToken ct)
    {
        var exporter = _exporters.FirstOrDefault(e =>
            string.Equals(e.Format, format, StringComparison.OrdinalIgnoreCase));
        if (exporter is null)
            return UnprocessableEntity(new ApiError
            {
                Code = "audit.unknown_format",
                MessageEn = $"Unsupported export format: {format}.",
            });

        var list = await _svc.ListForExportAsync(search, action, actor, from, to, ct);
        if (list.Exceeded)
            return UnprocessableEntity(new ApiError
            {
                Code = "audit.export_too_large",
                MessageEn = $"Filter matches {list.MatchCount:N0} rows; hard cap is {AuditLogQueryService.ExportHardCap:N0}. Narrow the date range or add an action/actor filter.",
            });

        var ctx = new AuditLogExportContext(
            Title: "Audit Log",
            FilterDescription: BuildFilterDescription(search, action, actor, from, to),
            GeneratedAt: DateTime.UtcNow.ToLocalTime(),
            GeneratedBy: ActorName(),
            Culture: CultureInfo.InvariantCulture);

        byte[] bytes;
        try
        {
            bytes = exporter.Export(list.Items, ctx);
        }
        catch (Exception ex)
        {
            return UnprocessableEntity(new ApiError
            {
                Code = "audit.export_failed",
                MessageEn = $"Export failed: {ex.GetType().Name}.",
            });
        }

        var ts = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var filename = $"AuditLog_{ts}.{exporter.FileExtension}";

        await _audit.EmitAsync(
            action: AuditAction.AuditExport,
            actor: ActorName(),
            actorRole: ActorRole(),
            targetType: "AuditLog",
            targetId: "(batch)",
            detail: JsonSerializer.Serialize(new
            {
                format = exporter.Format,
                search = search ?? "",
                action_filter = action ?? "",
                actor_filter = actor ?? "",
                from = from?.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                to = to?.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                rows = list.Items.Count,
                filename,
                content_length = bytes.Length,
            }));

        return File(bytes, exporter.ContentType, filename);
    }

    private static string? BuildFilterDescription(
        string? search, string? action, string? actor, DateTime? from, DateTime? to)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(search)) parts.Add($"search=\"{search}\"");
        if (!string.IsNullOrWhiteSpace(action)) parts.Add($"action={action}");
        if (!string.IsNullOrWhiteSpace(actor))  parts.Add($"actor=\"{actor}\"");
        if (from.HasValue) parts.Add($"from={from.Value.ToUniversalTime():yyyy-MM-dd}");
        if (to.HasValue)   parts.Add($"to={to.Value.ToUniversalTime():yyyy-MM-dd}");
        return parts.Count == 0 ? null : string.Join("; ", parts);
    }

    private string ActorName() => User.FindFirstValue(ClaimTypes.Name) ?? "anonymous";
    private string ActorRole() => User.FindFirstValue(ClaimTypes.Role) ?? "";
}
