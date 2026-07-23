using System.Security.Claims;
using System.Text.Json;
using CCL.MES.Application;
using CCL.MES.Application.Audit;
using CCL.MES.Application.Services;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Entities;
using CCL.MES.Domain.Routing;
using CCL.MES.Domain.StateMachine;
using CCL.MES.Shared;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.Routing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// P11-2 — Multi-Method Routing DAG (fork-join) write surface.
///
/// Endpoints (all under /api/v2/work-orders):
///   GET  {id}/legs                       scan picker view (WO + legs + edges)
///   POST {id}/legs/materialize           fork: routing → leg DAG, PREPRESS → SPLIT
///   POST {id}/legs/{legId}/advance       tiến 1 leg 1 phase; join khi mọi terminal LEG_DONE
///   POST {id}/legs/{legId}/rework        reject 1 leg → PREPRESS (Q10)
///
/// Concurrency: materialize dùng If-Match trên WO.RowVersion; advance/rework
/// dùng If-Match trên LEG.RowVersion (per-leg — 2+ công nhân không đụng lock
/// nhau). Contract HTTP mirror RunningSurface (428/400/404/409/422/200).
/// Atomic: single SaveChanges; join cascade (SPLIT → FQC_PENDING) đi CÙNG
/// transaction với advance của leg cuối.
/// </summary>
[ApiController]
[Authorize]
[Route(ApiVersion.Prefix + "/work-orders")]
public sealed class RoutingController : ControllerBase
{
    private readonly IMesDbContext _db;
    private readonly IAuditWriter _audit;

    public RoutingController(IMesDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    // ── GET {id}/legs ──────────────────────────────────────────────

    [HttpGet("{id:long}/legs")]
    public async Task<IActionResult> GetLegs(long id, CancellationToken ct = default)
    {
        var wo = await _db.WorkOrders.AsNoTracking()
            .Include(w => w.Legs).Include(w => w.LegEdges)
            .FirstOrDefaultAsync(w => w.Id == id, ct);
        if (wo is null)
            return NotFound(ApiError.Of("wo.not_found", $"No work order with id {id}."));

        var view = new LegsView
        {
            WoId = wo.Id, WoNo = wo.WoNo, MesPhase = wo.MesPhase,
            WoETag = B64(wo.RowVersion), TargetQty = wo.TargetQty,
            Edges = wo.LegEdges.Select(e => new LegEdgeRow
            {
                LegId = e.LegId, DependsOnLegId = e.DependsOnLegId,
                DependencyGate = e.DependencyGate, RequiredQty = e.RequiredQty,
            }).ToList(),
        };
        foreach (var l in wo.Legs.OrderBy(l => l.Sequence))
        {
            var (soft, hard) = DependencyStatus(wo, l);
            view.Legs.Add(new LegRow
            {
                LegId = l.Id, Sequence = l.Sequence, LegKind = l.LegKind,
                Method = l.Method, ProcessLine = l.ProcessLine, LegPhase = l.LegPhase,
                SurfaceProfile = l.SurfaceProfile, InputSource = l.InputSource,
                SpecRevisionId = l.SpecRevisionId, QtyDoneCached = l.QtyDoneCached,
                QtyNgCached = l.QtyNgCached, LegDoneAt = l.LegDoneAt, LegETag = B64(l.RowVersion),
                IsTerminal = WorkOrderStateMachine.IsTerminalLeg(wo, l),
                SoftWaiting = soft, HardBlocked = hard,
            });
        }
        Response.Headers.ETag = $"\"{view.WoETag}\"";
        return Ok(view);
    }

    // ── POST {id}/legs/materialize ─────────────────────────────────

    [HttpPost("{id:long}/legs/materialize")]
    public async Task<IActionResult> Materialize(long id, CancellationToken ct = default)
    {
        var actor = User.FindFirstValue(ClaimTypes.Name) ?? "anonymous";
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";

        var (err, wo) = await WoPreludeAsync(id, actor, role, "legs_materialize");
        if (err is not null) return err;
        wo = await _db.WorkOrders.Include(w => w.Legs).Include(w => w.LegEdges)
            .FirstAsync(w => w.Id == id, ct);

        // Idempotent: đã fork rồi → trả trạng thái hiện tại (không tạo trùng).
        if (wo.Legs.Count > 0)
            return Ok(new LegMaterializeResponse
            {
                Ok = true, Forked = false, LegCount = wo.Legs.Count,
                MesPhase = wo.MesPhase, WoETag = B64(wo.RowVersion),
            });

        // Resolve routing → leg plan (đúng recipe IpqcReviewController).
        var productCode = await _db.Products.AsNoTracking()
            .Where(p => p.Id == wo.ProductId).Select(p => p.ProductCode).FirstOrDefaultAsync(ct);
        var ops = string.IsNullOrWhiteSpace(productCode)
            ? new List<RoutingLegResolver.RoutingOp>()
            : await _db.RoutingOperations.AsNoTracking().Where(r => r.PartNo == productCode)
                .Select(r => new RoutingLegResolver.RoutingOp(r.OpNo, r.Operation, r.WorkCenterNo, r.WorkCenterDescription))
                .ToListAsync(ct);
        var map = await _db.ProcessLegMaps.AsNoTracking().Where(m => m.Active)
            .Select(m => new RoutingLegResolver.MapEntry(m.MatchType, m.MatchValue, m.LegKind, m.Method, m.ProcessLine, m.Sort))
            .ToListAsync(ct);

        var plan = RoutingLegResolver.Resolve(ops, map);

        if (plan.Unmapped.Count > 0)
            return UnprocessableEntity(ApiError.Of("routing.unmapped",
                $"{plan.Unmapped.Count} op không map được leg — cần người duyệt: {string.Join(" | ", plan.Unmapped)}"));

        // <2 leg = WO tuyến tính (T1 / không đủ routing) → KHÔNG fork.
        if (plan.Legs.Count < 2)
            return Ok(new LegMaterializeResponse
            {
                Ok = true, Forked = false, LegCount = plan.Legs.Count,
                MesPhase = wo.MesPhase, WoETag = B64(wo.RowVersion),
                Unmapped = plan.Unmapped.ToList(),
            });

        // Phase 1: tạo leg (cần Id trước khi dựng cạnh).
        foreach (var node in plan.Legs)
            wo.Legs.Add(new WoLeg
            {
                WorkOrderId = wo.Id, Sequence = node.Sequence, LegKind = node.LegKind,
                Method = node.Method, ProcessLine = node.ProcessLine,
                SurfaceProfile = SurfaceFor(node.LegKind), InputSource = nameof(InputSource.IN_LINE),
                LegPhase = nameof(LegPhase.PREPRESS), CreatedBy = actor,
            });

        if (_db is DbContext ctx1)
        {
            await using var tx = await ctx1.Database.BeginTransactionAsync(ct);
            await _db.SaveChangesAsync();   // legs → Ids

            var seqToId = wo.Legs.ToDictionary(l => l.Sequence, l => l.Id);
            foreach (var e in plan.Edges)
                wo.LegEdges.Add(new WoLegDependency
                {
                    WorkOrderId = wo.Id, LegId = seqToId[e.ToSeq], DependsOnLegId = seqToId[e.FromSeq],
                    DependencyGate = e.Gate.ToString(),
                    RequiredQty = e.Gate == DependencyGate.HARD ? wo.TargetQty : 0,
                    CreatedBy = actor,
                });

            var (ok, dagErr) = RoutingDagValidator.IsValid(wo);
            if (!ok)
            {
                await tx.RollbackAsync(ct);
                return UnprocessableEntity(ApiError.Of("routing.invalid_dag", $"DAG invalid: {dagErr}"));
            }

            wo.MesPhase = nameof(MesPhase.SPLIT);
            wo.UpdatedAt = DateTime.UtcNow; wo.UpdatedBy = actor;
            await _db.SaveChangesAsync();
            await tx.CommitAsync(ct);
        }

        var fresh = await _db.WorkOrders.AsNoTracking().Where(w => w.Id == id).Select(w => w.RowVersion).FirstAsync(ct);
        await _audit.EmitAsync(AuditAction.WoSplitForked, actor, role, "WorkOrder", id.ToString(),
            JsonSerializer.Serialize(new { wo_id = id, wo_no = wo.WoNo, leg_count = plan.Legs.Count, edge_count = plan.Edges.Count }));

        Response.Headers.ETag = $"\"{B64(fresh)}\"";
        return Ok(new LegMaterializeResponse
        {
            Ok = true, Forked = true, LegCount = plan.Legs.Count,
            MesPhase = nameof(MesPhase.SPLIT), WoETag = B64(fresh),
        });
    }

    // ── POST {id}/legs/{legId}/advance ─────────────────────────────

    [HttpPost("{id:long}/legs/{legId:long}/advance")]
    public async Task<IActionResult> Advance(long id, long legId, [FromBody] LegAdvanceRequest? req, CancellationToken ct = default)
    {
        var actor = User.FindFirstValue(ClaimTypes.Name) ?? "anonymous";
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";

        var (err, wo, leg) = await LegPreludeAsync(id, legId, actor, role, "leg_advance");
        if (err is not null) return err;

        if (req is null || string.IsNullOrWhiteSpace(req.ToPhase) || !RoutingLegGate.TryParse(req.ToPhase, out var toPhase))
            return UnprocessableEntity(ApiError.Of("leg.invalid_phase", "ToPhase không hợp lệ."));

        var check = RoutingLegGate.CanEnter(wo!, leg!, toPhase);
        if (!check.Allowed)
        {
            var code = check.Error == WoErrorCode.AssemblyInputsMissing ? "leg.inputs_not_ready" : "leg.invalid_phase";
            return UnprocessableEntity(ApiError.Of(code,
                $"Leg {legId} không thể vào {toPhase} từ {leg!.LegPhase} ({check.Error})."));
        }

        // P11.5-2 — gate FROM_STOCK/MIXED: assembly xuất kho phải reserve đủ
        // semi trước khi vào RUNNING (RoutingLegGate chỉ gác in-line HARD;
        // stock-satisfaction cần SemiAllocation → kiểm ở đây).
        if (toPhase == LegPhase.RUNNING
            && string.Equals(leg!.LegKind, nameof(LegKind.ASSEMBLY), StringComparison.OrdinalIgnoreCase)
            && !string.Equals(leg.InputSource, nameof(InputSource.IN_LINE), StringComparison.OrdinalIgnoreCase))
        {
            var allocs = await _db.SemiAllocations.AsNoTracking()
                .Where(a => a.WorkOrderId == id && a.AssemblyLegId == legId).ToListAsync(ct);
            // MIXED: cộng phần in-line đã done (qty của các HARD predecessor LEG_DONE).
            int inlineDone = wo.LegEdges.Where(e => e.LegId == leg.Id)
                .Select(e => wo.Legs.FirstOrDefault(l => l.Id == e.DependsOnLegId))
                .Where(l => l is not null && l.LegPhase == nameof(LegPhase.LEG_DONE))
                .Sum(l => l!.QtyDoneCached);
            if (!SemiStockAllocator.IsSatisfied(allocs, legId, wo.TargetQty, inlineDone))
                return UnprocessableEntity(ApiError.Of("leg.inputs_not_ready",
                    $"Assembly chưa đủ semi từ kho (cần {wo.TargetQty}) — reserve thêm trước khi chạy."));
        }

        var now = DateTime.UtcNow;
        leg!.LegPhase = toPhase.ToString();
        leg.UpdatedAt = now; leg.UpdatedBy = actor;
        bool legDone = toPhase == LegPhase.LEG_DONE;
        if (legDone) leg.LegDoneAt = now;

        // Join: leg cuối terminal LEG_DONE → SPLIT → FQC_PENDING (cùng transaction).
        bool joined = false;
        if (legDone && wo!.MesPhase == nameof(MesPhase.SPLIT))
        {
            var terminals = wo.Legs.Where(l => WorkOrderStateMachine.IsTerminalLeg(wo, l)).ToList();
            if (terminals.All(l => l.LegPhase == nameof(LegPhase.LEG_DONE)))
            {
                wo.MesPhase = nameof(MesPhase.FQC_PENDING);
                wo.UpdatedAt = now; wo.UpdatedBy = actor;
                joined = true;
            }
        }

        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return await LegConflictAsync(id, legId, wo!); }

        await _audit.EmitAsync(AuditAction.WoLegPhaseAdvanced, actor, role, "WoLeg", legId.ToString(),
            JsonSerializer.Serialize(new { wo_id = id, wo_no = wo!.WoNo, leg_id = legId, leg_kind = leg.LegKind, to_phase = leg.LegPhase, soft_waiting = check.SoftWaiting }));
        if (legDone)
            await _audit.EmitAsync(AuditAction.WoLegDone, actor, role, "WoLeg", legId.ToString(),
                JsonSerializer.Serialize(new { wo_id = id, leg_id = legId, leg_kind = leg.LegKind }));
        if (joined)
            await _audit.EmitAsync(AuditAction.WoSplitJoined, actor, role, "WorkOrder", id.ToString(),
                JsonSerializer.Serialize(new { wo_id = id, wo_no = wo.WoNo, from_phase = "SPLIT", to_phase = "FQC_PENDING" }));

        return await LegOkAsync(id, legId, wo, joined, check.SoftWaiting);
    }

    // ── POST {id}/legs/{legId}/rework ──────────────────────────────

    [HttpPost("{id:long}/legs/{legId:long}/rework")]
    public async Task<IActionResult> Rework(long id, long legId, [FromBody] LegReworkRequest? req, CancellationToken ct = default)
    {
        var actor = User.FindFirstValue(ClaimTypes.Name) ?? "anonymous";
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";

        var (err, wo, leg) = await LegPreludeAsync(id, legId, actor, role, "leg_rework");
        if (err is not null) return err;

        if (req is null || string.IsNullOrWhiteSpace(req.Reason) || req.Reason.Length > 500)
            return UnprocessableEntity(ApiError.Of("leg.invalid_reason", "Reason bắt buộc (1-500 ký tự)."));

        // Q10: reject 1 leg → chỉ leg đó về PREPRESS; WO giữ ở SPLIT.
        var now = DateTime.UtcNow;
        leg!.LegPhase = nameof(LegPhase.PREPRESS);
        leg.LegDoneAt = null;
        leg.UpdatedAt = now; leg.UpdatedBy = actor;

        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return await LegConflictAsync(id, legId, wo!); }

        await _audit.EmitAsync(AuditAction.WoLegRework, actor, role, "WoLeg", legId.ToString(),
            JsonSerializer.Serialize(new { wo_id = id, wo_no = wo!.WoNo, leg_id = legId, leg_kind = leg.LegKind, reason = req.Reason }));

        return await LegOkAsync(id, legId, wo!, joined: false, softWaiting: false);
    }

    // ── Helpers ────────────────────────────────────────────────────

    private static string B64(byte[]? rv) => rv is { Length: > 0 } ? Convert.ToBase64String(rv) : "";

    private static string SurfaceFor(string legKind) =>
        legKind is nameof(LegKind.TAPE) or nameof(LegKind.ASSEMBLY)
            ? nameof(SurfaceProfile.LITE) : nameof(SurfaceProfile.FULL);

    // SOFT/HARD status của 1 leg (cho GET picker): hard = có predecessor
    // HARD chưa LEG_DONE (leg chưa vào RUNNING); soft = predecessor SOFT chưa done.
    private static (bool soft, bool hard) DependencyStatus(WorkOrder wo, WoLeg leg)
    {
        if (leg.LegPhase == nameof(LegPhase.RUNNING) || leg.LegPhase == nameof(LegPhase.LEG_DONE))
            return (false, false);
        bool soft = false, hard = false;
        foreach (var e in wo.LegEdges.Where(e => e.LegId == leg.Id))
        {
            var pred = wo.Legs.FirstOrDefault(l => l.Id == e.DependsOnLegId);
            if (pred is null) continue;
            var done = pred.LegPhase == nameof(LegPhase.LEG_DONE)
                       && (e.RequiredQty <= 0 || pred.QtyDoneCached >= e.RequiredQty);
            if (done) continue;
            if (string.Equals(e.DependencyGate, nameof(DependencyGate.HARD), StringComparison.OrdinalIgnoreCase)) hard = true;
            else soft = true;
        }
        return (soft, hard);
    }

    // WO-scoped prelude (materialize) — If-Match trên WO.RowVersion.
    private async Task<(IActionResult? Error, WorkOrder? Wo)> WoPreludeAsync(long id, string actor, string role, string action)
    {
        if (string.IsNullOrWhiteSpace(Request.Headers["Idempotency-Key"].ToString()))
            return (BadRequest(ApiError.Of("wo.idempotency_key_required", "Idempotency-Key header required.")), null);
        var ifMatch = Request.Headers.IfMatch.ToString();
        if (string.IsNullOrWhiteSpace(ifMatch))
            return (StatusCode(StatusCodes.Status428PreconditionRequired, ApiError.Of("wo.if_match_required", "If-Match header required.")), null);

        var wo = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id);
        if (wo is null) return (NotFound(ApiError.Of("wo.not_found", $"No work order with id {id}.")), null);

        if (!string.Equals(B64(wo.RowVersion), Norm(ifMatch), StringComparison.Ordinal))
        {
            await EmitConflict(id, wo.WoNo, action, Norm(ifMatch), B64(wo.RowVersion), actor, role);
            Response.Headers.ETag = $"\"{B64(wo.RowVersion)}\"";
            return (Conflict(new LegMaterializeResponse { Ok = false, ErrorCode = "wo.state_conflict", MesPhase = wo.MesPhase, WoETag = B64(wo.RowVersion) }), null);
        }
        return (null, wo);
    }

    // Leg-scoped prelude (advance/rework) — If-Match trên LEG.RowVersion.
    private async Task<(IActionResult? Error, WorkOrder? Wo, WoLeg? Leg)> LegPreludeAsync(long id, long legId, string actor, string role, string action)
    {
        if (string.IsNullOrWhiteSpace(Request.Headers["Idempotency-Key"].ToString()))
            return (BadRequest(ApiError.Of("wo.idempotency_key_required", "Idempotency-Key header required.")), null, null);
        var ifMatch = Request.Headers.IfMatch.ToString();
        if (string.IsNullOrWhiteSpace(ifMatch))
            return (StatusCode(StatusCodes.Status428PreconditionRequired, ApiError.Of("wo.if_match_required", "If-Match header required.")), null, null);

        var wo = await _db.WorkOrders.Include(w => w.Legs).Include(w => w.LegEdges).FirstOrDefaultAsync(w => w.Id == id);
        if (wo is null) return (NotFound(ApiError.Of("wo.not_found", $"No work order with id {id}.")), null, null);
        var leg = wo.Legs.FirstOrDefault(l => l.Id == legId);
        if (leg is null) return (NotFound(ApiError.Of("leg.not_found", $"No leg {legId} on WO {id}.")), null, null);

        if (!string.Equals(B64(leg.RowVersion), Norm(ifMatch), StringComparison.Ordinal))
        {
            await EmitConflict(id, wo.WoNo, action, Norm(ifMatch), B64(leg.RowVersion), actor, role);
            Response.Headers.ETag = $"\"{B64(leg.RowVersion)}\"";
            return (Conflict(new LegSetResponse { Ok = false, ErrorCode = "wo.state_conflict", LegId = legId, LegPhase = leg.LegPhase, LegETag = B64(leg.RowVersion), WoMesPhase = wo.MesPhase }), null, null);
        }
        return (null, wo, leg);
    }

    private async Task<IActionResult> LegOkAsync(long id, long legId, WorkOrder wo, bool joined, bool softWaiting)
    {
        var freshLegRv = await _db.WoLegs.AsNoTracking().Where(l => l.Id == legId).Select(l => l.RowVersion).FirstOrDefaultAsync();
        var freshPhase = await _db.WoLegs.AsNoTracking().Where(l => l.Id == legId).Select(l => l.LegPhase).FirstOrDefaultAsync();
        var woPhase = await _db.WorkOrders.AsNoTracking().Where(w => w.Id == id).Select(w => w.MesPhase).FirstOrDefaultAsync();
        var etag = B64(freshLegRv);
        Response.Headers.ETag = $"\"{etag}\"";
        return Ok(new LegSetResponse
        {
            Ok = true, LegId = legId, LegPhase = freshPhase ?? "", LegETag = etag,
            WoMesPhase = woPhase ?? wo.MesPhase, Joined = joined, SoftWaiting = softWaiting,
        });
    }

    private async Task<IActionResult> LegConflictAsync(long id, long legId, WorkOrder wo)
    {
        if (_db is DbContext ctx) ctx.ChangeTracker.Clear();
        var fresh = await _db.WoLegs.AsNoTracking().Where(l => l.Id == legId)
            .Select(l => new { l.RowVersion, l.LegPhase }).FirstOrDefaultAsync();
        var etag = B64(fresh?.RowVersion);
        Response.Headers.ETag = $"\"{etag}\"";
        return Conflict(new LegSetResponse { Ok = false, ErrorCode = "wo.state_conflict", LegId = legId, LegPhase = fresh?.LegPhase ?? "", LegETag = etag, WoMesPhase = wo.MesPhase });
    }

    private async Task EmitConflict(long id, string woNo, string action, string clientV, string serverV, string actor, string role)
        => await _audit.EmitAsync(AuditAction.WoStateConflict, actor, role, "WorkOrder", id.ToString(),
            JsonSerializer.Serialize(new { wo_id = id, wo_no = woNo, attempted_action = action, client_version = clientV, server_version = serverV }));

    private static string Norm(string raw)
    {
        var s = raw.Trim();
        if (s.StartsWith("W/", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"') s = s[1..^1];
        return s;
    }
}
