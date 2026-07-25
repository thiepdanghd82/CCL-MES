using CCL.MES.Domain;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Application.Services;

/// <summary>
/// P11 per-leg check-flow gate (Q2 blocking, Henry-approved) — the DATA
/// conditions that must hold before a leg may advance a LegFlow step:
///   • PREPRESS → SETTING       : the leg's Pre-press surface is all OK.
///   • SETTING  → IPQC_WAIT      : the leg's Setting session is finished.
///   • IPQC_WAIT → IPQC_APPROVED : the leg's IPQC items are all OK.
///
/// PARITY: every predicate returns <c>true</c> ("not gated") when the leg has
/// NO materialised surface of that kind (WoLegId rows absent) — so a WO with no
/// per-leg data (1-leg / legacy) is never blocked by these. Multi-leg WOs
/// materialise the surface at fork, so they ARE gated.
///
/// Per-leg semantics differ from the WO-level rollup: an ABSENT plate/cutter is
/// N/A (OK), not "not ready" — an ASSEMBLY leg legitimately has neither, yet must
/// still be able to advance once its materials (if any) are OK.
///
/// <c>WoLegId</c> is a shadow property → EF.Property in LINQ.
/// </summary>
public sealed class PerLegCheckGate
{
    private readonly IMesDbContext _db;
    public PerLegCheckGate(IMesDbContext db) => _db = db;

    /// <summary>PREPRESS → SETTING: leg materials (if any) all Ok + plate Ok
    /// (if present) + cutter Ok (if present). Vacuously true when the leg has no
    /// Pre-press surface at all.</summary>
    public async Task<bool> PrepressReadyAsync(long legId, CancellationToken ct = default)
    {
        var mats = await _db.WoMaterials
            .Where(m => EF.Property<long?>(m, "WoLegId") == legId)
            .Select(m => m.Status).ToListAsync(ct);
        var plate = await _db.WoPlateChecks
            .Where(p => EF.Property<long?>(p, "WoLegId") == legId)
            .Select(p => (PrepressCheckStatus?)p.Status).FirstOrDefaultAsync(ct);
        var cutter = await _db.WoCutterChecks
            .Where(c => EF.Property<long?>(c, "WoLegId") == legId)
            .Select(c => (PrepressCheckStatus?)c.Status).FirstOrDefaultAsync(ct);

        var hasSurface = mats.Count > 0 || plate is not null || cutter is not null;
        if (!hasSurface) return true;   // no per-leg surface → not gated (parity)

        var matsOk = mats.All(s => s == PrepressCheckStatus.Ok);        // vacuously true if none
        var plateOk = plate is null || plate == PrepressCheckStatus.Ok;  // absent = N/A
        var cutterOk = cutter is null || cutter == PrepressCheckStatus.Ok;
        return matsOk && plateOk && cutterOk;
    }

    /// <summary>SETTING → IPQC_WAIT: the leg has a finished Setting session
    /// (a WoRunSession scoped to the leg with EndedAt stamped — Q1 reuse).
    /// True (not gated) when the leg never opened a setting session AND has no
    /// per-leg IPQC surface (legacy safety); once a session is opened it must be
    /// closed.</summary>
    public async Task<bool> SettingDoneAsync(long legId, CancellationToken ct = default)
    {
        var anySession = await _db.WoRunSessions
            .AnyAsync(s => EF.Property<long?>(s, "WoLegId") == legId, ct);
        if (!anySession) return true;   // no setting session tracked → not gated
        return await _db.WoRunSessions
            .AnyAsync(s => EF.Property<long?>(s, "WoLegId") == legId && s.EndedAt != null, ct);
    }

    /// <summary>IPQC_WAIT → IPQC_APPROVED: every IPQC item of the leg is Ok.
    /// Vacuously true when the leg has no IPQC items materialised.</summary>
    public async Task<bool> IpqcAllOkAsync(long legId, CancellationToken ct = default)
    {
        var items = await _db.WoIpqcCheckItems
            .Where(i => EF.Property<long?>(i, "WoLegId") == legId)
            .Select(i => i.Status).ToListAsync(ct);
        if (items.Count == 0) return true;   // no per-leg IPQC surface → not gated
        return items.All(s => s == IpqcCheckStatus.Ok);
    }
}
