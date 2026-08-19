using System.Text;
using CCL.MES.Application;
using CCL.MES.Application.Audit;
using CCL.MES.Application.Services;
using CCL.MES.Domain;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Infrastructure.QcLibrary;
using CCL.MES.Shared;
using CCL.MES.Shared.CheckLibrary;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.ReasonCodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

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
    private readonly MesDbContext _write;   // concrete — DbSeeder import + upsert/delete
    private readonly IAuditWriter _audit;
    public CheckItemLibraryController(IMesDbContext db, MesDbContext write, IAuditWriter audit)
    {
        _db = db; _write = write; _audit = audit;
    }

    private string Actor => User.FindFirstValue(ClaimTypes.Name) ?? "anonymous";
    private string Role => User.FindFirstValue(ClaimTypes.Role) ?? "";

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

    // ── Smart platform — write surface (Admin/Supervisor/QC) ─────────────

    /// <summary>Upsert 1 hạng mục theo ItemId (Add new / sửa tick-box + field).
    /// Idempotent theo ItemId. Emit <c>QC_LIBRARY_ITEM_SET</c>.</summary>
    [HttpPut("{itemId}")]
    [Authorize(Roles = "Admin,Supervisor,QC")]
    public async Task<ActionResult<CheckLibraryItemDto>> Upsert(
        string itemId, [FromBody] CheckLibraryUpsertDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(itemId) || string.IsNullOrWhiteSpace(dto.ProcessLine))
            return UnprocessableEntity(new { error = "qclib.invalid_body" });

        var e = await _write.CheckItemLibraries.FirstOrDefaultAsync(x => x.ItemId == itemId, ct);
        var isNew = e is null;
        if (e is null)
        {
            e = new CheckItemLibrary { ItemId = itemId, CreatedBy = Actor };
            _write.CheckItemLibraries.Add(e);
        }
        else { e.UpdatedAt = DateTime.UtcNow; e.UpdatedBy = Actor; }
        Apply(e, dto);

        // Defect code mới → mở rộng ReasonCode(Scrap) như importer.
        if (!string.IsNullOrWhiteSpace(dto.DefectCode)
            && !await _write.ReasonCodes.AnyAsync(r => r.Code == dto.DefectCode && r.Kind == ReasonCodeKind.Scrap, ct))
        {
            _write.ReasonCodes.Add(new ReasonCode
            {
                Code = dto.DefectCode!, LabelEn = dto.DefectCode!, LabelVi = dto.DefectCode!,
                Kind = ReasonCodeKind.Scrap, Sort = 900, CreatedBy = Actor,
            });
        }

        await _write.SaveChangesAsync(ct);
        await _audit.EmitAsync(AuditAction.QcLibraryItemSet, Actor, Role, "CheckItemLibrary", itemId,
            $"{{\"new\":{(isNew ? "true" : "false")},\"line\":\"{e.ProcessLine}\"}}");
        return Ok(ToDto(e));
    }

    /// <summary>Xoá 1 hạng mục. Emit <c>QC_LIBRARY_ITEM_DELETE</c>.</summary>
    [HttpDelete("{itemId}")]
    [Authorize(Roles = "Admin,Supervisor,QC")]
    public async Task<IActionResult> Delete(string itemId, CancellationToken ct = default)
    {
        var e = await _write.CheckItemLibraries.FirstOrDefaultAsync(x => x.ItemId == itemId, ct);
        if (e is null) return NotFound();
        _write.CheckItemLibraries.Remove(e);
        await _write.SaveChangesAsync(ct);
        await _audit.EmitAsync(AuditAction.QcLibraryItemDelete, Actor, Role, "CheckItemLibrary", itemId);
        return NoContent();
    }

    /// <summary>Import file thư viện (.xlsx sheet IPQC_FQC_OQC_MAP hoặc .csv legacy)
    /// → upsert idempotent. Emit <c>QC_LIBRARY_IMPORT</c>.</summary>
    [HttpPost("import")]
    [Authorize(Roles = "Admin,Supervisor,QC")]
    [RequestSizeLimit(20_000_000)]
    public async Task<ActionResult<CheckLibraryImportResult>> Import(IFormFile? file, CancellationToken ct = default)
    {
        if (file is null || file.Length == 0) return UnprocessableEntity(new { error = "qclib.no_file" });

        IReadOnlyList<QcCheckLibraryRow> rows;
        if (file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            await using var s = file.OpenReadStream();
            using var ms = new MemoryStream();
            await s.CopyToAsync(ms, ct);
            ms.Position = 0;
            rows = QcLibraryV5Parser.Parse(ms);
        }
        else
        {
            using var reader = new StreamReader(file.OpenReadStream());
            rows = QcCheckLibraryCsv.ParseDetailed(await reader.ReadToEndAsync(ct)).Rows;
        }

        var r = await DbSeeder.SeedCheckItemLibraryAsync(_write, rows);
        var total = await _write.CheckItemLibraries.CountAsync(ct);
        await _audit.EmitAsync(AuditAction.QcLibraryImport, Actor, Role, "CheckItemLibrary", file.FileName,
            $"{{\"inserted\":{r.LibInserted},\"updated\":{r.LibUpdated},\"reason_added\":{r.ReasonAdded},\"total\":{total}}}");
        return Ok(new CheckLibraryImportResult
        {
            Inserted = r.LibInserted, Updated = r.LibUpdated, ReasonAdded = r.ReasonAdded, Total = total,
        });
    }

    /// <summary>Export CSV toàn bộ thư viện (ma trận tick-box + mô tả).</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string? line, CancellationToken ct = default)
    {
        var q = _db.CheckItemLibraries.AsNoTracking().Where(c => c.Active);
        if (!string.IsNullOrWhiteSpace(line)) q = q.Where(c => c.ProcessLine == line);
        var items = await q.OrderBy(c => c.ProcessLine).ThenBy(c => c.Sort).ThenBy(c => c.ItemId).ToListAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine(CsvHeader);
        char B(bool b) => b ? '●' : '·';
        foreach (var c in items)
            sb.AppendLine(string.Join(",",
                Csv(c.ItemId), Csv(c.ProcessLine),
                B(c.BlankLabel), B(c.Flexo), B(c.LetterPress), B(c.HpIndigo), B(c.SilkScreen),
                B(c.Flatbed), B(c.Rdc), B(c.Laminate), B(c.Zebra), B(c.SheetCut), B(c.PunchHole),
                B(c.DrillHole), B(c.Slit), B(c.Ipqc), B(c.Fqc), B(c.Oqc),
                Csv(c.GroupLabel), Csv(c.Code), Csv(c.ItemVi), Csv(c.ItemEn), Csv(c.AcceptanceVi),
                Csv(c.AcceptanceEn), Csv(c.Method), Csv(c.Severity), Csv(c.Aql), Csv(c.Sampling),
                Csv(c.CheckType), Csv(c.DefectCode), Csv(c.IsoRef), Csv(c.AppliesWhen), Csv(c.Note)));

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv", $"qc-library-{(line ?? "all").ToLowerInvariant()}.csv");
    }

    /// <summary>
    /// Hàng tiêu đề CSV — nguồn DUY NHẤT cho cả <c>export</c> lẫn <c>template</c>.
    /// Trước đây chuỗi này nằm inline trong Export; tách ra để file mẫu KHÔNG THỂ
    /// lệch khỏi file xuất (và khỏi thứ tự cột mà importer đọc).
    /// </summary>
    private const string CsvHeader =
        "ItemID,Line,BlankLabel,Flexo,LetterPress,HpIndigo,SilkScreen,Flatbed,RDC,Laminate,Zebra,SheetCut,PunchHole,DrillHole,Slit,IPQC,FQC,OQC,Group,Code,ItemVI,ItemEN,AcceptanceVI,AcceptanceEN,Method,Severity,AQL,Sampling,CheckType,Defect,ISO,Condition,Note";

    /// <summary>
    /// Tải FILE MẪU để nhập liệu (khác <c>export</c> — export xuất dữ liệu đang có).
    ///
    /// <para>Kèm đúng một dòng ví dụ vì quy ước tick <c>●</c> / <c>·</c> không tự
    /// hiển nhiên: người điền lần đầu không có cách nào đoán ra phải gõ ký tự gì
    /// vào 15 cột phương pháp. Một dòng mẫu rẻ hơn một trang hướng dẫn.</para>
    ///
    /// Đây là mục còn thiếu duy nhất về nhập liệu sau khi PR #127 (mô hình cũ)
    /// bị đóng — viết lại trên nền v5.
    /// </summary>
    [HttpGet("template")]
    [Authorize(Roles = "Admin,Supervisor,QC")]
    public IActionResult Template()
    {
        var sb = new StringBuilder();
        sb.AppendLine(CsvHeader);
        sb.AppendLine(string.Join(",",
            "LBL-A99", "Label",
            "·", "●", "●", "●", "·", "·", "·", "·", "·", "·", "·", "·", "·",
            "●", "●", "·",
            "A·Ngoại quan", "SAMPLE",
            Csv("Ví dụ: mô tả hạng mục (VI)"), Csv("Example: item description (EN)"),
            Csv("Tiêu chí chấp nhận (VI)"), Csv("Acceptance criteria (EN)"),
            "Visual", "Major", "2.5", "AQL", "Attribute", "SAMPLE", "ISO-2859", "", ""));

        var bytes = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv", "qc-library-template.csv");
    }

    /// <summary>
    /// Bật/tắt một hạng mục mà KHÔNG xoá.
    ///
    /// <para>Với master data thì "ngưng dùng" mới đúng, không phải "xoá": WO cũ và
    /// snapshot QC đã đóng băng còn tham chiếu tới hạng mục đó, xoá đi là làm hồ sơ
    /// chất lượng cũ trỏ vào khoảng không. Cột <c>Active</c> đã có sẵn trong entity
    /// v5 nhưng trước đây không có đường nào trên UI để bật/tắt — chỉ xoá được.</para>
    ///
    /// Dùng lại mã audit <c>QC_LIBRARY_ITEM_SET</c> (đổi Active LÀ một dạng set)
    /// thay vì thêm mã mới, vì <c>AuditAction</c> nằm trong baseline read-only.
    /// </summary>
    [HttpPatch("{itemId}/active")]
    [Authorize(Roles = "Admin,Supervisor,QC")]
    public async Task<ActionResult<CheckLibraryItemDto>> SetActive(
        string itemId, [FromQuery] bool active,
        [FromServices] CCL.MES.Api.Services.ICheckLibraryAdminService admin,
        CancellationToken ct = default)
    {
        var r = await admin.SetActiveAsync(itemId, active, Actor, Role, ct);
        return r is null ? NotFound() : Ok(ToDto(r.Item));
    }

    private static string Csv(string? s)
    {
        s ??= "";
        return s.Contains(',') || s.Contains('"') || s.Contains('\n')
            ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }

    private static void Apply(CheckItemLibrary e, CheckLibraryUpsertDto d)
    {
        e.ProcessLine = d.ProcessLine; e.GroupLabel = d.GroupLabel; e.Code = d.Code;
        e.BlankLabel = d.BlankLabel; e.Flexo = d.Flexo; e.LetterPress = d.LetterPress; e.HpIndigo = d.HpIndigo;
        e.SilkScreen = d.SilkScreen; e.Flatbed = d.Flatbed; e.Rdc = d.Rdc; e.Laminate = d.Laminate;
        e.Zebra = d.Zebra; e.SheetCut = d.SheetCut; e.PunchHole = d.PunchHole; e.DrillHole = d.DrillHole;
        e.Slit = d.Slit; e.Ipqc = d.Ipqc; e.Fqc = d.Fqc; e.Oqc = d.Oqc;
        e.ItemVi = d.ItemVi; e.ItemEn = d.ItemEn; e.AcceptanceVi = d.AcceptanceVi; e.AcceptanceEn = d.AcceptanceEn;
        e.Method = d.Method; e.Severity = d.Severity; e.Aql = d.Aql; e.Sampling = d.Sampling;
        e.CheckType = d.CheckType; e.DefectCode = d.DefectCode; e.IsoRef = d.IsoRef;
        e.AppliesWhen = d.AppliesWhen; e.Note = d.Note; e.Active = d.Active; e.Sort = d.Sort;
    }

    private static CheckLibraryItemDto ToDto(CheckItemLibrary c) => new()
    {
        ItemId = c.ItemId, ProcessLine = c.ProcessLine, ProductCode = c.ProductCode,
        GroupLabel = c.GroupLabel, Code = c.Code,
        BlankLabel = c.BlankLabel, Flexo = c.Flexo, LetterPress = c.LetterPress, HpIndigo = c.HpIndigo,
        SilkScreen = c.SilkScreen, Flatbed = c.Flatbed, Rdc = c.Rdc, Laminate = c.Laminate, Zebra = c.Zebra,
        SheetCut = c.SheetCut, PunchHole = c.PunchHole, DrillHole = c.DrillHole, Slit = c.Slit,
        Ipqc = c.Ipqc, Fqc = c.Fqc, Oqc = c.Oqc,
        ItemVi = c.ItemVi, ItemEn = c.ItemEn, AcceptanceVi = c.AcceptanceVi, AcceptanceEn = c.AcceptanceEn,
        Method = c.Method, Severity = c.Severity, Aql = c.Aql, Sampling = c.Sampling, CheckType = c.CheckType,
        DefectCode = c.DefectCode, IsoRef = c.IsoRef, AppliesWhen = c.AppliesWhen, Note = c.Note,
        Active = c.Active, Sort = c.Sort,
    };
}
