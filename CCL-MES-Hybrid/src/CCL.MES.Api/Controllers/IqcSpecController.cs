using System.Security.Claims;
using CCL.MES.Application.Services;
using CCL.MES.Shared;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.Quality;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// P12 bước 2b — soạn <b>tiêu chuẩn kiểm NVL theo mã nguyên liệu</b>.
///
/// <para>590 mã đang kiểm theo ma trận mặc định vì chưa ai soạn spec riêng.
/// Đây là đường để Engineer soạn dần ngay trong app, thay vì chờ vòng import
/// file master kế tiếp.</para>
///
/// <para>ĐỌC gated <c>IqcSpecRead</c> (thêm QC — người kiểm cần xem tiêu chuẩn
/// của mã đang cầm); GHI gated <c>IqcSpecWrite</c> (Engineer+). Mọi lệnh ghi
/// đòi <c>Idempotency-Key</c>, cùng luật với <c>POST /api/v2/iqc</c>.</para>
///
/// <para><b>Controller MỎNG.</b> Không truy vấn DbContext, không lệnh ghi —
/// toàn bộ luật (sinh số spec cục bộ, chặn hạng mục ngoài thư viện, xoá mềm,
/// audit, RBAC) nằm ở <see cref="IqcSpecEditService"/>.</para>
/// </summary>
[ApiController]
[Authorize(Policy = "IqcSpecRead")]
[Route(ApiVersion.Prefix + "/iqc/specs")]
public sealed class IqcSpecController : ControllerBase
{
    private readonly IqcSpecEditService _svc;
    private readonly IqcStandardSpecCatalogService _catalog;
    private readonly IIqcStandardSpecFolderScanner _folderScanner;

    public IqcSpecController(
        IqcSpecEditService svc,
        IqcStandardSpecCatalogService catalog,
        IIqcStandardSpecFolderScanner folderScanner)
    {
        _svc = svc;
        _catalog = catalog;
        _folderScanner = folderScanner;
    }

    private (string Actor, string Role) Who() => (
        User.FindFirstValue(ClaimTypes.Name) ?? "anonymous",
        User.FindFirstValue(ClaimTypes.Role) ?? "");

    /// <summary>Danh mục tiêu chuẩn <c>CCL-SPEC-QC*</c> (tab Standard spec).</summary>
    [HttpGet("catalog")]
    public async Task<ActionResult<IqcStandardSpecListResponse>> Catalog(
        [FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var pageData = await _catalog.ListAsync(q, page, pageSize, ct);
        return Ok(new IqcStandardSpecListResponse
        {
            Total = pageData.Total,
            Page = pageData.Page,
            PageSize = pageData.PageSize,
            Items = pageData.Items.Select(x => new IqcStandardSpecListItem
            {
                SpecNo = x.SpecNo,
                MaterialCode = x.MaterialCode,
                MaterialCodeIfs = x.MaterialCodeIfs,
                SupplierName = x.SupplierName,
                Revision = x.Revision,
                Approval = x.Approval,
                ImportSource = x.ImportSource,
                Active = x.Active,
                ItemCount = x.ItemCount,
            }).ToList(),
        });
    }

    /// <summary>Import header từ folder file Form. Đường dẫn: body hoặc
    /// env <c>MES_IQC_STANDARD_SPEC_DIR</c>.</summary>
    [HttpPost("catalog/import"), Authorize(Policy = "IqcSpecWrite")]
    public async Task<ActionResult<IqcStandardSpecImportResponse>> ImportCatalog(
        [FromBody] IqcStandardSpecImportBody? body, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(Request.Headers["Idempotency-Key"].ToString()))
            return BadRequest(ApiError.Of("wo.idempotency_key_required",
                "Idempotency-Key header required."));

        var folder = body?.FolderPath?.Trim();
        if (string.IsNullOrWhiteSpace(folder))
            folder = Environment.GetEnvironmentVariable(IqcStandardSpecCatalogService.DefaultEnvDir);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return UnprocessableEntity(ApiError.Of("iqc.stdspec_folder_missing",
                "Set FolderPath or MES_IQC_STANDARD_SPEC_DIR to an existing folder."));

        var rows = _folderScanner.Scan(folder);
        var (actor, role) = Who();
        var r = await _catalog.ImportParsedAsync(rows, actor, role, folder, commit: true, ct);
        return Ok(new IqcStandardSpecImportResponse
        {
            FilesSeen = r.FilesSeen,
            FilesSkipped = r.FilesSkipped,
            Inserted = r.Inserted,
            Updated = r.Updated,
            AlreadyPresent = r.AlreadyPresent,
            FolderPath = r.FolderPath,
            Warnings = r.Warnings,
        });
    }

    /// <summary>Tiêu chuẩn hiện có của một mã nguyên liệu + thư viện hạng mục
    /// để chọn thêm. Mã chưa có spec vẫn trả 200 với <c>specNo=null</c> — "chưa
    /// có tiêu chuẩn riêng" là một câu trả lời, không phải lỗi.</summary>
    /// <remarks>
    /// Mã nguyên liệu đi qua <b>QUERY STRING</b>, KHÔNG phải path segment.
    /// Đo trên catalog thật: 623/946 mother code có dấu cách, 56 có <c>/</c>,
    /// 15 có <c>#</c>/<c>?</c>. Dấu cách trong request line làm Kestrel trả 400
    /// TRƯỚC khi tới routing (client chỉ thấy "http.non_success"), còn
    /// <c>%2F</c> thì ASP.NET chặn mặc định. Khoá nghiệp vụ dạng văn bản tự do
    /// không thuộc về path.
    /// </remarks>
    [HttpGet]
    public async Task<ActionResult<IqcSpecEditResponse>> Get(
        [FromQuery] string? materialCode, [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
    {
        var v = await _svc.GetByMaterialCodeAsync(materialCode, includeInactive, ct);
        if (v.ErrorCode is not null)
            return UnprocessableEntity(ApiError.Of(v.ErrorCode, v.ErrorEn ?? v.ErrorCode));
        return Ok(Map(v));
    }

    /// <summary>Thêm một dòng tiêu chuẩn. Mã chưa có spec thì server tự tạo spec
    /// cục bộ (<c>MES-SPEC-####</c>) rồi thêm vào đó. 201 khi thành công.</summary>
    [HttpPost("items"), Authorize(Policy = "IqcSpecWrite")]
    public async Task<IActionResult> AddItem(
        [FromBody] AddIqcSpecItemBody? body, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(Request.Headers["Idempotency-Key"].ToString()))
            return BadRequest(ApiError.Of("wo.idempotency_key_required",
                "Idempotency-Key header required."));

        var (actor, role) = Who();
        var r = await _svc.AddItemAsync(
            body?.MaterialCode, body?.ItemId,
            body?.AcceptanceVi, body?.AcceptanceEn,
            body?.MethodVi, body?.MethodEn, body?.SourceFrequency,
            actor, role, ct);

        return r.Ok
            ? StatusCode(StatusCodes.Status201Created,
                  new { specNo = r.SpecNo, specCreated = r.SpecCreated, itemId = r.ItemId })
            : Problem(r);
    }

    /// <summary>Xoá MỀM một dòng tiêu chuẩn (<c>Active=false</c>). Phiếu đã mở
    /// giữ bản đóng băng riêng nên không bị ảnh hưởng — chỉ lô nhập sau mới
    /// thấy khác.</summary>
    [HttpDelete("items/{itemId:long}"), Authorize(Policy = "IqcSpecWrite")]
    public async Task<IActionResult> DeactivateItem(long itemId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(Request.Headers["Idempotency-Key"].ToString()))
            return BadRequest(ApiError.Of("wo.idempotency_key_required",
                "Idempotency-Key header required."));

        var (actor, role) = Who();
        var r = await _svc.DeactivateItemAsync(itemId, actor, role, ct);
        return r.Ok ? Ok(new { itemId = r.ItemId, active = false }) : Problem(r);
    }

    /// <summary>Bật lại một dòng đã tắt.</summary>
    /// <summary>Bật/tắt CẢ MỘT BỘ tiêu chuẩn (xoá mềm). SpecNo đi trong BODY
    /// cùng lý do với mã nguyên liệu — nó có thể mang ký tự mà Kestrel từ chối
    /// trong path.</summary>
    /// <summary>Gộp mọi bộ tiêu chuẩn của một mã về MỘT bộ: chép hạng mục còn
    /// thiếu sang bộ giữ lại TRƯỚC, rồi mới tắt các bộ kia.</summary>
    [HttpPost("consolidate"), Authorize(Policy = "IqcSpecWrite")]
    public async Task<IActionResult> Consolidate(
        [FromBody] ConsolidateIqcSpecBody? body, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(Request.Headers["Idempotency-Key"].ToString()))
            return BadRequest(ApiError.Of("wo.idempotency_key_required",
                "Idempotency-Key header required."));

        var (actor, role) = Who();
        var r = await _svc.ConsolidateAsync(body?.MaterialCode, actor, role, commit: true, ct);
        if (!r.Ok)
            return StatusCode(r.HttpStatus,
                ApiError.Of(r.ErrorCode ?? "iqc.spec_consolidate_failed", "Consolidate failed."));

        return Ok(new IqcSpecConsolidateResponse
        {
            KeptSpecNo = r.KeptSpecNo,
            ItemsMerged = r.ItemsMerged,
            SpecsDeactivated = r.SpecsDeactivated,
            DeactivatedSpecNos = r.DeactivatedSpecNos.ToList(),
        });
    }

    [HttpPut("active"), Authorize(Policy = "IqcSpecWrite")]
    public async Task<IActionResult> SetSpecActive(
        [FromBody] SetIqcSpecActiveBody? body, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(Request.Headers["Idempotency-Key"].ToString()))
            return BadRequest(ApiError.Of("wo.idempotency_key_required",
                "Idempotency-Key header required."));

        var (actor, role) = Who();
        var r = await _svc.SetSpecActiveAsync(body?.SpecNo, body?.Active ?? false, actor, role, ct);
        return r.Ok ? Ok(new { specNo = r.SpecNo }) : Problem(r);
    }

    [HttpPost("items/{itemId:long}/restore"), Authorize(Policy = "IqcSpecWrite")]
    public async Task<IActionResult> ReactivateItem(long itemId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(Request.Headers["Idempotency-Key"].ToString()))
            return BadRequest(ApiError.Of("wo.idempotency_key_required",
                "Idempotency-Key header required."));

        var (actor, role) = Who();
        var r = await _svc.ReactivateItemAsync(itemId, actor, role, ct);
        return r.Ok ? Ok(new { itemId = r.ItemId, active = true }) : Problem(r);
    }

    private ObjectResult Problem(IqcSpecEditResult r) => r.HttpStatus switch
    {
        404 => NotFound(ApiError.Of(r.ErrorCode!, r.MessageEn!)),
        403 => StatusCode(StatusCodes.Status403Forbidden,
                   ApiError.Of(r.ErrorCode!, r.MessageEn!)),
        400 => BadRequest(ApiError.Of(r.ErrorCode!, r.MessageEn!)),
        _   => UnprocessableEntity(ApiError.Of(r.ErrorCode!, r.MessageEn!)),
    };

    private static IqcSpecEditResponse Map(IqcSpecEditView v) => new()
    {
        MaterialCode = v.MaterialCode,
        QueriedCode = v.QueriedCode,
        ResolvedViaMother = v.ResolvedViaMother,
        SpecNo = v.SpecNo,
        SpecActive = v.SpecActive,
        IsLocalSpec = v.IsLocalSpec,
        Specs = v.Specs.Select(x => new IqcSpecHeaderDto
        {
            SpecNo = x.SpecNo, Active = x.Active, IsLocal = x.IsLocal,
            Approval = x.Approval, ImportSource = x.ImportSource,
            SupplierName = x.SupplierName, TestMethod = x.TestMethod,
        }).ToList(),
        Items = v.Items.Select(x => new IqcSpecItemDto
        {
            Id = x.Id, SpecNo = x.SpecNo, ItemId = x.ItemId, Seq = x.Seq,
            GroupCode = x.GroupCode, GroupLabelVi = x.GroupLabelVi, GroupLabelEn = x.GroupLabelEn,
            LabelVi = x.LabelVi, LabelEn = x.LabelEn,
            AcceptanceVi = x.AcceptanceVi, AcceptanceEn = x.AcceptanceEn,
            MethodVi = x.MethodVi, MethodEn = x.MethodEn,
            SourceFrequency = x.SourceFrequency,
            Active = x.Active, FromMasterFile = x.FromMasterFile,
        }).ToList(),
        Library = v.Library.Select(x => new IqcLibraryOptionDto
        {
            ItemId = x.ItemId, GroupCode = x.GroupCode,
            GroupLabelVi = x.GroupLabelVi, GroupLabelEn = x.GroupLabelEn,
            ItemVi = x.ItemVi, ItemEn = x.ItemEn,
            DefaultAcceptanceVi = x.DefaultAcceptanceVi,
            DefaultAcceptanceEn = x.DefaultAcceptanceEn,
            DefaultMethodVi = x.DefaultMethodVi,
            DefaultMethodEn = x.DefaultMethodEn,
        }).ToList(),
        AppliesTo = v.AppliesTo.Select(x => new IqcSpecAppliesToDto
        {
            PartNo = x.PartNo, PartDescription = x.PartDescription, WidthMm = x.WidthMm,
        }).ToList(),
        AppliesToTotal = v.AppliesToTotal,
    };
}
