using CCL.MES.Shared.IpqcReview;
using Xunit;

namespace CCL.MES.Hybrid.Client.Tests;

/// <summary>
/// i18n bảng IPQC — mắt xích 3: chọn ngôn ngữ trên DTO. Bốn trường QC (nhãn ·
/// nhóm · phương pháp · tiêu chí) là DỮ LIỆU ĐÃ ĐÓNG BĂNG chứ không phải khoá
/// resx, nên không đi qua <c>T()</c>; chúng đi qua <c>*For(bool)</c>.
///
/// <para>Luật bất di: THIẾU bản EN thì rơi về bản VI, KHÔNG BAO GIỜ để ô trống.
/// Mọi hạng mục materialize trước tính năng này đều ở trạng thái thiếu EN, nên
/// đường lùi mới là đường chạy thường xuyên, không phải ngoại lệ hiếm.</para>
///
/// Mắt xích 1 (từ vựng) + 2 (đóng băng) nằm ở <c>CCL.MES.Tests.Unit.IpqcItemEnglishTests</c>.
/// </summary>
public sealed class IpqcViewItemLanguageTests
{


    [Fact]
    public void For_true_lay_EN_For_false_lay_VI()
    {
        var i = new IpqcViewItem
        {
            ItemKey = "LBL-A1",
            GroupLabel = "A·Ngoại quan", GroupLabelEn = "A·Appearance",
            Label = "Đúng nội dung in", LabelEn = "Print content correct",
            AcceptanceCriteria = "Đúng so spec", AcceptanceCriteriaEn = "Matches the spec",
            Method = "Soi mắt", MethodEn = "Visual inspection",
        };

        Assert.Equal("A·Appearance", i.GroupLabelFor(english: true));
        Assert.Equal("Print content correct", i.LabelFor(english: true));
        Assert.Equal("Matches the spec", i.AcceptanceCriteriaFor(english: true));
        Assert.Equal("Visual inspection", i.MethodFor(english: true));

        Assert.Equal("A·Ngoại quan", i.GroupLabelFor(english: false));
        Assert.Equal("Đúng nội dung in", i.LabelFor(english: false));
        Assert.Equal("Đúng so spec", i.AcceptanceCriteriaFor(english: false));
        Assert.Equal("Soi mắt", i.MethodFor(english: false));
    }

    [Fact]
    public void Thieu_EN_thi_roi_ve_VI_chu_khong_de_o_trong()
    {
        // Đây là trạng thái của MỌI hạng mục materialize trước tính năng này.
        var i = new IpqcViewItem
        {
            ItemKey = "LBL-A1",
            GroupLabel = "A·Ngoại quan",
            Label = "Đúng nội dung in",
            AcceptanceCriteria = "Đúng so spec",
            Method = "Soi mắt",
        };

        Assert.Equal("A·Ngoại quan", i.GroupLabelFor(english: true));
        Assert.Equal("Đúng nội dung in", i.LabelFor(english: true));
        Assert.Equal("Đúng so spec", i.AcceptanceCriteriaFor(english: true));
        Assert.Equal("Soi mắt", i.MethodFor(english: true));
    }

    [Fact]
    public void Ban_EN_rong_cung_roi_ve_VI()
    {
        var i = new IpqcViewItem
        {
            ItemKey = "LBL-A1",
            Label = "Đúng nội dung in", LabelEn = "",
            Method = "Soi mắt", MethodEn = "   ",
        };

        Assert.Equal("Đúng nội dung in", i.LabelFor(english: true));
        Assert.Equal("Soi mắt", i.MethodFor(english: true));
    }
}
