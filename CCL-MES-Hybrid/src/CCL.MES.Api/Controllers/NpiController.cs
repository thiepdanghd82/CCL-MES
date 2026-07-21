using System.Globalization;
using System.Text;
using CCL.MES.Application;
using CCL.MES.Application.Services;
using CCL.MES.Shared;
using CCL.MES.Shared.Envelopes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// NPI master-data read surface — Work Centers / Raw Materials / Routings /
/// Structures. <c>NpiRead</c> policy mirrors the legacy page-level gate
/// (Admin / Supervisor / Engineer / QC). All four endpoints are
/// server-side paginated since the underlying tables can grow into the
/// thousands.
/// </summary>
[ApiController]
[Authorize(Policy = "NpiRead")]
[Route(ApiVersion.Prefix + "/npi")]
public sealed class NpiController : ControllerBase
{
    private readonly NpiService _svc;
    private readonly NpiImportService _import;
    public NpiController(NpiService svc, NpiImportService import)
    {
        _svc = svc;
        _import = import;
    }

    // P10.5 follow-up — CSV import for the three big grids (SpecHub
    // "Import…" parity). Write surface → tighten to editor roles even
    // though the read grids are open to QC too.
    [HttpPost("{kind}/import")]
    [Authorize(Roles = "Admin,Supervisor,Engineer")]
    [RequestSizeLimit(256L * 1024 * 1024)]
    public async Task<IActionResult> Import(string kind, IFormFile? file, CancellationToken ct)
    {
        var k = (kind ?? "").ToLowerInvariant();
        if (k is not ("structures" or "routings" or "rawmaterials"))
            return NotFound();
        if (file is null || file.Length == 0)
            return UnprocessableEntity(new { code = "import.no_file", error = "No CSV file was uploaded." });

        await using var stream = file.OpenReadStream();
        var actor = User.Identity?.Name;
        var result = await _import.ImportAsync(k, stream, actor, ct);
        return Ok(result);
    }

    [HttpGet("workcenters")]
    public async Task<IActionResult> WorkCenters(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
        => Ok(await _svc.WorkCentersAsync(search, page, pageSize));

    [HttpGet("workcenters/{id:long}")]
    public async Task<IActionResult> WorkCenterDetail(long id)
    {
        var dto = await _svc.WorkCenterDetailAsync(id);
        return dto is null ? NotFound() : Ok(dto);
    }

    // ── Work Center write surface — Admin only (server-enforced) ──────
    // Read grids stay NpiRead (Admin/Supervisor/Engineer/QC); create/edit/
    // delete/import are Admin. WorkCenter has no RowVersion → last-write-wins.

    [HttpPost("workcenters")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> CreateWorkCenter([FromBody] UpdateWorkCenterRequest? req)
    {
        if (req is null) return UnprocessableEntity(ApiError.Of("workcenters.body_required", "A body is required."));
        try
        {
            var row = await _svc.CreateWorkCenterAsync(req, User.Identity?.Name);
            return Ok(row);
        }
        catch (WorkCenterValidationException ex) { return MapWcValidation(ex); }
    }

    [HttpPut("workcenters/{id:long}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateWorkCenter(long id, [FromBody] UpdateWorkCenterRequest? req)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Code))
            return UnprocessableEntity(ApiError.Of("workcenters.code_required", "Code is required."));
        try
        {
            var row = await _svc.UpdateWorkCenterAsync(id, req, User.Identity?.Name);
            return row is null ? NotFound() : Ok(row);
        }
        catch (WorkCenterValidationException ex) { return MapWcValidation(ex); }
        catch (InvalidOperationException) // code clash from the Phase 8 service
        {
            return Conflict(ApiError.Of("workcenters.code_in_use", $"WC Code '{req.Code}' already exists."));
        }
    }

    [HttpDelete("workcenters/{id:long}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteWorkCenter(long id)
    {
        var ok = await _svc.DeleteWorkCenterAsync(id, User.Identity?.Name);
        return ok ? NoContent() : NotFound();
    }

    [HttpPost("workcenters/import")]
    [Authorize(Roles = "Admin")]
    [RequestSizeLimit(64L * 1024 * 1024)]
    public async Task<IActionResult> ImportWorkCenters(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return UnprocessableEntity(ApiError.Of("import.no_file", "No CSV file was uploaded."));
        await using var stream = file.OpenReadStream();
        var report = await _svc.ImportWorkCentersAsync(stream, User.Identity?.Name);
        return Ok(report);
    }

    // Export respects the current search filter (mirrors the grid). CSV is
    // UTF-8 WITH BOM so Excel opens it in the right encoding. xlsx is deferred
    // until a spreadsheet lib is vetted (no heavy dependency added blindly).
    [HttpGet("workcenters/export")]
    public async Task<IActionResult> ExportWorkCenters([FromQuery] string? search, [FromQuery] string format = "csv")
    {
        if (!string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
            return UnprocessableEntity(ApiError.Of("export.format_unsupported", "Only 'csv' is supported for now (xlsx pending a vetted lib)."));

        var rows = await _svc.ExportWorkCentersAsync(search);
        var sb = new StringBuilder();
        sb.Append('﻿');   // BOM
        sb.Append("ID,Code,Description,Area,Ideal Speed (pcs/h),Shift Pattern,Status\r\n");
        foreach (var r in rows)
        {
            sb.Append(string.Join(",", new[]
            {
                r.Id.ToString(CultureInfo.InvariantCulture),
                Csv(r.Code), Csv(r.Description), Csv(r.Area),
                r.IdealSpeedPcsH?.ToString("0.####", CultureInfo.InvariantCulture) ?? "",
                Csv(r.ShiftPattern),
                r.Active switch { true => "Active", false => "Inactive", _ => "" },
            }));
            sb.Append("\r\n");
        }
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/csv; charset=utf-8", "workcenters.csv");
    }

    private IActionResult MapWcValidation(WorkCenterValidationException ex) =>
        ex.Code == "workcenters.code_in_use"
            ? Conflict(ApiError.Of(ex.Code, ex.Message))
            : UnprocessableEntity(ApiError.Of(ex.Code, ex.Message));

    private static string Csv(string? v)
    {
        v ??= "";
        return v.Contains(',') || v.Contains('"') || v.Contains('\n') || v.Contains('\r')
            ? "\"" + v.Replace("\"", "\"\"") + "\""
            : v;
    }

    [HttpGet("rawmaterials")]
    public async Task<IActionResult> RawMaterials(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
        => Ok(await _svc.RawMaterialsAsync(search, page, pageSize));

    [HttpGet("routings")]
    public async Task<IActionResult> Routings(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
        => Ok(await _svc.RoutingAsync(search, page, pageSize));

    [HttpGet("structures")]
    public async Task<IActionResult> Structures(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
        => Ok(await _svc.StructuresAsync(search, page, pageSize));
}
