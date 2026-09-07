using CCL.MES.Application.Services;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// Hạng mục kiểm nào rơi vào MỤC nào của stepper phiếu IQC
/// (Documents · Packaging · Visual · Dimension · Functional).
/// </summary>
public sealed class IqcTicketSectionTests
{
    private static readonly (string Item, string Group)[] Library =
    [
        ("NL-01", "NL"),
        ("NQ-01", "NQ"), ("NQ-02", "NQ"), ("NQ-03", "NQ"),
        ("NQ-04", "NQ"), ("NQ-05", "NQ"), ("NQ-06", "NQ"),
        ("KT-01", "KT"), ("KT-02", "KT"), ("KT-03", "KT"), ("KT-04", "KT"),
        ("MT-01", "MT"), ("MT-02", "MT"), ("MT-03", "MT"),
        ("BD-01", "BD"), ("BD-02", "BD"),
        ("CU-01", "CU"), ("XS-01", "XS"), ("TL-01", "TL"), ("BO-01", "BO"),
        ("KH-01", "KH"),
        ("LB-01", "LB"),
        ("RD-01", "NQ"), ("PD-01", "NQ"),
    ];

    private static int Count(int section) =>
        Library.Count(x => IqcTicketSection.Of(x.Item, x.Group) == section);

    [Fact]
    public void Muc_1_chi_co_ho_so_giay_MT_02()
    {
        Assert.Equal(1, Count(IqcTicketSection.Documents));
        Assert.Equal(IqcTicketSection.Documents, IqcTicketSection.Of("MT-02", "MT"));
    }

    [Fact]
    public void Muc_2_dong_goi_NQ_01_va_NQ_06()
    {
        Assert.Equal(IqcTicketSection.Packaging, IqcTicketSection.Of("NQ-01", "NQ"));
        Assert.Equal(IqcTicketSection.Packaging, IqcTicketSection.Of("NQ-06", "NQ"));
        Assert.Equal(2, Count(IqcTicketSection.Packaging));
    }

    [Fact]
    public void Muc_3_ngoai_quan_gom_NL_va_NQ_con_lai()
    {
        Assert.Equal(IqcTicketSection.Visual, IqcTicketSection.Of("NL-01", "NL"));
        Assert.Equal(IqcTicketSection.Visual, IqcTicketSection.Of("NQ-02", "NQ"));
        Assert.Equal(IqcTicketSection.Visual, IqcTicketSection.Of("RD-01", "NQ"));
        Assert.Equal(IqcTicketSection.Visual, IqcTicketSection.Of("PD-01", "NQ"));
        Assert.Equal(7, Count(IqcTicketSection.Visual)); // NL-01 + NQ-02..05 + RD-01 + PD-01
    }

    [Fact]
    public void Muc_4_kich_thuoc_nhom_KT()
    {
        Assert.Equal(IqcTicketSection.Dimension, IqcTicketSection.Of("KT-03", "KT"));
        Assert.Equal(4, Count(IqcTicketSection.Dimension));
    }

    [Fact]
    public void Muc_5_chuc_nang_va_lab()
    {
        Assert.Equal(IqcTicketSection.Functional, IqcTicketSection.Of("BD-01", "BD"));
        Assert.Equal(IqcTicketSection.Functional, IqcTicketSection.Of("CU-01", "CU"));
        Assert.Equal(IqcTicketSection.Functional, IqcTicketSection.Of("LB-01", "LB"));
        Assert.Equal(IqcTicketSection.Functional, IqcTicketSection.Of("MT-01", "MT"));
    }

    [Fact]
    public void Moi_hang_muc_deu_co_MOT_muc()
    {
        var total = Count(IqcTicketSection.Documents)
                  + Count(IqcTicketSection.Packaging)
                  + Count(IqcTicketSection.Visual)
                  + Count(IqcTicketSection.Dimension)
                  + Count(IqcTicketSection.Functional);
        Assert.Equal(Library.Length, total);
    }

    [Fact]
    public void Nhom_MT_bi_che_theo_MA()
    {
        Assert.Equal(IqcTicketSection.Documents,  IqcTicketSection.Of("MT-02", "MT"));
        Assert.Equal(IqcTicketSection.Functional, IqcTicketSection.Of("MT-01", "MT"));
        Assert.Equal(IqcTicketSection.Functional, IqcTicketSection.Of("MT-03", "MT"));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("ZZ-99", "ZZ")]
    public void Hang_muc_la_roi_ve_Functional(string? item, string? group)
        => Assert.Equal(IqcTicketSection.Functional, IqcTicketSection.Of(item, group));
}
