using System.Net;
using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Razor.Shared;
using CCL.MES.Hybrid.Razor.Tests._Support;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.RunningSurface;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// P10.7c-3 — bUnit render tests for <see cref="SettingDashboard"/>.
///
/// Rule 4: every <input>/<button>/<select> rendered here uses plain HTML
/// elements (no &lt;InputText&gt;) to avoid the renderer-crash lesson.
/// Rule 7.3 wire-mirror: every wire-path probe here has a paired server
/// integration test in <c>RunningSurfaceControllerTests</c> hitting the
/// same endpoint via TestServer. The mirror trail is called out per
/// fixture.
/// </summary>
public sealed class SettingDashboardTests : TestContext
{
    public SettingDashboardTests()
    {
        var api = new RecordingApi();
        Services.AddSingleton<ICclApiClient>(api);
        Services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>),
            typeof(NullLogger<>));
        Services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance);
        this.AddTestAuthorization().SetAuthorized("test-user");
    }

    private static RunningSurfaceView SettingView(
        string etag = "abc==",
        DateTime? startAt = null,
        string phase = "SETTING") => new()
    {
        WoId = 42,
        WoNo = "WO-26-3701",
        MesPhase = phase,
        ETag = etag,
        TargetQty = 1000,
        SettingStartAt = startAt,
    };

    // ── Initial render ─────────────────────────────────────────────

    [Fact]
    public void Initial_render_shows_loading_then_view_when_api_resolves()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.RunningSurfaceViewImpl = (_, _) => Task.FromResult(SettingView(startAt: DateTime.UtcNow.AddMinutes(-2)));

        var cut = RenderComponent<SettingDashboard>(p => p.Add(d => d.WorkOrderId, 42L));

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='setting-dashboard']"));
            Assert.NotNull(cut.Find("[data-testid='setting-timer']"));
            Assert.NotNull(cut.Find("[data-testid='setting-checklist']"));
        });
        Assert.Single(api.RunningSurfaceViewCalls);
        Assert.Equal(42L, api.RunningSurfaceViewCalls[0]);
    }

    [Fact]
    public void Initial_load_failure_shows_localised_error_banner()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.RunningSurfaceViewImpl = (_, _) => Task.FromException<RunningSurfaceView>(
            new ApiException((int)HttpStatusCode.NotFound,
                new ApiError { Code = "wo.not_found", MessageEn = "no wo" }));

        var cut = RenderComponent<SettingDashboard>(p => p.Add(d => d.WorkOrderId, 42L));

        cut.WaitForAssertion(() =>
        {
            var banner = cut.Find("[data-testid='setting-initial-error']");
            Assert.Contains("Không tìm thấy WO trên máy chủ.", banner.TextContent);
        });
    }

    [Fact]
    public void Invalid_phase_banner_renders_when_phase_is_not_SETTING()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.RunningSurfaceViewImpl = (_, _) => Task.FromResult(SettingView(phase: "RUNNING"));

        var cut = RenderComponent<SettingDashboard>(p => p.Add(d => d.WorkOrderId, 42L));

        cut.WaitForAssertion(() =>
        {
            var banner = cut.Find("[data-testid='setting-invalid-phase']");
            Assert.Contains("không ở giai đoạn SETTING", banner.TextContent);
        });
    }

    // ── Entry stamp (closes 7c-2 gap) ──────────────────────────────
    // Wire-mirror: RunningSurfaceControllerTests.Setting_enter_idempotent_no_double_stamp
    // (added as part of this stack to confirm POST /setting/enter contract)

    [Fact]
    public void SettingStartAt_null_fires_setting_enter_with_If_Match()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        var callIdx = 0;
        api.RunningSurfaceViewImpl = (_, _) =>
        {
            callIdx++;
            return Task.FromResult(callIdx == 1
                ? SettingView(etag: "v1", startAt: null)
                : SettingView(etag: "v2", startAt: DateTime.UtcNow));
        };
        api.SettingEnterImpl = (_, _, _) => Task.FromResult(new RunningSurfaceSetResponse
        {
            Ok = true,
            ETag = "v2",
            MesPhase = "SETTING",
        });

        var cut = RenderComponent<SettingDashboard>(p => p.Add(d => d.WorkOrderId, 42L));

        cut.WaitForAssertion(() =>
        {
            Assert.Single(api.SettingEnterCalls);
            Assert.Equal("v1", api.SettingEnterCalls[0].ETag);
            // First load (initial) + reload after enter = 2.
            Assert.Equal(2, api.RunningSurfaceViewCalls.Count);
        });
    }

    [Fact]
    public void SettingStartAt_already_stamped_skips_setting_enter_call()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.RunningSurfaceViewImpl = (_, _) => Task.FromResult(
            SettingView(etag: "v1", startAt: DateTime.UtcNow.AddMinutes(-3)));

        var cut = RenderComponent<SettingDashboard>(p => p.Add(d => d.WorkOrderId, 42L));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='setting-timer']")));
        Assert.Empty(api.SettingEnterCalls);
    }

    // ── Checklist gating ───────────────────────────────────────────

    [Fact]
    public void Setting_done_button_disabled_until_all_checklist_items_ticked()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.RunningSurfaceViewImpl = (_, _) => Task.FromResult(
            SettingView(startAt: DateTime.UtcNow.AddMinutes(-1)));

        var cut = RenderComponent<SettingDashboard>(p => p.Add(d => d.WorkOrderId, 42L));

        cut.WaitForAssertion(() =>
        {
            var btn = cut.Find("[data-testid='setting-done-btn']");
            Assert.True(btn.HasAttribute("disabled"),
                "Checklist not all checked → done button must be disabled.");
            Assert.NotNull(cut.Find("[data-testid='setting-action-hint']"));
        });
    }

    [Fact]
    public void Setting_done_button_enabled_when_all_checklist_items_ticked()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.RunningSurfaceViewImpl = (_, _) => Task.FromResult(
            SettingView(startAt: DateTime.UtcNow.AddMinutes(-1)));

        var cut = RenderComponent<SettingDashboard>(p => p.Add(d => d.WorkOrderId, 42L));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='setting-checklist']")));
        Assert.Equal(6, cut.FindAll("[data-testid^='setting-check-']").Count);
        // Re-find on each iteration: the parent component re-renders on
        // every check toggle (CheckedCount pill rebuilds) which would
        // invalidate cached element refs if we cached up-front.
        for (var i = 0; i < 6; i++)
            cut.Find($"[data-testid='setting-check-{i}']").Change(true);

        cut.WaitForAssertion(() =>
        {
            var btn = cut.Find("[data-testid='setting-done-btn']");
            Assert.False(btn.HasAttribute("disabled"),
                "All 6 ticked → done button must be enabled.");
        });
    }

    // ── Happy path — setting/done ──────────────────────────────────
    // Wire-mirror: RunningSurfaceControllerTests.Setting_done_from_SETTING_returns_200_advances_to_IPQC_WAIT

    [Fact]
    public void Setting_done_click_posts_with_If_Match_and_reloads_view()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        var startAt = DateTime.UtcNow.AddMinutes(-5);
        api.RunningSurfaceViewImpl = (_, _) => Task.FromResult(SettingView(etag: "v1", startAt: startAt));
        api.SettingDoneImpl = (_, ifMatch, _) => Task.FromResult(new RunningSurfaceSetResponse
        {
            Ok = true,
            ETag = "v2",
            MesPhase = "IPQC_WAIT",
        });

        var cut = RenderComponent<SettingDashboard>(p => p.Add(d => d.WorkOrderId, 42L));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='setting-done-btn']")));
        for (var i = 0; i < 6; i++)
            cut.Find($"[data-testid='setting-check-{i}']").Change(true);

        cut.Find("[data-testid='setting-done-btn']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(api.SettingDoneCalls);
            Assert.Equal("v1", api.SettingDoneCalls[0].ETag);
        });
    }

    // ── L21 auto-refresh (Henry RCA on PR #119) ────────────────────

    [Fact]
    public void Setting_done_success_invokes_OnPhaseChanged()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.RunningSurfaceViewImpl = (_, _) => Task.FromResult(
            SettingView(etag: "v1", startAt: DateTime.UtcNow.AddMinutes(-1)));
        api.SettingDoneImpl = (_, _, _) => Task.FromResult(new RunningSurfaceSetResponse
        {
            Ok = true, ETag = "v2", MesPhase = "IPQC_WAIT",
        });

        var phaseChangedCount = 0;
        var cut = RenderComponent<SettingDashboard>(p => p
            .Add(d => d.WorkOrderId, 42L)
            .Add(d => d.OnPhaseChanged, EventCallback.Factory.Create(this, () => phaseChangedCount++)));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='setting-done-btn']")));
        for (var i = 0; i < 6; i++)
            cut.Find($"[data-testid='setting-check-{i}']").Change(true);
        cut.Find("[data-testid='setting-done-btn']").Click();

        cut.WaitForAssertion(() => Assert.Equal(1, phaseChangedCount));
    }

    [Fact]
    public void Setting_done_409_conflict_does_NOT_invoke_OnPhaseChanged()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.RunningSurfaceViewImpl = (_, _) => Task.FromResult(
            SettingView(etag: "v1", startAt: DateTime.UtcNow.AddMinutes(-1)));
        api.SettingDoneImpl = (_, _, _) => Task.FromResult(new RunningSurfaceSetResponse
        {
            Ok = false, ErrorCode = "wo.state_conflict", ETag = "v2", MesPhase = "SETTING",
        });

        var phaseChangedCount = 0;
        var cut = RenderComponent<SettingDashboard>(p => p
            .Add(d => d.WorkOrderId, 42L)
            .Add(d => d.OnPhaseChanged, EventCallback.Factory.Create(this, () => phaseChangedCount++)));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='setting-done-btn']")));
        for (var i = 0; i < 6; i++)
            cut.Find($"[data-testid='setting-check-{i}']").Change(true);
        cut.Find("[data-testid='setting-done-btn']").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='setting-set-error']")));
        Assert.Equal(0, phaseChangedCount);
    }

    // ── 409 conflict surfaces banner ───────────────────────────────

    [Fact]
    public void Setting_done_409_state_conflict_renders_set_error_banner()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.RunningSurfaceViewImpl = (_, _) => Task.FromResult(
            SettingView(etag: "v1", startAt: DateTime.UtcNow.AddMinutes(-1)));
        api.SettingDoneImpl = (_, _, _) => Task.FromResult(new RunningSurfaceSetResponse
        {
            Ok = false,
            ErrorCode = "wo.state_conflict",
            ETag = "v2",
            MesPhase = "SETTING",
        });

        var cut = RenderComponent<SettingDashboard>(p => p.Add(d => d.WorkOrderId, 42L));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='setting-done-btn']")));
        for (var i = 0; i < 6; i++)
            cut.Find($"[data-testid='setting-check-{i}']").Change(true);
        cut.Find("[data-testid='setting-done-btn']").Click();

        cut.WaitForAssertion(() =>
        {
            var banner = cut.Find("[data-testid='setting-set-error']");
            Assert.Contains("Một thao tác khác đã cập nhật WO này", banner.TextContent);
        });
    }
}
