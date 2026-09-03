using System.Text.Json;
using CCL.MES.Application.Audit;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Auth;
using CCL.MES.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Application.Services;

/// <summary>
/// P12 bước 2b — thêm / tắt tiêu chuẩn kiểm <b>theo mã nguyên liệu</b>.
///
/// <para>590 mã đang kiểm theo ma trận mặc định vì chưa ai soạn spec riêng.
/// Đây là đường để Engineer soạn dần, ngay trong app, thay vì chờ vòng
/// import file master kế tiếp.</para>
///
/// <para><b>Xoá là XOÁ MỀM</b> (<c>Active=false</c>). Phiếu đã mở giữ bản đóng
/// băng riêng nên không bị ảnh hưởng; xoá cứng thì mất luôn dấu vết vì sao một
/// hạng mục từng có mặt.</para>
///
/// <para><b>Spec người dùng tạo mang tiền tố riêng</b>
/// (<see cref="LocalSpecPrefix"/>) — file master đánh số <c>CCL-SPEC-QC###</c>,
/// đụng số là lần import sau ghi đè mất công soạn.</para>
/// </summary>
public sealed class IqcSpecEditService
{
    private readonly IMesDbContext _db;
    private readonly IAuditWriter _audit;

    public IqcSpecEditService(IMesDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    /// <summary>Ghi master data chỉ dành Engineer+ — cùng luật với
    /// <c>SettingItemAdd</c> (P10.7g). QC kiểm được nhưng không soạn tiêu chuẩn.</summary>
    private static readonly HashSet<string> EditorRoles =
        new(StringComparer.OrdinalIgnoreCase)
        { UserRole.Admin, UserRole.Supervisor, UserRole.Engineer };

    public static bool CanEdit(string? role) => EditorRoles.Contains(role ?? "");

    /// <summary>Tiền tố spec do người dùng tạo trong app. File master dùng
    /// <c>CCL-SPEC-…</c>; giữ hai không gian tên rời nhau để importer không bao
    /// giờ ghi đè spec người thật soạn, và ngược lại.</summary>
    public const string LocalSpecPrefix = "MES-SPEC-";

    // ── đọc ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Tiêu chuẩn hiện có của một mã nguyên liệu, kèm danh sách hạng mục thư
    /// viện còn có thể thêm. Read-only.
    /// </summary>
    /// <param name="includeInactive">Kèm cả dòng đã tắt (để khôi phục).</param>
    public async Task<IqcSpecEditView> GetByMaterialCodeAsync(
        string? materialCode, bool includeInactive = false, CancellationToken ct = default)
    {
        var code = (materialCode ?? "").Trim();
        var view = new IqcSpecEditView { MaterialCode = code };
        if (code.Length == 0) return view;

        var lib = await _db.IqcCheckItemLibraries.AsNoTracking()
            .Where(x => x.Active).OrderBy(x => x.Sort).ToListAsync(ct);
        view.Library = lib.Select(x => new IqcLibraryOptionRow
        {
            ItemId = x.ItemId, GroupCode = x.GroupCode,
            GroupLabelVi = x.GroupLabelVi, GroupLabelEn = x.GroupLabelEn,
            ItemVi = x.ItemVi, ItemEn = x.ItemEn,
            DefaultAcceptanceVi = x.DefaultAcceptanceVi,
            DefaultAcceptanceEn = x.DefaultAcceptanceEn,
            DefaultMethodVi = x.DefaultMethodVi,
            DefaultMethodEn = x.DefaultMethodEn,
        }).ToList();

        var spec = await FindSpecAsync(code, ct);
        if (spec is null) return view;   // 1 trong 590 mã chưa có spec

        view.SpecNo = spec.SpecNo;
        view.SpecActive = spec.Active;
        view.IsLocalSpec = spec.SpecNo.StartsWith(LocalSpecPrefix, StringComparison.OrdinalIgnoreCase);

        var byId = lib.ToDictionary(x => x.ItemId, StringComparer.OrdinalIgnoreCase);
        var rows = await _db.IqcSpecItems.AsNoTracking()
            .Where(x => x.SpecNo == spec.SpecNo && (includeInactive || x.Active))
            .ToListAsync(ct);

        view.Items = rows
            .OrderBy(x => byId.TryGetValue(x.ItemId, out var l) ? l.Sort : int.MaxValue)
            .ThenBy(x => x.Seq)
            .Select(x => new IqcSpecItemRow
            {
                Id = x.Id, ItemId = x.ItemId, Seq = x.Seq,
                GroupCode = byId.TryGetValue(x.ItemId, out var l1) ? l1.GroupCode : null,
                GroupLabelVi = byId.TryGetValue(x.ItemId, out var l2) ? l2.GroupLabelVi : null,
                GroupLabelEn = byId.TryGetValue(x.ItemId, out var l3) ? l3.GroupLabelEn : null,
                LabelVi = byId.TryGetValue(x.ItemId, out var l4) ? l4.ItemVi : x.ItemId,
                LabelEn = byId.TryGetValue(x.ItemId, out var l5) ? l5.ItemEn : null,
                AcceptanceVi = x.AcceptanceVi, AcceptanceEn = x.AcceptanceEn,
                MethodVi = x.MethodVi, MethodEn = x.MethodEn,
                SourceFrequency = x.SourceFrequency,
                Active = x.Active,
                // Dòng đến từ file master: sửa ở đây thì lần import sau ghi đè.
                FromMasterFile = !view.IsLocalSpec,
            }).ToList();

        return view;
    }

    // ── thêm ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Thêm một hạng mục tiêu chuẩn cho mã nguyên liệu. Mã chưa có spec thì
    /// <b>tạo spec cục bộ</b> rồi thêm vào đó.
    /// </summary>
    public async Task<IqcSpecEditResult> AddItemAsync(
        string? materialCode, string? itemId,
        string? acceptanceVi, string? acceptanceEn,
        string? methodVi, string? methodEn, string? sourceFrequency,
        string actor, string actorRole, CancellationToken ct = default)
    {
        if (!CanEdit(actorRole))
            return IqcSpecEditResult.Fail(403, "iqc.spec_edit_forbidden",
                "Editing IQC standards requires Engineer, Supervisor or Admin.");

        var code = (materialCode ?? "").Trim();
        if (code.Length is 0 or > 256)
            return IqcSpecEditResult.Fail(422, "iqc.invalid_material_code",
                "Material code is required (1-256 characters).");

        var key = (itemId ?? "").Trim();
        if (key.Length == 0)
            return IqcSpecEditResult.Fail(422, "iqc.invalid_item_id",
                "Check item is required.");

        // Hạng mục phải có trong thư viện 21 mục. Cho gõ tự do thì sáu tháng
        // sau có 40 biến thể của cùng một phép đo và không ai tổng hợp được.
        var lib = await _db.IqcCheckItemLibraries.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Active && x.ItemId == key, ct);
        if (lib is null)
            return IqcSpecEditResult.Fail(422, "iqc.item_not_in_library",
                $"Check item '{key}' is not in the IQC check-item library.");

        if (acceptanceVi is { Length: > 1024 } || acceptanceEn is { Length: > 1024 })
            return IqcSpecEditResult.Fail(422, "iqc.invalid_acceptance",
                "Acceptance criteria must be 1024 characters or fewer.");
        if (methodVi is { Length: > 512 } || methodEn is { Length: > 512 })
            return IqcSpecEditResult.Fail(422, "iqc.invalid_method",
                "Method must be 512 characters or fewer.");
        if (sourceFrequency is { Length: > 256 })
            return IqcSpecEditResult.Fail(422, "iqc.invalid_frequency",
                "Frequency must be 256 characters or fewer.");

        var spec = await FindSpecAsync(code, ct);
        var specCreated = false;
        if (spec is null)
        {
            spec = new IqcMaterialSpec
            {
                SpecNo = await NextLocalSpecNoAsync(ct),
                MaterialCode = code,
                Active = true,
                CreatedBy = actor,
            };
            _db.IqcMaterialSpecs.Add(spec);
            specCreated = true;
        }
        else if (!spec.Active)
        {
            // Mã từng có spec rồi bị tắt: thêm hạng mục nghĩa là dùng lại.
            spec.Active = true;
            spec.UpdatedAt = DateTime.UtcNow;
            spec.UpdatedBy = actor;
        }

        // Seq = số thứ tự trong CÙNG mã hạng mục. 12 cặp trong file master có
        // nhiều tiêu chí cùng mã, nên khoá tự nhiên phải ba phần.
        var maxSeq = await _db.IqcSpecItems
            .Where(x => x.SpecNo == spec.SpecNo && x.ItemId == key)
            .Select(x => (int?)x.Seq)
            .MaxAsync(ct) ?? 0;

        var row = new IqcSpecItem
        {
            SpecNo = spec.SpecNo, ItemId = key, Seq = maxSeq + 1,
            AcceptanceVi = Norm(acceptanceVi), AcceptanceEn = Norm(acceptanceEn),
            MethodVi = Norm(methodVi), MethodEn = Norm(methodEn),
            SourceFrequency = Norm(sourceFrequency),
            Sort = lib.Sort, Active = true, CreatedBy = actor,
        };
        _db.IqcSpecItems.Add(row);
        await _db.SaveChangesAsync(ct);

        if (specCreated)
        {
            await _audit.EmitAsync(
                AuditAction.IqcSpecCreated, actor, actorRole,
                targetType: "IqcMaterialSpec", targetId: spec.Id.ToString(),
                detail: JsonSerializer.Serialize(new
                {
                    spec_no = spec.SpecNo, material_code = spec.MaterialCode,
                }));
        }

        await _audit.EmitAsync(
            AuditAction.IqcSpecItemAdded, actor, actorRole,
            targetType: "IqcSpecItem", targetId: row.Id.ToString(),
            detail: JsonSerializer.Serialize(new
            {
                spec_no = row.SpecNo, material_code = code,
                item_id = row.ItemId, seq = row.Seq,
                acceptance_vi = row.AcceptanceVi, method_vi = row.MethodVi,
                source_frequency = row.SourceFrequency,
            }));

        return new IqcSpecEditResult
        {
            Ok = true, HttpStatus = 201,
            SpecNo = spec.SpecNo, SpecCreated = specCreated, ItemId = row.Id,
        };
    }

    // ── tắt / bật lại ────────────────────────────────────────────────────

    /// <summary>Tắt một hạng mục (xoá mềm). Phiếu đã mở giữ bản đóng băng riêng
    /// nên KHÔNG bị ảnh hưởng — chỉ lô nhập sau mới thấy khác.</summary>
    public Task<IqcSpecEditResult> DeactivateItemAsync(
        long itemDbId, string actor, string actorRole, CancellationToken ct = default)
        => SetItemActiveAsync(itemDbId, active: false, actor, actorRole, ct);

    /// <summary>Bật lại một hạng mục đã tắt (bấm nhầm thì gỡ được).</summary>
    public Task<IqcSpecEditResult> ReactivateItemAsync(
        long itemDbId, string actor, string actorRole, CancellationToken ct = default)
        => SetItemActiveAsync(itemDbId, active: true, actor, actorRole, ct);

    private async Task<IqcSpecEditResult> SetItemActiveAsync(
        long itemDbId, bool active, string actor, string actorRole, CancellationToken ct)
    {
        if (!CanEdit(actorRole))
            return IqcSpecEditResult.Fail(403, "iqc.spec_edit_forbidden",
                "Editing IQC standards requires Engineer, Supervisor or Admin.");

        var row = await _db.IqcSpecItems.FirstOrDefaultAsync(x => x.Id == itemDbId, ct);
        if (row is null)
            return IqcSpecEditResult.Fail(404, "iqc.spec_item_not_found",
                "Standard row not found.");

        if (row.Active == active)
        {
            // Đã ở đúng trạng thái — trả OK chứ không báo lỗi: bấm hai lần
            // (mạng chậm, chạm đúp) không phải là sự cố.
            return new IqcSpecEditResult { Ok = true, SpecNo = row.SpecNo, ItemId = row.Id };
        }

        row.Active = active;
        row.UpdatedAt = DateTime.UtcNow;
        row.UpdatedBy = actor;
        await _db.SaveChangesAsync(ct);

        var materialCode = await _db.IqcMaterialSpecs.AsNoTracking()
            .Where(x => x.SpecNo == row.SpecNo)
            .Select(x => x.MaterialCode)
            .FirstOrDefaultAsync(ct);

        await _audit.EmitAsync(
            active ? AuditAction.IqcSpecItemReactivated : AuditAction.IqcSpecItemDeactivated,
            actor, actorRole,
            targetType: "IqcSpecItem", targetId: row.Id.ToString(),
            detail: JsonSerializer.Serialize(new
            {
                spec_no = row.SpecNo, material_code = materialCode,
                item_id = row.ItemId, seq = row.Seq,
            }));

        return new IqcSpecEditResult { Ok = true, SpecNo = row.SpecNo, ItemId = row.Id };
    }

    // ── phụ trợ ──────────────────────────────────────────────────────────

    /// <summary>Spec của một mã nguyên liệu. Khớp NOCASE + trim ở tầng bộ nhớ
    /// vì cột không mang collation NOCASE (cùng luật với CreateTicketAsync).</summary>
    private async Task<IqcMaterialSpec?> FindSpecAsync(string code, CancellationToken ct)
    {
        var upper = code.ToUpperInvariant();
        var candidates = await _db.IqcMaterialSpecs
            .Where(x => x.MaterialCode.ToUpper() == upper)
            .ToListAsync(ct);

        // Ưu tiên spec còn bật; mã có cả bản tắt lẫn bản bật thì bản bật thắng.
        return candidates.FirstOrDefault(x => x.Active) ?? candidates.FirstOrDefault();
    }

    /// <summary>Số spec kế tiếp trong không gian tên CỤC BỘ.</summary>
    private async Task<string> NextLocalSpecNoAsync(CancellationToken ct)
    {
        var existing = await _db.IqcMaterialSpecs
            .Where(x => x.SpecNo.StartsWith(LocalSpecPrefix))
            .Select(x => x.SpecNo)
            .ToListAsync(ct);

        var max = 0;
        foreach (var s in existing)
        {
            var tail = s[LocalSpecPrefix.Length..];
            if (int.TryParse(tail, out var n) && n > max) max = n;
        }
        return $"{LocalSpecPrefix}{max + 1:0000}";
    }

    private static string? Norm(string? v) =>
        string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}

// ── kiểu trả về (Application-layer) ──────────────────────────────────────

/// <summary>Màn soạn tiêu chuẩn của một mã nguyên liệu.</summary>
public sealed class IqcSpecEditView
{
    public string MaterialCode { get; set; } = "";

    /// <summary><c>null</c> = mã này chưa có spec (1 trong 590) — đang kiểm theo
    /// ma trận mặc định.</summary>
    public string? SpecNo { get; set; }
    public bool SpecActive { get; set; }

    /// <summary>Spec do người dùng tạo trong app, không phải từ file master.</summary>
    public bool IsLocalSpec { get; set; }

    public List<IqcSpecItemRow> Items { get; set; } = new();

    /// <summary>21 hạng mục thư viện để chọn khi thêm dòng.</summary>
    public List<IqcLibraryOptionRow> Library { get; set; } = new();
}

/// <summary>Một dòng tiêu chuẩn của mã nguyên liệu.</summary>
public sealed class IqcSpecItemRow
{
    public long Id { get; init; }
    public string ItemId { get; init; } = "";
    public int Seq { get; init; }
    public string? GroupCode { get; init; }
    public string? GroupLabelVi { get; init; }
    public string? GroupLabelEn { get; init; }
    public string? LabelVi { get; init; }
    public string? LabelEn { get; init; }
    public string? AcceptanceVi { get; init; }
    public string? AcceptanceEn { get; init; }
    public string? MethodVi { get; init; }
    public string? MethodEn { get; init; }
    public string? SourceFrequency { get; init; }
    public bool Active { get; init; }
    public bool FromMasterFile { get; init; }
}

/// <summary>Một hạng mục thư viện để chọn khi thêm dòng.</summary>
public sealed class IqcLibraryOptionRow
{
    public string ItemId { get; init; } = "";
    public string? GroupCode { get; init; }
    public string? GroupLabelVi { get; init; }
    public string? GroupLabelEn { get; init; }
    public string? ItemVi { get; init; }
    public string? ItemEn { get; init; }
    public string? DefaultAcceptanceVi { get; init; }
    public string? DefaultAcceptanceEn { get; init; }
    public string? DefaultMethodVi { get; init; }
    public string? DefaultMethodEn { get; init; }
}

/// <summary>Kết quả một thao tác soạn tiêu chuẩn.</summary>
public sealed class IqcSpecEditResult
{
    public bool Ok { get; init; }
    public int HttpStatus { get; init; } = 200;
    public string? ErrorCode { get; init; }
    public string? MessageEn { get; init; }
    public string? SpecNo { get; init; }
    public bool SpecCreated { get; init; }
    public long ItemId { get; init; }

    public static IqcSpecEditResult Fail(int status, string code, string msg) =>
        new() { Ok = false, HttpStatus = status, ErrorCode = code, MessageEn = msg };
}
