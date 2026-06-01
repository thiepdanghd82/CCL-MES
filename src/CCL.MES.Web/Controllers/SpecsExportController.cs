using System.Globalization;
using CCL.MES.Application;
using CCL.MES.Application.Audit;
using CCL.MES.Application.Services;
using CCL.MES.Application.SpecDetail;
using CCL.MES.Application.SpecExport;
using CCL.MES.Domain.Audit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CCL.MES.Web.Controllers;

/// <summary>
/// Phase 8 PR #31c — Export NPI Spec Library list view 14 cột sang 3 format
/// (CSV / XLSX / PDF). Endpoints:
///   GET /api/specs/export/csv?search=...
///   GET /api/specs/export/xlsx?search=...
///   GET /api/specs/export/pdf?search=...
///
/// RBAC: `NpiSpecRead` (Admin / Supervisor / Engineer) — pattern Ops Control
/// v1.2 csvExport (PR #90: read = export). QC role KHÔNG có quyền theo matrix
/// Spec (verified Program.cs:NpiSpecRead policy).
///
/// Pattern: pull all rows match filter (pageSize int.MaxValue) → dispatch
/// exporter via format-keyed lookup → FileContentResult. Filename pattern
/// `NpiSpecLibrary_<yyyyMMdd-HHmmss>.<ext>`. Try-catch wrap mọi step → 500
/// với detail message; KHÔNG leak stack trace.
///
/// Audit emit `SPEC_EXPORT` per cycle với detail JSON
/// {format, search, rows, filename, content_length}.
///
/// Route note: dùng path segment `csv/xlsx/pdf` thay vì extension-style
/// `.csv/.xlsx/.pdf` để tránh xung đột với static-file middleware (dot trong
/// URL bị route nhầm sang file lookup → 404).
/// </summary>
[ApiController]
[Route("api/specs/export")]
// RBAC NpiSpecRead matrix: Admin / Supervisor / Engineer (QC KHÔNG có). Dùng
// `Roles=` attribute thay Policy để giữ API auth response 401/403 thay vì
// cookie-auth challenge redirect → SPA fallback `_Host.cshtml` (200 HTML
// thay 401). Mirror Phase 6 Policy "NpiSpecRead" trong Program.cs.
[Authorize(Roles = "Admin,Supervisor,Engineer")]
public class SpecsExportController : ControllerBase
{
    private readonly SpecService _spec;
    private readonly CsvSpecListExporter _csv;
    private readonly Infrastructure.SpecExport.XlsxSpecListExporter _xlsx;
    private readonly Infrastructure.SpecExport.PdfSpecListExporter _pdf;
    private readonly IAuditWriter _audit;

    public SpecsExportController(
        SpecService spec,
        CsvSpecListExporter csv,
        Infrastructure.SpecExport.XlsxSpecListExporter xlsx,
        Infrastructure.SpecExport.PdfSpecListExporter pdf,
        IAuditWriter audit)
    {
        _spec = spec;
        _csv = csv;
        _xlsx = xlsx;
        _pdf = pdf;
        _audit = audit;
    }

    [HttpGet("csv")]
    public Task<IActionResult> ExportCsv([FromQuery] string? search)
        => ExportAsync(_csv, search);

    [HttpGet("xlsx")]
    public Task<IActionResult> ExportXlsx([FromQuery] string? search)
        => ExportAsync(_xlsx, search);

    [HttpGet("pdf")]
    public Task<IActionResult> ExportPdf([FromQuery] string? search)
        => ExportAsync(_pdf, search);

    /// <summary>
    /// PR #31d — Single-spec detail sheet PDF export. Path segment-only route
    /// (bài học #33: KHÔNG dot-extension trong URL — static-file middleware
    /// có thể claim path-with-dot trước controller routing).
    /// URL: `/api/specs/export/sheet/{revisionId}` → returns PDF (browser
    /// auto-handle Content-Disposition filename).
    /// </summary>
    [HttpGet("sheet/{revisionId:long}")]
    public async Task<IActionResult> ExportSheetPdf(
        long revisionId,
        [FromServices] Infrastructure.SpecExport.PdfSpecSheetExporter sheetPdf)
    {
        try
        {
            var detail = await _spec.SpecDetailAsync(revisionId);
            if (detail is null) return NotFound(new { error = "Spec not found or trashed", revisionId });

            var ctx = new SpecExportContext(
                Title: $"Spec Sheet {detail.RefNo ?? detail.SpecCode} Rev {detail.RevisionCode}",
                FilterDescription: null,
                GeneratedAt: DateTime.UtcNow.ToLocalTime(),
                GeneratedBy: User?.Identity?.Name ?? "anonymous",
                Culture: CultureInfo.InvariantCulture);
            var bytes = sheetPdf.Export(detail, ctx);
            var ts = DateTime.UtcNow.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var refNoSafe = (detail.RefNo ?? detail.SpecCode)?.Replace("/", "-").Replace(" ", "_") ?? "spec";
            var filename = $"SpecSheet_{refNoSafe}_Rev{detail.RevisionCode}_{ts}.pdf";

            await _audit.EmitAsync(
                AuditAction.SpecExport,
                User?.Identity?.Name ?? "anonymous",
                actorRole: "",
                targetType: "ProductRevision",
                targetId: revisionId.ToString(CultureInfo.InvariantCulture),
                detail: System.Text.Json.JsonSerializer.Serialize(new
                {
                    format = "pdf",
                    kind = "single_spec_sheet",
                    revision_id = revisionId,
                    ref_no = detail.RefNo,
                    spec_code = detail.SpecCode,
                    filename,
                    content_length = bytes.Length,
                }));

            return File(bytes, sheetPdf.ContentType, filename);
        }
        catch (Exception ex)
        {
            return Problem(title: "Spec sheet PDF export failed", detail: ex.Message, statusCode: 500);
        }
    }

    private async Task<IActionResult> ExportAsync(ISpecListExporter exporter, string? search)
    {
        try
        {
            // Pull all rows match filter — pageSize int.MaxValue để export full
            // result, KHÔNG paginate. Read-only operation; KHÔNG impact DB.
            var paged = await _spec.SpecsAsync(search, page: 1, pageSize: int.MaxValue);
            var ctx = new SpecExportContext(
                Title: "NPI Spec Library",
                FilterDescription: string.IsNullOrWhiteSpace(search) ? null : $"search = \"{search}\"",
                GeneratedAt: DateTime.UtcNow.ToLocalTime(),
                GeneratedBy: User?.Identity?.Name ?? "anonymous",
                Culture: CultureInfo.InvariantCulture);

            var bytes = exporter.Export(paged.Items, ctx);
            var ts = DateTime.UtcNow.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var filename = $"NpiSpecLibrary_{ts}.{exporter.FileExtension}";

            // Audit emit — async fire-and-forget OK trong scope của request
            // (await để giữ deterministic ordering với response).
            await _audit.EmitAsync(
                AuditAction.SpecExport,
                User?.Identity?.Name ?? "anonymous",
                actorRole: "",
                targetType: "ProductRevisionList",
                targetId: "(batch)",
                detail: System.Text.Json.JsonSerializer.Serialize(new
                {
                    format = exporter.Format,
                    search = search ?? "",
                    rows = paged.Items.Count,
                    filename,
                    content_length = bytes.Length,
                }));

            return File(bytes, exporter.ContentType, filename);
        }
        catch (Exception ex)
        {
            // Bài học hotfix PR #27 — KHÔNG leak stack trace; trả error message
            // structured để client display banner.
            return Problem(
                title: "Export failed",
                detail: ex.Message,
                statusCode: 500);
        }
    }
}
