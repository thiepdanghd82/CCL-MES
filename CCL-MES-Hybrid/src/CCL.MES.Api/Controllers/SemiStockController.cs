using System.Security.Claims;
using System.Text.Json;
using CCL.MES.Application;
using CCL.MES.Application.Audit;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Entities;
using CCL.MES.Domain.Routing;
using CCL.MES.Shared;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.Routing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// P11.5-2 — Semi-Stock decoupling wire (keep-stock bán thành phẩm).
///   POST /api/v2/semi-lots                         post 1 lô (Semi WO done)
///   GET  /api/v2/semi-lots?kind=&spec=&status=     kho view (FEFO order)
///   POST /api/v2/work-orders/{id}/legs/{legId}/semi/reserve   assembly giữ lô
///   POST /api/v2/work-orders/{id}/legs/{legId}/semi/consume   assembly done
///
/// Concurrency reserve/consume: EF optimistic-lock trên <c>SemiLot.RowVersion</c>
/// (L38) — 2 assembly reserve cùng lô → 1 win, 1 × 409. Reserve atomic
/// (FEFO nhiều lô trong 1 SaveChanges; shortfall → 422, KHÔNG partial).
/// </summary>
[ApiController]
[Authorize]
[Route(ApiVersion.Prefix)]
public sealed class SemiStockController : ControllerBase
{
    private readonly IMesDbContext _db;
    private readonly IAuditWriter _audit;

    public SemiStockController(IMesDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    private (string actor, string role) Who() =>
        (User.FindFirstValue(ClaimTypes.Name) ?? "anonymous", User.FindFirstValue(ClaimTypes.Role) ?? "");
    private IActionResult Invalid(string c, string d) => UnprocessableEntity(ApiError.Of(c, d));
    private bool NeedIdem() => string.IsNullOrWhiteSpace(Request.Headers["Idempotency-Key"].ToString());

    // ── POST /semi-lots — post 1 lô vào kho ────────────────────────

    [HttpPost("semi-lots")]
    public async Task<IActionResult> PostLot([FromBody] PostSemiLotRequest? req, CancellationToken ct = default)
    {
        var (actor, role) = Who();
        if (NeedIdem()) return BadRequest(ApiError.Of("wo.idempotency_key_required", "Idempotency-Key header required."));
        if (req is null || string.IsNullOrWhiteSpace(req.LotNo)) return Invalid("semi.invalid_lot_no", "LotNo bắt buộc.");
        if (!Enum.TryParse<SemiKind>(req.SemiKind, out _)) return Invalid("semi.invalid_kind", "SemiKind phải PRINTED_SEMI | TAPE_SEMI.");
        if (req.Qty <= 0) return Invalid("semi.invalid_qty", "Qty phải > 0.");
        if (await _db.SemiLots.AnyAsync(l => l.LotNo == req.LotNo, ct))
            return Conflict(new SemiSetResponse { Ok = false, ErrorCode = "semi.lot_exists" });

        var lot = new SemiLot
        {
            LotNo = req.LotNo, SemiKind = req.SemiKind, SpecRevisionId = req.SpecRevisionId,
            SourceWorkOrderId = req.SourceWorkOrderId, QtyProduced = req.Qty, QtyAvailable = req.Qty,
            QtyReserved = 0, Status = nameof(SemiLotStatus.AVAILABLE), ExpiryAt = req.ExpiryAt, CreatedBy = actor,
        };
        _db.SemiLots.Add(lot);
        await _db.SaveChangesAsync(ct);
        await _audit.EmitAsync(AuditAction.SemiLotPost, actor, role, "SemiLot", lot.Id.ToString(),
            JsonSerializer.Serialize(new { lot_no = lot.LotNo, semi_kind = lot.SemiKind, source_wo_id = lot.SourceWorkOrderId, qty = lot.QtyProduced }));
        return Ok(new SemiSetResponse { Ok = true, SemiLotId = lot.Id, Allocated = lot.QtyProduced });
    }

    // ── GET /semi-lots — kho view (FEFO) ───────────────────────────

    [HttpGet("semi-lots")]
    public async Task<IActionResult> ListLots(string? kind = null, long? spec = null, string? status = null, CancellationToken ct = default)
    {
        var q = _db.SemiLots.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(kind)) q = q.Where(l => l.SemiKind == kind);
        if (spec is not null) q = q.Where(l => l.SpecRevisionId == spec);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(l => l.Status == status);

        var lots = await q.OrderBy(l => l.ExpiryAt ?? DateTime.MaxValue).ThenBy(l => l.CreatedAt).ThenBy(l => l.Id)
            .Select(l => new SemiLotRow
            {
                Id = l.Id, LotNo = l.LotNo, SemiKind = l.SemiKind, SpecRevisionId = l.SpecRevisionId,
                SourceWorkOrderId = l.SourceWorkOrderId, QtyProduced = l.QtyProduced, QtyAvailable = l.QtyAvailable,
                QtyReserved = l.QtyReserved, Status = l.Status, ExpiryAt = l.ExpiryAt,
                Expiring = l.ExpiryAt != null && l.ExpiryAt <= DateTime.UtcNow.AddDays(7),
            }).ToListAsync(ct);

        return Ok(new SemiStockView
        {
            Lots = lots,
            TotalAvailable = lots.Sum(l => l.QtyAvailable),
            TotalReserved = lots.Sum(l => l.QtyReserved),
        });
    }

    // ── POST /legs/{legId}/semi/reserve ────────────────────────────

    [HttpPost("work-orders/{id:long}/legs/{legId:long}/semi/reserve")]
    public async Task<IActionResult> Reserve(long id, long legId, [FromBody] ReserveSemiRequest? req, CancellationToken ct = default)
    {
        var (actor, role) = Who();
        if (NeedIdem()) return BadRequest(ApiError.Of("wo.idempotency_key_required", "Idempotency-Key header required."));
        if (req is null || req.Qty <= 0) return Invalid("semi.invalid_qty", "Qty phải > 0.");
        if (!Enum.TryParse<SemiKind>(req.SemiKind, out _)) return Invalid("semi.invalid_kind", "SemiKind không hợp lệ.");

        var leg = await _db.WoLegs.FirstOrDefaultAsync(l => l.Id == legId && l.WorkOrderId == id, ct);
        if (leg is null) return NotFound(ApiError.Of("leg.not_found", $"No leg {legId} on WO {id}."));
        if (!string.Equals(leg.LegKind, nameof(LegKind.ASSEMBLY), StringComparison.OrdinalIgnoreCase))
            return Invalid("semi.not_assembly", "Chỉ leg ASSEMBLY mới reserve semi từ kho.");
        if (string.Equals(leg.InputSource, nameof(InputSource.IN_LINE), StringComparison.OrdinalIgnoreCase))
            return Invalid("semi.leg_in_line", "Leg này là IN_LINE — không xuất kho (dùng semi trong cùng WO).");

        // Chọn lô: scan LotNo cụ thể, hoặc FEFO auto.
        List<SemiLot> lots;
        if (!string.IsNullOrWhiteSpace(req.LotNo))
        {
            var one = await _db.SemiLots.FirstOrDefaultAsync(l => l.LotNo == req.LotNo, ct);
            if (one is null) return Invalid("semi.lot_not_found", $"Không tìm thấy lô '{req.LotNo}'.");
            lots = new List<SemiLot> { one };
        }
        else
        {
            lots = await _db.SemiLots
                .Where(l => l.SemiKind == req.SemiKind && l.Status == nameof(SemiLotStatus.AVAILABLE) && l.QtyAvailable > 0)
                .ToListAsync(ct);
        }

        var plan = SemiStockAllocator.Fefo(lots, req.SemiKind, leg.SpecRevisionId, req.Qty);
        if (plan.Shortfall > 0)
            return Invalid("semi.insufficient_stock",
                $"Kho không đủ: cần {req.Qty}, khả dụng {plan.Allocated} (thiếu {plan.Shortfall}).");

        var lotIds = new List<long>();
        foreach (var pick in plan.Picks)
        {
            var lot = lots.First(l => l.Id == pick.SemiLotId);
            lot.QtyAvailable -= pick.Qty;
            lot.QtyReserved += pick.Qty;
            lot.UpdatedAt = DateTime.UtcNow; lot.UpdatedBy = actor;
            _db.SemiAllocations.Add(new SemiAllocation
            {
                WorkOrderId = id, AssemblyLegId = legId, SemiLotId = lot.Id, QtyReserved = pick.Qty, CreatedBy = actor,
            });
            lotIds.Add(lot.Id);
        }

        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            if (_db is DbContext ctx) ctx.ChangeTracker.Clear();
            return Conflict(new SemiSetResponse { Ok = false, ErrorCode = "semi.lot_conflict" });
        }

        await _audit.EmitAsync(AuditAction.SemiLotReserve, actor, role, "WoLeg", legId.ToString(),
            JsonSerializer.Serialize(new { wo_id = id, assembly_leg_id = legId, qty = plan.Allocated, lot_ids = lotIds }));
        return Ok(new SemiSetResponse { Ok = true, Allocated = plan.Allocated, Shortfall = 0, LotIds = lotIds });
    }

    // ── POST /legs/{legId}/semi/consume ────────────────────────────

    [HttpPost("work-orders/{id:long}/legs/{legId:long}/semi/consume")]
    public async Task<IActionResult> Consume(long id, long legId, CancellationToken ct = default)
    {
        var (actor, role) = Who();
        if (NeedIdem()) return BadRequest(ApiError.Of("wo.idempotency_key_required", "Idempotency-Key header required."));

        var allocs = await _db.SemiAllocations.Where(a => a.WorkOrderId == id && a.AssemblyLegId == legId && a.QtyReserved > 0).ToListAsync(ct);
        if (allocs.Count == 0) return Invalid("semi.nothing_reserved", "Không có semi nào đang reserve cho leg này.");

        int consumed = 0;
        var lotIds = new List<long>();
        foreach (var a in allocs)
        {
            var lot = await _db.SemiLots.FirstAsync(l => l.Id == a.SemiLotId, ct);
            lot.QtyReserved -= a.QtyReserved;
            lot.UpdatedAt = DateTime.UtcNow; lot.UpdatedBy = actor;
            if (lot.QtyAvailable == 0 && lot.QtyReserved == 0) lot.Status = nameof(SemiLotStatus.DEPLETED);
            a.QtyConsumed += a.QtyReserved; consumed += a.QtyReserved; a.QtyReserved = 0;
            lotIds.Add(lot.Id);
        }

        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            if (_db is DbContext ctx) ctx.ChangeTracker.Clear();
            return Conflict(new SemiSetResponse { Ok = false, ErrorCode = "semi.lot_conflict" });
        }

        await _audit.EmitAsync(AuditAction.SemiLotConsume, actor, role, "WoLeg", legId.ToString(),
            JsonSerializer.Serialize(new { wo_id = id, assembly_leg_id = legId, qty = consumed, lot_ids = lotIds }));
        return Ok(new SemiSetResponse { Ok = true, Allocated = consumed, LotIds = lotIds });
    }
}
