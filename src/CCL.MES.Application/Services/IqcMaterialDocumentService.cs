using System.Text.Json;
using System.Text.RegularExpressions;
using CCL.MES.Application.Audit;
using CCL.MES.Application.Storage;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Auth;
using CCL.MES.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Application.Services;

/// <summary>
/// P12 bước 4 — hồ sơ HSF (TDS · MSDS · RoHS · REACH · ISO 9001) của một
/// <b>MÃ NGUYÊN LIỆU</b>.
///
/// <para>Gắn theo mã chứ không theo phiếu (Henry chốt 2026-09-03): upload TDS
/// một lần thì mọi lô sau của mã đó đều thấy. Gắn theo phiếu sẽ bắt người kiểm
/// upload lại cùng một file cho từng lô, và sáu tháng sau không ai biết bản nào
/// đang hiệu lực.</para>
///
/// <para><b>File nằm trên SERVER, không nằm trong DB.</b> Bảng chỉ giữ khoá
/// blob; nội dung đi qua <see cref="IBlobStore"/> vào
/// <c>&lt;DataDir&gt;/blobs/IQC/Documents/&lt;mã&gt;/</c>.</para>
/// </summary>
public sealed class IqcMaterialDocumentService
{
    private readonly IMesDbContext _db;
    private readonly IBlobStore _blobs;
    private readonly IAuditWriter _audit;

    public IqcMaterialDocumentService(IMesDbContext db, IBlobStore blobs, IAuditWriter audit)
    {
        _db = db;
        _blobs = blobs;
        _audit = audit;
    }

    /// <summary>Upload/xoá hồ sơ: QC trở lên (Henry chốt) — người nhận lô là
    /// người cầm giấy của NCC, phải đưa lên được ngay lúc nhận.</summary>
    private static readonly HashSet<string> EditorRoles =
        new(StringComparer.OrdinalIgnoreCase)
        { UserRole.Admin, UserRole.Supervisor, UserRole.Engineer, UserRole.Qc };

    public static bool CanEdit(string? role) => EditorRoles.Contains(role ?? "");

    /// <summary>Thư mục gốc trên blob store. Hai tầng đúng như Henry mô tả:
    /// <c>IQC/Documents/&lt;mã nguyên liệu&gt;/</c>.</summary>
    public const string RootPrefix = "IQC/Documents";

    /// <summary>Năm loại hồ sơ dựng sẵn cho mọi mã — đúng các dòng đang hiện
    /// trên màn hình. Người dùng thêm loại khác được, nhưng năm cái này luôn có
    /// mặt để không ai quên mất một tờ.</summary>
    public static readonly IReadOnlyList<(string DocType, string Vi, string En, int Sort)> DefaultTypes =
    [
        ("TDS",     "TDS — Bảng thông số kỹ thuật", "TDS — Technical data sheet", 10),
        ("MSDS",    "MSDS",                          "MSDS",                       20),
        ("ROHS",    "RoHS",                          "RoHS",                       30),
        ("REACH",   "REACH",                         "REACH",                      40),
        ("ISO9001", "ISO 9001 — NCC",                "ISO 9001 — Supplier",        50),
    ];

    // ── đọc ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Hồ sơ của một mã. Lần đầu chạm tới mã nào thì <b>dựng sẵn 5 dòng mặc
    /// định</b> cho mã đó (cùng cách <c>PrepressBomSnapshotService</c> làm với
    /// BOM) — có dòng thật trong DB thì xoá mềm và sửa mới có chỗ bám.
    /// </summary>
    public async Task<IReadOnlyList<IqcMaterialDocument>> ListAsync(
        string? materialCode, bool includeInactive = false, CancellationToken ct = default)
    {
        var code = (materialCode ?? "").Trim();
        if (code.Length == 0) return Array.Empty<IqcMaterialDocument>();

        await MaterializeDefaultsAsync(code, ct);

        return await _db.IqcMaterialDocuments
            .Where(x => x.MaterialCode == code && (includeInactive || x.Active))
            .OrderBy(x => x.Sort).ThenBy(x => x.Id)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Đổi username → tên hiển thị cho MỘT MẺ dòng.
    ///
    /// <para>Cột <c>CreatedBy</c>/<c>UpdatedBy</c> vẫn lưu USERNAME chứ không
    /// lưu tên hiển thị, và đó là chủ ý: username là định danh ỔN ĐỊNH, còn tên
    /// hiển thị sửa được. Đóng băng tên hiển thị vào bảng thì hôm nào sửa lại
    /// tên một người (gõ sai dấu, đổi họ) là mọi dòng cũ mang tên sai vĩnh
    /// viễn. Giải ở lúc ĐỌC thì sửa một chỗ, cả lịch sử hiện đúng.</para>
    ///
    /// <para>Một truy vấn cho cả trang, không N+1. Username nào không còn trong
    /// bảng Users (tài khoản đã xoá) thì KHÔNG có trong map — caller hiện lại
    /// username thô, vì mất dấu người làm còn tệ hơn hiện một cái tên xấu.</para>
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> ResolveDisplayNamesAsync(
        IEnumerable<string?> usernames, CancellationToken ct = default)
    {
        var keys = usernames
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (keys.Count == 0)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var rows = await _db.Users.AsNoTracking()
            .Where(u => keys.Contains(u.Username))
            .Select(u => new { u.Username, u.DisplayName })
            .ToListAsync(ct);

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows)
            if (!string.IsNullOrWhiteSpace(r.DisplayName))
                map[r.Username] = r.DisplayName!;
        return map;
    }

    /// <summary>Idempotent: chỉ thêm loại nào CHƯA có. Không đụng dòng đã có,
    /// kể cả dòng người dùng đã tắt — bật lại là việc của người dùng.</summary>
    private async Task MaterializeDefaultsAsync(string code, CancellationToken ct)
    {
        var have = await _db.IqcMaterialDocuments
            .Where(x => x.MaterialCode == code)
            .Select(x => x.DocType)
            .ToListAsync(ct);
        var set = new HashSet<string>(have, StringComparer.OrdinalIgnoreCase);

        var added = 0;
        foreach (var (docType, vi, en, sort) in DefaultTypes)
        {
            if (set.Contains(docType)) continue;
            _db.IqcMaterialDocuments.Add(new IqcMaterialDocument
            {
                MaterialCode = code, DocType = docType,
                LabelVi = vi, LabelEn = en, Sort = sort,
                Active = true, CreatedBy = "system",
            });
            added++;
        }
        if (added > 0) await _db.SaveChangesAsync(ct);
    }

    // ── sửa ba trường bắt buộc ───────────────────────────────────────────

    /// <summary>
    /// Lưu số hiệu + ngày cấp + ngày hết hạn. <b>Cả ba BẮT BUỘC</b> — hồ sơ
    /// chất lượng không có số và hạn thì không chứng minh được điều gì, và
    /// cột "còn hạn / hết hạn" mất nghĩa.
    /// </summary>
    public async Task<IqcDocResult> SaveRowAsync(
        long id, string? docNumber, DateTime? issueDate, DateTime? expiryDate,
        string actor, string actorRole, CancellationToken ct = default)
    {
        if (!CanEdit(actorRole))
            return IqcDocResult.Fail(403, "iqc.doc_edit_forbidden",
                "Editing IQC documents requires QC, Engineer, Supervisor or Admin.");

        var row = await _db.IqcMaterialDocuments.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null)
            return IqcDocResult.Fail(404, "iqc.doc_not_found", "Document row not found.");

        var no = (docNumber ?? "").Trim();
        if (no.Length == 0)
            return IqcDocResult.Fail(422, "iqc.doc_number_required", "Document number is required.");
        if (no.Length > 64)
            return IqcDocResult.Fail(422, "iqc.doc_number_too_long", "Document number must be 64 characters or fewer.");
        if (issueDate is null)
            return IqcDocResult.Fail(422, "iqc.doc_issue_required", "Issue date is required.");
        if (expiryDate is null)
            return IqcDocResult.Fail(422, "iqc.doc_expiry_required", "Expiry date is required.");
        if (expiryDate <= issueDate)
            return IqcDocResult.Fail(422, "iqc.doc_expiry_before_issue",
                "Expiry date must be after the issue date.");

        row.DocNumber = no;
        row.IssueDate = issueDate;
        row.ExpiryDate = expiryDate;
        Stamp(row, actor);
        await _db.SaveChangesAsync(ct);

        await EmitAsync(AuditAction.IqcDocSet, actor, actorRole, row, new
        {
            doc_number = row.DocNumber,
            issue_date = row.IssueDate?.ToString("yyyy-MM-dd"),
            expiry_date = row.ExpiryDate?.ToString("yyyy-MM-dd"),
        });
        return new IqcDocResult { Ok = true, Id = row.Id };
    }

    // ── thêm / xoá dòng ──────────────────────────────────────────────────

    public async Task<IqcDocResult> AddRowAsync(
        string? materialCode, string? docType, string? labelVi, string? labelEn,
        string actor, string actorRole, CancellationToken ct = default)
    {
        if (!CanEdit(actorRole))
            return IqcDocResult.Fail(403, "iqc.doc_edit_forbidden",
                "Editing IQC documents requires QC, Engineer, Supervisor or Admin.");

        var code = (materialCode ?? "").Trim();
        if (code.Length is 0 or > 256)
            return IqcDocResult.Fail(422, "iqc.invalid_material_code",
                "Material code is required (1-256 characters).");

        // DocType đi vào TÊN FILE nên phải an toàn với hệ thống tệp: chỉ
        // chữ/số/gạch, viết hoa. "RoHS 2 / 3" thành "ROHS-2-3".
        var type = NormalizeDocType(docType);
        if (type.Length == 0)
            return IqcDocResult.Fail(422, "iqc.doc_type_required",
                "Document type is required (letters, digits and dashes).");
        if (type.Length > 64)
            return IqcDocResult.Fail(422, "iqc.doc_type_too_long", "Document type must be 64 characters or fewer.");

        var dup = await _db.IqcMaterialDocuments
            .FirstOrDefaultAsync(x => x.MaterialCode == code && x.DocType == type, ct);
        if (dup is not null)
        {
            // Đã có nhưng đang tắt ⇒ bật lại thay vì báo trùng: người dùng gõ
            // lại đúng loại đã gỡ nghĩa là họ muốn nó quay lại.
            if (!dup.Active)
            {
                dup.Active = true;
                Stamp(dup, actor);
                await _db.SaveChangesAsync(ct);
                await EmitAsync(AuditAction.IqcDocRestored, actor, actorRole, dup, new { });
                return new IqcDocResult { Ok = true, HttpStatus = 200, Id = dup.Id };
            }
            return IqcDocResult.Fail(409, "iqc.doc_type_duplicate",
                $"Document type '{type}' already exists for this material.");
        }

        var maxSort = await _db.IqcMaterialDocuments
            .Where(x => x.MaterialCode == code)
            .Select(x => (int?)x.Sort).MaxAsync(ct) ?? 0;

        var label = (labelVi ?? "").Trim();
        var row = new IqcMaterialDocument
        {
            MaterialCode = code, DocType = type,
            LabelVi = label.Length > 0 ? label : type,
            LabelEn = string.IsNullOrWhiteSpace(labelEn) ? null : labelEn.Trim(),
            Sort = maxSort + 10, Active = true, CreatedBy = actor,
        };
        _db.IqcMaterialDocuments.Add(row);
        await _db.SaveChangesAsync(ct);

        await EmitAsync(AuditAction.IqcDocAdded, actor, actorRole, row, new { });
        return new IqcDocResult { Ok = true, HttpStatus = 201, Id = row.Id };
    }

    /// <summary>Xoá MỀM. File trên blob store <b>giữ nguyên</b> — hồ sơ chất
    /// lượng đã từng có mặt thì không được biến mất không dấu vết; bật lại dòng
    /// là thấy lại file cũ.</summary>
    public async Task<IqcDocResult> DeactivateRowAsync(
        long id, string actor, string actorRole, CancellationToken ct = default)
    {
        if (!CanEdit(actorRole))
            return IqcDocResult.Fail(403, "iqc.doc_edit_forbidden",
                "Editing IQC documents requires QC, Engineer, Supervisor or Admin.");

        var row = await _db.IqcMaterialDocuments.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null)
            return IqcDocResult.Fail(404, "iqc.doc_not_found", "Document row not found.");
        if (!row.Active) return new IqcDocResult { Ok = true, Id = row.Id };

        row.Active = false;
        Stamp(row, actor);
        await _db.SaveChangesAsync(ct);

        await EmitAsync(AuditAction.IqcDocRemoved, actor, actorRole, row, new { });
        return new IqcDocResult { Ok = true, Id = row.Id };
    }

    // ── đính file ────────────────────────────────────────────────────────

    /// <summary>
    /// Đính PDF vào một dòng. Tên file được <b>chuẩn hoá lại</b> theo
    /// <c>&lt;mã nguyên liệu&gt;_&lt;loại&gt;.pdf</c> — vd
    /// <c>336T-AT1_TDS.pdf</c> — bất kể NCC gửi tên gì. Tên NCC đặt
    /// (<c>scan001.pdf</c>, <c>Untitled.pdf</c>) là thứ sáu tháng sau không ai
    /// tra được.
    /// </summary>
    public async Task<IqcDocResult> AttachFileAsync(
        long id, Stream content, string? originalFileName, string contentType,
        string actor, string actorRole, CancellationToken ct = default)
    {
        if (!CanEdit(actorRole))
            return IqcDocResult.Fail(403, "iqc.doc_edit_forbidden",
                "Editing IQC documents requires QC, Engineer, Supervisor or Admin.");

        var row = await _db.IqcMaterialDocuments.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null)
            return IqcDocResult.Fail(404, "iqc.doc_not_found", "Document row not found.");

        var ext = Path.GetExtension(originalFileName ?? "").TrimStart('.').ToLowerInvariant();
        if (ext.Length == 0) ext = "pdf";

        var fileName = $"{SafeSegment(row.MaterialCode)}_{row.DocType}.{ext}";
        var key = $"{RootPrefix}/{SafeSegment(row.MaterialCode)}/{fileName}";

        BlobPutResult put;
        try
        {
            put = await _blobs.PutAsync(content, key, contentType, ct);
        }
        catch (InvalidOperationException ex)
        {
            // BlobStore tự chặn đuôi lạ và file quá cỡ. Trả 422 kèm nguyên văn
            // để người dùng biết phải làm gì, thay vì 500 trống rỗng.
            return IqcDocResult.Fail(422, "iqc.doc_file_rejected", ex.Message);
        }

        row.StorageKey = put.Key;
        row.FileName = fileName;
        row.FileSha256 = put.Sha256Hex;
        row.FileSizeBytes = put.SizeBytes;
        Stamp(row, actor);
        await _db.SaveChangesAsync(ct);

        await EmitAsync(AuditAction.IqcDocFileAttached, actor, actorRole, row, new
        {
            file_name = row.FileName,
            size_bytes = row.FileSizeBytes,
            sha256 = row.FileSha256,
        });
        return new IqcDocResult { Ok = true, Id = row.Id, FileName = fileName, StorageKey = put.Key };
    }

    /// <summary>Mở file để tải về. Read-only ⇒ không cần vai editor.</summary>
    public async Task<(Stream? Content, string? FileName)> OpenFileAsync(
        long id, CancellationToken ct = default)
    {
        var row = await _db.IqcMaterialDocuments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row?.StorageKey is null) return (null, null);
        if (!await _blobs.ExistsAsync(row.StorageKey, ct)) return (null, null);
        return (await _blobs.GetAsync(row.StorageKey, ct), row.FileName);
    }

    // ── phụ trợ ──────────────────────────────────────────────────────────

    private static readonly Regex UnsafeChars = new(@"[^A-Za-z0-9._-]+", RegexOptions.Compiled);

    /// <summary>Một đoạn đường dẫn an toàn. Mã nguyên liệu thật có dấu cách,
    /// dấu <c>/</c> và ngoặc (<c>3M SP7533 (3KG / CAN)</c>) — đưa thẳng vào tên
    /// thư mục là tạo cây thư mục ngoài ý muốn hoặc lỗi ghi file.</summary>
    public static string SafeSegment(string? s)
    {
        var t = UnsafeChars.Replace((s ?? "").Trim(), "-").Trim('-');
        return t.Length == 0 ? "unknown" : t;
    }

    public static string NormalizeDocType(string? s) =>
        UnsafeChars.Replace((s ?? "").Trim().ToUpperInvariant(), "-").Trim('-');

    private static void Stamp(IqcMaterialDocument row, string actor)
    {
        row.UpdatedAt = DateTime.UtcNow;
        row.UpdatedBy = actor;   // "Last modified by" LUÔN do server đóng dấu
    }

    private Task EmitAsync(string action, string actor, string role,
        IqcMaterialDocument row, object extra)
    {
        var baseFields = new Dictionary<string, object?>
        {
            ["material_code"] = row.MaterialCode,
            ["doc_type"] = row.DocType,
        };
        foreach (var p in extra.GetType().GetProperties())
            baseFields[p.Name] = p.GetValue(extra);

        return _audit.EmitAsync(action, actor, role,
            targetType: "IqcMaterialDocument", targetId: row.Id.ToString(),
            detail: JsonSerializer.Serialize(baseFields));
    }
}

/// <summary>P12 bước 4 — kết quả một thao tác trên hồ sơ HSF.</summary>
public sealed class IqcDocResult
{
    public bool Ok { get; init; }
    public int HttpStatus { get; init; } = 200;
    public string? ErrorCode { get; init; }
    public string? MessageEn { get; init; }
    public long Id { get; init; }
    public string? FileName { get; init; }
    public string? StorageKey { get; init; }

    public static IqcDocResult Fail(int status, string code, string msg) =>
        new() { Ok = false, HttpStatus = status, ErrorCode = code, MessageEn = msg };
}
