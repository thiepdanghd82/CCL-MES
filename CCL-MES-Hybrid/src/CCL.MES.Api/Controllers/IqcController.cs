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

    // ── feat/iqc-module-tabs — IQC Data list (DTO) + Dashboard KPI ────────

    /// <summary>Danh sách phiếu IQC đã lưu cho tab "IQC Data" — trả DTO thuần
    /// (KHÔNG entity). Lọc <c>?group=</c> optional (Materials/Chemical/Tools/
    /// Other; giá trị lạ bị bỏ qua = tất cả) + <c>?search=</c>. Read-only ⇒
    /// QcRead đủ.</summary>
    [HttpGet("tickets")]
    public async Task<ActionResult<IqcTicketListResponse>> Tickets(
        [FromQuery] string? group = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var r = await _svc.ListTicketsAsync(group, search, page, pageSize, ct);
        return Ok(new IqcTicketListResponse
        {
            Page = r.Page,
            PageSize = r.PageSize,
            Total = r.Total,
            Items = r.Items.Select(x => new IqcTicketListItem
            {
                Id = x.Id,
                ReceiptNo = x.ReceiptNo,
                Group = x.Group,
                CodeIfs = x.CodeIfs,
                MotherCode = x.MotherCode,
                MaterialDescription = x.MaterialDescription,
                LotBatchNo = x.LotBatchNo,
                ManufactureDate = x.ManufactureDate,
                MakerName = x.MakerName,
                SupplierName = x.SupplierName,
                Inspector = x.Inspector,
                ReceivedDate = x.ReceivedDate,
                Quantity = x.Quantity,
                Uom = x.Uom,
                Result = x.Result,
            }).ToList(),
        });
    }

    /// <summary>KPI đếm thật cho tab Dashboard (tổng · theo nhóm · theo trạng
    /// thái). Read-only ⇒ QcRead đủ.</summary>
    [HttpGet("dashboard")]
    public async Task<ActionResult<IqcDashboardResponse>> Dashboard(CancellationToken ct = default)
    {
        var d = await _svc.DashboardAsync(ct);
        return Ok(new IqcDashboardResponse
        {
            Total = d.Total,
            Materials = d.Materials,
            Chemical = d.Chemical,
            Tools = d.Tools,
            Other = d.Other,
            Pending = d.Pending,
            Pass = d.Pass,
            Fail = d.Fail,
        });
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

    /// <summary>Tra vật liệu theo mã PartNo hoặc mô tả (query <c>desc</c>).
    /// Kết quả là danh sách Code IFS distinct. Read-only ⇒ QcRead đủ, không
    /// cần Idempotency-Key. Dưới ngưỡng → <c>tooShort=true</c> + rỗng.</summary>
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
                MotherCode = x.MotherCode,
                WidthMm = x.WidthMm,
                PartDescription = x.PartDescription,
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
            Group = body?.Group,
            CodeIfs = body?.CodeIfs ?? "",
            LotBatchNo = body?.LotBatchNo ?? "",
            ManufactureDate = body?.ManufactureDate,
            MakerName = body?.MakerName,
            SupplierName = body?.SupplierName,
            Quantity = body?.Quantity ?? 0,
            Uom = body?.Uom,
            SampleSize = body?.SampleSize,
            SampleSizeOverrideReason = body?.SampleSizeOverrideReason,
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
            Group = r.Group,
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

    // ── P12 bước 3 — hạng mục kiểm của phiếu ──────────────────────

    /// <summary>Bộ hạng mục kiểm đã đóng băng trên phiếu, kèm số MỤC của
    /// stepper. Read-only ⇒ QcRead đủ. 404 khi phiếu không tồn tại.</summary>
    [HttpGet("tickets/{id:long}/items")]
    public async Task<ActionResult<IqcTicketItemsResponse>> TicketItems(
        long id, CancellationToken ct = default)
    {
        var r = await _svc.GetTicketItemsAsync(id, ct);
        if (r is null) return NotFound(ApiError.Of("iqc.ticket_not_found", "IQC ticket not found."));

        return Ok(new IqcTicketItemsResponse
        {
            TicketId = r.TicketId,
            SpecNo = r.SpecNo,
            SpecApproval = r.SpecApproval,
            FromDefaultMatrix = r.FromDefaultMatrix,
            Items = r.Items.Select(x => new IqcCheckItemDto
            {
                Id = x.Id,
                ItemKey = x.ItemKey,
                Seq = x.Seq,
                Section = x.Section,
                GroupCode = x.GroupCode,
                GroupLabelVi = x.GroupLabelVi,
                GroupLabelEn = x.GroupLabelEn,
                LabelVi = x.LabelVi,
                LabelEn = x.LabelEn,
                AcceptanceVi = x.AcceptanceVi,
                AcceptanceEn = x.AcceptanceEn,
                MethodVi = x.MethodVi,
                MethodEn = x.MethodEn,
                SourceFrequency = x.SourceFrequency,
                FromDefaultMatrix = x.FromDefaultMatrix,
                AcceptanceUnspecified = x.AcceptanceUnspecified,
                Pass = x.Pass,
                MeasuredValue = x.MeasuredValue,
                DefectCode = x.DefectCode,
                Kind = x.Kind,
                MeasureCount = x.MeasureCount,
                DefectCount = x.DefectCount,
                LimitLow = x.LimitLow,
                LimitUp = x.LimitUp,
                LimitUnit = x.LimitUnit,
                LimitLabel = x.LimitLabel,
                TearIsPass = x.TearIsPass,
                TearObserved = x.TearObserved,
                Measurements = x.Measurements,
                AutoVerdict = x.AutoVerdict,
                AutoVerdictReason = x.AutoVerdictReason,
                AutoVerdictOffendingSeq = x.AutoVerdictOffendingSeq,
                OverrideReason = x.OverrideReason,
                OverriddenBy = x.OverriddenBy,
                OverriddenAt = x.OverriddenAt,
            }).ToList(),
        });
    }

    /// <summary>CHỐT phiếu. Từ chối 422 <c>iqc.items_incomplete</c> khi còn hạng
    /// mục chưa kiểm — người kiểm phải đánh giá HẾT rồi mới chốt được. Kết luận
    /// suy ra từ chính các hạng mục, không cho gõ trái với dữ liệu vừa chấm.</summary>
    [HttpPost("tickets/{id:long}/complete"), Authorize(Policy = "QcEdit")]
    public async Task<IActionResult> CompleteTicket(long id, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(Request.Headers["Idempotency-Key"].ToString()))
            return BadRequest(ApiError.Of("wo.idempotency_key_required",
                "Idempotency-Key header required."));

        var (actor, role) = Who();
        var r = await _svc.CompleteTicketAsync(id, actor, role, ct);

        if (!r.Ok)
        {
            return r.HttpStatus switch
            {
                404 => NotFound(ApiError.Of(r.ErrorCode!, r.MessageEn!)),
                403 => StatusCode(StatusCodes.Status403Forbidden,
                           ApiError.Of(r.ErrorCode!, r.MessageEn!)),
                _   => UnprocessableEntity(ApiError.Of(r.ErrorCode!, r.MessageEn!)),
            };
        }
        return Ok(new CompleteIqcResponse
        {
            Result = r.Result, Total = r.Total, Pending = r.Pending, Failed = r.Failed,
        });
    }

    /// <summary>Ghi phán định một hạng mục. QcEdit + Idempotency-Key bắt buộc
    /// (cùng luật với POST tạo phiếu — <c>IqcInspection</c> không có RowVersion
    /// nên KHÔNG có If-Match ở đây). 404 phiếu/hạng mục; 422 tiêu chuẩn còn
    /// placeholder hoặc input xấu.</summary>
    [HttpPut("tickets/{id:long}/items/{itemId:long}"), Authorize(Policy = "QcEdit")]
    public async Task<IActionResult> SetTicketItem(
        long id, long itemId, [FromBody] SetIqcItemBody? body, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(Request.Headers["Idempotency-Key"].ToString()))
            return BadRequest(ApiError.Of("wo.idempotency_key_required",
                "Idempotency-Key header required."));

        var (actor, role) = Who();
        var r = await _svc.SetItemVerdictAsync(
            id, itemId, body?.Pass, body?.MeasuredValue, body?.DefectCode, actor, role,
            defectCount: body?.DefectCount,
            measurements: body?.Measurements,
            tearObserved: body?.TearObserved,
            overrideReason: body?.OverrideReason,
            ct: ct);

        if (!r.Ok)
        {
            return r.HttpStatus switch
            {
                404 => NotFound(ApiError.Of(r.ErrorCode!, r.MessageEn!)),
                403 => StatusCode(StatusCodes.Status403Forbidden,
                           ApiError.Of(r.ErrorCode!, r.MessageEn!)),
                _   => UnprocessableEntity(ApiError.Of(r.ErrorCode!, r.MessageEn!)),
            };
        }
        return Ok(new { itemId = r.ItemId, pass = r.Pass });
    }
}
