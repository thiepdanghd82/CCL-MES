using CCL.MES.Application.Services;
using Xunit;

namespace CCL.MES.Tests.Unit;

public sealed class IqcParetoLabelTests
{
    [Fact]
    public void SplitBilingual_strips_latin_parenthetical()
    {
        var (vi, en) = IqcParetoLabel.SplitBilingual("Độ bám dính keo (Adhesive)");
        Assert.Equal("Độ bám dính keo", vi);
        Assert.Equal("Adhesive", en);
    }

    [Fact]
    public void Classify_adhesive_is_one_language_each()
    {
        var fam = IqcParetoLabel.Classify("Độ bám dính keo (Adhesive)", null, "BD-01", null);
        Assert.Equal("adhesion", fam.Key);
        Assert.Equal("Độ bám dính keo", fam.Vi);
        Assert.Equal("Adhesive", fam.En);
        Assert.DoesNotContain("(", fam.Vi);
        Assert.DoesNotContain("(", fam.En);
    }

    [Fact]
    public void Classify_groups_vi_and_en_scratch_together()
    {
        var a = IqcParetoLabel.Classify("Xước", null, "RD-05", null);
        var b = IqcParetoLabel.Classify(null, "Scratch", null, null);
        Assert.Equal(a.Key, b.Key);
        Assert.Equal("Xước", a.Vi);
        Assert.Equal("Scratch", a.En);
    }

    [Fact]
    public void Classify_other_and_loi_khac_share_key()
    {
        var vi = IqcParetoLabel.Classify("Lỗi khác", null, "RD-13", null);
        var en = IqcParetoLabel.Classify(null, "Other", null, null);
        Assert.Equal("other", vi.Key);
        Assert.Equal(vi.Key, en.Key);
        Assert.Equal("Other", vi.En);
    }

    [Fact]
    public void Classify_dim_oot_stays_code()
    {
        var fam = IqcParetoLabel.Classify("DIM-OOT", "DIM-OOT", "DIM-OOT", null);
        Assert.Equal("DIM-OOT", fam.Vi);
        Assert.Equal("DIM-OOT", fam.En);
    }
}
