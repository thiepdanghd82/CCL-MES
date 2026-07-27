using CCL.MES.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Application.Services;

/// <summary>
/// P11 per-leg OP-Setting (Q1 reuse WoRunSession, Henry-approved) — the Setting
/// timer for a leg. WoLeg has no setting columns and the WO-level SettingStartAt/
/// EndAt/DurationSec are singletons, so a per-leg setting session is modelled as a
/// <see cref="WoRunSession"/> scoped by the WoLegId shadow: StartedAt = operator
/// began setup, EndedAt = "Finish Setting". No migration.
///
/// Idempotent: Enter reuses an already-open session; Done is a NOOP when nothing
/// is open. Never touches WO-level setting columns → 1-leg parity intact.
/// </summary>
public sealed class SettingLegService
{
    private readonly IMesDbContext _db;
    public SettingLegService(IMesDbContext db) => _db = db;

    /// <summary>Open the leg's setting session (idempotent). Returns false only
    /// if the leg does not exist.</summary>
    public async Task<bool> EnterAsync(long legId, string actor, CancellationToken ct = default)
    {
        var ctx = (DbContext)_db;
        var leg = await _db.WoLegs.FirstOrDefaultAsync(l => l.Id == legId, ct);
        if (leg is null) return false;

        var hasOpen = await _db.WoRunSessions
            .AnyAsync(s => EF.Property<long?>(s, "WoLegId") == legId && s.EndedAt == null, ct);
        if (hasOpen) return true;   // idempotent — reuse the open session

        var now = DateTime.UtcNow;
        var sess = new WoRunSession
        {
            WoId = leg.WorkOrderId, StartedAt = now, StartedBy = actor,
            CreatedAt = now, CreatedBy = actor,
        };
        _db.WoRunSessions.Add(sess);
        ctx.Entry(sess).Property("WoLegId").CurrentValue = legId;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Finish the leg's setting session (stamp EndedAt). Returns false
    /// when there is no open session to close.</summary>
    public async Task<bool> DoneAsync(long legId, string actor, CancellationToken ct = default)
    {
        var open = await _db.WoRunSessions
            .FirstOrDefaultAsync(s => EF.Property<long?>(s, "WoLegId") == legId && s.EndedAt == null, ct);
        if (open is null) return false;

        var now = DateTime.UtcNow;
        open.EndedAt = now; open.EndedBy = actor;
        open.UpdatedAt = now; open.UpdatedBy = actor;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
