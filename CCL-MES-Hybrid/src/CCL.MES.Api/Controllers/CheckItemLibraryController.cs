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
[Authorize(Policy = "NpiRead")] // F5 — QC/NPI read (Admin/Supervisor/Engineer/QC), không phải mọi user
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
        {
            // stage ∈ {IPQC,FQC,OQC} → lọc theo cờ ma trận tương ứng.
            var s = stage.Trim().ToUpperInvariant();
            query = s switch
            {
                "IPQC" => query.Where(c => c.Ipqc),
                "FQC" => query.Where(c => c.Fqc),
                "OQC" => query.Where(c => c.Oqc),
                _ => query,
            };
        }
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(c => c.ItemVi.Contains(q) || c.ItemId.Contains(q) || c.Code.Contains(q));

        var rows = await query
            .OrderBy(c => c.ProcessLine).ThenBy(c => c.Sort).ThenBy(c => c.ItemId)
            .Select(c => new CheckLibraryItemDto
            {
                ItemId = c.ItemId, ProcessLine = c.ProcessLine, ProductCode = c.ProductCode,
                GroupLabel = c.GroupLabel, Code = c.Code,
                BlankLabel = c.BlankLabel, Flexo = c.Flexo, LetterPress = c.LetterPress,
                HpIndigo = c.HpIndigo, SilkScreen = c.SilkScreen, Flatbed = c.Flatbed,
                Rdc = c.Rdc, Laminate = c.Laminate, Zebra = c.Zebra, SheetCut = c.SheetCut,
                PunchHole = c.PunchHole, DrillHole = c.DrillHole, Slit = c.Slit,
                Ipqc = c.Ipqc, Fqc = c.Fqc, Oqc = c.Oqc,
                ItemVi = c.ItemVi, ItemEn = c.ItemEn, AcceptanceVi = c.AcceptanceVi,
                AcceptanceEn = c.AcceptanceEn, Method = c.Method, Severity = c.Severity,
                Aql = c.Aql, Sampling = c.Sampling, CheckType = c.CheckType,
                DefectCode = c.DefectCode, IsoRef = c.IsoRef, AppliesWhen = c.AppliesWhen,
                Note = c.Note, Active = c.Active, Sort = c.Sort,
            })
            .ToListAsync(ct);
        return Ok(rows);
    }

    /// <summary>Phương án C — Bước 6. Xem bảng map process→QC line hiện hành
    /// (data-driven, quyết định #5). Sửa map qua seed (UI sửa = backlog).</summary>
    [HttpGet("~/" + ApiVersion.Prefix + "/qc/library/process-map")]
    public async Task<ActionResult<IReadOnlyList<ProcessLineMapDto>>> ProcessMap(CancellationToken ct = default)
    {
        var rows = await _db.ProcessLineMaps.AsNoTracking()
            .OrderBy(m => m.Sort).ThenBy(m => m.MatchType).ThenBy(m => m.MatchValue)
            .Select(m => new ProcessLineMapDto
            {
                MatchType = m.MatchType, MatchValue = m.MatchValue, QcLine = m.QcLine,
                Sort = m.Sort, Active = m.Active, Note = m.Note,
            })
            .ToListAsync(ct);
        return Ok(rows);
    }

    [HttpGet("lines")]
    public async Task<ActionResult<IReadOnlyList<CheckLibraryLineDto>>> Lines(CancellationToken ct = default)
    {
        var rows = await _db.CheckItemLibraries.AsNoTracking().Where(c => c.Active)
            .GroupBy(c => c.ProcessLine)
            .Select(g => new CheckLibraryLineDto
            {
                ProcessLine = g.Key, Count = g.Count(),
                IpqcCount = g.Count(c => c.Ipqc),
                FqcCount = g.Count(c => c.Fqc),
                OqcCount = g.Count(c => c.Oqc),
            })
            .OrderBy(x => x.ProcessLine)
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
