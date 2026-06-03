using CCL.MES.Shared.Hardware;

namespace CCL.MES.Hybrid.Client.Hardware;

/// <summary>
/// P10.3 stub for <see cref="ILabelPrinterService"/>. Lights up only
/// when a concrete print workflow ships (see plan §1). Calls to
/// <see cref="PrintZplAsync"/> throw <see cref="NotImplementedException"/>
/// — louder than a silent no-op, which is what we want for a feature
/// the operator might think is wired up just because it appears in the
/// /hardware page.
/// </summary>
public sealed class StubLabelPrinterService : ILabelPrinterService
{
    public Task<HardwareAvailability> IsAvailableAsync(CancellationToken ct = default)
        => Task.FromResult(HardwareAvailability.NotImplemented("Máy in nhãn"));

    public Task PrintZplAsync(string zpl, CancellationToken ct = default)
        => throw new NotImplementedException(
            "Label printer impl deferred to a post-P10.3 phase. " +
            "See PHASE10-P10.3-HARDWARE-PLAN.md §1 priority cut.");
}
