using System.Text.Json;
using CCL.MES.Application;
using CCL.MES.Application.Audit;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Api.Services;

/// <summary>Kết quả của một lần commit mutation WO.</summary>
/// <param name="Conflict">true nghĩa là thua cuộc đua RowVersion (409); ETag +
/// audit WO_STATE_CONFLICT đã được xử lý bên trong executor.</param>
/// <param name="ETag">RowVersion mới (base64) sau save, hoặc RowVersion server
/// hiện tại khi conflict — đã set sẵn vào header ETag.</param>
/// <param name="Fresh">Bản WO đọc lại (AsNoTracking) sau save/conflict, hoặc
/// null nếu WO vừa bị xoá giữa chừng (gần như không xảy ra).</param>
public readonly record struct WoCommitOutcome(bool Conflict, string ETag, WorkOrder? Fresh);

/// <summary>
/// A2 — bộ THỰC THI mutation WO: gói phần CƠ HỌC tất-định + an-toàn-concurrency
/// mà mọi bề mặt ghi WO lặp lại y hệt, để rút <c>SaveChangesAsync</c> ra khỏi
/// <c>Controllers/</c> (tầng controller mỏng đi, L40) và để đường L45 (conflict
/// PHẢI để lại vết) sống ở MỘT nơi được test + gate, thay vì sao chép trong
/// từng controller.
///
/// <para><b>Ranh giới.</b> Controller vẫn tự: fetch + validate + mutate entity +
/// touch <c>wo.UpdatedAt/By</c> (+ rollup riêng của nó) TRƯỚC khi gọi; và tự
/// dựng response body + emit audit THÀNH CÔNG SAU khi gọi (detail mỗi bề mặt một
/// khác nên giữ ở controller để byte-identical). Executor chỉ nắm khúc giữa:
/// <c>SaveChanges</c> → (thành công) đọc lại ETag; (đua thua) clear tracker →
/// đọc lại → set ETag → emit <c>WO_STATE_CONFLICT</c> (L45) → trả outcome.</para>
///
/// <para><b>Byte-identical.</b> Cơ chế + detail conflict + thứ tự thao tác giữ
/// nguyên hệt bản <c>CommitAndAuditAsync</c>/<c>HandleConcurrencyAsync</c> inline
/// đã thay: emit đứng SAU <c>ChangeTracker.Clear()</c> (nếu không, ApiAuditWriter
/// dùng chung DbContext scoped sẽ kéo theo UPDATE đã fail và ném lại — nuốt mất
/// dòng audit). Soak N=10 của RunningSurface/Prepress + gate-audit-emit (đã mở
/// rộng quét Services/) là bằng chứng L45 còn nguyên.</para>
/// </summary>
public sealed class WoMutationExecutor
{
    private readonly IMesDbContext _db;
    private readonly IAuditWriter _audit;

    public WoMutationExecutor(IMesDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    /// <summary>
    /// Commit các thay đổi đã tracked (controller đã mutate + touch WO). Thành
    /// công: đọc lại RowVersion mới, set header ETag, trả outcome. Đua thua
    /// (<see cref="DbUpdateConcurrencyException"/>): clear tracker, đọc lại WO,
    /// set header ETag, emit <c>WO_STATE_CONFLICT</c> (L45), trả outcome.Conflict.
    /// </summary>
    /// <param name="actorId">Khi != null, chèn <c>actor_id</c> vào detail conflict
    /// (trước <c>source</c>) — dùng cho các surface admin/advance vốn ghi actor_id.
    /// Bỏ trống → detail giữ nguyên shape cũ (byte-identical cho 4 surface đã migrate).</param>
    public async Task<WoCommitOutcome> SaveAndResolveAsync(
        HttpContext http, long woId, string woNo, string actor, string role, string attemptedAction,
        string? actorId = null)
    {
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // L45 — nhánh cuộc đua THẬT (hai operator ghi cùng WO): PHẢI để lại
            // vết. Emit đứng SAU ChangeTracker.Clear(): ApiAuditWriter dùng CHUNG
            // DbContext scoped của request; tracker còn bẩn thì SaveChanges của
            // audit kéo theo UPDATE đã fail và ném lại, nuốt mất dòng audit lần nữa.
            if (_db is DbContext ctx) ctx.ChangeTracker.Clear();
            var freshC = await _db.WorkOrders.Where(w => w.Id == woId).AsNoTracking().FirstOrDefaultAsync();
            var freshEtagC = B64(freshC?.RowVersion);
            http.Response.Headers.ETag = $"\"{freshEtagC}\"";
            var clientVer = NormalizeETag(http.Request.Headers.IfMatch.ToString());
            // Hai shape để giữ THỨ TỰ field byte-identical: không actor_id (4 surface
            // cũ) vs có actor_id trước source (advance / force-phase).
            object conflictDetail = actorId is null
                ? new
                {
                    wo_id = woId,
                    wo_no = woNo,
                    attempted_action = attemptedAction,
                    client_version = clientVer,
                    server_version = freshEtagC,
                    source = "ef_concurrency",
                }
                : new
                {
                    wo_id = woId,
                    wo_no = woNo,
                    attempted_action = attemptedAction,
                    client_version = clientVer,
                    server_version = freshEtagC,
                    actor_id = actorId,
                    source = "ef_concurrency",
                };
            await _audit.EmitAsync(
                action: AuditAction.WoStateConflict,
                actor: actor,
                actorRole: role,
                targetType: "WorkOrder",
                targetId: woId.ToString(),
                detail: JsonSerializer.Serialize(conflictDetail));
            return new WoCommitOutcome(true, freshEtagC, freshC);
        }

        // Post-save: re-read RowVersion via AsNoTracking (Lesson L11 — SQLite
        // UPDATE trigger fires AFTER the RETURNING clause).
        var fresh = await _db.WorkOrders.Where(w => w.Id == woId).AsNoTracking().FirstOrDefaultAsync();
        var freshEtag = B64(fresh?.RowVersion);
        http.Response.Headers.ETag = $"\"{freshEtag}\"";
        return new WoCommitOutcome(false, freshEtag, fresh);
    }

    private static string B64(byte[]? rv) => rv is { Length: > 0 } ? Convert.ToBase64String(rv) : "";

    private static string NormalizeETag(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("W/", StringComparison.Ordinal)) trimmed = trimmed[2..];
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
            trimmed = trimmed[1..^1];
        return trimmed;
    }
}
