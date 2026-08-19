using System.Security.Claims;
using CCL.MES.Application.Services;
using CCL.MES.Shared;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.Quality;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// IQC (incoming raw-material) inspection surface. Read routes stay gated by
/// the <c>QcRead</c> policy (Admin / Supervisor / QC). The ticket-create
/// mutation (feat/iqc-ticket) is gated by <c>QcEdit</c> (same membership) and
/// requires an <c>Idempotency-Key</c> header, mirroring
/// <c>MaterialLotsController</c>.
///
/// <para><b>Controller MỎNG.</b> Không một truy vấn <c>DbContext</c>, không một
/// lệnh ghi. Toàn bộ luật (sinh ReceiptNo, match Code IFS NOCASE, cache mô tả,
/// mở lô Quarantine trong cùng giao dịch, audit, RBAC) nằm ở
/// <see cref="IqcService"/> — ở đây chỉ bind body → lấy actor/role → gọi một
/// hàm → map sang HTTP.</para>
/// </summary>
[ApiController]
[Authorize(Policy = "QcRead")]
[Route(ApiVersion.Prefix + "/iqc")]
public sealed class IqcController : ControllerBase
{
    private readonly IqcService _svc;
    public IqcController(IqcService svc) => _svc = svc;

    private (string Actor, string Role) Who() => (
        User.FindFirstValue(ClaimTypes.Name) ?? "anonymous",
        User.FindFirstValue(ClaimTypes.Role) ?? "");

    // ── Read (giữ nguyên) ─────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] CCL.MES.Domain.QcResult? status = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
        => Ok(await _svc.ListAsync(search, status, from, to, page, pageSize));

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id)
    {
        var insp = await _svc.GetWithDetailsAsync(id);
        return insp is null ? NotFound() : Ok(insp);
    }

    // ── feat/iqc-ticket — resolve Code IFS (UI auto-fill) ─────────

    /// <summary>Auto-fill Material/IFS description + matchStatus trước submit.
    /// Read-only ⇒ QcRead đủ, không cần Idempotency-Key.</summary>
    [HttpGet("resolve-code")]
    public async Task<ActionResult<ResolveIqcCodeResponse>> ResolveCode(
        [FromQuery] string? codeIfs, CancellationToken ct = default)
    {
        var r = await _svc.ResolveCodeAsync(codeIfs, ct);
        return Ok(new ResolveIqcCodeResponse
        {
            MatchStatus = r.MatchStatus,
            PartNo = r.PartNo,
            MaterialDescription = r.MaterialDescription,
            IfsDescription = r.IfsDescription,
            SupplierName = r.SupplierName,
        });
    }

    /// <summary>Tra vật liệu theo MÔ TẢ (feat/iqc-search-by-desc). Mô tả là ô
    /// tìm chính, kết quả là danh sách Code IFS distinct để chọn nhiều. Read-only
    /// ⇒ QcRead đủ, không cần Idempotency-Key. <c>desc</c> dưới ngưỡng → server
    /// trả <c>tooShort=true</c> + rỗng (không quét bảng).</summary>
    [HttpGet("search-material")]
    public async Task<ActionResult<IqcMaterialSearchResponse>> SearchMaterial(
        [FromQuery] string? desc,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var r = await _svc.SearchMaterialByDescriptionAsync(desc, page, pageSize, ct);
        return Ok(new IqcMaterialSearchResponse
        {
            TooShort = r.TooShort,
            Page = r.Page,
            PageSize = r.PageSize,
            Total = r.Total,
            Items = r.Items.Select(x => new IqcMaterialSearchItem
            {
                CodeIfs = x.CodeIfs,
                IfsDescription = x.IfsDescription,
            }).ToList(),
        });
    }

    /// <summary>Gợi ý Maker/Supplier cho dropdown search. Read-only.</summary>
    [HttpGet("makers")]
    public async Task<ActionResult<List<string>>> Makers(
        [FromQuery] string? search, CancellationToken ct = default)
        => Ok(await _svc.MakerSuggestionsAsync(search, ct));

    // ── feat/iqc-ticket — tạo phiếu (mutation) ────────────────────

    /// <summary>Tạo phiếu IQC + mở lô Quarantine (1 giao dịch). QcEdit +
    /// Idempotency-Key bắt buộc. 201 khi thành công; 422 khi input xấu; 409
    /// khi đua số phiếu / trùng lô; 403 khi role không đủ (policy gate).</summary>
    [HttpPost, Authorize(Policy = "QcEdit")]
    public async Task<IActionResult> Create(
        [FromBody] CreateIqcTicketBody? body, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(Request.Headers["Idempotency-Key"].ToString()))
            return BadRequest(ApiError.Of("wo.idempotency_key_required",
                "Idempotency-Key header required."));

        var (actor, role) = Who();
        var r = await _svc.CreateTicketAsync(new CreateIqcTicketRequest
        {
            CodeIfs = body?.CodeIfs ?? "",
            LotBatchNo = body?.LotBatchNo ?? "",
            ManufactureDate = body?.ManufactureDate,
            MakerName = body?.MakerName,
            SupplierName = body?.SupplierName,
            Quantity = body?.Quantity ?? 0,
            Uom = body?.Uom,
            SampleSize = body?.SampleSize,
            ExpiryAt = body?.ExpiryAt,
        }, actor, role, ct);

        if (!r.Ok)
        {
            return r.HttpStatus switch
            {
                403 => StatusCode(StatusCodes.Status403Forbidden,
                           ApiError.Of(r.ErrorCode!, r.MessageEn!)),
                409 => Conflict(ApiError.Of(r.ErrorCode!, r.MessageEn!)),
                400 => BadRequest(ApiError.Of(r.ErrorCode!, r.MessageEn!)),
                500 => StatusCode(StatusCodes.Status500InternalServerError,
                           ApiError.Of(r.ErrorCode!, r.MessageEn!)),
                _   => UnprocessableEntity(ApiError.Of(r.ErrorCode!, r.MessageEn!)),
            };
        }

        var resp = new CreateIqcTicketResponse
        {
            ReceiptNo = r.ReceiptNo,
            IqcInspectionId = r.IqcInspectionId,
            MaterialLotId = r.MaterialLotId,
            MaterialDescription = r.MaterialDescription,
            IfsDescription = r.IfsDescription,
            MatchStatus = r.MatchStatus,
            LotStatus = r.LotStatus,
        };
        return CreatedAtAction(nameof(Get), new { id = r.IqcInspectionId }, resp);
    }
}
