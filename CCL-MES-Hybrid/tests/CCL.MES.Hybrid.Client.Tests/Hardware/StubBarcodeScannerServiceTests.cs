using CCL.MES.Hybrid.Client.Hardware;
using Microsoft.Extensions.Options;

namespace CCL.MES.Hybrid.Client.Tests.Hardware;

public sealed class StubBarcodeScannerServiceTests
{
    [Fact]
    public async Task IsAvailable_reports_feature_disabled_when_flag_off()
    {
        var stub = new StubBarcodeScannerService(
            Options.Create(new HardwareOptions { ScanEnabled = false }));
        var a = await stub.IsAvailableAsync();
        Assert.False(a.IsAvailable);
        Assert.Equal("feature_disabled", a.Reason);
    }

    [Fact]
    public async Task IsAvailable_reports_not_implemented_when_flag_on()
    {
        var stub = new StubBarcodeScannerService(
            Options.Create(new HardwareOptions { ScanEnabled = true }));
        var a = await stub.IsAvailableAsync();
        Assert.False(a.IsAvailable);
        Assert.Equal("not_implemented", a.Reason);
    }

    [Fact]
    public async Task ScanOnce_returns_null_regardless_of_flag()
    {
        var off = new StubBarcodeScannerService(Options.Create(new HardwareOptions { ScanEnabled = false }));
        var on  = new StubBarcodeScannerService(Options.Create(new HardwareOptions { ScanEnabled = true }));
        Assert.Null(await off.ScanOnceAsync());
        Assert.Null(await on.ScanOnceAsync());
    }

    [Fact]
    public async Task ScanStream_yields_nothing()
    {
        var stub = new StubBarcodeScannerService(
            Options.Create(new HardwareOptions { ScanEnabled = true }));
        var count = 0;
        await foreach (var _ in stub.ScanStreamAsync(CancellationToken.None))
            count++;
        Assert.Equal(0, count);
    }
}
