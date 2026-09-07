using CCL.MES.Infrastructure.IqcMaster;
using Xunit;

namespace CCL.MES.Tests.Unit;

public sealed class IqcHistoryLedgerDimParseTests
{
    [Theory]
    [InlineData("290x301", 290.0, 301.0)]
    [InlineData("455x456", 455.0, 456.0)]
    [InlineData("291x300", 291.0, 300.0)]
    [InlineData("97.66", 97.66, null)]
    [InlineData("392.28", 392.28, null)]
    [InlineData("", null, null)]
    public void ParseWidthLengthCell_splits_WxL(string raw, double? w, double? l)
    {
        var (gotW, gotL) = IqcHistoryLedgerReader.ParseWidthLengthCell(
            string.IsNullOrEmpty(raw) ? null : raw);
        Assert.Equal(w, gotW);
        Assert.Equal(l, gotL);
    }

    [Fact]
    public void ParseSizeSpec_tolerance_and_WxL()
    {
        var (n, low, up, len) = IqcHistoryLedgerReader.ParseSizeSpec("97.5+0.5-0.2");
        Assert.Equal(97.5, n);
        Assert.Equal(97.3, low);
        Assert.Equal(98.0, up);
        Assert.Null(len);

        var wxl = IqcHistoryLedgerReader.ParseSizeSpec("290x300");
        Assert.Equal(290, wxl.Nominal);
        Assert.Equal(300, wxl.LengthNominal);
    }

    [Fact]
    public void IsPcsLengthCompanion_same_code_null_stt()
    {
        var open = Row(7, 2, "30030545", new DateTime(2026, 1, 5));
        var cont = Row(8, null, "30030545", DateTime.MinValue);
        Assert.True(IqcHistoryLedgerReader.IsPcsLengthCompanion(open, cont));
    }

    [Fact]
    public void IsPcsLengthCompanion_same_code_same_day_with_stt()
    {
        var open = Row(128, 99, "30030545", new DateTime(2026, 4, 28));
        var cont = Row(129, 100, "30030545", new DateTime(2026, 4, 28));
        Assert.True(IqcHistoryLedgerReader.IsPcsLengthCompanion(open, cont));
    }

    [Fact]
    public void IsPcsLengthCompanion_rejects_different_code()
    {
        var open = Row(21, 8, "30030636", new DateTime(2026, 1, 12));
        var next = Row(22, 9, "30030675", new DateTime(2026, 1, 12));
        Assert.False(IqcHistoryLedgerReader.IsPcsLengthCompanion(open, next));
    }

    private static Application.Services.IqcHistoryLedgerRow Row(
        int excel, int? stt, string code, DateTime at)
        => new("PCS", excel, stt, at, "NCC", code, null, "Panel", "PO", 1, "pcs", "OK", "Hải",
            Checks: new Application.Services.IqcHistoryLedgerChecks(
                null, null, null, null, null, null, null,
                null, Array.Empty<Application.Services.IqcLedgerDefectCell>(), null, null,
                null, null, null,
                WidthSamples: new double?[] { 1, 2, 3, 4, 5 },
                WidthSampleTexts: Array.Empty<string?>(),
                WidthPass: true,
                LengthNominal: null, LengthLow: null, LengthUp: null,
                LengthSamples: Array.Empty<double?>(),
                LengthSampleTexts: Array.Empty<string?>(),
                LengthPass: null, LengthSpec: null,
                ThicknessSpec: null, ThicknessSamples: Array.Empty<double?>(), ThicknessPass: null,
                DimensionInspector: null,
                FuncSpec: null, FuncPass: null, FuncInspector: null,
                LabSpec: null, LabSheets: Array.Empty<double?>(), LabPass: null, LabInspector: null));
}
