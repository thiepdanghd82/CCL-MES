using System.Globalization;
using CCL.MES.Infrastructure.SpecImport;

namespace CCL.MES.Api.Tests.SpecImport;

/// <summary>
/// Bất biến: **import spec KHÔNG được phụ thuộc locale của máy chạy import.**
///
/// <para>Vì sao đây là bug nghiêm trọng chứ không phải chuyện định dạng:
/// ClosedXML <c>GetFormattedString()</c> áp number-format của ô theo
/// <see cref="CultureInfo.CurrentCulture"/>. Trước khi chốt culture, CÙNG một
/// file .xlsx cho ra dữ liệu khác nhau — máy VN ra <c>"2.000 pcs/Roll"</c>,
/// runner CI en-US ra <c>"2,000 pcs/Roll"</c>. Dữ liệu đó bị **đóng băng** vào
/// <c>WoTraceSnapshot</c> và **in ra tờ Spec khách hàng audit**, nên hai kỹ sư
/// import cùng một file trên hai máy sẽ tạo ra hai bằng chứng khác nhau.</para>
///
/// <para>Triệu chứng đã xảy ra thật: 2 test parser xanh trên máy dev (locale VN)
/// nhưng đỏ trên CI ubuntu — "test-green / runtime-broken" đúng nghĩa. Test dưới
/// đây chạy parser dưới NHIỀU culture và bắt buộc kết quả giống hệt nhau, nên
/// nếu ai gỡ chốt culture thì CI đỏ ngay bất kể chạy ở đâu.</para>
/// </summary>
public sealed class XlsxImportCultureTests
{
    private static readonly string FixtureDir =
        Path.Combine(AppContext.BaseDirectory, "SpecImport", "Fixtures");

    private static CCL.MES.Application.SpecImport.ParsedSpecDto ParseUnder(
        string culture, string file, string category)
    {
        var prev = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var parser = new IndigoLetterpressXlsxParser();
            using var fs = File.OpenRead(Path.Combine(FixtureDir, file));
            return parser.Parse(fs, category);
        }
        finally
        {
            CultureInfo.CurrentCulture = prev;
        }
    }

    [Theory]
    // Ba locale với quy ước phân cách nghìn/thập phân KHÁC NHAU:
    //   en-US  1,234.5   ·  vi-VN 1.234,5  ·  de-DE 1.234,5  ·  Invariant 1,234.5
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("")]        // InvariantCulture
    public void Numeric_cells_render_identically_regardless_of_machine_locale(string culture)
    {
        var baseline = ParseUnder("vi-VN", "LP_letterpress_sample.xlsx", "letterpress");
        var underTest = ParseUnder(culture, "LP_letterpress_sample.xlsx", "letterpress");

        // Ô Packing là ô số có number-format — chính là ô đã làm CI đỏ.
        var expected = Assert.Single(baseline.FlexoCuttingRows).Packing;
        var actual = Assert.Single(underTest.FlexoCuttingRows).Packing;
        Assert.Equal(expected, actual);

        // Không chỉ một ô: kích thước sản phẩm cũng đi qua cùng đường parse.
        Assert.Equal(baseline.ProductSizeW, underTest.ProductSizeW);
        Assert.Equal(baseline.ProductSizeH, underTest.ProductSizeH);
        Assert.Equal(baseline.RefNo, underTest.RefNo);
    }

    [Fact]
    public void Parsing_does_not_leak_its_culture_to_the_caller()
    {
        // Import chốt culture cho riêng nó; phần còn lại của request (i18n,
        // format số trên UI) phải giữ nguyên culture của người dùng.
        var before = CultureInfo.GetCultureInfo("en-US");
        var prev = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = before;
            var parser = new IndigoLetterpressXlsxParser();
            using var fs = File.OpenRead(Path.Combine(FixtureDir, "LP_letterpress_sample.xlsx"));
            parser.Parse(fs, "letterpress");

            Assert.Equal(before.Name, CultureInfo.CurrentCulture.Name);
        }
        finally
        {
            CultureInfo.CurrentCulture = prev;
        }
    }
}
