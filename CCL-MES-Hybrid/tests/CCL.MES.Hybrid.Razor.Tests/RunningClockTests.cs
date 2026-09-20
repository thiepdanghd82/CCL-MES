using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Razor.Shared;
using CCL.MES.Hybrid.Razor.Tests._Support;
using CCL.MES.Shared.ReasonCodes;
using CCL.MES.Shared.RunningSurface;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// Đồng hồ giờ máy trên <see cref="RunningDashboard"/> — nguồn số cho OEE.
///
/// <para><b>Bug class được chặn ở đây.</b> <c>Pause</c> ĐÓNG phiên chạy và
/// <c>Resume</c> mở phiên MỚI (<c>WoRunSessionService.Close</c> gọi từ luồng
/// pause). Nên một đồng hồ đếm thẳng từ <c>ActiveSessionStartAt</c> sẽ về 0 sau
/// mỗi lần tạm dừng và báo THIẾU giờ máy — đúng thứ làm hỏng chỉ số OEE mà cái
/// đồng hồ này sinh ra để phục vụ. Giờ đã chốt phải đến từ
/// <c>RunSecondsClosed</c> (server tính bằng <c>WoRuntimeMath</c>, cùng công
/// thức báo cáo OEE dùng); client chỉ cộng thêm phần đang mở.</para>
///
/// <para>Ai nối lại đồng hồ vào riêng <c>ActiveSessionStartAt</c> sẽ thấy
/// <see cref="Clock_keeps_banked_seconds_from_sessions_closed_by_a_pause"/> đỏ.</para>
/// </summary>
public sealed class RunningClockTests : TestContext
{
    public RunningClockTests()
    {
        Services.AddSingleton<ICclApiClient>(new RecordingApi());
        Services.AddI18n();
        Services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        Services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance);
        this.AddTestAuthorization().SetAuthorized("test-user");
    }

    private static RunningSurfaceView View(
        string phase = "RUNNING",
        long runSecondsClosed = 0,
        long pauseSecondsClosed = 0,
        DateTime? activeSessionStartAt = null,
        DateTime? activePauseStartAt = null) => new()
        {
            WoId = 42,
            WoNo = "WO-TEST-02",
            MesPhase = phase,
            ETag = "abc==",
            TargetQty = 500,
            QtyDoneCached = 0,
            QtyNgCached = 0,
            RunSecondsClosed = runSecondsClosed,
            PauseSecondsClosed = pauseSecondsClosed,
            ActiveSessionId = activeSessionStartAt is null ? null : 7,
            ActiveSessionStartAt = activeSessionStartAt,
            ActivePauseId = activePauseStartAt is null ? null : 9,
            ActivePauseStartAt = activePauseStartAt,
            RecentEntries = Array.Empty<RunningQtyEntryRow>(),
        };

    private IRenderedComponent<RunningDashboard> Render(RunningSurfaceView view)
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.RunningSurfaceViewImpl = (_, _) => Task.FromResult(view);
        return RenderComponent<RunningDashboard>(p => p
            .Add(d => d.WorkOrderId, view.WoId)
            .Add(d => d.ScrapReasons, Array.Empty<ReasonCodeOption>())
            .Add(d => d.PauseReasons, Array.Empty<ReasonCodeOption>()));
    }

    private static string Text(IRenderedComponent<RunningDashboard> c, string testId)
        => c.Find("[data-testid=\"" + testId + "\"]").TextContent.Trim();

    // ── THE regression ──────────────────────────────────────────────

    /// <summary>
    /// Các phiên đã đóng cộng lại 50 phút (bị cắt bởi một lần tạm dừng), rồi
    /// phiên thứ ba đang chạy 10 phút. Đồng hồ phải đọc ~01:00:00, KHÔNG phải
    /// chỉ 10 phút của phiên hiện tại.
    /// </summary>
    [Fact]
    public void Clock_keeps_banked_seconds_from_sessions_closed_by_a_pause()
    {
        var c = Render(View(
            runSecondsClosed: 50 * 60,
            pauseSecondsClosed: 7 * 60,
            activeSessionStartAt: DateTime.UtcNow.AddMinutes(-10)));

        var run = Text(c, "running-clock-run");
        Assert.StartsWith("01:0", run);              // 50' đã chốt + ~10' đang chạy
        Assert.NotEqual("00:10:00", run);            // ← giá trị NẾU bỏ mất phần đã chốt
        Assert.Equal("00:07:00", Text(c, "running-clock-pause"));
    }

    [Fact]
    public void Clock_reads_zero_before_any_time_is_banked()
    {
        var c = Render(View(activeSessionStartAt: DateTime.UtcNow));
        Assert.Equal("00:00:00", Text(c, "running-clock-run"));
        Assert.Equal("00:00:00", Text(c, "running-clock-pause"));
    }

    /// <summary>Giờ KHÔNG cuộn về 0 sau 24h — WO qua đêm vẫn phải đọc đúng
    /// tổng giờ máy, nếu không báo cáo OEE hụt trọn một ngày.</summary>
    [Fact]
    public void Clock_does_not_wrap_after_twenty_four_hours()
    {
        var c = Render(View(runSecondsClosed: 26 * 3600 + 5 * 60 + 9));
        Assert.Equal("26:05:09", Text(c, "running-clock-run"));
    }

    // ── Availability — phải khớp công thức của báo cáo OEE ──────────

    /// <summary>
    /// Availability = run / (run + pause) — đúng công thức Nakajima trong
    /// <c>WoSummaryReportBuilder</c>. Phiên và lần dừng không chồng nhau (pause
    /// đóng phiên) nên mẫu số không đếm trùng. 45' chạy / 15' dừng ⇒ 75%.
    /// </summary>
    [Fact]
    public void Availability_matches_the_oee_report_formula()
    {
        var c = Render(View(runSecondsClosed: 45 * 60, pauseSecondsClosed: 15 * 60));
        Assert.Equal("75%", Text(c, "running-clock-availability"));
    }

    /// <summary>Chưa đo được giây nào thì hiện "—". 0% nghĩa là máy đứng suốt,
    /// khác hẳn "chưa có số liệu" — hiện nhầm là nói dối người đọc chỉ số.</summary>
    [Fact]
    public void Availability_shows_a_dash_not_zero_percent_when_nothing_measured_yet()
    {
        var c = Render(View(activeSessionStartAt: DateTime.UtcNow));
        Assert.Equal("—", Text(c, "running-clock-availability"));
    }

    // ── Trạng thái tạm dừng ─────────────────────────────────────────

    /// <summary>Đang dừng thì đồng hồ giờ máy ĐỨNG YÊN — phải nhìn ra ngay,
    /// nếu không người đứng máy tưởng máy chạy mà số không lên.</summary>
    [Fact]
    public void Clock_is_visibly_flagged_while_paused()
    {
        var c = Render(View(
            phase: "PAUSED",
            runSecondsClosed: 20 * 60,
            activePauseStartAt: DateTime.UtcNow.AddMinutes(-3)));

        Assert.Contains("is-paused", c.Find("[data-testid=\"running-clock\"]").GetAttribute("class")!);
        Assert.Equal("00:20:00", Text(c, "running-clock-run"));   // giờ máy đứng ở mốc đã chốt
        Assert.StartsWith("00:0", Text(c, "running-clock-pause")); // chỉ thời gian DỪNG chạy tiếp
    }

    // ── Chỉ hiện ở phase còn ý nghĩa ────────────────────────────────

    [Theory]
    [InlineData("IPQC_WAIT")]
    [InlineData("DONE")]
    [InlineData("CANCELLED")]
    public void Clock_is_hidden_on_phases_where_machine_time_is_meaningless(string phase)
    {
        var c = Render(View(phase: phase, runSecondsClosed: 99 * 60));
        Assert.Empty(c.FindAll("[data-testid=\"running-clock\"]"));
    }

    // ── i18n ────────────────────────────────────────────────────────

    /// <summary>Nhãn đi qua catalog, không nằm trần trong markup. Khoá luôn để
    /// không ai hardcode "Thời gian chạy máy" vào .razor (cmes-i18n-parity).</summary>
    [Fact]
    public void Clock_labels_come_from_the_translation_catalog()
    {
        var c = Render(View(runSecondsClosed: 60));
        var clock = c.Find("[data-testid=\"running-clock\"]").TextContent;
        Assert.Contains("Thời gian chạy máy", clock, StringComparison.Ordinal);
        Assert.DoesNotContain("running.timer", clock, StringComparison.Ordinal);
    }
}
