using System.Runtime.CompilerServices;
using CCL.MES.Shared.Hardware;
using Microsoft.Extensions.Options;

namespace CCL.MES.Hybrid.Client.Hardware;

/// <summary>
/// W1 placeholder for <see cref="IBarcodeScannerService"/>. Reports
/// <see cref="HardwareAvailability"/> with reason
/// <c>"feature_disabled"</c> when the
/// <see cref="HardwareOptions.ScanEnabled"/> flag is false, OR
/// <c>"not_implemented"</c> when the flag is on but no real platform
/// impl is registered (the host project decides — Catalyst/Windows
/// W2/W3 work overrides this stub registration).
///
/// <para>
/// Never returns a fake scan result. <see cref="ScanOnceAsync"/> always
/// returns null and <see cref="ScanStreamAsync"/> yields nothing. The
/// UI MUST gate the scan button behind <see cref="IsAvailableAsync"/>
/// — calling Scan* without checking just produces a silent no-op,
/// which would be the worst failure mode (P10.2 lesson: no silent fail).
/// </para>
/// </summary>
public sealed class StubBarcodeScannerService : IBarcodeScannerService
{
    private readonly HardwareOptions _opts;

    public StubBarcodeScannerService(IOptions<HardwareOptions> opts)
    {
        _opts = opts.Value;
    }

    public Task<HardwareAvailability> IsAvailableAsync(CancellationToken ct = default)
    {
        if (!_opts.ScanEnabled)
            return Task.FromResult(HardwareAvailability.FeatureDisabled());
        return Task.FromResult(HardwareAvailability.NotImplemented("Scanner"));
    }

    public Task<ScanResult?> ScanOnceAsync(CancellationToken ct = default)
        => Task.FromResult<ScanResult?>(null);

    public async IAsyncEnumerable<ScanResult> ScanStreamAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        // No-op stream — caller awaits forever (or until ct). Suppresses
        // the CS1998 'async method lacks await' warning explicitly.
        await Task.CompletedTask;
        yield break;
    }
}
