using System.Security.Claims;
using CCL.MES.Application.Services;
using CCL.MES.Shared;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.Quality;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// A1 — mạch lô nguyên vật liệu (wire).
///
/// <code>
/// POST /api/v2/material-lots                                   nhập lô lúc làm IQC
/// POST /api/v2/material-lots/{id}/status                       QC/Supervisor/Admin
/// POST /api/v2/material-lots/{id}/extend-expiry                Đ3 — hai vai khác nhau
/// POST /api/v2/work-orders/{id}/materials/{idx}/consume        quét lô lên dòng BOM
/// POST /api/v2/material-consumptions/{id}/reverse              Đ3 — Supervisor đảo
/// GET  /api/v2/work-orders/{id}/material-genealogy             bảng truy xuất
/// POST /api/v2/material-lots/backfill                          AdminOnly — dựng mạch lô cũ
/// </code>
///
/// <para><b>Controller MỎNG.</b> Không một truy vấn <c>DbContext</c>, không một
/// lệnh ghi xuống DB. Toàn bộ luật (thứ tự điểm chặn, RBAC, grace period,
/// optimistic lock, audit) nằm ở <see cref="MaterialLotScanService"/> — nhờ vậy
/// kiểm được bằng unit/integration test mà không phải giả lập HTTP, và dùng lại
/// được cho job nền / adapter ERP về sau. Ở đây chỉ còn: bind body → lấy
/// actor/role → gọi một hàm → map mã lỗi sang HTTP status.</para>
///
/// <para>Vì controller không tự ghi và cũng không tự emit audit, hai gate
/// <c>gate-thin</c> / <c>gate-audit</c> phải KHÔNG thấy dấu vết ghi DB ở đây —
/// kể cả trong lời chú thích, vì chúng khớp chuỗi thô chưa strip comment.</para>
///
/// <para><b>Idempotency-Key bắt buộc cho mọi mutation.</b> Đ4 bỏ unique index
/// chống quét lặp, nên header này là thứ DUY NHẤT còn chặn double-tap: cùng key
/// → middleware phát lại đúng response cũ, 1 dòng; khác key → 2 dòng và cả hai
/// hiện trong hồ sơ. Đây là đánh đổi Henry chọn có ý thức.</para>
/// </summary>
[ApiController]
[Authorize]
[Route(ApiVersion.Prefix)]
public sealed class MaterialLotsController : ControllerBase
{
    private readonly MaterialLotScanService _svc;

    public MaterialLotsController(MaterialLotScanService svc) => _svc = svc;

    private (string Actor, string Role) Who() => (
        User.FindFirstValue(ClaimTypes.Name) ?? "anonymous",
        User.FindFirstValue(ClaimTypes.Role) ?? "");

    /// <summary>Header bắt buộc — kiểm ở đây vì đây là quan tâm thuần HTTP
    /// (không chạm DB), đúng ranh giới controller/service.</summary>
    private IActionResult? RequireIdempotencyKey() =>
        string.IsNullOrWhiteSpace(Request.Headers["Idempotency-Key"].ToString())
            ? BadRequest(ApiError.Of("wo.idempotency_key_required",
                "Idempotency-Key header required."))
            : null;

    // ── POST /material-lots ────────────────────────────────────────

    [HttpPost("material-lots")]
    public async Task<IActionResult> Create(
        [FromBody] CreateMaterialLotRequest? req, CancellationToken ct = default)
    {
        if (RequireIdempotencyKey() is { } bad) return bad;
        var (actor, role) = Who();
        var r = await _svc.CreateLotAsync(
            req?.LotNo, req?.PartNo, req?.QtyReceived ?? 0, req?.Uom, req?.ExpiryAt,
            req?.IqcInspectionId, req?.SupplierName, req?.SupplierLotNo, actor, role, ct);
        return Map(r);
    }

    // ── POST /material-lots/{id}/status ────────────────────────────

    [HttpPost("material-lots/{id:long}/status")]
    public async Task<IActionResult> SetStatus(
        long id, [FromBody] SetMaterialLotStatusRequest? req, CancellationToken ct = default)
    {
        if (RequireIdempotencyKey() is { } bad) return bad;
        var (actor, role) = Who();
        return Map(await _svc.SetStatusAsync(id, req?.Status, req?.Reason, actor, role, ct));
    }

    // ── POST /material-lots/{id}/extend-expiry ─────────────────────

    [HttpPost("material-lots/{id:long}/extend-expiry")]
    public async Task<IActionResult> ExtendExpiry(
        long id, [FromBody] ExtendMaterialLotExpiryRequest? req, CancellationToken ct = default)
    {
        if (RequireIdempotencyKey() is { } bad) return bad;
        var (actor, role) = Who();
        return Map(await _svc.ExtendExpiryAsync(
            id, req?.ExpiryExtendedTo, req?.Reason, actor, role, ct));
    }

    // ── POST /work-orders/{id}/materials/{idx}/consume ─────────────

    [HttpPost("work-orders/{id:long}/materials/{bomLineIdx:int}/consume")]
    public async Task<IActionResult> Consume(
        long id, int bomLineIdx, [FromBody] ConsumeMaterialLotRequest? req,
        CancellationToken ct = default)
    {
        if (RequireIdempotencyKey() is { } bad) return bad;
        var (actor, role) = Who();
        var r = await _svc.ConsumeAsync(
            id, bomLineIdx, req?.LotNo, req?.QtyUsed ?? 0, req?.LegId, actor, role,
            ifMatch: Request.Headers.IfMatch.ToString(), ct: ct);
        return Map(r);
    }

    // ── POST /material-consumptions/{id}/reverse ───────────────────

    [HttpPost("material-consumptions/{id:long}/reverse")]
    public async Task<IActionResult> Reverse(
        long id, [FromBody] ReverseConsumptionRequest? req, CancellationToken ct = default)
    {
        if (RequireIdempotencyKey() is { } bad) return bad;
        var (actor, role) = Who();
        return Map(await _svc.ReverseAsync(id, req?.Reason, actor, role, ct));
    }

    // ── GET /work-orders/{id}/material-genealogy ───────────────────

    [HttpGet("work-orders/{id:long}/material-genealogy")]
    public async Task<ActionResult<MaterialGenealogyView>> Genealogy(
        long id, CancellationToken ct = default)
    {
        var rows = await _svc.GenealogyAsync(id, ct);
        return Ok(new MaterialGenealogyView
        {
            WoId = id,
            Rows = rows.Select(r => new MaterialGenealogyRowDto
            {
                ConsumptionId = r.ConsumptionId,
                BomLineIdx = r.BomLineIdx,
                MaterialCode = r.MaterialCode,
                LegId = r.LegId,
                MaterialLotId = r.MaterialLotId,
                LotNo = r.LotNo,
                PartNo = r.PartNo,
                LotStatus = r.LotStatus,
                ExpiryAt = r.ExpiryAt,
                SupplierName = r.SupplierName,
                IqcInspectionId = r.IqcInspectionId,
                IqcFailCount = r.IqcFailCount,
                IqcFailCodes = r.IqcFailCodes,
                QtyUsed = r.QtyUsed,
                QtyUsedTotalForLot = r.QtyUsedTotalForLot,
                Uom = r.Uom,
                ScannedBy = r.ScannedBy,
                ScannedAt = r.ScannedAt,
                ReversedAt = r.ReversedAt,
                ReversedBy = r.ReversedBy,
            }).ToList(),
        });
    }

    // ── POST /material-lots/backfill ──────────────────────────────

    /// <summary>A1 — backfill MỘT LẦN, dựng mạch lô cho dữ liệu ĐÃ CÓ: mỗi
    /// <c>WoMaterials.LotNo</c> khác rỗng sinh một <c>MaterialLot</c> + một dòng
    /// tiêu hao, để WO cũ cũng trả lời được câu "cuộn nào đã vào đơn này".
    ///
    /// <para><b>AdminOnly</b> — ghi hàng loạt trên toàn bộ dữ liệu lịch sử,
    /// không phải việc của operator. Cùng hình dạng với
    /// <c>POST /quality/traceability/backfill</c> đã có.</para>
    ///
    /// <para><b>Idempotent ở tầng service</b> bằng dấu <c>backfill-a1</c>
    /// (hợp đồng §2 hệ quả 2), nên gọi lại KHÔNG sinh thêm dòng — lần hai trả
    /// <c>skipped == candidates</c> và <c>consumptionsCreated == 0</c>. Header
    /// <c>Idempotency-Key</c> vẫn bắt buộc để đồng nhất với mọi mutation khác
    /// của controller này (double-tap được middleware phát lại response cũ).</para>
    ///
    /// <para><c>quarantined</c> là con số phải đọc TRƯỚC khi lật cờ
    /// <c>Mes:MaterialLot:EnforceReleased</c>: đó là số lô lịch sử không khớp
    /// phiếu IQC nào (Đ6), tức số ca sẽ bị chặn khi bật cưỡng chế.</para></summary>
    [HttpPost("material-lots/backfill"), Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Backfill(
        [FromServices] MaterialLotBackfillService backfill, CancellationToken ct = default)
    {
        if (RequireIdempotencyKey() is { } bad) return bad;
        var (actor, role) = Who();

        // Service emit đúng MỘT dòng audit cho cả lần chạy kèm counts — hợp đồng
        // chỉ liệt kê 5 mã, ở đây KHÔNG thêm mã audit thứ hai.
        var r = await backfill.RunAsync(actor, role, ct);

        return Ok(new MaterialLotBackfillResponse
        {
            Candidates = r.Candidates,
            LotsCreated = r.LotsCreated,
            LotsReused = r.LotsReused,
            ConsumptionsCreated = r.ConsumptionsCreated,
            Skipped = r.Skipped,
            Quarantined = r.Quarantined,
            InheritedFromIqc = r.InheritedFromIqc,
        });
    }

    // ── Map kết quả service → HTTP ────────────────────────────────

    private IActionResult Map(MaterialLotOutcome r)
    {
        var body = new MaterialLotSetResponse
        {
            Ok = r.Ok,
            ErrorCode = r.ErrorCode,
            Warning = r.Warning,
            Enforced = r.Enforced,
            ConsumptionId = r.ConsumptionId,
            MaterialLotId = r.MaterialLotId,
            LotNo = r.LotNo,
            LotStatus = r.LotStatus,
            QtyAvailableAfter = r.QtyAvailableAfter,
        };
        if (r.Ok) return Ok(body);

        // Ca 403/404/409 trả ApiError để khớp shape lỗi chung của API; 422 trả
        // envelope đầy đủ vì UI cần LotStatus/QtyAvailableAfter để hiện lý do
        // ngay trên màn hình quét mà không phải gọi thêm một vòng.
        return r.HttpStatus switch
        {
            403 => StatusCode(StatusCodes.Status403Forbidden,
                       ApiError.Of(r.ErrorCode!, r.MessageEn!)),
            404 => NotFound(ApiError.Of(r.ErrorCode!, r.MessageEn!)),
            409 => Conflict(body),
            400 => BadRequest(ApiError.Of(r.ErrorCode!, r.MessageEn!)),
            _   => UnprocessableEntity(body),
        };
    }
}
