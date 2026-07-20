using System.Security.Claims;
using System.Text.Json;
using CCL.MES.Api.Services;
using CCL.MES.Application;
using CCL.MES.Application.Services;
using CCL.MES.Shared;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.Quality;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// Quality → Traceability (read-only, [Authorize(Policy="QcRead")]).
///   GET /api/v2/quality/traceability?search=&amp;page=&amp;pageSize=
///        → paged list of WOs that have ≥1 frozen snapshot.
///   GET /api/v2/quality/traceability/{woNo}
///        → merged detail: newest version of each of the 4 phases.
/// BOTH read ONLY WoTraceSnapshots.PayloadJson — never JOIN back to the
/// live source (WoMaterial / WoIpqcCheck / WoQcChecks / BOM). The freeze
/// side is <see cref="ITraceFreezeService"/>, hooked into the confirm points.
/// </summary>
[ApiController]
[Route(ApiVersion.Prefix + "/quality")]
[Authorize(Policy = "QcRead")]
public sealed class TraceabilityController : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IMesDbContext _db;
    public TraceabilityController(IMesDbContext db) => _db = db;

    [HttpGet("traceability")]
    public async Task<IActionResult> GetList(
        string? search = null, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 500 ? 50 : pageSize;

        // Real-time list = the MUTABLE index (a WO appears the moment it's
        // scanned/found, before any freeze). Newest scan first.
        var q = _db.WoTraceIndexes.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var like = $"%{search.Trim()}%";   // SQLite LIKE folds ASCII case
            q = q.Where(x => EF.Functions.Like(x.WoNo, like));
        }

        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(x => x.LastScannedAtUtc)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new TraceListRow
            {
                WoId = x.WoId, WoNo = x.WoNo,
                ProductName = x.ProductName, ProductCode = x.ProductCode, Customer = x.Customer,
                CurrentMesPhase = x.CurrentMesPhase,
                LastScannedAtUtc = x.LastScannedAtUtc,
                LatestFrozenAtUtc = x.LatestFrozenAtUtc,
                FrozenPhases = BuildFrozenPhases(x.ProductFrozen, x.IpqcFrozen, x.FqcFrozen, x.OqcFrozen),
            })
            .ToListAsync(ct);

        return Ok(new PagedResult<TraceListRow>(rows, total, page, pageSize));
    }

    private static List<string> BuildFrozenPhases(bool p, bool i, bool f, bool o)
    {
        var l = new List<string>();
        if (p) l.Add(TracePhase.Product);
        if (i) l.Add(TracePhase.Ipqc);
        if (f) l.Add(TracePhase.Fqc);
        if (o) l.Add(TracePhase.Oqc);
        return l;
    }

    /// <summary>One-off idempotent backfill (AdminOnly) — index every WO and
    /// freeze already-concluded phases so pre-existing WOs show trace data.</summary>
    [HttpPost("traceability/backfill"), Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Backfill(
        [FromServices] TraceBackfillService backfill, CancellationToken ct = default)
    {
        var actor = User.FindFirstValue(ClaimTypes.Name) ?? "system";
        var r = await backfill.BackfillAllAsync(actor, ct);
        return Ok(new { workOrders = r.WorkOrders, phasesFrozen = r.PhasesFrozen });
    }

    [HttpGet("traceability/{woNo}")]
    public async Task<IActionResult> GetDetail(string woNo, CancellationToken ct = default)
    {
        // Exact WoNo but case-insensitive (LIKE with no wildcard).
        var snaps = await _db.WoTraceSnapshots.AsNoTracking()
            .Where(s => EF.Functions.Like(s.WoNo, woNo))
            .Select(s => new { s.WoId, s.WoNo, s.Phase, s.Version, s.SchemaVersion, s.FrozenAtUtc, s.FrozenBy, s.PayloadJson })
            .ToListAsync(ct);
        if (snaps.Count == 0)
            return NotFound(ApiError.Of("trace.not_found", $"No frozen trace for WO '{woNo}'."));

        var woId = snaps[0].WoId;
        var productName = await _db.WorkOrders.AsNoTracking()
            .Where(w => w.Id == woId).Select(w => w.ProductName).FirstOrDefaultAsync(ct) ?? "";

        TracePhaseDto? Latest(string phase)
        {
            var s = snaps.Where(x => x.Phase == phase).OrderByDescending(x => x.Version).FirstOrDefault();
            if (s is null) return null;
            var payload = JsonSerializer.Deserialize<TracePayload>(s.PayloadJson, Json) ?? new TracePayload();
            return new TracePhaseDto
            {
                Version = s.Version, SchemaVersion = s.SchemaVersion,
                FrozenAtUtc = s.FrozenAtUtc, FrozenBy = s.FrozenBy, Payload = payload,
            };
        }

        return Ok(new TraceabilityDetailDto
        {
            WoNo = snaps[0].WoNo,
            ProductName = productName,
            Product = Latest(TracePhase.Product),
            Ipqc = Latest(TracePhase.Ipqc),
            Fqc = Latest(TracePhase.Fqc),
            Oqc = Latest(TracePhase.Oqc),
        });
    }
}
