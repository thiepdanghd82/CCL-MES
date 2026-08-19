using CCL.MES.Api.Observability;
using CCL.MES.Application;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Api.Services;

/// <summary>
/// Đợt 1 C3 — resolves the canonical OEE speed input for a WO.
///
/// <c>WorkOrder.MachineCode</c> is a string, not an FK; it matches
/// <c>WorkCenter.Code</c>, the same entity the machine dashboard and
/// ShopOrdersController already read from. Everything about the speed leg
/// of OEE now enters through here, which is what makes
/// <c>gate-oee-single-source.sh</c> enforceable: there is one query to
/// audit rather than one per endpoint.
///
/// It exists as a service rather than an inline controller query for two
/// reasons — <c>cmes-thin-controller</c> forbids <c>DbContext</c> in the
/// HTTP layer, and this is the natural place to stamp the work-center code
/// onto <see cref="MesRequestContext"/> so it reaches the request log
/// without any controller knowing observability exists.
/// </summary>
public sealed class WorkCenterSpeedLookup
{
    private readonly IMesDbContext _db;
    private readonly MesRequestContext _obs;

    public WorkCenterSpeedLookup(IMesDbContext db, MesRequestContext obs)
    {
        _db = db;
        _obs = obs;
    }

    /// <param name="Resolved">A WorkCenter row matched the code. False
    /// means master data is missing, which is a different problem from a
    /// work center that exists but has no speed set.</param>
    /// <param name="IdealSpeedPcsH">Pieces per hour; null or ≤ 0 on the 38
    /// of 43 work centers nobody has filled in yet.</param>
    public readonly record struct Speed(bool Resolved, double? IdealSpeedPcsH);

    /// <param name="woNo">The work order being reported on. Stamped onto
    /// <see cref="MesRequestContext"/> so read-path log lines carry
    /// <c>wo_no</c> too — on mutations the audit writer supplies it, but a
    /// GET emits no audit row and would otherwise log without it.</param>
    /// <param name="machineCode"><c>WorkOrder.MachineCode</c>, matched
    /// against <c>WorkCenter.Code</c>.</param>
    public async Task<Speed> ResolveAsync(string? woNo, string? machineCode, CancellationToken ct)
    {
        _obs.NoteWorkOrder(woNo);

        if (string.IsNullOrWhiteSpace(machineCode)) return new Speed(false, null);

        var row = await _db.WorkCenters.AsNoTracking()
            .Where(wc => wc.Code == machineCode)
            .Select(wc => new { wc.Code, wc.IdealSpeedPcsH })
            .FirstOrDefaultAsync(ct);

        if (row is null) return new Speed(false, null);

        _obs.NoteWorkCenter(row.Code);
        return new Speed(true, row.IdealSpeedPcsH);
    }
}
