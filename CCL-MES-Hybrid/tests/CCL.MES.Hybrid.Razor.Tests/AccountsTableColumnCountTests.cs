using System.Text.RegularExpressions;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// Hàng TỔNG của bảng phân quyền phải rộng ĐÚNG BẰNG hàng tiêu đề.
///
/// <para><b>Sự cố 2026-09-15.</b> <c>&lt;td colspan="8"&gt;</c> trong khi chỉ có
/// 7 cột đứng trước nhóm ô tick. Hàng TỔNG vì thế dài hơn hàng tiêu đề đúng một
/// ô, và MỌI con số tổng trượt sang phải một cột: số của "Xem dữ liệu" hiện dưới
/// "Nhập/Sửa", số của "Nhập/Sửa" hiện dưới "Phê duyệt QC"… Bảng trông hoàn toàn
/// bình thường — không lệch, không vỡ, không lỗi — nó chỉ nói sai ai có quyền gì.
/// Đó là kiểu hỏng tệ nhất với một hồ sơ phân quyền.</para>
///
/// <para>Test đọc thẳng file <c>.razor</c> thay vì render: cái cần khoá là QUAN HỆ
/// SỐ HỌC giữa hai hàng, nó nằm trong markup và đọc được mà không cần dựng cả
/// trang (trang này cần client API + phiên đăng nhập).</para>
/// </summary>
public sealed class AccountsTableColumnCountTests
{
    private static string RazorSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName,
                "src", "CCL.MES.Hybrid.Razor", "Pages", "SettingsAccounts.razor");
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            "Không tìm thấy SettingsAccounts.razor khi đi ngược từ " + AppContext.BaseDirectory);
    }

    [Fact]
    public void Hang_TONG_rong_dung_bang_hang_tieu_de()
    {
        var src = RazorSource();

        var thead = Between(src, "<thead>", "</thead>");
        var tfoot = Between(src, "<tfoot>", "</tfoot>");

        // Cột cố định = <th> viết thẳng, KHÔNG nằm trong @foreach.
        // "actions" cũng là <th>@T("accounts.col.…") nhưng đứng CUỐI, sau nhóm ô
        // tick — không thuộc nhóm cột dẫn đầu. Bỏ nó ra, nếu không phép đếm lệch 1
        // (chính tôi đã mắc lúc viết test này).
        var leadingTh = LeadingHeaderCount(thead);
        var colspan = int.Parse(Regex.Match(tfoot, @"colspan=""(\d+)""").Groups[1].Value);

        Assert.Equal(leadingTh, colspan);

        // Hai hàng cùng lặp trên PermKeys, nên chỉ cần khớp phần đuôi cố định:
        // tiêu đề có thêm "Số quyền" + "Thao tác"; hàng tổng có 2 ô rỗng tương ứng.
        var trailingTh = Regex.Matches(thead, @"<th class=""acc-perm-th"">@T\(""accounts\.perm\.count").Count
                       + Regex.Matches(thead, @"<th>@T\(""accounts\.col\.actions").Count;
        var trailingEmpty = Regex.Matches(tfoot, @"<td></td>").Count;
        Assert.Equal(trailingTh, trailingEmpty);

        // Cả hai đều lặp đúng một lần trên cùng bộ khoá quyền.
        Assert.Equal(1, Regex.Matches(thead, @"@foreach \(var perm in PermKeys\)").Count);
        Assert.Equal(1, Regex.Matches(tfoot, @"@foreach \(var perm in PermKeys\)").Count);
    }

    [Fact]
    public void So_the_col_khop_so_cot_tieu_de()
    {
        // <colgroup> lái bề rộng; lệch với <thead> thì mọi cột sau đó rộng sai.
        var src = RazorSource();
        var colgroup = Between(src, "<colgroup>", "</colgroup>");
        var thead = Between(src, "<thead>", "</thead>");

        var leadingCols = Regex.Matches(colgroup, @"<col class=""acc-col-(?!perm\b)").Count;
        var leadingTh = LeadingHeaderCount(thead);

        // acc-col-permcount + acc-col-actions nằm trong leadingCols nhưng ở CUỐI,
        // nên trừ chúng ra để so với nhóm <th> đứng trước ô tick.
        Assert.Equal(leadingTh + 2, leadingCols);
    }

    private static int LeadingHeaderCount(string thead) =>
        Regex.Matches(thead, @"<th>@T\(""accounts\.col\.(?!actions)").Count;

    private static string Between(string src, string open, string close)
    {
        var i = src.IndexOf(open, StringComparison.Ordinal);
        var j = src.IndexOf(close, StringComparison.Ordinal);
        Assert.True(i >= 0 && j > i, $"Không thấy khối {open}…{close}");
        return src[i..j];
    }
}
