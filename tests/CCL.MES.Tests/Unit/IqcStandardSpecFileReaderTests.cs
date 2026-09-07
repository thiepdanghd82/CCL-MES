using CCL.MES.Infrastructure.IqcMaster;
using Xunit;

namespace CCL.MES.Tests.Unit;

public sealed class IqcStandardSpecFileReaderTests
{
    [Theory]
    [InlineData(
        "CCL-SPEC-QC001 -  IQC TIÊU CHUẨN (R03) - SW-7325F.xlsx",
        "CCL-SPEC-QC001", "SW-7325F", "R03", null)]
    [InlineData(
        "CCL-SPEC-QC0192 -  IQC (R03) - FooBar.xlsx",
        "CCL-SPEC-QC192", "FooBar", "R03", null)]
    [InlineData(
        "CCL-SPEC-QC417 -  IQC (R03) - Dupont AD25(70000808).xlsx",
        "CCL-SPEC-QC417", "Dupont AD25", "R03", "70000808")]
    public void TryParseFileName_extracts_spec_mother_rev(
        string name, string spec, string mother, string rev, string? ifs)
    {
        Assert.True(IqcStandardSpecFileReader.TryParseFileName(name, out var p));
        Assert.NotNull(p);
        Assert.Equal(spec, p!.SpecNo);
        Assert.Equal(mother, p.MaterialCode);
        Assert.Equal(rev, p.Revision);
        Assert.Equal(ifs, p.MaterialCodeIfs);
    }

    [Theory]
    [InlineData("CCL-SPEC-QC00X - X.xlsx")]
    [InlineData("CCL-SPEC-QC001 - Copy.xlsx")]
    [InlineData("List số spec.xlsx")]
    public void TryParseFileName_skips_templates(string name)
        => Assert.False(IqcStandardSpecFileReader.TryParseFileName(name, out _));

    [Theory]
    [InlineData("Tem nhãn", "NQ-01")]
    [InlineData("Kiểm tra vật liệu", "NL-01")]
    [InlineData("HSF", "MT-02")]
    [InlineData("Keo của nguyên liệu", "BD-01")]
    [InlineData("Something unknown", "KH-01")]
    public void MapItemId_matches_library_codes(string label, string id)
        => Assert.Equal(id, IqcStandardSpecFileReader.MapItemId(label));
}
