using System.Text.Json;
using CCL.MES.Application.Audit;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Application.Services;

/// <summary>Kết quả một thao tác trên khối NG — controller map thẳng sang HTTP.</summary>
public sealed class IqcNgResult
{
    public bool Ok { get; init; }
    public int HttpStatus { get; init; } = 200;
    public string? ErrorCode { get; init; }
    public string? MessageEn { get; init; }
    public long Id { get; init; }
    public string Status { get; init; } = nameof(IqcNgStatus.Open);

    public static IqcNgResult Fail(int status, string code, string msg) =>
        new() { Ok = false, HttpStatus = status, ErrorCode = code, MessageEn = msg };
}

/// <summary>
/// P13 bước 5 — khối NG nguyên liệu và việc đòi bồi thường nhà cung cấp.
///
/// <para><b>Vì sao đây là service RIÊNG, không nhét vào <see cref="IqcService"/>.</b>
/// Đo trên sheet <c>NG Material</c> 2026: <b>64/169 = 38%</b> vụ được phát hiện
/// ở SẢN XUẤT, khi không có phiếu IQC nào đang mở. Treo khối NG vào phiếu IQC
/// nghĩa là 38% số vụ tiếp tục sống ngoài app, đúng như một năm vừa rồi.</para>
///
/// <para>Vòng đời năm trạng thái ở <see cref="IqcNgWorkflow"/> — thuần, test
/// được mà không cần DB.</para>
/// </summary>
public class IqcNgService
{
    private readonly IMesDbContext _db;
    private readonly IAuditWriter _audit;

    public IqcNgService(IMesDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    // Cùng bộ vai với IqcService — khối NG là dữ liệu chất lượng, không phải
    // ghi chú tự do. Sản xuất phát hiện thì báo QC ghi, giống hệt cách đang làm
    // trên giấy; mở quyền ghi cho Operator là một quyết định RIÊNG, phải có
    // người ký.
    private static readonly HashSet<string> _editorRoles = new(StringComparer.OrdinalIgnoreCase)
    { "Admin", "Supervisor", "QC" };

    private static void RequireEditorRole(string actorRole)
    {
        if (!_editorRoles.Contains(actorRole ?? ""))
            throw new UnauthorizedAccessException(
                $"Role '{actorRole}' không có quyền ghi khối NG. " +
                $"Yêu cầu: {string.Join(" | ", _editorRoles)}.");
    }

    /// <summary>
    /// Ghi nhận một vụ nguyên liệu hỏng.
    ///
    /// <para>Bản ghi MỚI phải đủ ngày phát hiện · tên lỗi · ít nhất một đơn vị
    /// số lượng · nói được là vật liệu nào. Dữ liệu lịch sử có 17% dòng thiếu
    /// những thứ này; nạp lịch sử đi đường riêng và KHÔNG sửa lịch sử, nhưng
    /// bản ghi mới thì không được để tỉ lệ đó đi tiếp.</para>
    /// </summary>
    public async Task<IqcNgResult> CreateAsync(
        IqcNgRecord row, string actor, string actorRole, CancellationToken ct = default)
    {
        RequireEditorRole(actorRole);

        if (IqcNgWorkflow.ValidateNew(row) is { } err)
            return IqcNgResult.Fail(422, err, "NG record is missing required information.");

        // Trạng thái khởi tạo do SERVER đặt, client không khai: một bản ghi mới
        // sinh ra ở trạng thái "đã xử lý xong" là hồ sơ bịa.
        row.Status = IqcNgStatus.Open;
        row.Settlement = IqcClaimSettlement.None;
        row.ClaimedAt = null;
        row.SettledAt = null;
        row.CreatedBy = actor;
        row.CreatedAt = DateTime.UtcNow;

        _db.IqcNgRecords.Add(row);
        await _db.SaveChangesAsync(ct);

        await _audit.EmitAsync(
            AuditAction.IqcNgCreate, actor, actorRole,
            targetType: "IqcNgRecord", targetId: row.Id.ToString(),
            detail: JsonSerializer.Serialize(new
            {
                part_no = row.PartNo,
                supplier_lot = row.SupplierLotNo,
                stage = row.DetectedStage.ToString(),
                defect = row.DefectName,
                qty = row.NgQty, area_m2 = row.NgAreaM2, rolls = row.NgRolls,
                iqc_inspection_id = row.IqcInspectionId,
            }));

        return new IqcNgResult { Ok = true, Id = row.Id, Status = row.Status.ToString() };
    }

    /// <summary>Gửi claim cho NCC. Số tham chiếu là SỐ HỒ SƠ tự do
    /// ("CCL COMPLAINT 20260407", "CCL#260203 8D") — 95 chuỗi phân biệt trên
    /// 138 dòng, không phải một danh mục để chuẩn hoá.</summary>
    public Task<IqcNgResult> ClaimAsync(
        long id, string? claimRef, DateTime? claimedAt,
        string actor, string actorRole, CancellationToken ct = default) =>
        TransitionAsync(id, IqcNgStatus.Claimed, actor, actorRole, AuditAction.IqcNgClaim, ct,
            apply: r =>
            {
                r.ClaimedAt = claimedAt ?? DateTime.UtcNow;
                r.ClaimRef = string.IsNullOrWhiteSpace(claimRef) ? null : claimRef.Trim();
                return null;
            },
            extra: r => new { claim_ref = r.ClaimRef, claimed_at = r.ClaimedAt });

    /// <summary>NCC đã xác nhận, đang chờ xử lý (6/169 vụ đi qua bước này).</summary>
    public Task<IqcNgResult> SupplierConfirmAsync(
        long id, string? note, string actor, string actorRole, CancellationToken ct = default) =>
        TransitionAsync(id, IqcNgStatus.SupplierConfirmed, actor, actorRole, AuditAction.IqcNgClaim, ct,
            apply: r =>
            {
                r.SupplierNote = string.IsNullOrWhiteSpace(note) ? r.SupplierNote : note.Trim();
                return null;
            },
            extra: r => new { supplier_note = r.SupplierNote });

    /// <summary>NCC đã đền xong. Bắt buộc nói RÕ hình thức: "đã xử lý" mà không
    /// biết bù hàng hay trừ tiền thì kế toán không đối chiếu được.</summary>
    public Task<IqcNgResult> SettleAsync(
        long id, IqcClaimSettlement settlement, DateTime? settledAt, string? note,
        string actor, string actorRole, CancellationToken ct = default) =>
        TransitionAsync(id, IqcNgStatus.Settled, actor, actorRole, AuditAction.IqcNgSettle, ct,
            apply: r =>
            {
                if (IqcNgWorkflow.ValidateSettle(r, settlement) is { } e) return e;
                r.Settlement = settlement;
                r.SettledAt = settledAt ?? DateTime.UtcNow;
                r.SupplierNote = string.IsNullOrWhiteSpace(note) ? r.SupplierNote : note.Trim();
                return null;
            },
            extra: r => new { settlement = r.Settlement.ToString(), settled_at = r.SettledAt });

    /// <summary>Khép lại mà không đòi được. Vẫn nằm trong hồ sơ: đây chính là
    /// con số cần khi đàm phán lại hợp đồng với NCC đó.</summary>
    public Task<IqcNgResult> CloseNoClaimAsync(
        long id, string reason, string actor, string actorRole, CancellationToken ct = default) =>
        TransitionAsync(id, IqcNgStatus.ClosedNoClaim, actor, actorRole, AuditAction.IqcNgClose, ct,
            apply: r =>
            {
                // Khép một vụ mà không nói vì sao thì sáu tháng sau không ai
                // biết là NCC từ chối, hay là mình quên đòi.
                if (string.IsNullOrWhiteSpace(reason)) return "iqc.ng.close_reason_required";
                r.Remark = reason.Trim();
                return null;
            },
            extra: r => new { reason = r.Remark });

    private async Task<IqcNgResult> TransitionAsync(
        long id, IqcNgStatus to, string actor, string actorRole, string auditAction,
        CancellationToken ct, Func<IqcNgRecord, string?> apply, Func<IqcNgRecord, object> extra)
    {
        RequireEditorRole(actorRole);

        var row = await _db.IqcNgRecords.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null)
            return IqcNgResult.Fail(404, "iqc.ng.not_found", "NG record not found.");

        var from = row.Status;
        if (!IqcNgWorkflow.CanTransition(from, to))
            return IqcNgResult.Fail(422, "iqc.ng.invalid_transition",
                $"Cannot move an NG record from {from} to {to}.");

        if (apply(row) is { } err)
            return IqcNgResult.Fail(422, err, "NG record is missing required information.");

        row.Status = to;
        row.UpdatedBy = actor;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _audit.EmitAsync(
            auditAction, actor, actorRole,
            targetType: "IqcNgRecord", targetId: row.Id.ToString(),
            detail: JsonSerializer.Serialize(new
            {
                // Ghi CẢ HAI đầu: "đổi sang Settled" một mình không cho biết vụ
                // đó có đi qua bước NCC xác nhận hay không.
                from = from.ToString(),
                to = to.ToString(),
                part_no = row.PartNo,
                extra = extra(row),
            }));

        return new IqcNgResult { Ok = true, Id = row.Id, Status = row.Status.ToString() };
    }

    /// <summary>
    /// Danh sách vụ NG, mới nhất trước. Lọc theo trạng thái để trả lời câu hỏi
    /// hằng ngày của QC: "còn vụ nào chưa đòi được?".
    /// </summary>
    /// <summary>
    /// Đếm vụ theo trạng thái — trả lời câu hỏi mở-màn-hình-là-thấy: "còn bao
    /// nhiêu vụ chưa đòi được?".
    ///
    /// <para>Đếm ở DB chứ không đếm trên trang đã lấy: danh sách bị cắt ở
    /// <c>take</c>, nên đếm trên nó sẽ ra con số nhỏ hơn sự thật và không ai
    /// biết là nó sai.</para>
    /// </summary>
    public async Task<IReadOnlyDictionary<string, int>> CountByStatusAsync(
        CancellationToken ct = default)
    {
        var rows = await _db.IqcNgRecords.AsNoTracking()
            .GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, N = g.Count() })
            .ToListAsync(ct);

        // Trạng thái không có vụ nào vẫn phải xuất hiện với số 0 — chip biến mất
        // khỏi dải lọc thì người dùng tưởng app không hỗ trợ trạng thái đó.
        var all = Enum.GetValues<IqcNgStatus>().ToDictionary(s => s.ToString(), _ => 0);
        foreach (var r in rows) all[r.Status.ToString()] = r.N;
        return all;
    }

    /// <summary>
    /// Số tổng hợp cho dashboard nhỏ của tab — tính trên TOÀN BỘ bản ghi.
    ///
    /// <para>Cố ý KHÔNG nhận tham số lọc. <see cref="ListAsync"/> bị chặn 200
    /// dòng và đi theo chip trạng thái đang chọn; cộng từ danh sách đó sẽ ra số
    /// của một lát cắt chứ không phải của cả sổ, và sai đó chỉ lộ ra khi dữ
    /// liệu vượt 200 vụ — tức là muộn.</para>
    ///
    /// <para>Một lần đọc, gom trong bộ nhớ: 139 vụ hôm nay, và bảng này lớn
    /// theo số vụ NG một năm chứ không theo số phiếu, nên vẫn nhỏ trong nhiều
    /// năm tới. Đổi sang nhiều query GroupBy chỉ đổi một lần quét thành sáu.</para>
    /// </summary>
    public async Task<IqcNgSummaryCounts> SummaryAsync(int topN = 5, CancellationToken ct = default)
    {
        var rows = await _db.IqcNgRecords.AsNoTracking()
            .Select(x => new
            {
                x.Status, x.Settlement, x.DetectedStage, x.DetectedAt,
                x.SupplierName, x.DefectName, x.NgAreaM2,
            })
            .ToListAsync(ct);

        var s = new IqcNgSummaryCounts { Total = rows.Count };
        if (rows.Count == 0) return s;

        s.Open = rows.Count(x => x.Status == IqcNgStatus.Open);
        s.Claimed = rows.Count(x => x.Status == IqcNgStatus.Claimed);
        s.Settled = rows.Count(x => x.Status == IqcNgStatus.Settled);
        s.ClosedNoClaim = rows.Count(x => x.Status == IqcNgStatus.ClosedNoClaim);
        s.DetectedIqc = rows.Count(x => x.DetectedStage == IqcNgStage.Iqc);
        s.DetectedProduction = rows.Count(x => x.DetectedStage == IqcNgStage.Production);
        s.SettlementReplacement = rows.Count(x => x.Settlement == IqcClaimSettlement.Replacement);
        s.SettlementCreditNote = rows.Count(x => x.Settlement == IqcClaimSettlement.CreditNote);

        var areas = rows.Where(x => x.NgAreaM2 is not null).Select(x => x.NgAreaM2!.Value).ToList();
        s.TotalAreaM2 = areas.Count == 0 ? null : areas.Sum();

        // Xu hướng theo NĂM MỚI NHẤT CÓ VỤ, không phải năm hiện tại: sổ NG có
        // thể im lặng vài tháng, khoá cứng vào năm hệ thống sẽ ra 12 cột rỗng.
        var year = rows.Max(x => x.DetectedAt.Year);
        s.TrendYear = year;
        var byMonth = rows.Where(x => x.DetectedAt.Year == year)
                          .GroupBy(x => x.DetectedAt.Month)
                          .ToDictionary(g => g.Key, g => g.Count());
        // Luôn đủ 12 điểm: tháng không có vụ phải là 0 nhìn thấy được, không
        // phải một khoảng trống làm trục co lại.
        s.Monthly = Enumerable.Range(1, 12)
            .Select(m => new IqcNgMonthCount { Month = m, Count = byMonth.GetValueOrDefault(m) })
            .ToList();

        s.TopSuppliers = Rank(rows.Select(x => x.SupplierName), topN);
        s.TopDefects = Rank(rows.Select(x => x.DefectName), topN);
        return s;

        static List<IqcNgNameCountItem> Rank(IEnumerable<string?> src, int n) => src
            .Select(x => x?.Trim())
            .Where(x => !string.IsNullOrEmpty(x))
            .GroupBy(x => x!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Take(n)
            .Select(g => new IqcNgNameCountItem { Name = g.Key, Count = g.Count() })
            .ToList();
    }

    public async Task<IReadOnlyList<IqcNgRecord>> ListAsync(
        IqcNgStatus? status = null, string? partNo = null, int take = 200,
        CancellationToken ct = default)
    {
        var q = _db.IqcNgRecords.AsNoTracking().AsQueryable();
        if (status is { } s) q = q.Where(x => x.Status == s);
        if (!string.IsNullOrWhiteSpace(partNo))
        {
            var p = partNo.Trim();
            q = q.Where(x => x.PartNo == p);
        }
        return await q.OrderByDescending(x => x.DetectedAt).ThenByDescending(x => x.Id)
            .Take(Math.Clamp(take, 1, 1000)).ToListAsync(ct);
    }
}

/// <summary>Số tổng hợp tab NG — kiểu THUẦN của Application, controller map
/// sang DTO Shared. Application không tham chiếu Shared (giữ tầng sạch).</summary>
public sealed class IqcNgSummaryCounts
{
    public int Total { get; set; }
    public int Open { get; set; }
    public int Claimed { get; set; }
    public int Settled { get; set; }
    public int ClosedNoClaim { get; set; }
    public int DetectedIqc { get; set; }
    public int DetectedProduction { get; set; }
    public int SettlementReplacement { get; set; }
    public int SettlementCreditNote { get; set; }
    public double? TotalAreaM2 { get; set; }
    public int? TrendYear { get; set; }
    public List<IqcNgMonthCount> Monthly { get; set; } = new();
    public List<IqcNgNameCountItem> TopSuppliers { get; set; } = new();
    public List<IqcNgNameCountItem> TopDefects { get; set; } = new();
}

public sealed class IqcNgMonthCount { public int Month { get; set; } public int Count { get; set; } }
public sealed class IqcNgNameCountItem { public string Name { get; set; } = ""; public int Count { get; set; } }
