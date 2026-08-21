using Bunit;
using CCL.MES.Hybrid.Client.Windows;
using CCL.MES.Hybrid.Razor.Shared;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// HM1 — trong workspace đa-cửa-sổ, tiêu đề trang đã hiện trên thanh
/// <c>FloatingWindow</c> nên tiêu đề trong thân bị TRÙNG. PageHead nhận
/// cascaded <see cref="WindowContext"/> để ẩn tiêu đề NHÌN THẤY (thu lại khoảng
/// trống cho bảng) nhưng GIỮ &lt;h1 class="page-title"&gt; trong DOM dạng
/// sr-only — screen-reader + FocusOnNavigate(Selector="h1") + test bám
/// .page-title KHÔNG được vỡ. Full-route (không cascade) render y như cũ.
/// </summary>
public sealed class PageHeadWindowedTests : TestContext
{
    private void Wire() => Services.AddI18n();

    [Fact]
    public void Outside_a_window_the_title_renders_normally_with_no_windowed_class()
    {
        Wire();
        var cut = RenderComponent<PageHead>(p => p.Add(x => x.Title, "QC History"));

        // h1 tồn tại + đúng class + nội dung
        var h1 = cut.Find("h1.page-title");
        Assert.Equal("QC History", h1.TextContent);

        // KHÔNG có class ẩn khi ngoài cửa sổ
        Assert.DoesNotContain("is-windowed", cut.Markup);
        Assert.DoesNotContain("ix-sr-only", h1.ClassList);
    }

    [Fact]
    public void Inside_a_window_the_title_stays_in_the_dom_but_the_head_is_flagged_windowed()
    {
        Wire();
        var cut = RenderComponent<PageHead>(p => p
            .Add(x => x.Title, "QC History")
            .AddCascadingValue(new WindowContext { IsActive = true }));

        // h1 VẪN tồn tại trong DOM (screen-reader + focus target + test selector)
        var h1 = cut.Find("h1.page-title");
        Assert.Equal("QC History", h1.TextContent);

        // Khung đầu trang được đánh dấu windowed → CSS .ix-page-head.is-windowed
        // .page-title thu về sr-only (ẩn nhìn thấy, giữ khoảng trống cho bảng).
        var head = cut.Find(".ix-page-head");
        Assert.Contains("is-windowed", head.ClassList);
    }

    [Fact]
    public void Windowed_head_still_shows_eyebrow_and_subtitle_they_do_not_duplicate_the_titlebar()
    {
        Wire();
        var cut = RenderComponent<PageHead>(p => p
            .Add(x => x.Title, "QC History")
            .Add(x => x.Eyebrow, "QMS")
            .Add(x => x.Subtitle, "128 records")
            .AddCascadingValue(new WindowContext { IsActive = true }));

        // Eyebrow + subtitle KHÔNG trùng tiêu đề ⇒ vẫn hiển thị.
        Assert.Contains("QMS", cut.Find(".ix-page-eyebrow").TextContent);
        Assert.Contains("128 records", cut.Find(".ix-page-sub").TextContent);
    }
}
