using System.Security.Claims;
using System.Text.Json;
using CCL.MES.Application;
using CCL.MES.Application.Audit;
using CCL.MES.Application.Services;
using CCL.MES.Domain;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure.SpecExport;
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
    private readonly IAuditWriter _audit;
    public CheckItemLibraryController(IMesDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    private string Actor() => User.FindFirstValue(ClaimTypes.Name) ?? "anonymous";
    private string Role() => User.FindFirstValue(ClaimTypes.Role) ?? "";

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CheckLibraryItemDto>>> List(
        [FromQuery] string? line, [FromQuery] string? stage, [FromQuery] string? q,
        [FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        var query = _db.CheckItemLibraries.AsNoTracking().AsQueryable();
        if (!includeInactive)
            query = query.Where(c => c.Active); // admin grid passes includeInactive=true
        if (!string.IsNullOrWhiteSpace(line))
            query = query.Where(c => c.ProcessLine == line);
        if (!string.IsNullOrWhiteSpace(stage))
            query = query.Where(c => c.QcStage == stage);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(c => c.ItemVi.Contains(q) || c.ItemId.Contains(q) || c.Code.Contains(q));

        var rows = await query
            .OrderBy(c => c.ProcessLine).ThenBy(c => c.Sort).ThenBy(c => c.ItemId)
            .Select(c => ToDto(c))
            .ToListAsync(ct);
        return Ok(rows);
    }

    private static CheckLibraryItemDto ToDto(CheckItemLibrary c) => new()
    {
        Id = c.Id, ItemId = c.ItemId, ProcessLine = c.ProcessLine, ProductCode = c.ProductCode,
        QcStage = c.QcStage, GroupLabel = c.GroupLabel, Code = c.Code,
        ItemVi = c.ItemVi, ItemEn = c.ItemEn, AcceptanceVi = c.AcceptanceVi, AcceptanceEn = c.AcceptanceEn,
        Method = c.Method, Severity = c.Severity, DefectCode = c.DefectCode,
        Active = c.Active, Sort = c.Sort, RowVersion = c.RowVersion,
    };

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

    // ══════════ Admin: import + inline add/edit (write = Admin/Supervisor/Engineer) ══════════
    // WRITE = class NpiRead AND method Roles (QC read-only, operator blocked).
    // Freeze-safe: every mutation touches only CheckItemLibraries master data;
    // a WO's frozen ItemsProfileSnapshotJson is never read or written here.
    private const string WriteRoles = "Admin,Supervisor,Engineer";

    /// <summary>Bước 1 — import .xlsx (idempotent upsert by ItemId). 422 on non-xlsx.
    /// Áp cho WO MỚI; WO đang chạy giữ snapshot.</summary>
    [HttpPost("import")]
    [Authorize(Roles = WriteRoles)]
    [RequestSizeLimit(5_000_000)] // 5 MB cap
    public async Task<ActionResult<CheckLibraryImportResultDto>> Import(IFormFile? file, CancellationToken ct = default)
    {
        if (file is null || file.Length == 0)
            return UnprocessableEntity(ApiError.Of("import.no_file", "Chưa chọn file."));
        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return UnprocessableEntity(ApiError.Of("import.invalid_type", "Chỉ nhận file .xlsx."));

        QcCheckLibraryCsv.ParseResult parsed;
        try
        {
            using var s = file.OpenReadStream();
            parsed = QcCheckLibraryXlsx.ParseDetailed(s);
        }
        catch (Exception ex)
        {
            return UnprocessableEntity(ApiError.Of("import.parse_failed", $"Không đọc được file: {ex.Message}"));
        }

        var res = await CheckItemLibraryImporter.ImportAsync(_db, parsed, Actor(), ct);
        await _audit.EmitAsync(AuditAction.CheckItemLibraryImport, Actor(), Role(), "CheckItemLibrary", null,
            JsonSerializer.Serialize(new
            {
                parsed = res.Parsed, inserted = res.Inserted, updated = res.Updated,
                skipped = res.Skipped, errors_count = res.Errors.Count, filename = file.FileName,
            }));
        return Ok(new CheckLibraryImportResultDto
        {
            Parsed = res.Parsed, Inserted = res.Inserted, Updated = res.Updated,
            Skipped = res.Skipped, Errors = res.Errors,
        });
    }

    /// <summary>Bước 1 — tải template .xlsx (header + 1 dòng mẫu) để điền.</summary>
    [HttpGet("template")]
    [Authorize(Roles = WriteRoles)]
    public IActionResult Template()
        => File(QcCheckLibraryXlsx.BuildTemplate(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "CheckItemLibrary_Template.xlsx");

    /// <summary>Bước 2 — thêm 1 dòng. 422 nếu trùng natural key / invalid.</summary>
    [HttpPost]
    [Authorize(Roles = WriteRoles)]
    public async Task<ActionResult<CheckLibraryItemDto>> Add(
        [FromBody] UpsertCheckLibraryItemRequest req, CancellationToken ct = default)
    {
        var err = ValidateUpsert(req);
        if (err is not null) return UnprocessableEntity(ApiError.Of("check_library.invalid", err));
        if (await _db.CheckItemLibraries.AnyAsync(c => c.ItemId == req.ItemId, ct))
            return UnprocessableEntity(ApiError.Of("check_library.duplicate_item_id",
                $"ItemId '{req.ItemId}' đã tồn tại."));

        var e = new CheckItemLibrary
        {
            ItemId = req.ItemId,
            ProductCode = req.ProductCode,
            QcStage = string.IsNullOrWhiteSpace(req.QcStage) ? "IPQC" : req.QcStage,
            Active = req.Active,
            CreatedBy = Actor(),
        };
        CheckItemLibraryImporter.ApplyRow(e, ToRow(req), req.Sort);
        _db.CheckItemLibraries.Add(e);
        await _db.SaveChangesAsync(ct);
        await _audit.EmitAsync(AuditAction.CheckItemLibraryAdd, Actor(), Role(), "CheckItemLibrary",
            e.Id.ToString(), JsonSerializer.Serialize(new { item_id = e.ItemId, id = e.Id }));
        return Ok(ToDto(e));
    }

    /// <summary>Bước 2 — sửa 1 dòng (If-Match RowVersion → 409 nếu stale).</summary>
    [HttpPut("{id:long}")]
    [Authorize(Roles = WriteRoles)]
    public async Task<ActionResult<CheckLibraryItemDto>> Update(
        long id, [FromBody] UpsertCheckLibraryItemRequest req, CancellationToken ct = default)
    {
        var err = ValidateUpsert(req);
        if (err is not null) return UnprocessableEntity(ApiError.Of("check_library.invalid", err));

        var e = await _db.CheckItemLibraries.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (e is null) return NotFound(ApiError.Of("check_library.not_found", $"No item id {id}."));

        if (!string.Equals(e.ItemId, req.ItemId, StringComparison.Ordinal) &&
            await _db.CheckItemLibraries.AnyAsync(c => c.ItemId == req.ItemId && c.Id != id, ct))
            return UnprocessableEntity(ApiError.Of("check_library.duplicate_item_id",
                $"ItemId '{req.ItemId}' đã tồn tại."));

        // Apply edits.
        e.ItemId = req.ItemId;
        e.ProductCode = req.ProductCode;
        e.QcStage = string.IsNullOrWhiteSpace(req.QcStage) ? "IPQC" : req.QcStage;
        e.Active = req.Active;
        CheckItemLibraryImporter.ApplyRow(e, ToRow(req), req.Sort);
        e.UpdatedAt = DateTime.UtcNow;
        e.UpdatedBy = Actor();

        // Optimistic concurrency: WHERE RowVersion = client's If-Match token.
        var ifMatch = (req.RowVersion ?? Request.Headers.IfMatch.ToString() ?? "").Trim('"', ' ');
        if (_db is DbContext ctx)
            ctx.Entry(e).Property(x => x.RowVersion).OriginalValue = ifMatch;
        e.RowVersion = Guid.NewGuid().ToString("N");

        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            if (_db is DbContext c2) c2.ChangeTracker.Clear();
            return Conflict(ApiError.Of("check_library.stale",
                "Dòng đã bị người khác sửa. Tải lại rồi thử lại."));
        }
        await _audit.EmitAsync(AuditAction.CheckItemLibraryEdit, Actor(), Role(), "CheckItemLibrary",
            e.Id.ToString(), JsonSerializer.Serialize(new { item_id = e.ItemId, id = e.Id }));
        return Ok(ToDto(e));
    }

    /// <summary>Bước 2 — soft-delete toggle (Active). Ẩn khỏi list nhưng giữ audit + snapshot WO.</summary>
    [HttpPatch("{id:long}/active")]
    [Authorize(Roles = WriteRoles)]
    public async Task<ActionResult<CheckLibraryItemDto>> SetActive(
        long id, [FromQuery] bool active, CancellationToken ct = default)
    {
        var e = await _db.CheckItemLibraries.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (e is null) return NotFound(ApiError.Of("check_library.not_found", $"No item id {id}."));
        if (e.Active != active)
        {
            e.Active = active;
            e.UpdatedAt = DateTime.UtcNow;
            e.UpdatedBy = Actor();
            e.RowVersion = Guid.NewGuid().ToString("N");
            await _db.SaveChangesAsync(ct);
        }
        await _audit.EmitAsync(
            active ? AuditAction.CheckItemLibraryEdit : AuditAction.CheckItemLibraryDeactivate,
            Actor(), Role(), "CheckItemLibrary", e.Id.ToString(),
            JsonSerializer.Serialize(new { item_id = e.ItemId, id = e.Id, active }));
        return Ok(ToDto(e));
    }

    /// <summary>Bước 2 — hard-delete (Admin-only). Soft-delete (Active=false) là mặc định khuyến nghị.</summary>
    [HttpDelete("{id:long}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct = default)
    {
        var e = await _db.CheckItemLibraries.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (e is null) return NotFound(ApiError.Of("check_library.not_found", $"No item id {id}."));
        _db.CheckItemLibraries.Remove(e);
        await _db.SaveChangesAsync(ct);
        await _audit.EmitAsync(AuditAction.CheckItemLibraryDelete, Actor(), Role(), "CheckItemLibrary",
            e.Id.ToString(), JsonSerializer.Serialize(new { item_id = e.ItemId, id = e.Id }));
        return NoContent();
    }

    // ── Validation + mapping helpers (shared add/edit) ───────────────
    private static string? ValidateUpsert(UpsertCheckLibraryItemRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.ItemId)) return "ItemId bắt buộc.";
        if (!CheckItemLibraryImporter.AllowedLines.Contains(r.ProcessLine, StringComparer.OrdinalIgnoreCase))
            return $"ProcessLine phải ∈ {string.Join("/", CheckItemLibraryImporter.AllowedLines)}.";
        foreach (var (v, n) in new[]
        {
            (r.GroupLabel, "GroupLabel"), (r.Code, "Code"), (r.ItemVi, "ItemVi"),
            (r.ItemEn, "ItemEn"), (r.AcceptanceVi, "AcceptanceVi"), (r.AcceptanceEn, "AcceptanceEn"),
        })
            if (string.IsNullOrWhiteSpace(v)) return $"{n} bắt buộc.";
        return null;
    }

    private static QcCheckLibraryRow ToRow(UpsertCheckLibraryItemRequest r) => new()
    {
        ItemId = r.ItemId, ProcessLine = r.ProcessLine, GroupLabel = r.GroupLabel, Code = r.Code,
        ItemVi = r.ItemVi, ItemEn = r.ItemEn, AcceptanceVi = r.AcceptanceVi, AcceptanceEn = r.AcceptanceEn,
        Method = r.Method, Severity = r.Severity, Aql = r.Aql, Sampling = r.Sampling,
        CheckType = r.CheckType, DefectCode = r.DefectCode, ParetoPct = r.ParetoPct,
        ShortForm = r.ShortForm, IsoRef = r.IsoRef, AppliesWhen = r.AppliesWhen, Note = r.Note,
    };
}
