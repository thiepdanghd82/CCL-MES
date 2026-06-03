using CCL.MES.Hybrid.Client.Hardware;

namespace CCL.MES.Hybrid.Client.Tests.Hardware;

public sealed class StubLabelPrinterAndWeighScaleTests
{
    [Fact]
    public async Task LabelPrinter_isAvailable_says_not_implemented()
    {
        var p = new StubLabelPrinterService();
        var a = await p.IsAvailableAsync();
        Assert.False(a.IsAvailable);
        Assert.Equal("not_implemented", a.Reason);
        Assert.Contains("Máy in", a.OperatorMessage!);
    }

    [Fact]
    public async Task LabelPrinter_PrintZpl_throws_NotImplementedException()
    {
        var p = new StubLabelPrinterService();
        await Assert.ThrowsAsync<NotImplementedException>(() => p.PrintZplAsync("^XA^XZ"));
    }

    [Fact]
    public async Task WeighScale_isAvailable_says_not_implemented()
    {
        var s = new StubWeighScaleService();
        var a = await s.IsAvailableAsync();
        Assert.False(a.IsAvailable);
        Assert.Equal("not_implemented", a.Reason);
    }

    [Fact]
    public async Task WeighScale_stream_yields_nothing()
    {
        var s = new StubWeighScaleService();
        var count = 0;
        await foreach (var _ in s.WeightStreamGramsAsync(CancellationToken.None))
            count++;
        Assert.Equal(0, count);
    }
}
