using System.Security.Claims;
using CCL.MES.Application.Services;
using CCL.MES.Shared;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.Quality;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// P12 bước 4 — hồ sơ HSF (TDS · MSDS · RoHS · REACH · ISO 9001) của một
/// <b>mã nguyên liệu</b>.
///
/// <para>ĐỌC gated <c>IqcDocRead</c>; GHI gated <c>IqcDocWrite</c> (QC trở lên
/// — người nhận lô là người cầm giấy của NCC). Mọi lệnh ghi đòi
/// <c>Idempotency-Key</c>, cùng luật với phần còn lại của module IQC.</para>
///
/// <para>Mã nguyên liệu đi qua <b>query</b> (đọc) và <b>body</b> (ghi), KHÔNG
/// phải path segment — 623/946 mã có dấu cách và 56 mã có <c>/</c>; đặt vào
/// path thì Kestrel trả 400 trước khi tới routing. Xem
/// <see cref="IqcSpecController"/>.</para>
///
/// <para><b>Controller MỎNG</b> — validate bắt buộc, chuẩn hoá tên file, ghi
/// blob, audit đều nằm ở <see cref="IqcMaterialDocumentService"/>.</para>
/// </summary>
[ApiController]
[Authorize(Policy = "IqcDocRead")]
[Route(ApiVersion.Prefix + "/iqc/documents")]
public sealed class IqcDocumentController : ControllerBase
{
    private readonly IqcMaterialDocumentService _svc;
    public IqcDocumentController(IqcMaterialDocumentService svc) => _svc = svc;

    /// <summary>Chặn trên tuyến upload. Blob store còn một lớp cap riêng
    /// (<c>MES_BLOB_MAX_BYTES</c>) — lớp này chỉ để Kestrel không phải nuốt
    /// hết một file khổng lồ rồi mới từ chối. Attribute đặt cap + 64 KB cho
    /// phần bao multipart, đúng công thức của Drawings/Specs.</summary>
    private const long MaxUploadBytes = 20L * 1024 * 1024;

    private (string Actor, string Role) Who() => (
        User.FindFirstValue(ClaimTypes.Name) ?? "anonymous",
        User.FindFirstValue(ClaimTypes.Role) ?? "");

    private bool MissingIdemKey =>
        string.IsNullOrWhiteSpace(Request.Headers["Idempotency-Key"].ToString());

    private static readonly ApiError IdemRequired =
        ApiError.Of("wo.idempotency_key_required", "Idempotency-Key header required.");

    // ── đọc ──────────────────────────────────────────────────────────────

    /// <summary>Hồ sơ của một mã. Lần đầu chạm tới mã nào thì server dựng sẵn
    /// 5 dòng mặc định cho mã đó.</summary>
    [HttpGet]
    public async Task<ActionResult<IqcDocumentListResponse>> List(
        [FromQuery] string? materialCode, [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
    {
        var rows = await _svc.ListAsync(materialCode, includeInactive, ct);
        return Ok(new IqcDocumentListResponse
        {
            MaterialCode = (materialCode ?? "").Trim(),
            Items = rows.Select(Map).ToList(),
        });
    }

    /// <summary>Tải file của một dòng. 404 khi dòng chưa đính file — client
    /// dựa vào đó để không mở cửa sổ trống.</summary>
    [HttpGet("{id:long}/file")]
    public async Task<IActionResult> Download(long id, CancellationToken ct = default)
    {
        var (content, fileName) = await _svc.OpenFileAsync(id, ct);
        if (content is null)
            return NotFound(ApiError.Of("iqc.doc_file_missing",
                "This document row has no file attached."));

        // Tên tải về là tên NGƯỜI DÙNG thấy (336T-AT1_TDS.pdf) — khoá lưu trên
        // server mang thêm sha8 và KHÔNG lộ ra client.
        return File(content, ContentTypeFor(fileName), fileName ?? $"iqc-doc-{id}.pdf");
    }

    // ── ghi ──────────────────────────────────────────────────────────────

    /// <summary>Lưu số hiệu + ngày cấp + hạn. Cả ba BẮT BUỘC (422 khi thiếu).</summary>
    [HttpPut("{id:long}"), Authorize(Policy = "IqcDocWrite")]
    public async Task<IActionResult> Save(
        long id, [FromBody] SaveIqcDocumentBody? body, CancellationToken ct = default)
    {
        if (MissingIdemKey) return BadRequest(IdemRequired);

        var (actor, role) = Who();
        var r = await _svc.SaveRowAsync(
            id, body?.DocNumber, body?.IssueDate, body?.ExpiryDate, actor, role, ct);
        return r.Ok ? Ok(new { id = r.Id }) : Problem(r);
    }

    /// <summary>Thêm một loại hồ sơ. 201 khi tạo mới; 200 khi bật lại loại đã
    /// gỡ trước đó (kèm file cũ).</summary>
    [HttpPost, Authorize(Policy = "IqcDocWrite")]
    public async Task<IActionResult> Add(
        [FromBody] AddIqcDocumentBody? body, CancellationToken ct = default)
    {
        if (MissingIdemKey) return BadRequest(IdemRequired);

        var (actor, role) = Who();
        var r = await _svc.AddRowAsync(
            body?.MaterialCode, body?.DocType, body?.LabelVi, body?.LabelEn, actor, role, ct);
        return r.Ok
            ? StatusCode(r.HttpStatus, new { id = r.Id })
            : Problem(r);
    }

    /// <summary>Gỡ một dòng — xoá MỀM, file trên server giữ nguyên.</summary>
    [HttpDelete("{id:long}"), Authorize(Policy = "IqcDocWrite")]
    public async Task<IActionResult> Remove(long id, CancellationToken ct = default)
    {
        if (MissingIdemKey) return BadRequest(IdemRequired);

        var (actor, role) = Who();
        var r = await _svc.DeactivateRowAsync(id, actor, role, ct);
        return r.Ok ? Ok(new { id = r.Id, active = false }) : Problem(r);
    }

    /// <summary>Đính PDF. Tên file được chuẩn hoá lại theo
    /// <c>&lt;mã&gt;_&lt;loại&gt;.pdf</c> bất kể NCC gửi tên gì.</summary>
    [HttpPost("{id:long}/file"), Authorize(Policy = "IqcDocWrite")]
    [RequestSizeLimit(MaxUploadBytes + 64 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes + 64 * 1024)]
    public async Task<IActionResult> Upload(
        long id, IFormFile? file, CancellationToken ct = default)
    {
        if (MissingIdemKey) return BadRequest(IdemRequired);
        if (file is null || file.Length == 0)
            return UnprocessableEntity(ApiError.Of("iqc.doc_empty_upload",
                "No file uploaded or file is empty."));

        // Kiểm lại cỡ TRONG BODY dù đã có [RequestSizeLimit]: attribute chỉ sinh
        // 413 trần của framework, không có envelope ApiError để banner đọc. Tiền
        // lệ: BackupController:130 · DrawingsApiController:125 · SpecsController:632.
        if (file.Length > MaxUploadBytes)
            return UnprocessableEntity(ApiError.Of("iqc.doc_too_large",
                $"File exceeds {MaxUploadBytes / (1024 * 1024)} MB limit."));

        var (actor, role) = Who();
        await using var stream = file.OpenReadStream();
        var r = await _svc.AttachFileAsync(
            id, stream, file.FileName, file.ContentType ?? "application/octet-stream",
            actor, role, ct);

        return r.Ok ? Ok(new { id = r.Id, fileName = r.FileName }) : Problem(r);
    }

    // ── phụ trợ ──────────────────────────────────────────────────────────

    private ObjectResult Problem(IqcDocResult r) => r.HttpStatus switch
    {
        404 => NotFound(ApiError.Of(r.ErrorCode!, r.MessageEn!)),
        409 => Conflict(ApiError.Of(r.ErrorCode!, r.MessageEn!)),
        403 => StatusCode(StatusCodes.Status403Forbidden,
                   ApiError.Of(r.ErrorCode!, r.MessageEn!)),
        400 => BadRequest(ApiError.Of(r.ErrorCode!, r.MessageEn!)),
        _   => UnprocessableEntity(ApiError.Of(r.ErrorCode!, r.MessageEn!)),
    };

    private static string ContentTypeFor(string? fileName) =>
        Path.GetExtension(fileName ?? "").ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "application/octet-stream",
        };

    private static IqcDocumentDto Map(CCL.MES.Domain.Entities.IqcMaterialDocument x) => new()
    {
        Id = x.Id,
        MaterialCode = x.MaterialCode,
        DocType = x.DocType,
        LabelVi = x.LabelVi,
        LabelEn = x.LabelEn,
        DocNumber = x.DocNumber,
        IssueDate = x.IssueDate,
        ExpiryDate = x.ExpiryDate,
        FileName = x.FileName,
        FileSizeBytes = x.FileSizeBytes,
        // KHÔNG map StorageKey ra client — khoá lưu là chi tiết của server.
        LastModifiedBy = x.UpdatedBy ?? x.CreatedBy,
        LastModifiedAt = x.UpdatedAt ?? x.CreatedAt,
        Active = x.Active,
    };
}
