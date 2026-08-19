using System.Text.Json;
using CCL.MES.Application.Audit;
using CCL.MES.Domain;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Entities;
using CCL.MES.Domain.StateMachine;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Application.Services;

/// <summary>
/// Phase 6 Bước 7 — IQC (Incoming Quality Check) service.
///
/// Khác QcService:
///   - Không nhận <c>WorkOrderId</c> (IQC chạy pre-WO trên raw mat batch).
///   - <c>ApproveAsync</c> KHÔNG cascade <c>WO.Status=OnHold</c> khi Fail
///     vì chưa có WO. Audit row vẫn ghi, operator quyết action quarantine
///     ngoài app (Q4 — defer auto-quarantine sang Phase 7).
///   - <c>CreateAsync</c> resolve <c>PartNo → RawMaterialId</c> nếu catalog
///     có match; nếu không, để FK null + giữ PartNo text (hybrid Q1).
/// </summary>
public class IqcService
{
    private readonly IMesDbContext _db;
    private readonly IAuditWriter _audit;
    private readonly MaterialLotScanService _lots;

    public IqcService(IMesDbContext db, IAuditWriter audit, MaterialLotScanService lots)
    {
        _db = db;
        _audit = audit;
        _lots = lots;
    }

    // Phase 8 security hardening (docs/PERMISSION_MATRIX.md §6.3). Mirrors
    // the pattern in SpecQcCaptureService / SpecQcWindowService — server
    // re-validates the actor role even though the only known caller
    // (Pages/QcQa/Iqc.razor) already gates UI via
    // <AuthorizeView Roles="Admin,Supervisor,QC"> + a client-side
    // RoleCanMutate(role) helper. Defense in depth: if a future PR adds
    // an HTTP controller for IQC, it inherits the same role gate without
    // a separate code path. Throws UnauthorizedAccessException on bad
    // role; existing callers (Iqc.razor) already wrap mutations in
    // try/catch + show an error banner.
    private static readonly HashSet<string> _editorRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Admin",
        "Supervisor",
        "QC",
    };

    private static void RequireEditorRole(string actorRole)
    {
        if (!_editorRoles.Contains(actorRole ?? ""))
        {
            throw new UnauthorizedAccessException(
                $"Role '{actorRole}' không có quyền IQC mutation. " +
                $"Yêu cầu: {string.Join(" | ", _editorRoles)}.");
        }
    }

    public async Task<IqcInspection> CreateAsync(CreateIqcRequest r, string actor, string actorRole)
    {
        RequireEditorRole(actorRole);
        // Hybrid FK: tra catalog theo PartNo, nếu match thì set hard FK.
        // Snapshot SupplierName: nếu request không nêu, lấy từ RawMaterial.
        long? rawMaterialId = null;
        string? supplierSnapshot = r.SupplierName;
        if (!string.IsNullOrWhiteSpace(r.PartNo))
        {
            var rm = await _db.RawMaterials.FirstOrDefaultAsync(x => x.PartNo == r.PartNo);
            if (rm is not null)
            {
                rawMaterialId = rm.Id;
                if (string.IsNullOrWhiteSpace(supplierSnapshot))
                    supplierSnapshot = rm.SupplierName;
            }
        }

        var insp = new IqcInspection
        {
            RawMaterialId = rawMaterialId,
            PartNo = r.PartNo,
            BatchNumber = r.BatchNumber,
            LotNumber = r.LotNumber,
            ReceivedDate = r.ReceivedDate,
            SupplierName = supplierSnapshot,
            Quantity = r.Quantity,
            UomQty = r.UomQty,
            InspectorId = r.InspectorId,
            SampleSize = r.SampleSize,
            Result = QcResult.Pending,
        };
        foreach (var d in r.Details)
        {
            insp.Details.Add(new IqcResultDetail
            {
                ItemName = d.ItemName,
                MeasuredValue = d.MeasuredValue,
                Pass = d.Pass,
                DefectCode = d.DefectCode,
                Qty = d.Qty,
            });
        }
        _db.IqcInspections.Add(insp);
        await _db.SaveChangesAsync();

        // Detail JSON KHÔNG carry PII. PartNo / batch / qty là operational
        // metadata; InspectorId là username chứ không phải PII.
        await _audit.EmitAsync(
            AuditAction.IqcCreate, actor, actorRole,
            targetType: "IqcInspection", targetId: insp.Id.ToString(),
            detail: JsonSerializer.Serialize(new
            {
                part_no = r.PartNo,
                batch = r.BatchNumber,
                qty = r.Quantity,
                sample_size = r.SampleSize,
                detail_count = r.Details.Count,
                raw_material_id = rawMaterialId,
            }));
        return insp;
    }

    // ── feat/iqc-ticket — tạo phiếu IQC + mở lô Quarantine (1 giao dịch) ──

    /// <summary>
    /// Tạo một PHIẾU IQC hoàn chỉnh cho lô nguyên liệu về kho, đồng thời mở
    /// một <see cref="MaterialLot"/> ở trạng thái Quarantine — TRONG CÙNG MỘT
    /// giao dịch, chống hồ sơ nửa vời (phiếu có mà lô không, hoặc ngược lại).
    ///
    /// <para><b>ReceiptNo do server sinh</b> theo <c>IQC-&lt;yyMMdd&gt;-&lt;STT4&gt;</c>,
    /// STT = MAX đuôi trong ngày +1. Trùng (đua tạo phiếu) được unique index
    /// bắt → retry bounded. Client KHÔNG khai ReceiptNo/Inspector/desc.</para>
    ///
    /// <para><b>Match Code IFS</b> query-level NOCASE (quyết định #3 —
    /// <c>ToUpper()</c>, KHÔNG đổi collation cột <c>RawMaterials.PartNo</c>):
    /// đúng 1 → set FK + cache desc; &gt;1 (92 mã trùng) → <c>ambiguous</c>,
    /// KHÔNG auto-fill mù; 0 → <c>unmatched</c>, RawMaterialId=null, VẪN LƯU
    /// (quyết định #2, không chặn 422).</para>
    ///
    /// <para><b>PA-A cache</b> (quyết định #1): chụp <c>PartDescription</c> vào
    /// phiếu = bằng chứng bất biến, catalog rename về sau không đổi phiếu.</para>
    /// </summary>
    public async Task<CreateIqcTicketResult> CreateTicketAsync(
        CreateIqcTicketRequest r, string actor, string actorRole,
        CancellationToken ct = default)
    {
        RequireEditorRole(actorRole);

        var codeIfs = MaterialLotStatusPolicy.Normalize(r.CodeIfs);
        var lotBatchNo = MaterialLotStatusPolicy.Normalize(r.LotBatchNo);
        if (codeIfs.Length is 0 or > 64)
            return CreateIqcTicketResult.Fail(422, "iqc.invalid_code_ifs",
                "Code IFS is required (1-64 characters).");
        if (lotBatchNo.Length is 0 or > 64)
            return CreateIqcTicketResult.Fail(422, "iqc.invalid_lot_batch_no",
                "Lot/Batch No is required (1-64 characters).");
        if (double.IsNaN(r.Quantity) || double.IsInfinity(r.Quantity) || r.Quantity <= 0)
            return CreateIqcTicketResult.Fail(422, "iqc.invalid_quantity",
                "Quantity must be greater than zero.");

        // Quyết định #3 — match NOCASE ở tầng query, KHÔNG đổi collation cột.
        // Không dùng FirstOrDefault: cần đếm để phân biệt matched/ambiguous.
        var upper = codeIfs.ToUpperInvariant();
        var matches = await _db.RawMaterials.AsNoTracking()
            .Where(x => x.PartNo.ToUpper() == upper)
            .Select(x => new { x.Id, x.PartNo, x.PartDescription, x.SupplierName })
            .Take(2).ToListAsync(ct);

        long? rawMaterialId = null;
        string? materialDescription = null;   // snapshot mô tả nội bộ (PartDescription)
        string? ifsDescription = null;         // snapshot mô tả IFS (cùng nguồn hiện tại)
        string? partNoForLot = codeIfs;        // lô unresolved dùng chính chuỗi Code IFS
        string? supplierSnapshot = string.IsNullOrWhiteSpace(r.SupplierName) ? null : r.SupplierName.Trim();
        string matchStatus;

        if (matches.Count == 1)
        {
            var m = matches[0];
            rawMaterialId = m.Id;
            partNoForLot = m.PartNo;                       // dùng PartNo chuẩn của catalog
            materialDescription = m.PartDescription;       // PA-A cache (quyết định #1)
            ifsDescription = m.PartDescription;
            if (supplierSnapshot is null && !string.IsNullOrWhiteSpace(m.SupplierName))
                supplierSnapshot = m.SupplierName;
            matchStatus = "matched";
        }
        else if (matches.Count > 1)
        {
            // >1 (92 mã trùng): KHÔNG auto-fill mù — để operator xử lý ngoài.
            matchStatus = "ambiguous";
        }
        else
        {
            // 0 match — quyết định #2: cho lưu tạm, giữ text Code IFS.
            matchStatus = "unmatched";
        }

        // Giao dịch tường minh: cast sang DbContext (tiền lệ MaterialLotScanService
        // SetLotFk/ConflictAsync). Phiếu + lô cùng commit; lô lỗi → rollback phiếu.
        if (_db is not DbContext ctx)
            return CreateIqcTicketResult.Fail(500, "iqc.no_transaction",
                "Underlying context does not support explicit transactions.");

        await using var tx = await ctx.Database.BeginTransactionAsync(ct);

        // Sinh ReceiptNo + retry khi trúng unique (đua tạo phiếu cùng ngày).
        IqcInspection? insp = null;
        const int maxRetry = 5;
        for (var attempt = 0; attempt < maxRetry; attempt++)
        {
            // ReceiptNo dùng NGÀY NHẬN (hôm nay), không phải ngày sản xuất.
            var receiptNo = await NextReceiptNoAsync(DateTime.UtcNow, ct);
            insp = new IqcInspection
            {
                ReceiptNo = receiptNo,
                CodeIfs = codeIfs,
                RawMaterialId = rawMaterialId,
                PartNo = partNoForLot ?? codeIfs,
                BatchNumber = lotBatchNo,
                LotNumber = lotBatchNo,
                ReceivedDate = DateTime.UtcNow,
                ManufactureDate = r.ManufactureDate,
                MakerName = string.IsNullOrWhiteSpace(r.MakerName) ? null : r.MakerName.Trim(),
                SupplierName = supplierSnapshot,
                MaterialDescription = materialDescription,
                IfsDescription = ifsDescription,
                Quantity = r.Quantity,
                UomQty = string.IsNullOrWhiteSpace(r.Uom) ? null : r.Uom!.Trim(),
                InspectorId = actor,             // server-stamp, client KHÔNG khai
                SampleSize = r.SampleSize ?? 0,
                Result = QcResult.Pending,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = actor,
            };
            _db.IqcInspections.Add(insp);
            try
            {
                await _db.SaveChangesAsync(ct);
                break;   // thành công — có Id để nối lô
            }
            catch (DbUpdateException)
            {
                // Trúng IX_IqcInspections_ReceiptNo_Unique — STT bị đua. Gỡ entity
                // hỏng khỏi tracker rồi thử số kế tiếp.
                ctx.Entry(insp).State = EntityState.Detached;
                insp = null;
                if (attempt == maxRetry - 1)
                {
                    await tx.RollbackAsync(ct);
                    return CreateIqcTicketResult.Fail(409, "iqc.receipt_no_conflict",
                        "Could not allocate a unique receipt number; retry.");
                }
            }
        }

        if (insp is null)
        {
            await tx.RollbackAsync(ct);
            return CreateIqcTicketResult.Fail(409, "iqc.receipt_no_conflict",
                "Could not allocate a unique receipt number; retry.");
        }

        // Mở lô Quarantine nối vào phiếu (A1: CreateLotAsync khởi tạo
        // Status=Quarantine, resolve RawMaterialId từ PartNo). Cùng scope DbContext
        // ⇒ nằm trong transaction này; CreateLotAsync tự SaveChanges nội bộ.
        var lotOutcome = await _lots.CreateLotAsync(
            rawLotNo: lotBatchNo,
            rawPartNo: partNoForLot ?? codeIfs,
            qtyReceived: r.Quantity,
            uom: r.Uom,
            expiryAt: r.ExpiryAt,
            iqcInspectionId: insp.Id,
            supplierName: supplierSnapshot,
            supplierLotNo: null,
            actor: actor,
            role: actorRole,
            ct: ct);

        if (!lotOutcome.Ok)
        {
            // Lô lỗi (vd trùng lô) → rollback CẢ phiếu. Không để hồ sơ nửa vời.
            await tx.RollbackAsync(ct);
            return CreateIqcTicketResult.Fail(
                lotOutcome.HttpStatus, lotOutcome.ErrorCode ?? "iqc.lot_create_failed",
                lotOutcome.MessageEn ?? "Failed to open material lot for the ticket.");
        }

        await tx.CommitAsync(ct);

        // Audit — tái dùng IqcCreate, detail thêm receipt_no/code_ifs/match_status/
        // material_lot_id/raw_material_id. KHÔNG rò hash/token/cookie/PII.
        await _audit.EmitAsync(
            AuditAction.IqcCreate, actor, actorRole,
            targetType: "IqcInspection", targetId: insp.Id.ToString(),
            detail: JsonSerializer.Serialize(new
            {
                receipt_no = insp.ReceiptNo,
                code_ifs = insp.CodeIfs,
                match_status = matchStatus,
                material_lot_id = lotOutcome.MaterialLotId,
                raw_material_id = rawMaterialId,
                lot_batch_no = lotBatchNo,
                qty = r.Quantity,
            }));

        return new CreateIqcTicketResult
        {
            Ok = true,
            HttpStatus = 201,
            ReceiptNo = insp.ReceiptNo!,
            IqcInspectionId = insp.Id,
            MaterialLotId = lotOutcome.MaterialLotId,
            MaterialDescription = materialDescription,
            IfsDescription = ifsDescription,
            MatchStatus = matchStatus,
            LotStatus = lotOutcome.LotStatus,
        };
    }

    /// <summary>Sinh số phiếu kế tiếp <c>IQC-&lt;yyMMdd&gt;-&lt;STT4&gt;</c>.
    /// STT = MAX đuôi số trong cùng ngày +1 (đọc các ReceiptNo cùng prefix,
    /// tách 4 số cuối). Đua được unique index chặn ở tầng ghi + retry.</summary>
    private async Task<string> NextReceiptNoAsync(DateTime whenUtc, CancellationToken ct)
    {
        var prefix = $"IQC-{whenUtc:yyMMdd}-";
        var existing = await _db.IqcInspections.AsNoTracking()
            .Where(x => x.ReceiptNo != null && x.ReceiptNo.StartsWith(prefix))
            .Select(x => x.ReceiptNo!)
            .ToListAsync(ct);

        var maxSeq = 0;
        foreach (var rn in existing)
        {
            var tail = rn.Length >= 4 ? rn[^4..] : rn;
            if (int.TryParse(tail, out var n) && n > maxSeq) maxSeq = n;
        }
        return $"{prefix}{(maxSeq + 1):D4}";
    }

    /// <summary>Resolve Code IFS → (matchStatus, mô tả) để UI auto-fill trước
    /// submit. Cùng luật NOCASE query-level như <see cref="CreateTicketAsync"/>
    /// — KHÔNG ghi gì, thuần đọc.</summary>
    public async Task<ResolveIqcCodeResult> ResolveCodeAsync(string? codeIfs, CancellationToken ct = default)
    {
        var code = MaterialLotStatusPolicy.Normalize(codeIfs);
        if (code.Length == 0)
            return new ResolveIqcCodeResult { MatchStatus = "unmatched" };

        var upper = code.ToUpperInvariant();
        var matches = await _db.RawMaterials.AsNoTracking()
            .Where(x => x.PartNo.ToUpper() == upper)
            .Select(x => new { x.PartNo, x.PartDescription, x.SupplierName })
            .Take(2).ToListAsync(ct);

        if (matches.Count == 1)
            return new ResolveIqcCodeResult
            {
                MatchStatus = "matched",
                PartNo = matches[0].PartNo,
                MaterialDescription = matches[0].PartDescription,
                IfsDescription = matches[0].PartDescription,
                SupplierName = matches[0].SupplierName,
            };
        return new ResolveIqcCodeResult { MatchStatus = matches.Count > 1 ? "ambiguous" : "unmatched" };
    }

    /// <summary>Gợi ý nhà sản xuất/nhà cung cấp: distinct MakerName của phiếu IQC
    /// + SupplierName của catalog, khớp <c>Like</c>, top 20. AsNoTracking.</summary>
    public async Task<List<string>> MakerSuggestionsAsync(string? search, CancellationToken ct = default)
    {
        var s = (search ?? "").Trim();
        var makers = _db.IqcInspections.AsNoTracking()
            .Where(x => x.MakerName != null && x.MakerName != "")
            .Select(x => x.MakerName!);
        var suppliers = _db.RawMaterials.AsNoTracking()
            .Where(x => x.SupplierName != null && x.SupplierName != "")
            .Select(x => x.SupplierName!);
        if (s.Length > 0)
        {
            makers = makers.Where(m => EF.Functions.Like(m, $"%{s}%"));
            suppliers = suppliers.Where(m => EF.Functions.Like(m, $"%{s}%"));
        }
        var a = await makers.Distinct().Take(20).ToListAsync(ct);
        var b = await suppliers.Distinct().Take(20).ToListAsync(ct);
        return a.Concat(b)
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Take(20).ToList();
    }

    /// <summary>
    /// Phê duyệt IQC. KHÔNG cascade WO (IQC là pre-WO; raw mat fail thì
    /// quarantine ngoài app theo Q4).
    /// </summary>
    public async Task<IqcInspection?> ApproveAsync(long inspectionId, bool pass, string actor, string actorRole)
    {
        RequireEditorRole(actorRole);
        var insp = await _db.IqcInspections
            .Include(i => i.Details)
            .FirstOrDefaultAsync(i => i.Id == inspectionId);
        if (insp is null) return null;
        if (insp.Result != QcResult.Pending) return insp;  // idempotent — đã approved

        insp.Result = pass ? QcResult.Pass : QcResult.Fail;
        insp.ApprovedBy = actor;
        insp.ApprovedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _audit.EmitAsync(
            AuditAction.IqcApprove, actor, actorRole,
            targetType: "IqcInspection", targetId: insp.Id.ToString(),
            detail: JsonSerializer.Serialize(new
            {
                part_no = insp.PartNo,
                batch = insp.BatchNumber,
                result = insp.Result.ToString(),
            }));
        return insp;
    }

    /// <summary>
    /// List paginated theo (search, status, date range). Search match
    /// PartNo / BatchNumber / SupplierName qua <c>EF.Functions.Like</c>
    /// (provider-agnostic — Bước 6.5 đã verify hoạt động đúng cả SQLite +
    /// SQL Server).
    /// </summary>
    public async Task<PagedResult<IqcInspection>> ListAsync(
        string? search, QcResult? status, DateTime? from, DateTime? to,
        int page, int pageSize)
    {
        var q = _db.IqcInspections.AsNoTracking()
            .OrderByDescending(x => x.ReceivedDate)
            .ThenByDescending(x => x.Id)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(x =>
                EF.Functions.Like(x.PartNo, $"%{s}%")
                || EF.Functions.Like(x.BatchNumber, $"%{s}%")
                || (x.SupplierName != null && EF.Functions.Like(x.SupplierName, $"%{s}%")));
        }
        if (status.HasValue)
            q = q.Where(x => x.Result == status.Value);
        if (from.HasValue)
            q = q.Where(x => x.ReceivedDate >= from.Value);
        if (to.HasValue)
            q = q.Where(x => x.ReceivedDate <= to.Value);

        return await PagingHelper.PageAsync(q, page, pageSize);
    }

    public async Task<IqcInspection?> GetWithDetailsAsync(long id)
    {
        return await _db.IqcInspections
            .AsNoTracking()
            .Include(i => i.Details)
            .FirstOrDefaultAsync(i => i.Id == id);
    }
}

public record CreateIqcRequest(
    string PartNo,
    string BatchNumber,
    string? LotNumber,
    DateTime ReceivedDate,
    string? SupplierName,
    double Quantity,
    string? UomQty,
    string? InspectorId,
    int SampleSize,
    List<CreateIqcDetail> Details);

public record CreateIqcDetail(
    string ItemName,
    string? MeasuredValue,
    bool Pass,
    string? DefectCode,
    int Qty);

// ── feat/iqc-ticket — request/result cho tạo phiếu IQC ────────────────────

/// <summary>Body tạo phiếu IQC. Client KHÔNG khai ReceiptNo/Inspector/desc —
/// server sinh/resolve (quyết định #1/#3).</summary>
public sealed class CreateIqcTicketRequest
{
    public string CodeIfs { get; set; } = "";
    public string LotBatchNo { get; set; } = "";
    public DateTime? ManufactureDate { get; set; }
    public string? MakerName { get; set; }
    public string? SupplierName { get; set; }
    public double Quantity { get; set; }
    public string? Uom { get; set; }
    public int? SampleSize { get; set; }
    public DateTime? ExpiryAt { get; set; }
}

/// <summary>Kết quả tạo phiếu — controller map sang HTTP.</summary>
public sealed class CreateIqcTicketResult
{
    public bool Ok { get; init; }
    public int HttpStatus { get; init; } = 201;
    public string? ErrorCode { get; init; }
    public string? MessageEn { get; init; }

    public string ReceiptNo { get; init; } = "";
    public long IqcInspectionId { get; init; }
    public long? MaterialLotId { get; init; }
    public string? MaterialDescription { get; init; }
    public string? IfsDescription { get; init; }

    /// <summary>matched / ambiguous / unmatched (quyết định #2/#3).</summary>
    public string MatchStatus { get; init; } = "unmatched";
    public string? LotStatus { get; init; }

    public static CreateIqcTicketResult Fail(int status, string code, string msg) =>
        new() { Ok = false, HttpStatus = status, ErrorCode = code, MessageEn = msg };
}

/// <summary>Kết quả resolve Code IFS (UI auto-fill trước submit).</summary>
public sealed class ResolveIqcCodeResult
{
    public string MatchStatus { get; init; } = "unmatched";
    public string? PartNo { get; init; }
    public string? MaterialDescription { get; init; }
    public string? IfsDescription { get; init; }
    public string? SupplierName { get; init; }
}
