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
            return await RaceConflictAsync(http, woId, woNo, actor, role, attemptedAction, actorId);
        }

        // Post-save: re-read RowVersion via AsNoTracking (Lesson L11 — SQLite
        // UPDATE trigger fires AFTER the RETURNING clause).
        var fresh = await _db.WorkOrders.Where(w => w.Id == woId).AsNoTracking().FirstOrDefaultAsync();
        var freshEtag = EtagCodec.Base64(fresh?.RowVersion);
        http.Response.Headers.ETag = $"\"{freshEtag}\"";
        return new WoCommitOutcome(false, freshEtag, fresh);
    }

    /// <summary>
    /// Đường conflict cho caller mà SaveChanges nằm Ở NƠI KHÁC (vd
    /// <c>/advance</c> save trong WorkOrderService): gọi TRONG khối
    /// <c>catch (DbUpdateConcurrencyException)</c>. Cùng cơ chế L45 với nhánh
    /// catch của <see cref="SaveAndResolveAsync"/> (clear tracker → reread →
    /// set ETag → emit WO_STATE_CONFLICT có source=ef_concurrency). Trả outcome
    /// để controller dựng typed 409 body của nó.
    /// </summary>
    public Task<WoCommitOutcome> ResolveWoConflictAsync(
        HttpContext http, long woId, string woNo, string actor, string role, string attemptedAction,
        string? actorId = null)
        => RaceConflictAsync(http, woId, woNo, actor, role, attemptedAction, actorId);

    // L45 — dùng chung bởi catch của SaveAndResolveAsync + ResolveWoConflictAsync.
    // Emit đứng SAU ChangeTracker.Clear(): ApiAuditWriter dùng CHUNG DbContext
    // scoped của request; tracker còn bẩn thì SaveChanges của audit kéo theo
    // UPDATE đã fail và ném lại, nuốt mất dòng audit lần nữa.
    private async Task<WoCommitOutcome> RaceConflictAsync(
        HttpContext http, long woId, string woNo, string actor, string role, string attemptedAction,
        string? actorId)
    {
        if (_db is DbContext ctx) ctx.ChangeTracker.Clear();
        var freshC = await _db.WorkOrders.Where(w => w.Id == woId).AsNoTracking().FirstOrDefaultAsync();
        var freshEtagC = EtagCodec.Base64(freshC?.RowVersion);
        http.Response.Headers.ETag = $"\"{freshEtagC}\"";
        var clientVer = EtagCodec.Normalize(http.Request.Headers.IfMatch.ToString());
        // Hai shape để giữ THỨ TỰ field byte-identical: không actor_id (4 surface
        // cũ) vs có actor_id trước source (advance / force-phase).
        object detail = actorId is null
            ? new
            {
                wo_id = woId, wo_no = woNo, attempted_action = attemptedAction,
                client_version = clientVer, server_version = freshEtagC, source = "ef_concurrency",
            }
            : new
            {
                wo_id = woId, wo_no = woNo, attempted_action = attemptedAction,
                client_version = clientVer, server_version = freshEtagC, actor_id = actorId, source = "ef_concurrency",
            };
        await _audit.EmitAsync(
            action: AuditAction.WoStateConflict, actor: actor, actorRole: role,
            targetType: "WorkOrder", targetId: woId.ToString(),
            detail: JsonSerializer.Serialize(detail));
        return new WoCommitOutcome(true, freshEtagC, freshC);
    }

    /// <summary>
    /// Pre-check ETag (đường prelude cho surface không dùng base PreludeAsync — vd
    /// <c>/advance</c>): so RowVersion hiện có với If-Match client. Lệch ⇒ stale:
    /// set ETag header + emit WO_STATE_CONFLICT (KHÔNG source — đây là phát hiện
    /// TRƯỚC khi ghi, không phải đua ef) + trả outcome.Conflict=true. Khớp ⇒
    /// Conflict=false. Byte-identical với pre-check inline cũ (detail có actor_id,
    /// KHÔNG source; dùng RowVersion của <paramref name="existing"/>, không reread).
    /// </summary>
    public async Task<WoCommitOutcome> PrecheckStaleAsync(
        HttpContext http, WorkOrder existing, string actor, string role, string attemptedAction,
        string? actorId = null)
    {
        var serverEtag = EtagCodec.Base64(existing.RowVersion);
        var clientEtag = EtagCodec.Normalize(http.Request.Headers.IfMatch.ToString());
        if (string.Equals(serverEtag, clientEtag, StringComparison.Ordinal))
            return new WoCommitOutcome(false, serverEtag, existing);

        object detail = actorId is null
            ? new
            {
                wo_id = existing.Id, wo_no = existing.WoNo, attempted_action = attemptedAction,
                client_version = clientEtag, server_version = serverEtag,
            }
            : new
            {
                wo_id = existing.Id, wo_no = existing.WoNo, attempted_action = attemptedAction,
                client_version = clientEtag, server_version = serverEtag, actor_id = actorId,
            };
        await _audit.EmitAsync(
            action: AuditAction.WoStateConflict, actor: actor, actorRole: role,
            targetType: "WorkOrder", targetId: existing.Id.ToString(),
            detail: JsonSerializer.Serialize(detail));
        http.Response.Headers.ETag = $"\"{serverEtag}\"";
        return new WoCommitOutcome(true, serverEtag, existing);
    }
}
