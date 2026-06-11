using System.Runtime.CompilerServices;
using CCL.MES.Shared.Hardware;

namespace CCL.MES.Hybrid.Client.Hardware;

/// <summary>
/// P10.3 stub for <see cref="IWeighScaleService"/>. Same shape as
/// <see cref="StubLabelPrinterService"/> — interface present so future
/// impls slot in without re-versioning; stream yields nothing in W1.
/// </summary>
public sealed class StubWeighScaleService : IWeighScaleService
{
    public Task<HardwareAvailability> IsAvailableAsync(CancellationToken ct = default)
        => Task.FromResult(HardwareAvailability.NotImplemented("Weigh scale"));

    public async IAsyncEnumerable<decimal> WeightStreamGramsAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask;
        yield break;
    }
}
