using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using CCL.MES.Application;
using CCL.MES.Application.Audit;
using CCL.MES.Application.Services;
using CCL.MES.Application.SpecExport;
using CCL.MES.Domain.Audit;
using CCL.MES.Infrastructure.SpecExport;
using CCL.MES.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// P10.5g — Spec list + sheet PDF export endpoints for the MAUI Hybrid
/// client. Mirrors the legacy <c>CCL.MES.Web.Controllers.SpecsExportController</c>
/// 1:1 (reuses the same 4 exporters from CCL.MES.Infrastructure /
/// CCL.MES.Application). The only deltas:
/// <list type="bullet">
///   <item>Route is <c>api/v2/specs/export/*</c> so it joins the existing
///         <see cref="ApiVersion.Prefix"/> family the Hybrid client knows.</item>
///   <item>Auth uses the JWT policy <c>NpiSpecRead</c> (matches
///         <see cref="SpecsController"/>) instead of cookie-auth Roles.</item>
///   <item>List endpoints accept the same <c>view</c> + <c>planner</c> chip
///         filter the Spec list page uses (Sprint P10.5c-3 parity), so
///         the export stays in sync with what the operator currently sees.</item>
///   <item>Audit emits <c>SPEC_EXPORT</c> + paired <c>SPEC_EXPORT_DEVICE</c>
///         when the <c>X-Device-Id</c> header is present (W4 pattern).</item>
/// </list>
/// Read = export per the same RBAC convention Henry approved in PR #31c
/// (Ops Control PR #90 lineage): no separate "export" right.
///
/// Path-segment routes (csv / xlsx / pdf) — Lesson #33: don't put the
/// dot-extension in the URL, the static-file middleware claims paths with
/// dots before controller routing.
/// </summary>
[ApiController]
[Route(ApiVersion.Prefix + "/specs/export")]
[Authorize(Policy = "NpiSpecRead")]
public sealed class SpecsExportController : ControllerBase
{
    private readonly SpecService _spec;
    private readonly CsvSpecListExporter _csv;
    private readonly XlsxSpecListExporter _xlsx;
    private readonly PdfSpecListExporter _pdf;
    private readonly PdfSpecSheetExporter _sheetPdf;
    private readonly IAuditWriter _audit;

    public SpecsExportController(
        SpecService spec,
        CsvSpecListExporter csv,
        XlsxSpecListExporter xlsx,
        PdfSpecListExporter pdf,
        PdfSpecSheetExporter sheetPdf,
        IAuditWriter audit)
    {
        _spec = spec;
        _csv = csv;
        _xlsx = xlsx;
        _pdf = pdf;
        _sheetPdf = sheetPdf;
        _audit = audit;
    }

    // ── List exports ────────────────────────────────────────────────

    [HttpGet("csv")]
    public Task<IActionResult> ListCsv(
        [FromQuery] string? search,
        [FromQuery] SpecListView view = SpecListView.Active,
        [FromQuery] string? planner = null)
        => ExportListAsync(_csv, search, view, planner);

    [HttpGet("xlsx")]
    public Task<IActionResult> ListXlsx(
        [FromQuery] string? search,
        [FromQuery] SpecListView view = SpecListView.Active,
        [FromQuery] string? planner = null)
        => ExportListAsync(_xlsx, search, view, planner);

    [HttpGet("pdf")]
    public Task<IActionResult> ListPdf(
        [FromQuery] string? search,
        [FromQuery] SpecListView view = SpecListView.Active,
        [FromQuery] string? planner = null)
        => ExportListAsync(_pdf, search, view, planner);

    // ── Per-spec sheet PDF ──────────────────────────────────────────

    /// <summary>
    /// Single-spec detail sheet PDF (mirror web PR #31d). Path-segment
    /// route — <c>{revisionId:long}/sheet/pdf</c> keeps the verb at the
    /// tail so future kinds (xlsx-sheet, csv-sheet) can slot in.
    /// </summary>
    [HttpGet("{revisionId:long}/sheet/pdf")]
    public async Task<IActionResult> SheetPdf(long revisionId)
    {
        try
        {
            var detail = await _spec.SpecDetailAsync(revisionId);
            if (detail is null)
                return NotFound(new { code = "not_found", revisionId });

            var ctx = new SpecExportContext(
                Title: $"Spec Sheet {detail.RefNo ?? detail.SpecCode} Rev {detail.RevisionCode}",
                FilterDescription: null,
                GeneratedAt: DateTime.UtcNow.ToLocalTime(),
                GeneratedBy: ActorName(),
                Culture: CultureInfo.InvariantCulture);

            var bytes = _sheetPdf.Export(detail, ctx);
            var filename = SpecExportFilename.SheetPdf(
                detail.RefNo, detail.SpecCode, detail.RevisionCode, ctx.GeneratedAt);

            await _audit.EmitAsync(
                action: AuditAction.SpecExport,
                actor: ActorName(),
                actorRole: User.FindFirstValue(ClaimTypes.Role) ?? "",
                targetType: "ProductRevision",
                targetId: revisionId.ToString(CultureInfo.InvariantCulture),
                detail: JsonSerializer.Serialize(new
                {
                    format = "pdf",
                    kind = "sheet",
                    revision_id = revisionId,
                    ref_no = detail.RefNo,
                    spec_code = detail.SpecCode,
                    revision_code = detail.RevisionCode,
                    filename,
                    content_length = bytes.Length,
                }));

            await EmitDeviceAuditIfHeaderPresent("SPEC_EXPORT_DEVICE", new
            {
                kind = "sheet",
                format = "pdf",
                revision_id = revisionId,
                filename,
            });

            return File(bytes, _sheetPdf.ContentType, filename);
        }
        catch (Exception ex)
        {
            return Problem(
                title: "Spec sheet PDF export failed",
                detail: ex.Message,
                statusCode: 500);
        }
    }

    // ── Implementation ──────────────────────────────────────────────

    private async Task<IActionResult> ExportListAsync(
        ISpecListExporter exporter,
        string? search,
        SpecListView view,
        string? planner)
    {
        try
        {
            // pageSize int.MaxValue → full filtered result. Read-only call,
            // no DB mutation. Mirrors the web pattern (PR #31c) exactly.
            var paged = await _spec.SpecsAsync(
                search, page: 1, pageSize: int.MaxValue, view, planner);

            var ctx = new SpecExportContext(
                Title: "NPI Spec Library",
                FilterDescription: SpecExportFilename.DescribeFilter(search, view, planner),
                GeneratedAt: DateTime.UtcNow.ToLocalTime(),
                GeneratedBy: ActorName(),
                Culture: CultureInfo.InvariantCulture);

            var bytes = exporter.Export(paged.Items, ctx);
            var filename = SpecExportFilename.List(exporter.FileExtension, ctx.GeneratedAt);

            await _audit.EmitAsync(
                action: AuditAction.SpecExport,
                actor: ActorName(),
                actorRole: User.FindFirstValue(ClaimTypes.Role) ?? "",
                targetType: "ProductRevisionList",
                targetId: "(batch)",
                detail: JsonSerializer.Serialize(new
                {
                    format = exporter.Format,
                    kind = "list",
                    search = search ?? "",
                    view = view.ToString(),
                    planner = planner ?? "",
                    rows = paged.Items.Count,
                    filename,
                    content_length = bytes.Length,
                }));

            await EmitDeviceAuditIfHeaderPresent("SPEC_EXPORT_DEVICE", new
            {
                kind = "list",
                format = exporter.Format,
                view = view.ToString(),
                planner = planner ?? "",
                rows = paged.Items.Count,
                filename,
            });

            return File(bytes, exporter.ContentType, filename);
        }
        catch (Exception ex)
        {
            return Problem(
                title: "Spec list export failed",
                detail: ex.Message,
                statusCode: 500);
        }
    }

    private string ActorName() =>
        User.FindFirstValue(ClaimTypes.Name) ?? "anonymous";

    private async Task EmitDeviceAuditIfHeaderPresent(string action, object detail)
    {
        var deviceId = Request.Headers["X-Device-Id"].ToString();
        if (string.IsNullOrWhiteSpace(deviceId)) return;

        var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["device_id"] = deviceId,
            ["detail"] = detail,
        });
        await _audit.EmitAsync(
            action: action,
            actor: ActorName(),
            actorRole: User.FindFirstValue(ClaimTypes.Role) ?? "",
            targetType: "Device",
            targetId: deviceId,
            detail: payload);
    }
}

/// <summary>
/// Pure helper — filename + filter-description string composition. Hoisted
/// so the test project can pin the format without spinning up the
/// controller (Q11 _submitting pattern: helpers test cheap, controllers
/// test once via the integration harness).
/// </summary>
public static class SpecExportFilename
{
    /// <summary>
    /// List-export filename — <c>NpiSpecLibrary_&lt;yyyyMMdd-HHmmss&gt;.&lt;ext&gt;</c>.
    /// Mirrors the legacy web pattern so Audit/Forensic queries written for
    /// the web stay valid on Hybrid-issued files too.
    /// </summary>
    public static string List(string fileExtension, DateTime generatedAt)
    {
        var ts = generatedAt.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        return $"NpiSpecLibrary_{ts}.{fileExtension}";
    }

    /// <summary>
    /// Sheet-PDF filename — <c>SpecSheet_&lt;RefNoOrSpecCode&gt;_Rev&lt;Code&gt;_&lt;yyyyMMdd-HHmmss&gt;.pdf</c>.
    /// Slashes and spaces normalised; final filename always passes the
    /// downstream Catalyst <c>UIDocumentPicker</c> validation.
    /// </summary>
    public static string SheetPdf(string? refNo, string specCode, string revisionCode, DateTime generatedAt)
    {
        var ts = generatedAt.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var head = SanitizeFilenameToken(refNo ?? specCode ?? "spec");
        var rev = SanitizeFilenameToken(revisionCode);
        return $"SpecSheet_{head}_Rev{rev}_{ts}.pdf";
    }

    /// <summary>
    /// "Filter: …" subtitle composed for the PDF/Excel header band. Returns
    /// null when no filter is active so the renderer skips the row.
    /// </summary>
    public static string? DescribeFilter(string? search, SpecListView view, string? planner)
    {
        var parts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(search))
            parts.Add($"search=\"{search.Trim()}\"");
        if (view != SpecListView.Active)
            parts.Add($"view={view}");
        if (!string.IsNullOrWhiteSpace(planner))
            parts.Add($"planner={planner.Trim().ToUpperInvariant()}");
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    /// <summary>
    /// Strip path-traversal and filesystem-hostile characters from a single
    /// filename token. We keep ASCII alphanumerics, dash, underscore, and
    /// dot; everything else collapses to underscore. Multiple consecutive
    /// underscores collapse to one.
    /// </summary>
    public static string SanitizeFilenameToken(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "spec";
        var buf = new System.Text.StringBuilder(raw.Length);
        char prev = '\0';
        foreach (var ch in raw.Trim())
        {
            char clean = char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' || ch == '.'
                ? ch
                : '_';
            if (clean == '_' && prev == '_') continue;
            buf.Append(clean);
            prev = clean;
        }
        var result = buf.ToString().Trim('_', '.');
        return string.IsNullOrEmpty(result) ? "spec" : result;
    }
}
