using CCL.MES.Application;
using CCL.MES.Domain;
using CCL.MES.Shared;
using CCL.MES.Shared.CheckLibrary;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.ReasonCodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// Phương án C — Bước 5 + 6. Read API cho THƯ VIỆN hạng mục kiểm
/// (<see cref="CCL.MES.Domain.Entities.CheckItemLibrary"/>):
///   GET /api/v2/check-item-library            list (lọc line/stage/q)
///   GET /api/v2/check-item-library/lines      tổng quan theo process line
///   GET /api/v2/check-item-library/reason-codes?lines=LABEL,PRESS_CNC
///       (Bước 5) mã lỗi NG SCOPE theo process line — chỉ trả những
///       ReasonCode(Scrap) là DefectCode của thư viện trong các line đó.
///
/// Đây là MASTER DATA read-only (sửa qua import idempotent — quyết định #4).
/// Auth: any authenticated (reference data; admin-edit UI để sau).
/// </summary>
[ApiController]
[Authorize]
[Route(ApiVersion.Prefix + "/check-item-library")]
public sealed class CheckItemLibraryController : ControllerBase
{
    private readonly IMesDbContext _db;
    public CheckItemLibraryController(IMesDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CheckLibraryItemDto>>> List(
        [FromQuery] string? line, [FromQuery] string? stage, [FromQuery] string? q,
        CancellationToken ct = default)
    {
        var query = _db.CheckItemLibraries.AsNoTracking().Where(c => c.Active);
        if (!string.IsNullOrWhiteSpace(line))
            query = query.Where(c => c.ProcessLine == line);
        if (!string.IsNullOrWhiteSpace(stage))
            query = query.Where(c => c.QcStage == stage);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(c => c.ItemVi.Contains(q) || c.ItemId.Contains(q) || c.Code.Contains(q));

        var rows = await query
            .OrderBy(c => c.ProcessLine).ThenBy(c => c.Sort).ThenBy(c => c.ItemId)
            .Select(c => new CheckLibraryItemDto
            {
                ItemId = c.ItemId, ProcessLine = c.ProcessLine, ProductCode = c.ProductCode,
                QcStage = c.QcStage, GroupLabel = c.GroupLabel, Code = c.Code,
                ItemVi = c.ItemVi, AcceptanceVi = c.AcceptanceVi, Method = c.Method,
                Severity = c.Severity, DefectCode = c.DefectCode, Active = c.Active, Sort = c.Sort,
            })
            .ToListAsync(ct);
        return Ok(rows);
    }

    [HttpGet("lines")]
    public async Task<ActionResult<IReadOnlyList<CheckLibraryLineDto>>> Lines(CancellationToken ct = default)
    {
        var rows = await _db.CheckItemLibraries.AsNoTracking().Where(c => c.Active)
            .GroupBy(c => new { c.ProcessLine, c.QcStage })
            .Select(g => new CheckLibraryLineDto
            {
                ProcessLine = g.Key.ProcessLine, QcStage = g.Key.QcStage, Count = g.Count(),
            })
            .OrderBy(x => x.ProcessLine).ThenBy(x => x.QcStage)
            .ToListAsync(ct);
        return Ok(rows);
    }

    /// <summary>
    /// Phương án C — Bước 5 (GATE B9). Mã lỗi NG hợp lệ SCOPE theo process line:
    /// các <see cref="ReasonCode"/> (Kind=Scrap) tương ứng DefectCode của thư
    /// viện trong các line yêu cầu. Dropdown NG dùng cái này → chỉ hiện mã hợp lệ.
    /// Không truyền lines → trả toàn bộ Scrap codes (fallback).
    /// </summary>
    [HttpGet("reason-codes")]
    public async Task<ActionResult<IReadOnlyList<ReasonCodeOption>>> ScopedReasonCodes(
        [FromQuery] string? lines, CancellationToken ct = default)
    {
        var lineList = (lines ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        // Tập DefectCode hợp lệ theo line (từ thư viện).
        List<string>? scopedCodes = null;
        if (lineList.Count > 0)
        {
            scopedCodes = await _db.CheckItemLibraries.AsNoTracking()
                .Where(c => c.Active && lineList.Contains(c.ProcessLine)
                            && c.DefectCode != null && c.DefectCode != "")
                .Select(c => c.DefectCode!)
                .Distinct()
                .ToListAsync(ct);
        }

        var query = _db.ReasonCodes.AsNoTracking().Where(r => r.Active && r.Kind == ReasonCodeKind.Scrap);
        if (scopedCodes is { Count: > 0 })
            query = query.Where(r => scopedCodes.Contains(r.Code));

        var rows = await query
            .OrderBy(r => r.Sort).ThenBy(r => r.Code)
            .Select(r => new ReasonCodeOption
            {
                Code = r.Code, LabelEn = r.LabelEn, LabelVi = r.LabelVi,
                Kind = r.Kind.ToString(), Sort = r.Sort,
            })
            .ToListAsync(ct);
        return Ok(rows);
    }
}
