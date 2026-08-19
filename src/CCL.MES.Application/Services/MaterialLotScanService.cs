using System.Text.Json;
using CCL.MES.Application.Audit;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Auth;
using CCL.MES.Domain.Entities;
using CCL.MES.Domain.StateMachine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CCL.MES.Application.Services;

/// <summary>A1 — kết quả một thao tác mạch lô. Controller chỉ map sang HTTP.</summary>
public sealed record MaterialLotOutcome
{
    public bool Ok { get; init; }
    public int HttpStatus { get; init; } = 200;
    public string? ErrorCode { get; init; }
    public string? MessageEn { get; init; }

    /// <summary>Grace period (Đ-cờ tắt): ghi vẫn thành công nhưng kèm cảnh báo
    /// đúng bằng mã lỗi lẽ ra đã chặn. Đây là số liệu để Henry biết lật cờ
    /// sẽ chặn bao nhiêu ca.</summary>
    public string? Warning { get; init; }

    public long? ConsumptionId { get; init; }
    public long? MaterialLotId { get; init; }
    public string? LotNo { get; init; }
    public string? LotStatus { get; init; }
    public double QtyAvailableAfter { get; init; }
    public bool Enforced { get; init; }

    public static MaterialLotOutcome Fail(int status, string code, string msg) =>
        new() { Ok = false, HttpStatus = status, ErrorCode = code, MessageEn = msg };
}

/// <summary>
/// A1 — toàn bộ luật quét / đảo / đổi trạng thái lô nguyên vật liệu.
///
/// <para><b>Logic ở đây, KHÔNG ở controller</b> (§5 hợp đồng + skill
/// <c>cmes-thin-controller</c>). Controller chỉ bind body, lấy actor/role, gọi
/// một hàm, map kết quả sang HTTP. Nhờ vậy đường quét dùng lại được cho job
/// nền / adapter ERP về sau mà không phải giả lập HTTP.</para>
///
/// <para><b>Concurrency.</b> Optimistic lock trên <see cref="MaterialLot.RowVersion"/>
/// (L38 Option-B: EF gửi <c>X''</c> lúc INSERT, trigger SQLite bump). N operator
/// quét cùng một lô ⇒ 1 người thắng, (N−1) người nhận 409 <c>lot.conflict</c> +
/// audit <c>WO_STATE_CONFLICT</c>. KHÔNG dùng "đọc rồi trừ" không khoá — đó
/// đúng là cách kho bán vượt tồn.</para>
/// </summary>
public sealed class MaterialLotScanService
{
    private readonly IMesDbContext _db;
    private readonly IAuditWriter _audit;
    private readonly MaterialLotOptions _opts;

    /// <summary>Chỉ ba vai này được đổi trạng thái lô / gia hạn. Operator được
    /// quét tiêu thụ nhưng KHÔNG được tự tuyên bố lô đạt.</summary>
    private static readonly string[] LotStatusRoles =
        { UserRole.Admin, UserRole.Supervisor, UserRole.Qc };

    /// <summary>Đảo tiêu thụ là quyền của Supervisor (Đ3) — Admin đi kèm vì
    /// Admin là superset vận hành.</summary>
    private static readonly string[] ReverseRoles =
        { UserRole.Admin, UserRole.Supervisor };

    /// <summary>Sau các phase này production đã kết thúc — quét thêm vật tư là
    /// nhập nhầm chỗ, không phải nghiệp vụ thật.</summary>
    private static readonly string[] ClosedPhases =
        { "FQC_PENDING", "OQC_PENDING", "DONE", "SHIPPED", "CANCELLED" };

    public MaterialLotScanService(
        IMesDbContext db, IAuditWriter audit, IOptions<MaterialLotOptions> opts)
    {
        _db = db;
        _audit = audit;
        _opts = opts.Value;
    }

    // ── Quét tiêu thụ ─────────────────────────────────────────────

    /// <summary>
    /// <c>POST /work-orders/{id}/materials/{bomLineIdx}/consume</c>.
    ///
    /// Thứ tự kiểm cố định (§5): <c>cú pháp → prelude → tìm lô →
    /// part mismatch → Rejected → Expired → status≠Released → đủ số lượng →
    /// ghi + optimistic lock</c>. Bốn kiểm giữa nằm trong
    /// <see cref="MaterialLotStatusPolicy.CanConsume"/> để test được rời.
    /// </summary>
    public async Task<MaterialLotOutcome> ConsumeAsync(
        long woId, int bomLineIdx, string? rawLotNo, double qtyUsed, long? legId,
        string actor, string role, string? ifMatch = null, CancellationToken ct = default)
    {
        // 1. Cú pháp — chưa chạm nghiệp vụ nên KHÔNG emit audit (§5).
        var lotNo = MaterialLotStatusPolicy.Normalize(rawLotNo);
        if (lotNo.Length == 0)
            return MaterialLotOutcome.Fail(422, MaterialLotStatusPolicy.InvalidRequest,
                "lot_no is required.");
        if (lotNo.Length > 64)
            return MaterialLotOutcome.Fail(422, MaterialLotStatusPolicy.InvalidRequest,
                "lot_no must be 64 characters or fewer.");
        if (double.IsNaN(qtyUsed) || double.IsInfinity(qtyUsed) || qtyUsed <= 0)
            return MaterialLotOutcome.Fail(422, MaterialLotStatusPolicy.InvalidRequest,
                "qty_used must be greater than zero.");

        // 2. Prelude — WO + dòng BOM + phase.
        var wo = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == woId, ct);
        if (wo is null)
            return MaterialLotOutcome.Fail(404, "wo.not_found", $"No work order with id {woId}.");

        var row = await _db.WoMaterials
            .FirstOrDefaultAsync(m => m.WorkOrderId == woId && m.BomLineIdx == bomLineIdx, ct);
        if (row is null)
            return MaterialLotOutcome.Fail(404, "wo.material_row_not_found",
                $"No wo_materials row for wo_id={woId}, bom_line_idx={bomLineIdx}.");

        if (ClosedPhases.Contains(wo.MesPhase))
        {
            await DenyAsync(wo, bomLineIdx, lotNo, null, "wo.invalid_phase", actor, role, legId);
            return MaterialLotOutcome.Fail(422, "wo.invalid_phase",
                $"Material cannot be consumed once the WO reaches {wo.MesPhase}.");
        }

        // 3. Tìm lô. So khớp bằng `==` thuần: cột LotNo mang COLLATE NOCASE nên
        //    SQLite tự so không phân biệt hoa thường. KHÔNG dùng
        //    EF.Functions.Collate(...) — thừa ở đây và phá cổng sang SQL Server.
        var lot = await _db.MaterialLots.FirstOrDefaultAsync(l => l.LotNo == lotNo, ct);
        if (lot is null)
        {
            await DenyAsync(wo, bomLineIdx, lotNo, null,
                MaterialLotStatusPolicy.NotFound, actor, role, legId);
            return MaterialLotOutcome.Fail(404, MaterialLotStatusPolicy.NotFound,
                $"No material lot '{lotNo}'.");
        }

        // 3b. If-Match (tuỳ chọn) — client muốn khoá chặt thì gửi ETag của LÔ.
        //     Không gửi thì vẫn an toàn: RowVersion ở bước ghi vẫn bắt cuộc đua.
        if (!string.IsNullOrWhiteSpace(ifMatch))
        {
            var serverTag = Convert.ToBase64String(lot.RowVersion);
            if (!string.Equals(serverTag, NormalizeETag(ifMatch), StringComparison.Ordinal))
                return await ConflictAsync(wo, lot, actor, role, "material_lot_consume");
        }

        // 4-7. Bốn điểm chặn theo đúng thứ tự — part mismatch TRƯỚC status.
        var verdict = MaterialLotStatusPolicy.CanConsume(
            lot, row.MaterialCode, qtyUsed, DateTime.UtcNow);

        string? warning = null;
        if (!verdict.Allowed)
        {
            // Grace period: part mismatch và depleted vẫn CHẶN THẬT dù cờ tắt —
            // đó là sai vật tư / sai số lượng, không phải "kho chưa kịp làm IQC".
            // Chỉ ba mã trạng thái được nới, vì chúng phụ thuộc việc kho đã nhập
            // liệu IQC đầy đủ chưa.
            var relaxable = verdict.ErrorCode is MaterialLotStatusPolicy.NotReleased
                or MaterialLotStatusPolicy.Expired or MaterialLotStatusPolicy.Rejected;

            if (_opts.EnforceReleased || !relaxable)
            {
                await DenyAsync(wo, bomLineIdx, lotNo, lot, verdict.ErrorCode!, actor, role, legId);
                return MaterialLotOutcome.Fail(422, verdict.ErrorCode!, verdict.MessageEn!);
            }

            // Nới trạng thái KHÔNG kéo theo nới số lượng. CanConsume dừng ngay ở
            // bước trạng thái, nên một lô đã Consumed cũng ra 'not_released';
            // nếu cho qua thì mỗi lần quét lại trừ tiếp một cuộn đã hết. Phải
            // kiểm lại tồn một cách độc lập ở đây.
            var qtyVerdict = MaterialLotStatusPolicy.CheckQuantity(lot, qtyUsed);
            if (!qtyVerdict.Allowed)
            {
                await DenyAsync(wo, bomLineIdx, lotNo, lot, qtyVerdict.ErrorCode!, actor, role, legId);
                return MaterialLotOutcome.Fail(422, qtyVerdict.ErrorCode!, qtyVerdict.MessageEn!);
            }
            warning = verdict.ErrorCode;
        }

        // 8. Ghi + optimistic lock.
        var now = DateTime.UtcNow;
        var consumption = new WoMaterialConsumption
        {
            WoId = woId,
            LegId = legId,
            WoMaterialId = row.Id,
            MaterialLotId = lot.Id,
            QtyUsed = qtyUsed,
            Uom = row.Uom ?? lot.Uom,
            ScannedBy = actor,
            ScannedAt = now,
            CreatedAt = now,
            CreatedBy = actor,
        };
        _db.WoMaterialConsumptions.Add(consumption);

        lot.QtyAvailable = Math.Max(0, lot.QtyAvailable - qtyUsed);
        if (lot.QtyAvailable <= 0
            && string.Equals(lot.Status, nameof(MaterialLotStatus.Released), StringComparison.OrdinalIgnoreCase))
        {
            lot.Status = nameof(MaterialLotStatus.Consumed);
            lot.StatusChangedBy = actor;
            lot.StatusChangedAt = now;
        }
        lot.UpdatedAt = now;
        lot.UpdatedBy = actor;

        // Mirror hiển thị (§3.3): server ghi LotNo TỪ ENTITY LÔ, không từ chuỗi
        // operator gõ — nhờ đó kiểu chữ luôn khớp nhãn đã nhập kho. FK
        // MaterialLotId mới là thứ mang nghĩa; LotNo chỉ là nhãn.
        SetLotFk(_db, row, lot.Id);
        row.LotNo = lot.LotNo;
        row.UpdatedAt = now;
        row.UpdatedBy = actor;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return await ConflictAsync(wo, lot, actor, role, "material_lot_consume");
        }

        await _audit.EmitAsync(
            MaterialLotAuditAction.MaterialLotConsume, actor, role,
            targetType: "MaterialLot", targetId: lot.Id.ToString(),
            detail: JsonSerializer.Serialize(new
            {
                wo_id = woId,
                wo_no = wo.WoNo,
                bom_line_idx = bomLineIdx,
                leg_id = legId,
                lot_no = lot.LotNo,
                material_lot_id = lot.Id,
                part_no = lot.PartNo,
                qty_used = qtyUsed,
                qty_available_after = lot.QtyAvailable,
                lot_status = lot.Status,
                consumption_id = consumption.Id,
                warning,
                enforced = _opts.EnforceReleased,
            }));

        return new MaterialLotOutcome
        {
            Ok = true,
            HttpStatus = 200,
            Warning = warning,
            ConsumptionId = consumption.Id,
            MaterialLotId = lot.Id,
            LotNo = lot.LotNo,
            LotStatus = lot.Status,
            QtyAvailableAfter = lot.QtyAvailable,
            Enforced = _opts.EnforceReleased,
        };
    }

    // ── Gắn nhãn lô cho PREPRESS (đường ghi cũ, §3.3) ─────────────

    /// <summary>
    /// Đường ghi <c>PUT /materials/{idx}</c> cũ: operator vẫn gõ mã lô trong ô
    /// nhập, nhưng chuỗi đó KHÔNG còn được gán thẳng vào cột nữa.
    ///
    /// <para><b>Vì sao đi qua đây.</b> Trước A1, <c>PrepressController</c> ghi
    /// <c>row.LotNo = req.LotNo</c> — chuỗi tự do, không qua một lớp kiểm nào,
    /// và đo trên live cho ra 5 chuỗi lô khớp được với <b>0</b> bản ghi khác.
    /// Bây giờ chuỗi được chuẩn hoá, được thử RESOLVE về một
    /// <see cref="MaterialLot"/> thật; resolve được thì set FK
    /// <c>MaterialLotId</c> và mirror <c>LotNo</c> <b>từ entity lô</b> (kiểu chữ
    /// theo nhãn đã nhập kho, không theo cách operator gõ).</para>
    ///
    /// <para>Chưa resolve được thì <b>vẫn giữ chuỗi</b> làm nhãn hiển thị và trả
    /// <c>warning</c> — siết thành read-only là Phase 3, sau go-live. Khi cờ
    /// <c>EnforceReleased</c> bật thì chuỗi không resolve được bị TỪ CHỐI.</para>
    ///
    /// <para><b>KHÔNG gọi SaveChanges</b> — chỉ mutate entity đang được track,
    /// để controller giữ nguyên một lần commit duy nhất (rollup + WO touch +
    /// dòng con nằm trong cùng một transaction).</para>
    /// </summary>
    public async Task<MaterialLotOutcome> AttachLotLabelAsync(
        WoMaterial row, string? rawLotNo, string actor, string role, CancellationToken ct = default)
    {
        var lotNo = MaterialLotStatusPolicy.Normalize(rawLotNo);
        if (lotNo.Length == 0) return new MaterialLotOutcome { Ok = true, Enforced = _opts.EnforceReleased };
        if (lotNo.Length > 64)
            return MaterialLotOutcome.Fail(422, MaterialLotStatusPolicy.InvalidRequest,
                "lot_no must be 64 characters or fewer.");

        var lot = await _db.MaterialLots.FirstOrDefaultAsync(l => l.LotNo == lotNo, ct);
        if (lot is null)
        {
            if (_opts.EnforceReleased)
                return MaterialLotOutcome.Fail(422, MaterialLotStatusPolicy.NotFound,
                    $"No material lot '{lotNo}'. Register it at IQC before loading.");

            // Grace period — giữ nhãn, đếm được số ca sẽ bị chặn khi lật cờ.
            row.LotNo = lotNo;
            return new MaterialLotOutcome
            {
                Ok = true, LotNo = lotNo,
                Warning = MaterialLotStatusPolicy.NotFound,
                Enforced = false,
            };
        }

        var verdict = MaterialLotStatusPolicy.CanConsume(lot, row.MaterialCode, 0, DateTime.UtcNow);
        if (!verdict.Allowed && _opts.EnforceReleased
            && verdict.ErrorCode != MaterialLotStatusPolicy.Depleted)
        {
            return MaterialLotOutcome.Fail(422, verdict.ErrorCode!, verdict.MessageEn!);
        }

        SetLotFk(_db, row, lot.Id);
        row.LotNo = lot.LotNo;
        return new MaterialLotOutcome
        {
            Ok = true,
            MaterialLotId = lot.Id,
            LotNo = lot.LotNo,
            LotStatus = lot.Status,
            QtyAvailableAfter = lot.QtyAvailable,
            Warning = verdict.Allowed ? null : verdict.ErrorCode,
            Enforced = _opts.EnforceReleased,
        };
    }

    // ── Đảo tiêu thụ (Đ3 — Supervisor) ────────────────────────────

    /// <summary>
    /// Đánh dấu một lần quét là đã đảo. <b>KHÔNG DELETE, KHÔNG sửa
    /// <c>QtyUsed</c></b> — dòng sai vẫn phải nhìn thấy được. Trả số lượng về
    /// lô; nếu tồn quay lên &gt; 0 thì lô <c>Consumed</c> trở lại
    /// <c>Released</c>.
    /// </summary>
    public async Task<MaterialLotOutcome> ReverseAsync(
        long consumptionId, string? reason, string actor, string role,
        CancellationToken ct = default)
    {
        if (!ReverseRoles.Contains(role))
            return MaterialLotOutcome.Fail(403, MaterialLotStatusPolicy.Forbidden,
                "Only a Supervisor (or Admin) can reverse a material consumption.");

        var trimmed = (reason ?? "").Trim();
        if (trimmed.Length is 0 or > 500)
            return MaterialLotOutcome.Fail(422, MaterialLotStatusPolicy.InvalidRequest,
                "A reversal reason of 1-500 characters is required.");

        var c = await _db.WoMaterialConsumptions.FirstOrDefaultAsync(x => x.Id == consumptionId, ct);
        if (c is null)
            return MaterialLotOutcome.Fail(404, MaterialLotStatusPolicy.NotFound,
                $"No consumption row {consumptionId}.");
        if (c.ReversedAt is not null)
            return MaterialLotOutcome.Fail(422, MaterialLotStatusPolicy.AlreadyReversed,
                "That consumption was already reversed.");

        var lot = await _db.MaterialLots.FirstOrDefaultAsync(l => l.Id == c.MaterialLotId, ct);
        if (lot is null)
            return MaterialLotOutcome.Fail(404, MaterialLotStatusPolicy.NotFound,
                "The lot behind that consumption is missing.");

        var now = DateTime.UtcNow;
        c.ReversedAt = now;
        c.ReversedBy = actor;
        c.ReversedReason = trimmed;
        c.UpdatedAt = now;
        c.UpdatedBy = actor;

        lot.QtyAvailable += c.QtyUsed;
        lot.Status = MaterialLotStatusPolicy.StatusAfterReversal(lot, lot.QtyAvailable);
        lot.StatusChangedBy = actor;
        lot.StatusChangedAt = now;
        lot.UpdatedAt = now;
        lot.UpdatedBy = actor;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return await ConflictAsync(null, lot, actor, role, "material_lot_reverse");
        }

        await _audit.EmitAsync(
            MaterialLotAuditAction.MaterialLotReverse, actor, role,
            targetType: "MaterialLot", targetId: lot.Id.ToString(),
            detail: JsonSerializer.Serialize(new
            {
                wo_id = c.WoId,
                leg_id = c.LegId,
                consumption_id = c.Id,
                material_lot_id = lot.Id,
                lot_no = lot.LotNo,
                qty_returned = c.QtyUsed,
                qty_available_after = lot.QtyAvailable,
                lot_status = lot.Status,
                reason = trimmed,
            }));

        return new MaterialLotOutcome
        {
            Ok = true,
            ConsumptionId = c.Id,
            MaterialLotId = lot.Id,
            LotNo = lot.LotNo,
            LotStatus = lot.Status,
            QtyAvailableAfter = lot.QtyAvailable,
            Enforced = _opts.EnforceReleased,
        };
    }

    // ── Đổi trạng thái lô (QC / Supervisor / Admin) ───────────────

    public async Task<MaterialLotOutcome> SetStatusAsync(
        long lotId, string? rawStatus, string? reason, string actor, string role,
        CancellationToken ct = default)
    {
        if (!LotStatusRoles.Contains(role))
            return MaterialLotOutcome.Fail(403, MaterialLotStatusPolicy.Forbidden,
                "Only QC, Supervisor or Admin can change a lot status.");

        var status = MaterialLotStatusPolicy.ParseStatus(rawStatus);
        if (status is null)
            return MaterialLotOutcome.Fail(422, MaterialLotStatusPolicy.InvalidStatus,
                "Status must be one of Quarantine / Released / Rejected / Consumed / Expired.");

        var lot = await _db.MaterialLots.FirstOrDefaultAsync(l => l.Id == lotId, ct);
        if (lot is null)
            return MaterialLotOutcome.Fail(404, MaterialLotStatusPolicy.NotFound,
                $"No material lot {lotId}.");

        // Rejected là terminal — trả hàng về không làm nó tốt lên.
        if (string.Equals(lot.Status, nameof(MaterialLotStatus.Rejected), StringComparison.OrdinalIgnoreCase)
            && status != nameof(MaterialLotStatus.Rejected))
        {
            return MaterialLotOutcome.Fail(422, MaterialLotStatusPolicy.Rejected,
                "A rejected lot is terminal; open a new lot instead.");
        }

        // Expired → Released chỉ đi qua đường gia hạn hai chữ ký (Đ3), không
        // đi tắt bằng endpoint đổi trạng thái.
        if (string.Equals(lot.Status, nameof(MaterialLotStatus.Expired), StringComparison.OrdinalIgnoreCase)
            && status == nameof(MaterialLotStatus.Released))
        {
            return MaterialLotOutcome.Fail(422, MaterialLotStatusPolicy.InvalidStatus,
                "Use the expiry-extension endpoint (two distinct signers) to release an expired lot.");
        }

        var now = DateTime.UtcNow;
        var from = lot.Status;
        lot.Status = status;
        lot.StatusReason = (reason ?? "").Trim() is { Length: > 0 } r ? r : null;
        lot.StatusChangedBy = actor;
        lot.StatusChangedAt = now;
        lot.UpdatedAt = now;
        lot.UpdatedBy = actor;

        // Kiểm lại lô hết hạn = chữ ký 1 của luật hai vai (Đ3).
        if (string.Equals(from, nameof(MaterialLotStatus.Expired), StringComparison.OrdinalIgnoreCase)
            && status == nameof(MaterialLotStatus.Quarantine))
        {
            lot.RetestedAt = now;
            lot.RetestedBy = actor;
        }

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return await ConflictAsync(null, lot, actor, role, "material_lot_status_set");
        }

        await _audit.EmitAsync(
            MaterialLotAuditAction.MaterialLotStatusSet, actor, role,
            targetType: "MaterialLot", targetId: lot.Id.ToString(),
            detail: JsonSerializer.Serialize(new
            {
                material_lot_id = lot.Id,
                lot_no = lot.LotNo,
                part_no = lot.PartNo,
                from_status = from,
                to_status = lot.Status,
                reason = lot.StatusReason,
                retested_by = lot.RetestedBy,
            }));

        return new MaterialLotOutcome
        {
            Ok = true, MaterialLotId = lot.Id, LotNo = lot.LotNo,
            LotStatus = lot.Status, QtyAvailableAfter = lot.QtyAvailable,
            Enforced = _opts.EnforceReleased,
        };
    }

    // ── Gia hạn lô hết hạn (Đ3 — hai vai khác nhau) ───────────────

    public async Task<MaterialLotOutcome> ExtendExpiryAsync(
        long lotId, DateTime? newExpiry, string? reason, string actor, string role,
        CancellationToken ct = default)
    {
        if (!LotStatusRoles.Contains(role))
            return MaterialLotOutcome.Fail(403, MaterialLotStatusPolicy.Forbidden,
                "Only QC, Supervisor or Admin can extend a lot expiry.");
        if (newExpiry is null)
            return MaterialLotOutcome.Fail(422, MaterialLotStatusPolicy.InvalidRequest,
                "expiry_extended_to is required.");

        var lot = await _db.MaterialLots.FirstOrDefaultAsync(l => l.Id == lotId, ct);
        if (lot is null)
            return MaterialLotOutcome.Fail(404, MaterialLotStatusPolicy.NotFound,
                $"No material lot {lotId}.");

        var now = DateTime.UtcNow;
        var verdict = MaterialLotStatusPolicy.CanExtendExpiry(lot, actor, newExpiry.Value, now);
        if (!verdict.Allowed)
        {
            await _audit.EmitAsync(
                MaterialLotAuditAction.MaterialLotScanDenied, actor, role,
                targetType: "MaterialLot", targetId: lot.Id.ToString(),
                detail: JsonSerializer.Serialize(new
                {
                    material_lot_id = lot.Id, lot_no = lot.LotNo,
                    attempted_action = "material_lot_expiry_extend",
                    error_code = verdict.ErrorCode, retested_by = lot.RetestedBy,
                }));
            return MaterialLotOutcome.Fail(422, verdict.ErrorCode!, verdict.MessageEn!);
        }

        var fromStatus = lot.Status;
        lot.ExpiryExtendedTo = newExpiry;
        lot.ExpiryExtendedBy = actor;
        lot.ExpiryAt = newExpiry;
        lot.Status = nameof(MaterialLotStatus.Released);
        lot.StatusReason = (reason ?? "").Trim() is { Length: > 0 } r ? r : null;
        lot.StatusChangedBy = actor;
        lot.StatusChangedAt = now;
        lot.UpdatedAt = now;
        lot.UpdatedBy = actor;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return await ConflictAsync(null, lot, actor, role, "material_lot_expiry_extend");
        }

        await _audit.EmitAsync(
            MaterialLotAuditAction.MaterialLotExpiryExtended, actor, role,
            targetType: "MaterialLot", targetId: lot.Id.ToString(),
            detail: JsonSerializer.Serialize(new
            {
                material_lot_id = lot.Id, lot_no = lot.LotNo, part_no = lot.PartNo,
                from_status = fromStatus, to_status = lot.Status,
                retested_by = lot.RetestedBy, approved_by = actor,
                expiry_extended_to = lot.ExpiryExtendedTo, reason = lot.StatusReason,
            }));

        return new MaterialLotOutcome
        {
            Ok = true, MaterialLotId = lot.Id, LotNo = lot.LotNo,
            LotStatus = lot.Status, QtyAvailableAfter = lot.QtyAvailable,
            Enforced = _opts.EnforceReleased,
        };
    }

    // ── Nhập lô vào kho (Đ2 — ExpiryAt nhập tay lúc làm IQC) ──────

    public async Task<MaterialLotOutcome> CreateLotAsync(
        string? rawLotNo, string? rawPartNo, double qtyReceived, string? uom,
        DateTime? expiryAt, long? iqcInspectionId, string? supplierName,
        string? supplierLotNo, string actor, string role, CancellationToken ct = default)
    {
        if (!LotStatusRoles.Contains(role))
            return MaterialLotOutcome.Fail(403, MaterialLotStatusPolicy.Forbidden,
                "Only QC, Supervisor or Admin can register an incoming lot.");

        var lotNo = MaterialLotStatusPolicy.Normalize(rawLotNo);
        var partNo = MaterialLotStatusPolicy.Normalize(rawPartNo);
        if (lotNo.Length is 0 or > 64 || partNo.Length is 0 or > 64)
            return MaterialLotOutcome.Fail(422, MaterialLotStatusPolicy.InvalidRequest,
                "lot_no and part_no are required (1-64 characters).");
        if (double.IsNaN(qtyReceived) || double.IsInfinity(qtyReceived) || qtyReceived <= 0)
            return MaterialLotOutcome.Fail(422, MaterialLotStatusPolicy.InvalidRequest,
                "qty_received must be greater than zero.");

        var rawMaterialId = await _db.RawMaterials
            .Where(r => r.PartNo == partNo).Select(r => (long?)r.Id).FirstOrDefaultAsync(ct);

        var now = DateTime.UtcNow;
        var lot = new MaterialLot
        {
            LotNo = lotNo,
            PartNo = partNo,
            RawMaterialId = rawMaterialId,
            IqcInspectionId = iqcInspectionId,
            SupplierName = supplierName?.Trim(),
            SupplierLotNo = MaterialLotStatusPolicy.Normalize(supplierLotNo) is { Length: > 0 } s ? s : null,
            ReceivedAt = now,
            ExpiryAt = expiryAt,
            QtyReceived = qtyReceived,
            QtyAvailable = qtyReceived,
            Uom = uom?.Trim(),
            Status = nameof(MaterialLotStatus.Quarantine),
            StatusChangedBy = actor,
            StatusChangedAt = now,
            CreatedAt = now,
            CreatedBy = actor,
        };
        _db.MaterialLots.Add(lot);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Trúng partial unique index — cùng LotNo cho cùng part (hoặc cùng
            // part chưa resolve). Đây chính là chỗ COLLATE NOCASE làm việc:
            // "LOT-001" và "lot-001" là MỘT lô, không phải hai.
            return MaterialLotOutcome.Fail(409, "lot.duplicate",
                $"Lot '{lotNo}' already exists for part '{partNo}'.");
        }

        await _audit.EmitAsync(
            MaterialLotAuditAction.MaterialLotStatusSet, actor, role,
            targetType: "MaterialLot", targetId: lot.Id.ToString(),
            detail: JsonSerializer.Serialize(new
            {
                material_lot_id = lot.Id, lot_no = lot.LotNo, part_no = lot.PartNo,
                raw_material_id = lot.RawMaterialId, iqc_inspection_id = lot.IqcInspectionId,
                from_status = (string?)null, to_status = lot.Status,
                qty_received = lot.QtyReceived, expiry_at = lot.ExpiryAt,
            }));

        return new MaterialLotOutcome
        {
            Ok = true, MaterialLotId = lot.Id, LotNo = lot.LotNo,
            LotStatus = lot.Status, QtyAvailableAfter = lot.QtyAvailable,
            Enforced = _opts.EnforceReleased,
        };
    }

    // ── Truy vấn mạch lô — mọi JOIN đi qua KHOÁ SỐ ────────────────

    /// <summary>
    /// WO → WoMaterialConsumptions → MaterialLots → IqcInspections →
    /// IqcResultDetails. <b>Không có một phép nối theo chuỗi lô nào</b> — đó là
    /// toàn bộ lý do A1 tồn tại, và <c>gate-material-lot-fk.sh</c> canh lại.
    ///
    /// <para>Trả về <b>từng lần quét</b> (đúng Đ4), kèm
    /// <c>QtyUsedTotalForLot</c> = <c>SUM(QtyUsed)</c> theo
    /// <c>(WoMaterialId, MaterialLotId)</c> — con số "đã tiêu hao bao nhiêu"
    /// KHÔNG được lấy từ dòng cuối.</para>
    /// </summary>
    public async Task<List<MaterialGenealogyRow>> GenealogyAsync(long woId, CancellationToken ct = default)
    {
        var rows = await (
            from c in _db.WoMaterialConsumptions.AsNoTracking()
            join m in _db.WoMaterials.AsNoTracking() on c.WoMaterialId equals m.Id
            join l in _db.MaterialLots.AsNoTracking() on c.MaterialLotId equals l.Id
            where c.WoId == woId
            select new MaterialGenealogyRow
            {
                ConsumptionId = c.Id,
                BomLineIdx = m.BomLineIdx,
                MaterialCode = m.MaterialCode,
                LegId = c.LegId,
                MaterialLotId = l.Id,
                LotNo = l.LotNo,
                PartNo = l.PartNo,
                LotStatus = l.Status,
                ExpiryAt = l.ExpiryAt,
                SupplierName = l.SupplierName,
                IqcInspectionId = l.IqcInspectionId,
                QtyUsed = c.QtyUsed,
                Uom = c.Uom,
                ScannedBy = c.ScannedBy,
                ScannedAt = c.ScannedAt,
                ReversedAt = c.ReversedAt,
                ReversedBy = c.ReversedBy,
            }).OrderBy(r => r.BomLineIdx).ThenBy(r => r.ScannedAt).ToListAsync(ct);

        var lotIds = rows.Where(r => r.IqcInspectionId != null)
            .Select(r => r.IqcInspectionId!.Value).Distinct().ToList();
        if (lotIds.Count > 0)
        {
            var fails = await _db.IqcResultDetails.AsNoTracking()
                .Where(d => lotIds.Contains(d.IqcInspectionId) && !d.Pass)
                .Select(d => new { d.IqcInspectionId, d.DefectCode })
                .ToListAsync(ct);
            var byInspection = fails.GroupBy(f => f.IqcInspectionId).ToDictionary(
                g => g.Key,
                g => (Count: g.Count(),
                      Codes: string.Join(",", g.Select(x => x.DefectCode ?? "?").Distinct())));
            foreach (var r in rows)
            {
                if (r.IqcInspectionId is not null
                    && byInspection.TryGetValue(r.IqcInspectionId.Value, out var agg))
                {
                    r.IqcFailCount = agg.Count;
                    r.IqcFailCodes = agg.Codes;
                }
            }
        }

        // Đ4 hệ quả 3 — tổng theo (WoMaterialId, MaterialLotId), bỏ dòng đã đảo.
        var totals = rows.Where(r => r.ReversedAt is null)
            .GroupBy(r => (r.BomLineIdx, r.MaterialLotId))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.QtyUsed));
        foreach (var r in rows)
            r.QtyUsedTotalForLot = totals.TryGetValue((r.BomLineIdx, r.MaterialLotId), out var t) ? t : 0;

        return rows;
    }

    // ── Nội bộ ────────────────────────────────────────────────────

    private async Task DenyAsync(
        WorkOrder wo, int bomLineIdx, string lotNo, MaterialLot? lot,
        string errorCode, string actor, string role, long? legId)
    {
        // Mọi ca từ chối đều để lại vết. "Ai đã cố nạp lô chưa Released, lên WO
        // nào, lúc nào" là dữ liệu điều tra chất lượng, không phải noise.
        await _audit.EmitAsync(
            MaterialLotAuditAction.MaterialLotScanDenied, actor, role,
            targetType: "WorkOrder", targetId: wo.Id.ToString(),
            detail: JsonSerializer.Serialize(new
            {
                wo_id = wo.Id,
                wo_no = wo.WoNo,
                bom_line_idx = bomLineIdx,
                leg_id = legId,
                lot_no = lotNo,
                material_lot_id = lot?.Id,
                part_no = lot?.PartNo,
                lot_status = lot?.Status,
                error_code = errorCode,
                enforced = _opts.EnforceReleased,
            }));
    }

    private async Task<MaterialLotOutcome> ConflictAsync(
        WorkOrder? wo, MaterialLot lot, string actor, string role, string attemptedAction)
    {
        // L45 — nhánh SaveChanges CŨNG phải để lại vết, và emit PHẢI đứng SAU
        // ChangeTracker.Clear(): audit writer dùng CHUNG DbContext scoped của
        // request; tracker còn bẩn thì SaveChanges của audit kéo theo UPDATE đã
        // fail và ném lại, nuốt mất dòng audit lần nữa.
        if (_db is DbContext ctx) ctx.ChangeTracker.Clear();

        var fresh = await _db.MaterialLots.AsNoTracking()
            .Where(l => l.Id == lot.Id)
            .Select(l => new { l.RowVersion, l.QtyAvailable, l.Status })
            .FirstOrDefaultAsync();

        await _audit.EmitAsync(
            AuditAction.WoStateConflict, actor, role,
            targetType: "MaterialLot", targetId: lot.Id.ToString(),
            detail: JsonSerializer.Serialize(new
            {
                wo_id = wo?.Id,
                wo_no = wo?.WoNo,
                material_lot_id = lot.Id,
                lot_no = lot.LotNo,
                attempted_action = attemptedAction,
                server_version = fresh is null ? "" : Convert.ToBase64String(fresh.RowVersion),
                source = "ef_concurrency",
            }));

        return new MaterialLotOutcome
        {
            Ok = false,
            HttpStatus = 409,
            ErrorCode = MaterialLotStatusPolicy.Conflict,
            MessageEn = "Another operator changed this lot first. Re-scan to see the current quantity.",
            MaterialLotId = lot.Id,
            LotNo = lot.LotNo,
            LotStatus = fresh?.Status,
            QtyAvailableAfter = fresh?.QtyAvailable ?? 0,
            Enforced = _opts.EnforceReleased,
        };
    }

    /// <summary>
    /// Ghi FK <c>WoMaterials.MaterialLotId</c>. Cột này là SHADOW PROPERTY (đúng
    /// tiền lệ <c>WoLegId</c>): sống ở model chứ không ở file entity, nên A1
    /// KHÔNG phải chạm <c>PrepressChecks.cs</c> — giữ đúng luật additive. Đổi lại
    /// phải đi qua ChangeTracker, và <c>IMesDbContext</c> không lộ
    /// <c>Entry</c> nên cần ép kiểu ở đây.
    /// </summary>
    internal static void SetLotFk(IMesDbContext db, WoMaterial row, long lotId)
    {
        if (db is DbContext ctx)
            ctx.Entry(row).Property("MaterialLotId").CurrentValue = lotId;
    }

    private static string NormalizeETag(string raw)
    {
        var s = raw.Trim();
        if (s.StartsWith("W/", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"') s = s.Substring(1, s.Length - 2);
        return s;
    }
}

/// <summary>A1 — một dòng của bảng truy xuất mạch lô (một LẦN QUÉT, Đ4).</summary>
public sealed class MaterialGenealogyRow
{
    public long ConsumptionId { get; set; }
    public int BomLineIdx { get; set; }
    public string MaterialCode { get; set; } = "";
    public long? LegId { get; set; }
    public long MaterialLotId { get; set; }
    public string LotNo { get; set; } = "";
    public string PartNo { get; set; } = "";
    public string LotStatus { get; set; } = "";
    public DateTime? ExpiryAt { get; set; }
    public string? SupplierName { get; set; }
    public long? IqcInspectionId { get; set; }
    public int IqcFailCount { get; set; }
    public string? IqcFailCodes { get; set; }
    public double QtyUsed { get; set; }

    /// <summary>SUM(QtyUsed) theo (WoMaterialId, MaterialLotId), bỏ dòng đã
    /// đảo — Đ4 hệ quả 3. KHÔNG lấy từ dòng cuối.</summary>
    public double QtyUsedTotalForLot { get; set; }

    public string? Uom { get; set; }
    public string ScannedBy { get; set; } = "";
    public DateTime ScannedAt { get; set; }
    public DateTime? ReversedAt { get; set; }
    public string? ReversedBy { get; set; }
}
