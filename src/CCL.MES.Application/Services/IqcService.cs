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

    /// <summary>
    /// P13 bước 4 — đóng dấu cỡ lô + cỡ mẫu ĐỀ XUẤT lên phiếu, và trả về lý do
    /// từ chối nếu người tạo đổi khác đề xuất mà không ghi lý do.
    ///
    /// <para>Dùng chung cho CẢ HAI đường tạo phiếu. Một đường có luật, đường
    /// kia không, thì đường kia là cửa sau — và cửa sau nào rồi cũng có người
    /// đi qua (L64).</para>
    ///
    /// <para>Không suy được cỡ lô (đơn vị m² / kg / lạ) ⇒ không đề xuất ⇒ KHÔNG
    /// đòi lý do. Bắt giải trình cho một sai lệch so với con số app chưa từng
    /// đưa ra là vô nghĩa.</para>
    /// </summary>
    /// <returns><c>null</c> khi hợp lệ; ngược lại là mã lỗi.</returns>
    private static string? ApplySampleSize(
        IqcInspection insp, int? requested, string? overrideReason)
    {
        insp.LotQty = IqcLotSize.For(insp.Quantity, insp.UomQty);
        insp.SampleSizeSuggested = IqcLotSize.SuggestSampleSize(insp.Quantity, insp.UomQty);

        // 0 hoặc null = người tạo KHÔNG khai ⇒ nhận đề xuất, không phải "đổi
        // thành 0". Đo trên live: 23/26 phiếu đang để SampleSize = 0.
        var actual = requested is > 0 ? requested.Value : (insp.SampleSizeSuggested ?? 0);
        insp.SampleSize = actual;

        var reason = (overrideReason ?? "").Trim();
        if (IqcLotSize.NeedsReason(insp.SampleSizeSuggested, actual) && reason.Length == 0)
            return "iqc.sample_size_reason_required";

        insp.SampleSizeOverrideReason = reason.Length == 0 ? null : reason;
        return null;
    }

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
            Result = QcResult.Pending,
        };
        if (ApplySampleSize(insp, r.SampleSize, r.SampleSizeOverrideReason) is { } err)
            throw new InvalidOperationException(err);
        if (r.Details.Count > 0)
        {
            // Client gửi hạng mục ⇒ tôn trọng (đường cũ, nhập tay). Không đè.
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
        }
        else
        {
            // P12 bước 2a — dựng hạng mục từ THƯ VIỆN và ĐÓNG BĂNG vào ticket.
            // Chỉ chạy khi client không gửi gì, nên đường nhập tay cũ vẫn nguyên.
            var mat = await MaterializeAsync(rawMaterialId, insp.Group);
            insp.MaterialCategory = mat.Category;
            foreach (var d in mat.Details) insp.Details.Add(d);
        }
        _db.IqcInspections.Add(insp);
        await _db.SaveChangesAsync();
        // Ô đo trống nối bằng FK trần ⇒ phải đợi dòng kết quả có Id.
        await MaterializeMeasurementsAsync(insp.Details);

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

        // feat/iqc-module-tabs — chuẩn hoá nhóm phiếu (form cũ không khai →
        // Materials). KHÔNG chặn giá trị lạ (Normalize fallback Materials) để
        // giữ backward-compat; whitelist canonical hoá về đúng 4 giá trị.
        var group = IqcGroup.Normalize(r.Group);

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
                Group = group,
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
                Result = QcResult.Pending,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = actor,
            };
            // Cỡ lô + cỡ mẫu đề xuất. Từ chối SỚM, trước khi ghi bất cứ thứ gì:
            // phiếu đã tồn tại rồi mới báo thiếu lý do là bắt người dùng dọn dẹp
            // hộ mình.
            if (ApplySampleSize(insp, r.SampleSize, r.SampleSizeOverrideReason) is { } ssErr)
            {
                await tx.RollbackAsync(ct);
                return CreateIqcTicketResult.Fail(422, ssErr,
                    "Sample size differs from the AQL suggestion; a reason is required.");
            }
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

        // P12 — dựng bộ hạng mục kiểm từ thư viện tiêu chuẩn và ĐÓNG BĂNG vào
        // phiếu. Nằm trong CÙNG transaction với phiếu + lô: không có chuyện phiếu
        // tồn tại mà bộ hạng mục nửa vời.
        //
        // Mã không resolve được (unmatched / ambiguous ⇒ rawMaterialId null) thì
        // trả rỗng — người kiểm nhập tay như trước. KHÔNG đoán bừa: dựng sai bộ
        // hạng mục còn tệ hơn không dựng.
        var mat = await MaterializeAsync(rawMaterialId, insp.Group, ct);
        insp.MaterialCategory = mat.Category;
        foreach (var d in mat.Details) insp.Details.Add(d);
        if (insp.Details.Count > 0)
        {
            await _db.SaveChangesAsync(ct);
            await MaterializeMeasurementsAsync(insp.Details, ct);
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
                group,
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
            Group = group,
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

    /// <summary>Ngưỡng ký tự tối thiểu để phát query LIKE search-by-description.
    /// Dưới ngưỡng: trả rỗng + <c>TooShort=true</c>, KHÔNG chạm DB (LIKE '%a%'
    /// trên 2127 dòng không index PartDescription là quét bảng vô ích).</summary>
    public const int SearchMinLength = 3;

    /// <summary>
    /// Tra vật liệu theo mã <em>hoặc</em> mô tả (ô Standards gõ <c>336</c> /
    /// phiếu IQC gõ <c>PET</c>). CONTAINS NOCASE trên
    /// <see cref="RawMaterial.PartNo"/> <b>hoặc</b>
    /// <see cref="RawMaterial.PartDescription"/>.
    ///
    /// <para><b>Chuẩn hoá</b>: trim + gộp mọi cụm khoảng trắng nội bộ về 1 space
    /// (để "NITTO  5000NS" hai dấu cách khớp "NITTO 5000NS").</para>
    ///
    /// <para><b>NOCASE query-level</b> (cùng luật <see cref="ResolveCodeAsync"/> —
    /// <c>ToUpper()</c>, KHÔNG đổi collation cột).</para>
    ///
    /// <para><b>DISTINCT theo PartNo</b>: PartNo KHÔNG unique (2035 distinct trên
    /// 2127 dòng) — group theo PartNo, lấy dòng đầu. Mỗi Code IFS xuất hiện 1 lần
    /// trong droplist (vd "NITTO 5000NS" → 14 distinct Code IFS).</para>
    ///
    /// <para>Thuần đọc, KHÔNG ghi. Phân trang qua <see cref="PagingHelper"/>.</para>
    /// </summary>
    public async Task<IqcMaterialSearchResult> SearchMaterialByDescriptionAsync(
        string? desc, int page, int pageSize, CancellationToken ct = default)
    {
        // Chuẩn hoá: trim + gộp khoảng trắng nội bộ (nhiều space/tab → 1 space).
        var norm = System.Text.RegularExpressions.Regex.Replace((desc ?? "").Trim(), @"\s+", " ");
        if (norm.Length < SearchMinLength)
            return new IqcMaterialSearchResult
            {
                TooShort = true,
                Page = page < 1 ? 1 : page,
                PageSize = pageSize,
                Total = 0,
                Items = new List<IqcMaterialSearchRow>(),
            };

        // CONTAINS NOCASE query-level (.ToUpper()). DISTINCT theo PartNo: group
        // rồi lấy min Id đại diện — PartNo không unique nên tránh trùng dòng.
        var upper = norm.ToUpperInvariant();
        var grouped = _db.RawMaterials.AsNoTracking()
            .Where(x => x.PartNo.ToUpper().Contains(upper)
                || (x.PartDescription != null && x.PartDescription.ToUpper().Contains(upper)))
            .GroupBy(x => x.PartNo)
            .Select(g => new IqcMaterialSearchRow
            {
                CodeIfs = g.Key,
                IfsDescription = g.OrderBy(x => x.Id).Select(x => x.PartDescription).FirstOrDefault(),
                // Dòng đại diện (OrderBy Id, đầu group) cho MotherCode/Width/PartDesc.
                MotherCode = g.OrderBy(x => x.Id).Select(x => x.MotherCode).FirstOrDefault(),
                WidthMm = g.OrderBy(x => x.Id).Select(x => x.WidthMm).FirstOrDefault(),
                PartDescription = g.OrderBy(x => x.Id).Select(x => x.PartDescription).FirstOrDefault(),
            })
            .OrderBy(r => r.CodeIfs);

        var paged = await PagingHelper.PageAsync(grouped, page, pageSize);
        return new IqcMaterialSearchResult
        {
            TooShort = false,
            Page = paged.Page,
            PageSize = paged.PageSize,
            Total = paged.Total,
            Items = paged.Items.ToList(),
        };
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

    // ── P12 bước 3 — hạng mục kiểm của một phiếu ─────────────────────────

    /// <summary>
    /// Bộ hạng mục kiểm đã ĐÓNG BĂNG trên phiếu, kèm số MỤC của stepper.
    /// Read-only ⇒ QcRead đủ, không cần vai editor.
    /// </summary>
    public async Task<IqcTicketItems?> GetTicketItemsAsync(long inspectionId, CancellationToken ct = default)
    {
        var exists = await _db.IqcInspections.AsNoTracking()
            .AnyAsync(x => x.Id == inspectionId, ct);
        if (!exists) return null;

        var rows = await _db.IqcResultDetails.AsNoTracking()
            .Where(d => d.IqcInspectionId == inspectionId)
            .OrderBy(d => d.Id)
            .ToListAsync(ct);

        // Phép đo nối bằng FK trần (không navigation) — nạp một lần rồi gom,
        // thay vì N+1 truy vấn cho một phiếu có 20 hạng mục.
        var ids = rows.Select(r => r.Id).ToList();
        var byDetail = (await _db.IqcResultMeasurements.AsNoTracking()
                .Where(m => ids.Contains(m.IqcResultDetailId))
                .OrderBy(m => m.Seq)
                .ToListAsync(ct))
            .GroupBy(m => m.IqcResultDetailId)
            .ToDictionary(g => g.Key, g => g.Select(m => m.Value).ToList());

        // Phiếu nào cũng chỉ khớp MỘT spec (hoặc không khớp cái nào).
        var specNo = rows.Select(r => r.SpecNo).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

        // Trạng thái duyệt đọc SỐNG, không đóng băng: nó là thuộc tính của bộ
        // tiêu chuẩn và sẽ đổi khi QC ký. Đóng băng thì băng cảnh báo còn treo
        // mãi trên phiếu cũ sau khi spec đã được duyệt, và người ta sẽ học cách
        // bỏ qua nó.
        var approval = specNo is null ? null : await _db.IqcMaterialSpecs.AsNoTracking()
            .Where(x => x.SpecNo == specNo)
            .Select(x => (IqcSpecApproval?)x.Approval)
            .FirstOrDefaultAsync(ct);

        return new IqcTicketItems
        {
            TicketId = inspectionId,
            SpecNo = specNo,
            SpecApproval = approval?.ToString(),
            FromDefaultMatrix = rows.Count > 0 && rows.All(r => r.FromDefaultMatrix),
            Items = rows.Select(r => new IqcCheckItemRow
            {
                Id = r.Id,
                ItemKey = r.ItemKey,
                Seq = r.Seq,
                Section = IqcTicketSection.Of(r.ItemKey, r.GroupCode),
                GroupCode = r.GroupCode,
                GroupLabelVi = r.GroupLabelVi,
                GroupLabelEn = r.GroupLabelEn,
                // Hạng mục nhập tay (đường cũ) không có LabelVi — rơi về ItemName
                // để dòng vẫn đọc được thay vì hiện ô trống.
                LabelVi = r.LabelVi ?? r.ItemName,
                LabelEn = r.LabelEn,
                AcceptanceVi = r.AcceptanceVi,
                AcceptanceEn = r.AcceptanceEn,
                MethodVi = r.MethodVi,
                MethodEn = r.MethodEn,
                SourceFrequency = r.SourceFrequency,
                FromDefaultMatrix = r.FromDefaultMatrix,
                AcceptanceUnspecified = r.AcceptanceUnspecified,
                Pass = r.Pass,
                MeasuredValue = r.MeasuredValue,
                DefectCode = r.DefectCode,

                // P13 — hình dạng + ngưỡng ĐÃ ĐÓNG BĂNG, để UI dựng đúng ô nhập
                // và người kiểm thấy mình đang so với con số nào.
                Kind = r.Kind.ToString(),
                MeasureCount = r.MeasureCount,
                DefectCount = r.DefectCount,
                LimitLow = r.LimitLow,
                LimitUp = r.LimitUp,
                LimitUnit = r.LimitUnit,
                LimitLabel = r.LimitLabel,
                TearIsPass = r.TearIsPass,
                TearObserved = r.TearObserved,
                Measurements = byDetail.TryGetValue(r.Id, out var mv) ? mv : new List<double?>(),
                AutoVerdict = r.AutoVerdict,
                AutoVerdictReason = r.AutoVerdictReason,
                AutoVerdictOffendingSeq = r.AutoVerdictOffendingSeq,
                OverrideReason = r.OverrideReason,
                OverriddenBy = r.OverriddenBy,
                OverriddenAt = r.OverriddenAt,
            }).ToList(),
        };
    }

    /// <summary>
    /// Ghi phán định cho MỘT hạng mục. <paramref name="pass"/> <c>null</c> đưa
    /// hạng mục về CHƯA KIỂM — người kiểm bấm nhầm phải gỡ được, nếu không họ sẽ
    /// để nguyên một phán định sai còn hơn đi xin admin sửa DB.
    /// </summary>
    public async Task<SetIqcItemResult> SetItemVerdictAsync(
        long inspectionId, long itemId, bool? pass,
        string? measuredValue, string? defectCode,
        string actor, string actorRole,
        int? defectCount = null,
        IReadOnlyList<double?>? measurements = null,
        bool? tearObserved = null,
        string? overrideReason = null,
        CancellationToken ct = default)
    {
        RequireEditorRole(actorRole);

        var row = await _db.IqcResultDetails
            .FirstOrDefaultAsync(d => d.Id == itemId && d.IqcInspectionId == inspectionId, ct);
        if (row is null)
            return SetIqcItemResult.Fail(404, "iqc.item_not_found",
                "Check item not found on this ticket.");

        // Tiêu chuẩn còn placeholder XXX ⇒ KHÔNG cho chấm ĐẠT. Hỏi người kiểm
        // "đạt hay không so với XXX?" rồi lưu chữ ký của họ là ghi một phán định
        // lên tiêu chí trống. Chấm NG vẫn cho (thấy hỏng thật thì phải ghi được).
        if (pass == true && row.AcceptanceUnspecified)
            return SetIqcItemResult.Fail(422, "iqc.acceptance_unspecified",
                "Acceptance criteria for this item is still a placeholder; ask QA to fill it in.");

        if (measuredValue is { Length: > 128 })
            return SetIqcItemResult.Fail(422, "iqc.invalid_measured_value",
                "Measured value must be 128 characters or fewer.");
        if (defectCode is { Length: > 32 })
            return SetIqcItemResult.Fail(422, "iqc.invalid_defect_code",
                "Defect code must be 32 characters or fewer.");

        if (defectCount is { } dc && dc < 0)
            return SetIqcItemResult.Fail(422, "iqc.invalid_defect_count",
                "Defect count must be zero or greater.");
        if (measurements is not null && measurements.Count != row.MeasureCount)
            return SetIqcItemResult.Fail(422, "iqc.measurement_count_mismatch",
                $"This item expects {row.MeasureCount} measurement(s), got {measurements.Count}.");

        row.MeasuredValue = string.IsNullOrWhiteSpace(measuredValue) ? null : measuredValue.Trim();
        row.DefectCode = string.IsNullOrWhiteSpace(defectCode) ? null : defectCode.Trim();
        if (defectCount is not null) row.DefectCount = defectCount;
        if (tearObserved is not null) row.TearObserved = tearObserved.Value;

        // Ô đo: ghi đè theo thứ tự. Dòng đã dựng sẵn lúc mở phiếu nên chỉ cập
        // nhật, không chèn mới — chèn mới sẽ đụng unique (detail, seq).
        var values = await ApplyMeasurementsAsync(row, measurements, ct);

        // ── MÁY CHẤM ─────────────────────────────────────────────────────
        var machine = JudgeRow(row, values);
        row.AutoVerdict = machine.Verdict.ToString();
        row.AutoVerdictReason = machine.ReasonCode;
        row.AutoVerdictOffendingSeq = machine.OffendingIndex;

        // Máy quyết được mà người chưa nói gì ⇒ NHẬN kết luận của máy. Người
        // kiểm đã làm phần việc thật (đếm lỗi / đo), tiêu chuẩn nói con số đó
        // nghĩa là gì; bắt họ bấm thêm một nút để lặp lại điều đó là việc thừa.
        // Muốn gỡ về CHƯA KIỂM thì xoá dữ liệu, lúc đó máy trả Undecidable.
        var decided = machine.Verdict is IqcAutoVerdict.Pass or IqcAutoVerdict.Fail;
        if (pass is null && decided) pass = machine.Verdict == IqcAutoVerdict.Pass;

        // Người nói KHÁC máy ⇒ phải ghi lý do (Henry chốt 2026-09-04: máy chấm
        // là RÀNG BUỘC). Máy chưa quyết được thì không có gì để mà trái.
        var reason = (overrideReason ?? "").Trim();
        var conflict = decided && pass is { } p && p != (machine.Verdict == IqcAutoVerdict.Pass);
        if (conflict && reason.Length == 0)
            return SetIqcItemResult.Fail(422, "iqc.verdict_override_reason_required",
                "Your verdict differs from the automatic judgement; a reason is required.");

        row.Pass = pass;
        if (conflict)
        {
            // Ai đổi, lúc nào — server đóng dấu theo token. Đây là bằng chứng,
            // không phải lời khai của client.
            row.OverrideReason = reason;
            row.OverriddenBy = actor;
            row.OverriddenAt = DateTime.UtcNow;
        }
        else
        {
            row.OverrideReason = null;
            row.OverriddenBy = null;
            row.OverriddenAt = null;
        }
        await _db.SaveChangesAsync(ct);

        await _audit.EmitAsync(
            AuditAction.IqcItemSet, actor, actorRole,
            targetType: "IqcResultDetail", targetId: row.Id.ToString(),
            detail: JsonSerializer.Serialize(new
            {
                iqc_inspection_id = inspectionId,
                item_key = row.ItemKey,
                seq = row.Seq,
                // "chưa kiểm" là một trạng thái thật, ghi rõ chứ không để trống.
                verdict = row.Pass is null ? "unchecked" : row.Pass == true ? "pass" : "fail",
                measured_value = row.MeasuredValue,
                defect_code = row.DefectCode,
                // Auditor phải trả lời được "máy nói gì, ai đổi, vì sao" mà
                // không cần mở lại bảng kết quả.
                defect_count = row.DefectCount,
                auto_verdict = row.AutoVerdict,
                auto_verdict_reason = row.AutoVerdictReason,
                override_reason = row.OverrideReason,
            }));

        return new SetIqcItemResult { Ok = true, ItemId = row.Id, Pass = row.Pass };
    }

    /// <summary>
    /// Ghi các phép đo và trả về TOÀN BỘ giá trị hiện hành của hạng mục, theo
    /// đúng thứ tự <c>Seq</c>.
    ///
    /// <para><paramref name="incoming"/> <c>null</c> = lần ghi này không đụng
    /// tới phép đo ⇒ đọc lại giá trị đang có, để máy vẫn chấm được trên dữ liệu
    /// đã nhập từ trước.</para>
    /// </summary>
    private async Task<IReadOnlyList<double?>> ApplyMeasurementsAsync(
        IqcResultDetail row, IReadOnlyList<double?>? incoming, CancellationToken ct)
    {
        if (row.Kind != IqcCheckKind.Measure || row.MeasureCount <= 0)
            return Array.Empty<double?>();

        var rows = await _db.IqcResultMeasurements
            .Where(m => m.IqcResultDetailId == row.Id)
            .OrderBy(m => m.Seq)
            .ToListAsync(ct);

        // Phiếu mở trước P13 chưa có ô đo nào — dựng bù tại chỗ thay vì để hạng
        // mục vĩnh viễn không chấm được.
        for (var seq = rows.Count + 1; seq <= row.MeasureCount; seq++)
        {
            var m = new IqcResultMeasurement { IqcResultDetailId = row.Id, Seq = seq, Value = null };
            _db.IqcResultMeasurements.Add(m);
            rows.Add(m);
        }

        if (incoming is not null)
            for (var i = 0; i < row.MeasureCount && i < rows.Count; i++)
                rows[i].Value = incoming[i];

        return rows.Take(row.MeasureCount).Select(m => m.Value).ToList();
    }

    /// <summary>
    /// Máy chấm MỘT hạng mục, dựa trên hình dạng và ngưỡng ĐÃ ĐÓNG BĂNG của
    /// chính dòng đó — không đọc lại thư viện hay spec.
    ///
    /// <para>Hạng mục người bấm (<c>Verdict</c>) và hồ sơ giấy (<c>Document</c>)
    /// thì máy im lặng: không có con số nào để so, và
    /// <see cref="IqcAutoVerdict.Undecidable"/> KHÔNG có nghĩa là đạt.</para>
    /// </summary>
    private static IqcJudgement JudgeRow(IqcResultDetail row, IReadOnlyList<double?> values) =>
        row.Kind switch
        {
            IqcCheckKind.DefectCount => IqcAcceptance.JudgeDefectCounts([row.DefectCount]),
            IqcCheckKind.Measure => IqcAcceptance.JudgeMeasurements(
                values,
                // Không cận nào ⇒ không có ngưỡng số ⇒ nhường người chấm. Dựng
                // một IqcSpecLimit rỗng ở đây sẽ đổi mã lý do thành
                // "limit_has_no_bound" và che mất sự thật là spec vốn không có số.
                row.LimitLow is null && row.LimitUp is null
                    ? null
                    : new IqcSpecLimit(row.LimitLow, row.LimitUp, null,
                        row.LimitUnit, row.LimitLabel, row.TearIsPass, row.AcceptanceVi ?? ""),
                row.TearObserved),
            _ => IqcJudgement.Undecidable("iqc.judge.human_only"),
        };

    // ── P12 — CHỐT phiếu: đánh giá xong hết mới được chốt ────────────────

    /// <summary>
    /// Chốt phiếu IQC. <b>Từ chối khi còn hạng mục chưa kiểm</b> — người kiểm
    /// phải đánh giá HẾT rồi mới chốt được (Henry chốt 2026-08-28).
    ///
    /// <para>Kết luận suy ra từ chính các hạng mục: còn một hạng mục KHÔNG ĐẠT
    /// thì lô KHÔNG ĐẠT. Không có ô cho người kiểm gõ kết luận trái với dữ liệu
    /// họ vừa chấm.</para>
    /// </summary>
    public async Task<CompleteIqcResult> CompleteTicketAsync(
        long inspectionId, string actor, string actorRole, CancellationToken ct = default)
    {
        RequireEditorRole(actorRole);

        var insp = await _db.IqcInspections
            .FirstOrDefaultAsync(x => x.Id == inspectionId, ct);
        if (insp is null)
            return CompleteIqcResult.Fail(404, "iqc.ticket_not_found", "IQC ticket not found.");

        var items = await _db.IqcResultDetails.AsNoTracking()
            .Where(d => d.IqcInspectionId == inspectionId)
            .Select(d => new { d.Pass, d.ItemKey })
            .ToListAsync(ct);

        // Phiếu cũ (trước P12) không có hạng mục nào để kiểm — vẫn chốt được,
        // nếu không thì 25 phiếu lịch sử mắc kẹt vĩnh viễn.
        var pending = items.Count(x => x.Pass is null);
        if (pending > 0)
            return CompleteIqcResult.Fail(422, "iqc.items_incomplete",
                $"{pending} of {items.Count} check items are not evaluated yet.")
                with { Total = items.Count, Pending = pending };

        var pass = items.All(x => x.Pass == true);
        insp.Result = pass ? QcResult.Pass : QcResult.Fail;
        insp.UpdatedAt = DateTime.UtcNow;
        insp.UpdatedBy = actor;
        await _db.SaveChangesAsync(ct);

        await _audit.EmitAsync(
            AuditAction.IqcComplete, actor, actorRole,
            targetType: "IqcInspection", targetId: insp.Id.ToString(),
            detail: JsonSerializer.Serialize(new
            {
                receipt_no = insp.ReceiptNo,
                result = insp.Result.ToString(),
                items_total = items.Count,
                items_failed = items.Count(x => x.Pass == false),
            }));

        return new CompleteIqcResult
        {
            Ok = true, Result = insp.Result.ToString(),
            Total = items.Count, Pending = 0,
            Failed = items.Count(x => x.Pass == false),
        };
    }

    // ── feat/iqc-module-tabs — IQC Data list (DTO) + Dashboard KPI ────────

    /// <summary>Danh sách phiếu IQC đã lưu cho tab "IQC Data" — trả DTO thuần
    /// (KHÔNG entity). Lọc theo <paramref name="group"/> (null = tất cả) +
    /// search (ReceiptNo/CodeIfs/MaterialDescription/PartNo/Supplier). Sort mới
    /// nhất trước. Thuần đọc.</summary>
    public async Task<IqcTicketPage> ListTicketsAsync(
        string? group, string? search, int page, int pageSize, CancellationToken ct = default)
    {
        var q = _db.IqcInspections.AsNoTracking()
            .OrderByDescending(x => x.ReceivedDate)
            .ThenByDescending(x => x.Id)
            .AsQueryable();

        if (IqcGroup.IsValid(group))
        {
            var g = IqcGroup.Normalize(group);
            q = q.Where(x => x.Group == g);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(x =>
                (x.ReceiptNo != null && EF.Functions.Like(x.ReceiptNo, $"%{s}%"))
                || (x.CodeIfs != null && EF.Functions.Like(x.CodeIfs, $"%{s}%"))
                || (x.MaterialDescription != null && EF.Functions.Like(x.MaterialDescription, $"%{s}%"))
                || EF.Functions.Like(x.PartNo, $"%{s}%")
                || (x.SupplierName != null && EF.Functions.Like(x.SupplierName, $"%{s}%")));
        }

        var paged = await PagingHelper.PageAsync(q, page, pageSize);

        // MÃ MẸ là khoá thư mục hồ sơ HSF (IQC/Documents/<mã mẹ>/). Phiếu chỉ
        // giữ RawMaterialId nên phải tra thêm — tra SAU khi phân trang để không
        // đụng vào truy vấn đếm, và một lượt cho cả trang chứ không N+1.
        var rawIds = paged.Items
            .Where(x => x.RawMaterialId is not null)
            .Select(x => x.RawMaterialId!.Value).Distinct().ToList();
        var motherByRaw = rawIds.Count == 0
            ? new Dictionary<long, string?>()
            : await _db.RawMaterials.AsNoTracking()
                .Where(m => rawIds.Contains(m.Id))
                .Select(m => new { m.Id, m.MotherCode })
                .ToDictionaryAsync(m => m.Id, m => m.MotherCode, ct);

        return new IqcTicketPage
        {
            Page = paged.Page,
            PageSize = paged.PageSize,
            Total = paged.Total,
            Items = paged.Items.Select(x => new IqcTicketRow
            {
                Id = x.Id,
                MotherCode = x.RawMaterialId is { } rid
                    && motherByRaw.TryGetValue(rid, out var mc) ? mc : null,
                ReceiptNo = x.ReceiptNo,
                Group = string.IsNullOrWhiteSpace(x.Group) ? IqcGroup.Materials : x.Group,
                CodeIfs = x.CodeIfs,
                MaterialDescription = x.MaterialDescription,
                LotBatchNo = x.LotNumber ?? x.BatchNumber,
                ManufactureDate = x.ManufactureDate,
                MakerName = x.MakerName,
                SupplierName = x.SupplierName,
                Inspector = x.InspectorId,
                ReceivedDate = x.ReceivedDate,
                Quantity = x.Quantity,
                Uom = x.UomQty,
                Result = x.Result.ToString(),
            }).ToList(),
        };
    }

    /// <summary>
    /// Sổ lịch sử IQC — chỉ phiếu đã kết luận (Pass/Fail), map sheet Excel
    /// Roll / PCS / Chem / Tool. Nguồn = <see cref="IqcInspection"/> sau approve;
    /// KHÔNG bảng song song (tránh clone 77 cột Excel).
    /// </summary>
    public async Task<IqcHistoryPage> ListHistoryAsync(
        string? sheet, string? search,
        DateTime? fromUtc, DateTime? toUtc,
        int page, int pageSize, CancellationToken ct = default)
    {
        var q = _db.IqcInspections.AsNoTracking()
            .Where(x => x.Result == QcResult.Pass || x.Result == QcResult.Fail)
            .OrderByDescending(x => x.ApprovedAt ?? x.ReceivedDate)
            .ThenByDescending(x => x.Id)
            .AsQueryable();

        var sheetKey = (sheet ?? "").Trim().ToUpperInvariant();
        q = sheetKey switch
        {
            "ROLL" => q.Where(x => x.MaterialCategory == IqcMaterialCategory.Roll),
            "PCS" => q.Where(x => x.MaterialCategory == IqcMaterialCategory.Pcs),
            "CHEM" => q.Where(x =>
                x.MaterialCategory == IqcMaterialCategory.Chem
                || x.Group == IqcGroup.Chemical),
            "TOOL" or "TOOLS" => q.Where(x =>
                x.MaterialCategory == IqcMaterialCategory.Tool
                || x.Group == IqcGroup.Tools),
            _ => q,
        };

        if (fromUtc is { } f)
            q = q.Where(x => (x.ApprovedAt ?? x.ReceivedDate) >= f);
        if (toUtc is { } t)
            q = q.Where(x => (x.ApprovedAt ?? x.ReceivedDate) < t);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(x =>
                (x.ReceiptNo != null && EF.Functions.Like(x.ReceiptNo, $"%{s}%"))
                || (x.CodeIfs != null && EF.Functions.Like(x.CodeIfs, $"%{s}%"))
                || (x.MaterialDescription != null && EF.Functions.Like(x.MaterialDescription, $"%{s}%"))
                || EF.Functions.Like(x.PartNo, $"%{s}%")
                || (x.SupplierName != null && EF.Functions.Like(x.SupplierName, $"%{s}%")));
        }

        var paged = await PagingHelper.PageAsync(q, page, pageSize);
        var rawIds = paged.Items
            .Where(x => x.RawMaterialId is not null)
            .Select(x => x.RawMaterialId!.Value).Distinct().ToList();
        var motherByRaw = rawIds.Count == 0
            ? new Dictionary<long, string?>()
            : await _db.RawMaterials.AsNoTracking()
                .Where(m => rawIds.Contains(m.Id))
                .Select(m => new { m.Id, m.MotherCode })
                .ToDictionaryAsync(m => m.Id, m => m.MotherCode, ct);

        return new IqcHistoryPage
        {
            Page = paged.Page,
            PageSize = paged.PageSize,
            Total = paged.Total,
            Items = paged.Items.Select(x =>
            {
                var group = string.IsNullOrWhiteSpace(x.Group) ? IqcGroup.Materials : x.Group;
                return new IqcHistoryRow
                {
                    Id = x.Id,
                    ReceiptNo = x.ReceiptNo,
                    Group = group,
                    MaterialCategory = x.MaterialCategory.ToString(),
                    Sheet = ToExcelSheet(group, x.MaterialCategory),
                    CodeIfs = x.CodeIfs,
                    MotherCode = x.RawMaterialId is { } rid
                        && motherByRaw.TryGetValue(rid, out var mc) ? mc : null,
                    MaterialDescription = x.MaterialDescription,
                    LotBatchNo = x.LotNumber ?? x.BatchNumber,
                    SupplierName = x.SupplierName,
                    Inspector = x.InspectorId,
                    ReceivedDate = x.ReceivedDate,
                    Quantity = x.Quantity,
                    Uom = x.UomQty,
                    Result = x.Result.ToString(),
                    ApprovedBy = x.ApprovedBy,
                    ApprovedAt = x.ApprovedAt,
                };
            }).ToList(),
        };
    }

    /// <summary>Nhãn sheet Excel từ group + category đóng băng trên phiếu.</summary>
    public static string ToExcelSheet(string group, IqcMaterialCategory category) => category switch
    {
        IqcMaterialCategory.Roll => "Roll",
        IqcMaterialCategory.Pcs => "PCS",
        IqcMaterialCategory.Chem => "Chem",
        IqcMaterialCategory.Tool => "Tool",
        _ when string.Equals(group, IqcGroup.Chemical, StringComparison.OrdinalIgnoreCase) => "Chem",
        _ when string.Equals(group, IqcGroup.Tools, StringComparison.OrdinalIgnoreCase) => "Tool",
        _ => "Materials",
    };

    /// <summary>KPI đếm thật cho tab Dashboard — 1 pass gom nhóm + gom trạng
    /// thái. Placeholder CÓ CẤU TRÚC (số liệu thật). Thuần đọc.</summary>
    public async Task<IqcDashboardCounts> DashboardAsync(CancellationToken ct = default)
    {
        // Gom theo (Group, Result) một lần rồi tổng hợp trong bộ nhớ — tránh
        // N query. Coalesce group rỗng (không nên có sau migration) về Materials.
        var rows = await _db.IqcInspections.AsNoTracking()
            .GroupBy(x => new { x.Group, x.Result })
            .Select(g => new { g.Key.Group, g.Key.Result, Count = g.Count() })
            .ToListAsync(ct);

        var d = new IqcDashboardCounts();
        foreach (var r in rows)
        {
            var g = string.IsNullOrWhiteSpace(r.Group) ? IqcGroup.Materials : IqcGroup.Normalize(r.Group);
            d.Total += r.Count;
            switch (g)
            {
                case IqcGroup.Materials: d.Materials += r.Count; break;
                case IqcGroup.Chemical:  d.Chemical += r.Count; break;
                case IqcGroup.Tools:     d.Tools += r.Count; break;
                case IqcGroup.Other:     d.Other += r.Count; break;
            }
            switch (r.Result)
            {
                case QcResult.Pending: d.Pending += r.Count; break;
                case QcResult.Pass:    d.Pass += r.Count; break;
                case QcResult.Fail:    d.Fail += r.Count; break;
            }
        }
        return d;
    }

    /// <summary>
    /// P12 — dựng bộ hạng mục kiểm cho lô NVL, đóng băng cả hai ngôn ngữ.
    ///
    /// <para>Khoá nối là <c>RawMaterials.MotherCode</c> (đo trên live: 352/356
    /// khớp), KHÔNG phải <c>PartNo</c> — <c>PartNo</c> là <c>300xxxxx</c> còn mã
    /// trong file spec là <c>7xxxxxxx</c>, khớp 0 dòng. Xem
    /// <see cref="IqcCheckResolver"/>.</para>
    ///
    /// <para>Không có nguyên liệu, hoặc mã rỗng ⇒ trả rỗng: ticket vẫn tạo
    /// được, người kiểm nhập tay như trước. KHÔNG đoán bừa bộ hạng mục — dựng
    /// sai còn tệ hơn không dựng.</para>
    /// </summary>
    /// <summary>Bộ hạng mục đã dựng + NHÓM đã dùng để dựng nó. Trả nhóm ra
    /// ngoài để người gọi đóng dấu lên phiếu: không có nó thì sau này không ai
    /// giải thích được vì sao phiếu đó có 13 ô đếm lỗi còn phiếu kia không.</summary>
    private sealed record Materialized(
        IqcMaterialCategory Category, IReadOnlyList<IqcResultDetail> Details)
    {
        public static readonly Materialized None =
            new(IqcMaterialCategory.Any, Array.Empty<IqcResultDetail>());
    }

    private async Task<Materialized> MaterializeAsync(
        long? rawMaterialId, string? ticketGroup, CancellationToken ct = default)
    {
        var lib = await _db.IqcCheckItemLibraries.AsNoTracking().Where(x => x.Active).ToListAsync(ct);
        if (lib.Count == 0) return Materialized.None;

        // Đơn vị tồn kho là thứ DUY NHẤT phân biệt được cuộn với tấm — nhóm
        // phiếu gộp cả hai vào "Materials". Xem IqcCategoryRule.
        var rm = rawMaterialId is null ? null : await _db.RawMaterials
            .Where(x => x.Id == rawMaterialId)
            .Select(x => new { x.MotherCode, x.InventoryUom })
            .FirstOrDefaultAsync(ct);

        var category = IqcCategoryRule.Resolve(ticketGroup, rm?.InventoryUom);
        var code = (rm?.MotherCode ?? "").Trim();

        var specs = code.Length == 0
            ? new List<IqcMaterialSpec>()
            : await _db.IqcMaterialSpecs.AsNoTracking()
                .Where(x => x.Active && x.MaterialCode == code)
                .ToListAsync(ct);
        var specNos = specs.Select(x => x.SpecNo).ToList();
        var specItems = specNos.Count == 0
            ? new List<IqcSpecItem>()
            : await _db.IqcSpecItems.AsNoTracking()
                .Where(x => x.Active && specNos.Contains(x.SpecNo))
                .ToListAsync(ct);

        var resolved = IqcCheckResolver.Resolve(code, category, specs, specItems, lib);
        if (resolved.Items.Count == 0) return new Materialized(category, Array.Empty<IqcResultDetail>());

        var details = resolved.Items.Select(i => new IqcResultDetail
        {
            // ItemName giữ bản VI để bản ghi vẫn đọc được bằng công cụ cũ.
            ItemName = i.LabelVi,
            Pass = null,                       // CHƯA KIỂM — xem chú thích ở entity
            Qty = 0,
            ItemKey = i.ItemKey,
            Seq = i.Seq,
            SpecNo = resolved.SpecNo,
            GroupCode = i.GroupCode,
            GroupLabelVi = i.GroupLabelVi,
            GroupLabelEn = i.GroupLabelEn,
            LabelVi = i.LabelVi,
            LabelEn = i.LabelEn,
            AcceptanceVi = i.AcceptanceVi,
            AcceptanceEn = i.AcceptanceEn,
            MethodVi = i.MethodVi,
            MethodEn = i.MethodEn,
            SourceFrequency = i.SourceFrequency,
            FromDefaultMatrix = i.FromDefaultMatrix,
            AcceptanceUnspecified = i.AcceptanceUnspecified,

            // P13 — ĐÓNG BĂNG hình dạng + ngưỡng đã dùng để chấm. Đọc lại từ
            // thư viện/spec lúc chấm thì hồ sơ đã ký sẽ đổi theo master data.
            Kind = i.Kind,
            MeasureCount = i.MeasureCount,
            LimitLow = i.LimitLow,
            LimitUp = i.LimitUp,
            LimitUnit = i.LimitUnit,
            LimitLabel = i.LimitLabel,
            TearIsPass = i.TearIsPass,
        }).ToList();

        return new Materialized(category, details);
    }

    /// <summary>
    /// Dựng sẵn các ô ĐO trống cho hạng mục kiểu <c>Measure</c> — 5 ô cho kích
    /// thước, 1 cho độ bám dính.
    ///
    /// <para>Gọi SAU khi <paramref name="details"/> đã có Id: bảng con nối bằng
    /// FK trần (không navigation property), đúng khuôn mọi bảng con IQC/QC.</para>
    ///
    /// <para><c>Value = null</c> = CHƯA ĐO, khác hẳn 0. Dựng sẵn đủ số ô để
    /// người kiểm thấy ngay phải đo mấy lần, và để máy phân biệt được "đo xong
    /// hết" với "mới đo 2/5" — không có ô trống sẵn thì hai trạng thái đó nhìn
    /// giống hệt nhau.</para>
    /// </summary>
    private async Task MaterializeMeasurementsAsync(
        IEnumerable<IqcResultDetail> details, CancellationToken ct = default)
    {
        var rows = new List<IqcResultMeasurement>();
        foreach (var d in details)
        {
            if (d.Kind != IqcCheckKind.Measure || d.MeasureCount <= 0) continue;
            for (var seq = 1; seq <= d.MeasureCount; seq++)
                rows.Add(new IqcResultMeasurement
                {
                    IqcResultDetailId = d.Id,
                    Seq = seq,
                    Value = null,
                });
        }
        if (rows.Count == 0) return;
        _db.IqcResultMeasurements.AddRange(rows);
        await _db.SaveChangesAsync(ct);
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
    List<CreateIqcDetail> Details,
    /// <summary>P13 — bắt buộc khi cỡ mẫu khác đề xuất AQL. Tham số cuối có giá
    /// trị mặc định để 2 chỗ gọi cũ không phải sửa; luật vẫn chạy vì mặc định là
    /// "không có lý do", tức là bị từ chối nếu thực sự có sai lệch.</summary>
    string? SampleSizeOverrideReason = null);

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
    /// <summary>feat/iqc-module-tabs — nhóm phiếu (Materials/Chemical/Tools/Other).
    /// Thiếu/không rõ → server chuẩn hoá về "Materials" (backward compat form cũ).</summary>
    public string? Group { get; set; }
    public string CodeIfs { get; set; } = "";
    public string LotBatchNo { get; set; } = "";
    public DateTime? ManufactureDate { get; set; }
    public string? MakerName { get; set; }
    public string? SupplierName { get; set; }
    public double Quantity { get; set; }
    public string? Uom { get; set; }
    public int? SampleSize { get; set; }

    /// <summary>P13 — lý do đổi cỡ mẫu khác đề xuất AQL. Thiếu nó khi có sai
    /// lệch ⇒ 422 <c>iqc.sample_size_reason_required</c> (Henry chốt
    /// 2026-09-04: mọi thay đổi đều phải ghi lý do).</summary>
    public string? SampleSizeOverrideReason { get; set; }

    public DateTime? ExpiryAt { get; set; }
}

/// <summary>Kết quả tạo phiếu — controller map sang HTTP.</summary>
public sealed class CreateIqcTicketResult
{
    public bool Ok { get; init; }
    public int HttpStatus { get; init; } = 201;
    public string? ErrorCode { get; init; }
    public string? MessageEn { get; init; }

    /// <summary>feat/iqc-module-tabs — nhóm phiếu canonical (server chuẩn hoá).</summary>
    public string Group { get; init; } = IqcGroup.Materials;
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

/// <summary>Một dòng kết quả tra vật liệu theo mô tả (feat/iqc-search-by-desc).
/// <c>CodeIfs</c> = PartNo (droplist chọn nhiều); <c>IfsDescription</c> =
/// PartDescription của dòng đại diện.</summary>
public sealed class IqcMaterialSearchRow
{
    public string CodeIfs { get; init; } = "";
    public string? IfsDescription { get; init; }
    // feat/iqc-materials-line-table — làm giàu dòng đại diện cho bảng line-items
    // (additive; dòng đại diện = OrderBy Id đầu group). Nullable, cột cũ giữ nguyên.
    public string? MotherCode { get; init; }
    public double? WidthMm { get; init; }
    public string? PartDescription { get; init; }
}

/// <summary>Kết quả tra vật liệu theo mô tả — phân trang + cờ desc-quá-ngắn.</summary>
public sealed class IqcMaterialSearchResult
{
    /// <summary>true khi desc &lt; <see cref="IqcService.SearchMinLength"/> ký tự —
    /// KHÔNG phát query, Items rỗng.</summary>
    public bool TooShort { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; }
    public int Total { get; init; }
    public List<IqcMaterialSearchRow> Items { get; init; } = new();
}

// ── feat/iqc-module-tabs — IQC Data list + Dashboard (Application-layer) ──

/// <summary>Một dòng phiếu IQC đã lưu (Application-layer; controller map sang
/// Shared DTO).</summary>
public sealed class IqcTicketRow
{
    public long Id { get; init; }
    public string? ReceiptNo { get; init; }
    public string Group { get; init; } = "Materials";
    public string? CodeIfs { get; init; }

    /// <summary>Mã mẹ của nguyên liệu — khoá thư mục hồ sơ HSF trên server
    /// (<c>IQC/Documents/&lt;mã mẹ&gt;/</c>). <c>null</c> khi phiếu nhập tay
    /// không khớp được dòng RawMaterials nào.</summary>
    public string? MotherCode { get; init; }

    public string? MaterialDescription { get; init; }
    public string? LotBatchNo { get; init; }
    public DateTime? ManufactureDate { get; init; }
    public string? MakerName { get; init; }
    public string? SupplierName { get; init; }
    public string? Inspector { get; init; }
    public DateTime ReceivedDate { get; init; }
    public double Quantity { get; init; }
    public string? Uom { get; init; }
    public string Result { get; init; } = "Pending";
}

/// <summary>Một dòng sổ lịch sử (Application-layer).</summary>
public sealed class IqcHistoryRow
{
    public long Id { get; init; }
    public string? ReceiptNo { get; init; }
    public string Group { get; init; } = "Materials";
    public string MaterialCategory { get; init; } = "Any";
    public string Sheet { get; init; } = "Materials";
    public string? CodeIfs { get; init; }
    public string? MotherCode { get; init; }
    public string? MaterialDescription { get; init; }
    public string? LotBatchNo { get; init; }
    public string? SupplierName { get; init; }
    public string? Inspector { get; init; }
    public DateTime ReceivedDate { get; init; }
    public double Quantity { get; init; }
    public string? Uom { get; init; }
    public string Result { get; init; } = "Pass";
    public string? ApprovedBy { get; init; }
    public DateTime? ApprovedAt { get; init; }
}

/// <summary>Trang lịch sử IQC (Application-layer).</summary>
public sealed class IqcHistoryPage
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; }
    public int Total { get; init; }
    public List<IqcHistoryRow> Items { get; init; } = new();
}

/// <summary>Trang phiếu IQC (Application-layer).</summary>
public sealed class IqcTicketPage
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; }
    public int Total { get; init; }
    public List<IqcTicketRow> Items { get; init; } = new();
}

/// <summary>P12 — kết quả chốt phiếu.</summary>
public sealed record CompleteIqcResult
{
    public bool Ok { get; init; }
    public int HttpStatus { get; init; } = 200;
    public string? ErrorCode { get; init; }
    public string? MessageEn { get; init; }

    /// <summary>Pending / Pass / Fail sau khi chốt.</summary>
    public string Result { get; init; } = "Pending";
    public int Total { get; init; }

    /// <summary>Số hạng mục CHƯA kiểm — UI hiện thẳng con số này.</summary>
    public int Pending { get; init; }
    public int Failed { get; init; }

    public static CompleteIqcResult Fail(int status, string code, string msg) =>
        new() { Ok = false, HttpStatus = status, ErrorCode = code, MessageEn = msg };
}

/// <summary>P12 — bộ hạng mục kiểm của một phiếu (Application-layer).</summary>
public sealed class IqcTicketItems
{
    public long TicketId { get; init; }
    public string? SpecNo { get; init; }

    /// <summary>P13 — <c>PendingQc</c> · <c>Approved</c> · <c>Rejected</c>, hoặc
    /// <c>null</c> khi dựng từ ma trận mặc định. Đo trên live 2026-09-05:
    /// <b>575/946</b> mã đang có nguyên liệu trong kho dùng spec CHƯA duyệt —
    /// người kiểm đang ký lên tiêu chuẩn chưa ai xác nhận mà không được nhắc.</summary>
    public string? SpecApproval { get; init; }

    public bool FromDefaultMatrix { get; init; }
    public List<IqcCheckItemRow> Items { get; init; } = new();
}

/// <summary>P12 — một hạng mục kiểm đã đóng băng (Application-layer).</summary>
public sealed class IqcCheckItemRow
{
    public long Id { get; init; }
    public string? ItemKey { get; init; }
    public int Seq { get; init; }
    public int Section { get; init; }
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
    public bool FromDefaultMatrix { get; init; }
    public bool AcceptanceUnspecified { get; init; }
    public bool? Pass { get; init; }
    public string? MeasuredValue { get; init; }
    public string? DefectCode { get; init; }

    // ── P13 bước 4 — hình dạng, ngưỡng, và dấu vết máy chấm ──────────────

    /// <summary>Verdict · DefectCount · Measure · Document — quyết định ô nhập.</summary>
    public string Kind { get; init; } = nameof(IqcCheckKind.Verdict);
    public int MeasureCount { get; init; }
    public int? DefectCount { get; init; }
    public double? LimitLow { get; init; }
    public double? LimitUp { get; init; }
    public string? LimitUnit { get; init; }
    public string? LimitLabel { get; init; }
    public bool TearIsPass { get; init; }
    public bool TearObserved { get; init; }

    /// <summary>Giá trị các phép đo theo thứ tự; phần tử <c>null</c> = chưa đo.</summary>
    public List<double?> Measurements { get; init; } = new();

    public string? AutoVerdict { get; init; }
    public string? AutoVerdictReason { get; init; }
    public int? AutoVerdictOffendingSeq { get; init; }
    public string? OverrideReason { get; init; }
    public string? OverriddenBy { get; init; }
    public DateTime? OverriddenAt { get; init; }
}

/// <summary>P12 — kết quả ghi phán định một hạng mục.</summary>
public sealed class SetIqcItemResult
{
    public bool Ok { get; init; }
    public int HttpStatus { get; init; } = 200;
    public string? ErrorCode { get; init; }
    public string? MessageEn { get; init; }
    public long ItemId { get; init; }
    public bool? Pass { get; init; }

    public static SetIqcItemResult Fail(int status, string code, string msg) =>
        new() { Ok = false, HttpStatus = status, ErrorCode = code, MessageEn = msg };
}

/// <summary>KPI đếm phiếu IQC (Application-layer).</summary>
public sealed class IqcDashboardCounts
{
    public int Total { get; set; }
    public int Materials { get; set; }
    public int Chemical { get; set; }
    public int Tools { get; set; }
    public int Other { get; set; }
    public int Pending { get; set; }
    public int Pass { get; set; }
    public int Fail { get; set; }
}
