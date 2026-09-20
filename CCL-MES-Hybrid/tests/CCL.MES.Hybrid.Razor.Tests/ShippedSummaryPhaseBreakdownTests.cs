using Bunit;
using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Razor.Shared;
using CCL.MES.Hybrid.Razor.Tests._Support;
using CCL.MES.Shared.WoQcReview;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// Khối "Thời gian theo công đoạn" trên <see cref="ShippedSummaryDashboard"/>
/// — mặt hiển thị của <c>WoPhaseSpan</c> (hợp đồng §5.7).
///
/// <para><b>Điều đáng khoá nhất</b> không phải con số tổng, mà là
/// <c>VisitCount &gt; 1</c>: nó nói WO ĐÃ BỊ TRẢ VỀ công đoạn đó. Trước khi có
/// bảng span, chỉ số rework vô hình hoàn toàn — mọi trường thời gian hiện có
/// đều là một-dòng-ghi-đè nên lần vào thứ hai xoá sạch lần thứ nhất.</para>
///
/// <para>Khoá thêm hai thứ dễ bị làm hỏng khi ai đó "dọn" sau này: nhãn công
/// đoạn phải đi qua <c>PhaseText()</c> (người đứng máy đọc "Chờ IPQC", không
/// phải <c>IPQC_WAIT</c> — gate phase-label), và WO không có mốc thì KHÔNG
/// render khối này chứ không hiện bảng rỗng.</para>
/// </summary>
public sealed class ShippedSummaryPhaseBreakdownTests : TestContext
{
    public ShippedSummaryPhaseBreakdownTests()
    {
        Services.AddSingleton<ICclApiClient>(new RecordingApi());
        Services.AddI18n();
        Services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        Services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance);
    }

    private static WoSummaryReport Report(params WoSummaryPhaseRow[] phases) => new()
    {
        WoId = 7,
        WoNo = "WO-26-3684",
        MesPhase = "SHIPPED",
        Totals = new() { QtyTarget = 1000, QtyDone = 980, QtyNg = 20 },
        Runtime = new() { RunSeconds = 7200, PauseSeconds = 600, SessionCount = 2 },
        Oee = new() { Availability = 0.92, Performance = 0.85, Quality = 0.98, Oee = 0.766 },
        PhaseBreakdown = phases,
        PhaseTotalSeconds = phases.Sum(p => p.TotalSeconds),
    };

    private IRenderedComponent<ShippedSummaryDashboard> Render(WoSummaryReport report)
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.WoSummaryReportImpl = (_, _) => Task.FromResult(report);
        return RenderComponent<ShippedSummaryDashboard>(p => p.Add(d => d.WorkOrderId, 7L));
    }

    // ── THE one worth locking ───────────────────────────────────────

    [Fact]
    public void Rework_count_is_shown_only_for_stages_the_wo_was_sent_back_to()
    {
        var cut = Render(Report(
            new WoSummaryPhaseRow { Phase = "PREPRESS", TotalSeconds = 2400, LastSeconds = 600, VisitCount = 3 },
            new WoSummaryPhaseRow { Phase = "IPQC_WAIT", TotalSeconds = 600, LastSeconds = 600, VisitCount = 1 }));

        cut.WaitForAssertion(() =>
            Assert.NotNull(cut.Find("[data-testid='shipped-phase-breakdown']")));

        // Công đoạn bị trả về ⇒ có chip "N lượt".
        var chip = cut.Find("[data-testid='shipped-phase-rework-PREPRESS']");
        Assert.Contains("3", chip.TextContent);
        Assert.Contains("has-rework", cut.Find("[data-testid='shipped-phase-PREPRESS']").GetAttribute("class")!);

        // Công đoạn đi một lần ⇒ KHÔNG chip. "1 lượt" không phải tin tức, và
        // một cột toàn "1 lượt" làm chìm mất dòng đáng nhìn.
        Assert.Empty(cut.FindAll("[data-testid='shipped-phase-rework-IPQC_WAIT']"));
        Assert.DoesNotContain("has-rework",
            cut.Find("[data-testid='shipped-phase-IPQC_WAIT']").GetAttribute("class")!);
    }

    // ── nhãn cho người đứng máy, không phải token ───────────────────

    [Fact]
    public void Stage_name_is_rendered_through_the_shared_phase_vocabulary()
    {
        var cut = Render(Report(
            new WoSummaryPhaseRow { Phase = "IPQC_WAIT", TotalSeconds = 600, LastSeconds = 600, VisitCount = 1 }));

        cut.WaitForAssertion(() =>
            Assert.NotNull(cut.Find("[data-testid='shipped-phase-breakdown']")));

        var row = cut.Find("[data-testid='shipped-phase-IPQC_WAIT']").TextContent;
        Assert.DoesNotContain("IPQC_WAIT", row, StringComparison.Ordinal);   // token thô
        Assert.DoesNotContain("shipped.phases", row, StringComparison.Ordinal); // key chưa dịch
    }

    // ── WO còn đang ở trong công đoạn ───────────────────────────────

    [Fact]
    public void Open_stage_is_flagged_so_the_number_is_not_read_as_final()
    {
        var cut = Render(Report(
            new WoSummaryPhaseRow { Phase = "OQC_PENDING", TotalSeconds = 900, LastSeconds = 900, VisitCount = 1, IsOpen = true }));

        cut.WaitForAssertion(() =>
            Assert.NotNull(cut.Find("[data-testid='shipped-phase-breakdown']")));

        Assert.Single(cut.FindAll(".shipped-phase-open"));
    }

    // ── không có mốc ⇒ không render bảng rỗng ───────────────────────

    /// <summary>WO chạy trước migration <c>AddWoPhaseSpan</c> không có span.
    /// Khối phải BIẾN MẤT, không phải hiện một bảng trống — bảng trống đọc
    /// thành "công đoạn tốn 0 giây", khác hẳn "chưa đo".</summary>
    [Fact]
    public void Section_is_absent_entirely_when_the_wo_has_no_spans()
    {
        var cut = Render(Report());

        cut.WaitForAssertion(() =>
            Assert.NotNull(cut.Find("[data-testid='shipped-summary-dashboard']")));
        Assert.Empty(cut.FindAll("[data-testid='shipped-phase-breakdown']"));
    }

    // ── tổng ────────────────────────────────────────────────────────

    [Fact]
    public void Total_across_stages_is_surfaced_as_the_card_pill()
    {
        var cut = Render(Report(
            new WoSummaryPhaseRow { Phase = "PREPRESS", TotalSeconds = 3600, LastSeconds = 1800, VisitCount = 2 },
            new WoSummaryPhaseRow { Phase = "FQC_PENDING", TotalSeconds = 1800, LastSeconds = 1800, VisitCount = 1 }));

        cut.WaitForAssertion(() =>
            Assert.NotNull(cut.Find("[data-testid='shipped-phase-total']")));
        // 5400s = 1h 30m — khớp FormatDuration của dashboard.
        Assert.Contains("1", cut.Find("[data-testid='shipped-phase-total']").TextContent);
    }
}
