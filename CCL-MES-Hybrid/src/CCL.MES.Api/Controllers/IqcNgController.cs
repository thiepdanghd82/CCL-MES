using System.Security.Claims;
using CCL.MES.Application.Services;
using CCL.MES.Domain.Entities;
using CCL.MES.Shared;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.Quality;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// P13 bước 5/6 — khối NG nguyên liệu và claim nhà cung cấp.
///
/// <para><b>Controller MỎNG (L40).</b> Không một truy vấn <c>DbContext</c>,
/// không một lệnh ghi DB. Toàn bộ luật — vòng đời năm trạng thái,
/// "khép vụ phải có ngày claim", "bản ghi mới phải đủ số lượng", RBAC, audit —
/// nằm ở <see cref="IqcNgService"/> và <see cref="IqcNgWorkflow"/>. Ở đây chỉ
/// bind body → phân giải chuỗi thành enum → gọi một hàm → map sang HTTP.</para>
///
/// <para><b>Vì sao là controller RIÊNG, không thêm route vào IqcController.</b>
/// Đo trên 169 vụ NG thật của 2026: <b>64 vụ (38%) phát hiện ở SẢN XUẤT</b>,
/// khi không có phiếu IQC nào đang mở. Treo khối NG dưới
/// <c>/iqc/tickets/{id}/…</c> nghĩa là ngần ấy vụ không có URL để tồn tại.</para>
/// </summary>
[ApiController]
[Authorize(Policy = "QcRead")]
[Route(ApiVersion.Prefix + "/iqc/ng")]
public sealed class IqcNgController : ControllerBase
{
    private readonly IqcNgService _svc;
    public IqcNgController(IqcNgService svc) => _svc = svc;

    private (string Actor, string Role) Who() => (
        User.FindFirstValue(ClaimTypes.Name) ?? "anonymous",
        User.FindFirstValue(ClaimTypes.Role) ?? "");

    /// <summary>Chuỗi → enum. Chuỗi lạ KHÔNG được lặng lẽ rơi về giá trị 0:
    /// gửi <c>"Setled"</c> (thiếu chữ t) mà nhận <c>None</c> thì người dùng
    /// tưởng đã ghi xong hình thức đền bù.</summary>
    private static bool TryEnum<T>(string? raw, out T value) where T : struct, Enum
        => Enum.TryParse(raw, ignoreCase: true, out value) && Enum.IsDefined(value);

    private static IqcNgListItem Map(IqcNgRecord r) => new()
    {
        Id = r.Id,
        IqcInspectionId = r.IqcInspectionId,
        MaterialLotId = r.MaterialLotId,
        PartNo = r.PartNo,
        SupplierLotNo = r.SupplierLotNo,
        SupplierName = r.SupplierName,
        MaterialName = r.MaterialName,
        PoNo = r.PoNo,
        DetectedAt = r.DetectedAt,
        DetectedStage = r.DetectedStage.ToString(),
        DefectName = r.DefectName,
        DefectCode = r.DefectCode,
        NgQty = r.NgQty,
        NgUom = r.NgUom,
        NgAreaM2 = r.NgAreaM2,
        NgRolls = r.NgRolls,
        Status = r.Status.ToString(),
        ClaimedAt = r.ClaimedAt,
        ClaimRef = r.ClaimRef,
        Settlement = r.Settlement.ToString(),
        SettledAt = r.SettledAt,
        SupplierNote = r.SupplierNote,
        Remark = r.Remark,
        ImportSource = r.ImportSource,
        CreatedBy = r.CreatedBy,
        CreatedAt = r.CreatedAt,
        UpdatedBy = r.UpdatedBy,
        UpdatedAt = r.UpdatedAt,
    };

    private IActionResult MapFail(IqcNgResult r) => r.HttpStatus switch
    {
        404 => NotFound(ApiError.Of(r.ErrorCode!, r.MessageEn!)),
        403 => StatusCode(StatusCodes.Status403Forbidden, ApiError.Of(r.ErrorCode!, r.MessageEn!)),
        400 => BadRequest(ApiError.Of(r.ErrorCode!, r.MessageEn!)),
        _   => UnprocessableEntity(ApiError.Of(r.ErrorCode!, r.MessageEn!)),
    };

    private IActionResult Ok(IqcNgResult r) =>
        r.Ok ? Ok(new IqcNgMutationResponse { Id = r.Id, Status = r.Status }) : MapFail(r);

    private bool RequireIdemKey(out IActionResult? bad)
    {
        if (!string.IsNullOrWhiteSpace(Request.Headers["Idempotency-Key"].ToString()))
        { bad = null; return true; }
        bad = BadRequest(ApiError.Of("wo.idempotency_key_required", "Idempotency-Key header required."));
        return false;
    }

    // ── đọc ──────────────────────────────────────────────────────────────

    /// <summary>Danh sách vụ NG, mới nhất trước, kèm số đếm theo trạng thái.</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? status, [FromQuery] string? partNo,
        [FromQuery] int take = 200, CancellationToken ct = default)
    {
        IqcNgStatus? filter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!TryEnum<IqcNgStatus>(status, out var s))
                return BadRequest(ApiError.Of("iqc.ng.bad_status", $"Unknown status '{status}'."));
            filter = s;
        }

        var rows = await _svc.ListAsync(filter, partNo, take, ct);
        // Số đếm KHÔNG lọc theo `status`: dải chip phải hiện tổng của MỌI trạng
        // thái, kể cả cái đang không được chọn — nếu không thì chọn một chip xong
        // các chip khác về 0 và người dùng tưởng dữ liệu biến mất.
        var counts = await _svc.CountByStatusAsync(ct);

        return Ok(new IqcNgListResponse
        {
            Items = rows.Select(Map).ToList(),
            CountByStatus = counts.ToDictionary(k => k.Key, v => v.Value),
        });
    }

    // ── ghi ──────────────────────────────────────────────────────────────

    [HttpPost, Authorize(Policy = "QcEdit")]
    public async Task<IActionResult> Create(
        [FromBody] CreateIqcNgBody? body, CancellationToken ct = default)
    {
        if (!RequireIdemKey(out var bad)) return bad!;

        var stage = IqcNgStage.Unknown;
        if (!string.IsNullOrWhiteSpace(body?.DetectedStage)
            && !TryEnum(body!.DetectedStage, out stage))
            return BadRequest(ApiError.Of("iqc.ng.bad_stage", $"Unknown stage '{body.DetectedStage}'."));

        var detected = body?.DetectedAt ?? DateTime.UtcNow;
        // Phát hiện ở TƯƠNG LAI là dữ liệu hỏng, không phải một lựa chọn. Chặn ở
        // đây vì đó là luật của INPUT, không phải luật nghiệp vụ.
        if (detected > DateTime.UtcNow.AddDays(1))
            return UnprocessableEntity(ApiError.Of(
                "iqc.ng.detected_in_future", "Detection date cannot be in the future."));

        var (actor, role) = Who();
        var r = await _svc.CreateAsync(new IqcNgRecord
        {
            IqcInspectionId = body?.IqcInspectionId,
            MaterialLotId = body?.MaterialLotId,
            PartNo = body?.PartNo?.Trim(),
            SupplierLotNo = body?.SupplierLotNo?.Trim(),
            SupplierName = body?.SupplierName?.Trim(),
            MaterialName = body?.MaterialName?.Trim(),
            PoNo = body?.PoNo?.Trim(),
            DetectedAt = detected,
            DetectedStage = stage,
            DefectName = body?.DefectName?.Trim(),
            DefectCode = body?.DefectCode?.Trim(),
            NgQty = body?.NgQty,
            NgUom = body?.NgUom?.Trim(),
            NgAreaM2 = body?.NgAreaM2,
            NgRolls = body?.NgRolls,
            Remark = body?.Remark?.Trim(),
        }, actor, role, ct);

        return r.Ok
            ? CreatedAtAction(nameof(List), null,
                new IqcNgMutationResponse { Id = r.Id, Status = r.Status })
            : MapFail(r);
    }

    [HttpPost("{id:long}/claim"), Authorize(Policy = "QcEdit")]
    public async Task<IActionResult> Claim(
        long id, [FromBody] IqcNgClaimBody? body, CancellationToken ct = default)
    {
        if (!RequireIdemKey(out var bad)) return bad!;
        var (actor, role) = Who();
        return Ok(await _svc.ClaimAsync(id, body?.ClaimRef, body?.ClaimedAt, actor, role, ct));
    }

    [HttpPost("{id:long}/supplier-confirm"), Authorize(Policy = "QcEdit")]
    public async Task<IActionResult> SupplierConfirm(
        long id, [FromBody] IqcNgSupplierConfirmBody? body, CancellationToken ct = default)
    {
        if (!RequireIdemKey(out var bad)) return bad!;
        var (actor, role) = Who();
        return Ok(await _svc.SupplierConfirmAsync(id, body?.Note, actor, role, ct));
    }

    [HttpPost("{id:long}/settle"), Authorize(Policy = "QcEdit")]
    public async Task<IActionResult> Settle(
        long id, [FromBody] IqcNgSettleBody? body, CancellationToken ct = default)
    {
        if (!RequireIdemKey(out var bad)) return bad!;
        if (!TryEnum<IqcClaimSettlement>(body?.Settlement, out var settlement))
            return BadRequest(ApiError.Of("iqc.ng.bad_settlement",
                $"Unknown settlement '{body?.Settlement}'."));

        var (actor, role) = Who();
        return Ok(await _svc.SettleAsync(
            id, settlement, body?.SettledAt, body?.Note, actor, role, ct));
    }

    [HttpPost("{id:long}/close-no-claim"), Authorize(Policy = "QcEdit")]
    public async Task<IActionResult> Close(
        long id, [FromBody] IqcNgCloseBody? body, CancellationToken ct = default)
    {
        if (!RequireIdemKey(out var bad)) return bad!;
        var (actor, role) = Who();
        return Ok(await _svc.CloseNoClaimAsync(id, body?.Reason ?? "", actor, role, ct));
    }
}
