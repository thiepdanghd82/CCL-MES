using CCL.MES.Hybrid.Client.Hardware;
using CCL.MES.Shared.Hardware;

namespace CCL.MES.Hybrid.Razor.Tests._Support;

/// <summary>
/// Minimal <see cref="IBarcodeScannerService"/> stub for the bUnit
/// render tests — the WorkOrders page injects it on construction;
/// these tests never trigger an actual scan modal so we just need
/// a "scanner available" availability + a no-op
/// <see cref="ScanOnceAsync"/>.
/// </summary>
public sealed class StubScannerService : IBarcodeScannerService
{
    public bool RaiseAvailable { get; set; } = true;

    public Func<Task<ScanResult?>>? ScanImpl { get; set; }

    public Task<HardwareAvailability> IsAvailableAsync(CancellationToken ct = default)
        => Task.FromResult(RaiseAvailable
            ? new HardwareAvailability(true, "ready", "Camera sẵn sàng")
            : new HardwareAvailability(false, "permission_denied", "Camera bị từ chối"));

    public Task<ScanResult?> ScanOnceAsync(CancellationToken ct = default)
        => ScanImpl is null
            ? Task.FromResult<ScanResult?>(null)
            : ScanImpl();

    public IAsyncEnumerable<ScanResult> ScanStreamAsync(CancellationToken ct = default)
        => EmptyStream(ct);

    private static async IAsyncEnumerable<ScanResult> EmptyStream(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask;
        yield break;
    }
}
