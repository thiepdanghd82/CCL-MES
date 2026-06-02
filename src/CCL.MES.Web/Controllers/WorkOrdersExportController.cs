using System.Globalization;
using System.Text.Json;
using CCL.MES.Application.Audit;
using CCL.MES.Application.Services;
using CCL.MES.Application.WorkOrderExport;
using CCL.MES.Domain.Audit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CCL.MES.Web.Controllers;

/// <summary>
/// Phase 8 PR #32c — Export the consolidated <c>/workorders</c> list view
/// (card + table share the same data via <c>Wo.ShopOrderListAsync</c>) to
/// CSV or XLSX. Endpoints use path segments (NOT dot-extensions) per the
/// PR #33 lesson: dots in URLs can collide with the static-file
/// middleware and 404 before controller routing fires.
///
///   GET /api/workorders/export/csv
///   GET /api/workorders/export/xlsx
///
/// RBAC: matches the page's <c>FallbackPolicy = RequireAuthenticatedUser</c>
/// — any authenticated user. Use <c>[Authorize]</c> attribute (no Policy)
/// so unauthorized API callers receive 401/403 instead of being redirected
/// to the cookie-auth login page (which would 200 the SPA shell back at a
/// JSON caller).
///
/// Audit emit <c>WO_EXPORT</c> per cycle with detail JSON
/// <c>{ format, rows, filename, content_length }</c>.
/// </summary>
[ApiController]
[Route("api/workorders/export")]
[Authorize]
public class WorkOrdersExportController : ControllerBase
{
    private readonly WorkOrderService _wo;
    private readonly CsvWorkOrderListExporter _csv;
    private readonly Infrastructure.WorkOrderExport.XlsxWorkOrderListExporter _xlsx;
    private readonly IAuditWriter _audit;

    public WorkOrdersExportController(
        WorkOrderService wo,
        CsvWorkOrderListExporter csv,
        Infrastructure.WorkOrderExport.XlsxWorkOrderListExporter xlsx,
        IAuditWriter audit)
    {
        _wo = wo;
        _csv = csv;
        _xlsx = xlsx;
        _audit = audit;
    }

    [HttpGet("csv")]
    public Task<IActionResult> ExportCsv() => ExportAsync(_csv);

    [HttpGet("xlsx")]
    public Task<IActionResult> ExportXlsx() => ExportAsync(_xlsx);

    private async Task<IActionResult> ExportAsync(IWorkOrderListExporter exporter)
    {
        try
        {
            var data = await _wo.ShopOrderListAsync();
            var ctx = new WoExportContext(
                Title: "Work Orders",
                GeneratedAt: DateTime.UtcNow.ToLocalTime(),
                GeneratedBy: User?.Identity?.Name ?? "anonymous",
                Culture: CultureInfo.InvariantCulture);

            var bytes = exporter.Export(data.Active, data.Closed, ctx);
            var ts = DateTime.UtcNow.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var filename = $"WorkOrders_{ts}.{exporter.FileExtension}";

            await _audit.EmitAsync(
                AuditAction.WoExport,
                User?.Identity?.Name ?? "anonymous",
                actorRole: "",
                targetType: "WorkOrderList",
                targetId: "(batch)",
                detail: JsonSerializer.Serialize(new
                {
                    format = exporter.Format,
                    rows = data.Active.Count + data.Closed.Count,
                    active = data.Active.Count,
                    closed = data.Closed.Count,
                    filename,
                    content_length = bytes.Length,
                }));

            return File(bytes, exporter.ContentType, filename);
        }
        catch (Exception ex)
        {
            return Problem(
                title: "Work Order export failed",
                detail: ex.Message,
                statusCode: 500);
        }
    }
}
