using System.Threading.Tasks;
using CCL.MES.Hybrid.Client.Printing;
using Xunit;

namespace CCL.MES.Hybrid.Client.Tests.Printing;

/// <summary>
/// P11.x — the stub print service is what tests + non-Catalyst hosts wire.
/// It must report native print as unavailable and never present anything so
/// the Spec sheet Print button deterministically falls back to the server
/// MigraDoc PDF. These locks keep the fallback contract honest.
/// </summary>
public sealed class StubPrintServiceTests
{
    [Fact]
    public void IsNativePrintSupported_is_false()
    {
        IPrintService sut = new StubPrintService();
        Assert.False(sut.IsNativePrintSupported);
    }

    [Fact]
    public async Task PrintCurrentViewAsync_returns_false_so_caller_falls_back()
    {
        IPrintService sut = new StubPrintService();

        Assert.False(await sut.PrintCurrentViewAsync());
        Assert.False(await sut.PrintCurrentViewAsync("SpecSheet_ABC_A"));
    }
}
