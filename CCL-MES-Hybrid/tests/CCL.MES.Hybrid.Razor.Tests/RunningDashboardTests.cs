using System.Net;
using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Razor.Shared;
using CCL.MES.Hybrid.Razor.Tests._Support;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.ReasonCodes;
using CCL.MES.Shared.RunningSurface;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// P10.7c-3 — bUnit render tests for <see cref="RunningDashboard"/>.
///
/// Rule 4: every <input>/<button>/<select> rendered uses plain HTML (no
/// &lt;InputText&gt;). Rule 7.3 wire-mirror: every wire-path probe has a
/// paired server integration test in <c>RunningSurfaceControllerTests</c>
/// hitting the same endpoint via TestServer.
/// </summary>
public sealed class RunningDashboardTests : TestContext
{
    public RunningDashboardTests()
    {
        var api = new RecordingApi();
        Services.AddSingleton<ICclApiClient>(api);
        Services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>),
            typeof(NullLogger<>));
        Services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance);
        this.AddTestAuthorization().SetAuthorized("test-user");
    }

    private static IReadOnlyList<ReasonCodeOption> SampleScrap() => new[]
    {
        new ReasonCodeOption { Code = "SC-COLOR", LabelVi = "Lệch màu", Kind = "Scrap", Sort = 10 },
        new ReasonCodeOption { Code = "SC-MISALIGN", LabelVi = "Lệch vị trí", Kind = "Scrap", Sort = 20 },
    };

    private static IReadOnlyList<ReasonCodeOption> SamplePause() => new[]
    {
        new ReasonCodeOption { Code = "PA-CHANGEOVER", LabelVi = "Đổi vật tư", Kind = "Pause", Sort = 10 },
        new ReasonCodeOption { Code = "PA-BREAKDOWN", LabelVi = "Hỏng máy", Kind = "Pause", Sort = 20 },
    };

    private static RunningSurfaceView View(
        string etag = "abc==",
        string phase = "RUNNING",
        int qtyDone = 250,
        int qtyNg = 5,
        int target = 1000,
        long? activeSessionId = 7,
        long? activePauseId = null,
        string? activePauseReason = null,
        IReadOnlyList<RunningQtyEntryRow>? entries = null) => new()
    {
        WoId = 42,
        WoNo = "WO-26-3702",
        MesPhase = phase,
        ETag = etag,
        TargetQty = target,
        QtyDoneCached = qtyDone,
        QtyNgCached = qtyNg,
        ActiveSessionId = activeSessionId,
        ActiveSessionStartAt = activeSessionId is null ? null : DateTime.UtcNow.AddMinutes(-15),
        ActivePauseId = activePauseId,
        ActivePauseStartAt = activePauseId is null ? null : DateTime.UtcNow.AddMinutes(-2),
        ActivePauseReasonCode = activePauseReason,
        RecentEntries = entries ?? new[]
        {
            new RunningQtyEntryRow
            {
                EntryId = 1,
                CreatedAt = DateTime.UtcNow.AddMinutes(-10),
                QtyDoneDelta = 100,
                QtyNgDelta = 0,
                EnteredBy = "alice",
            },
            new RunningQtyEntryRow
            {
                EntryId = 2,
                CreatedAt = DateTime.UtcNow.AddMinutes(-5),
                QtyDoneDelta = 150,
                QtyNgDelta = 5,
                NgReasonCode = "SC-COLOR",
                NgNote = "First batch off",
                EnteredBy = "alice",
            },
        },
    };

    private static Bunit.IRenderedComponent<RunningDashboard> Render(Bunit.TestContext ctx,
        RunningSurfaceView view, IReadOnlyList<ReasonCodeOption>? scrap = null,
        IReadOnlyList<ReasonCodeOption>? pause = null,
        Action<RecordingApi>? extraApiSetup = null)
    {
        var api = (RecordingApi)ctx.Services.GetRequiredService<ICclApiClient>();
        api.RunningSurfaceViewImpl = (_, _) => Task.FromResult(view);
        extraApiSetup?.Invoke(api);
        return ctx.RenderComponent<RunningDashboard>(p => p
            .Add(d => d.WorkOrderId, view.WoId)
            .Add(d => d.ScrapReasons, scrap ?? SampleScrap())
            .Add(d => d.PauseReasons, pause ?? SamplePause()));
    }

    // ── Initial render gate ────────────────────────────────────────

    [Fact]
    public void Initial_render_shows_counter_with_three_cells()
    {
        var cut = Render(this, View());

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='running-dashboard']"));
            Assert.NotNull(cut.Find("[data-testid='running-counter']"));
            Assert.Equal("250", cut.Find("[data-testid='counter-done']").TextContent.Trim());
            Assert.Equal("5", cut.Find("[data-testid='counter-ng']").TextContent.Trim());
            Assert.Equal("1000", cut.Find("[data-testid='counter-target']").TextContent.Trim());
        });
    }

    [Fact]
    public void IPQC_APPROVED_phase_shows_run_start_button_only()
    {
        var cut = Render(this, View(phase: "IPQC_APPROVED", qtyDone: 0, qtyNg: 0));

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='run-start-btn']"));
        });
        Assert.Empty(cut.FindAll("[data-testid='running-counter']"));
        Assert.Empty(cut.FindAll("[data-testid='running-tap-grid']"));
    }

    // ── +100 / +500 / +1000 tap ────────────────────────────────────
    // Wire-mirror: RunningSurfaceControllerTests.Run_qty_add_happy_path_increments_cache

    [Fact]
    public void Tap_done_100_posts_with_If_Match_and_delta_100()
    {
        var cut = Render(this, View(etag: "v1"), extraApiSetup: api =>
        {
            api.RunQtyAddImpl = (_, _, _, _) => Task.FromResult(new RunningSurfaceSetResponse
            {
                Ok = true,
                ETag = "v2",
                MesPhase = "RUNNING",
                QtyDoneCached = 350,
                QtyNgCached = 5,
            });
        });

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='tap-done-100']")));
        cut.Find("[data-testid='tap-done-100']").Click();

        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        cut.WaitForAssertion(() =>
        {
            Assert.Single(api.RunQtyAddCalls);
            Assert.Equal("v1", api.RunQtyAddCalls[0].ETag);
            Assert.Equal(100, api.RunQtyAddCalls[0].Req.QtyDoneDelta);
            Assert.Equal(0, api.RunQtyAddCalls[0].Req.QtyNgDelta);
        });
    }

    [Fact]
    public void Tap_done_500_and_1000_send_correct_deltas()
    {
        var cut = Render(this, View(etag: "v1"), extraApiSetup: api =>
        {
            api.RunQtyAddImpl = (_, _, _, _) => Task.FromResult(new RunningSurfaceSetResponse
            {
                Ok = true, ETag = "v2", MesPhase = "RUNNING",
            });
        });

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='tap-done-500']")));
        cut.Find("[data-testid='tap-done-500']").Click();
        cut.WaitForAssertion(() =>
        {
            var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
            Assert.Equal(500, api.RunQtyAddCalls[^1].Req.QtyDoneDelta);
        });

        cut.Find("[data-testid='tap-done-1000']").Click();
        cut.WaitForAssertion(() =>
        {
            var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
            Assert.Equal(1000, api.RunQtyAddCalls[^1].Req.QtyDoneDelta);
        });
    }

    // ── NG submit ──────────────────────────────────────────────────
    // Wire-mirror: RunningSurfaceControllerTests.Run_qty_add_requires_NG_reason_when_NgDelta_gt_0

    [Fact]
    public void Ng_submit_disabled_until_amount_reason_note_all_valid()
    {
        var cut = Render(this, View(etag: "v1"));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='ng-submit-btn']")));
        Assert.True(cut.Find("[data-testid='ng-submit-btn']").HasAttribute("disabled"));

        cut.Find("[data-testid='ng-amount-input']").Input("3");
        Assert.True(cut.Find("[data-testid='ng-submit-btn']").HasAttribute("disabled"));

        cut.Find("[data-testid='ng-reason-select']").Change("SC-COLOR");
        Assert.True(cut.Find("[data-testid='ng-submit-btn']").HasAttribute("disabled"));

        cut.Find("[data-testid='ng-note-input']").Input("Lệch màu lô đầu");
        cut.WaitForAssertion(() =>
            Assert.False(cut.Find("[data-testid='ng-submit-btn']").HasAttribute("disabled")));
    }

    [Fact]
    public void Ng_submit_click_posts_qty_with_NgDelta_reason_and_note()
    {
        var cut = Render(this, View(etag: "v1"), extraApiSetup: api =>
        {
            api.RunQtyAddImpl = (_, _, req, _) => Task.FromResult(new RunningSurfaceSetResponse
            {
                Ok = true,
                ETag = "v2",
                MesPhase = "RUNNING",
                QtyDoneCached = 250,
                QtyNgCached = 8,
            });
        });

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='ng-amount-input']")));
        cut.Find("[data-testid='ng-amount-input']").Input("3");
        cut.Find("[data-testid='ng-reason-select']").Change("SC-COLOR");
        cut.Find("[data-testid='ng-note-input']").Input("Lệch màu lô đầu");
        cut.WaitForAssertion(() =>
            Assert.False(cut.Find("[data-testid='ng-submit-btn']").HasAttribute("disabled")));
        cut.Find("[data-testid='ng-submit-btn']").Click();

        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        cut.WaitForAssertion(() =>
        {
            Assert.Single(api.RunQtyAddCalls);
            var req = api.RunQtyAddCalls[0].Req;
            Assert.Equal(0, req.QtyDoneDelta);
            Assert.Equal(3, req.QtyNgDelta);
            Assert.Equal("SC-COLOR", req.NgReasonCode);
            Assert.Equal("Lệch màu lô đầu", req.NgNote);
        });
    }

    // ── Pause flow ─────────────────────────────────────────────────
    // Wire-mirror: RunningSurfaceControllerTests.Run_pause_happy_path_transitions_RUNNING_to_PAUSED

    [Fact]
    public void Pause_button_opens_pause_modal()
    {
        var cut = Render(this, View(etag: "v1"));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='run-pause-btn']")));
        Assert.Empty(cut.FindAll("[data-testid='pause-modal']"));
        cut.Find("[data-testid='run-pause-btn']").Click();
        cut.WaitForAssertion(() =>
            Assert.NotNull(cut.Find("[data-testid='pause-modal']")));
    }

    [Fact]
    public void Pause_modal_confirm_posts_with_reason_and_note()
    {
        var cut = Render(this, View(etag: "v1"), extraApiSetup: api =>
        {
            api.RunPauseImpl = (_, _, _, _) => Task.FromResult(new RunningSurfaceSetResponse
            {
                Ok = true, ETag = "v2", MesPhase = "PAUSED",
            });
        });

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='run-pause-btn']")));
        cut.Find("[data-testid='run-pause-btn']").Click();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='pause-modal']")));

        cut.Find("[data-testid='pause-reason-select']").Change("PA-CHANGEOVER");
        cut.Find("[data-testid='pause-note-input']").Input("Đổi cuộn vật tư mới");
        cut.WaitForAssertion(() =>
            Assert.False(cut.Find("[data-testid='pause-confirm-btn']").HasAttribute("disabled")));
        cut.Find("[data-testid='pause-confirm-btn']").Click();

        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        cut.WaitForAssertion(() =>
        {
            Assert.Single(api.RunPauseCalls);
            Assert.Equal("PA-CHANGEOVER", api.RunPauseCalls[0].Req.ReasonCode);
            Assert.Equal("Đổi cuộn vật tư mới", api.RunPauseCalls[0].Req.Note);
        });
    }

    // ── Resume flow ────────────────────────────────────────────────
    // Wire-mirror: RunningSurfaceControllerTests.Run_resume_happy_path_transitions_PAUSED_to_RUNNING

    [Fact]
    public void Paused_phase_shows_resume_button_not_pause_button()
    {
        var cut = Render(this, View(etag: "v1", phase: "PAUSED",
            activePauseId: 11, activePauseReason: "PA-BREAKDOWN"));

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='run-resume-btn']"));
            Assert.NotNull(cut.Find("[data-testid='running-pause-banner']"));
        });
        Assert.Empty(cut.FindAll("[data-testid='run-pause-btn']"));
    }

    [Fact]
    public void Resume_click_posts_with_If_Match()
    {
        var cut = Render(this, View(etag: "v1", phase: "PAUSED",
            activePauseId: 11, activePauseReason: "PA-BREAKDOWN"),
            extraApiSetup: api =>
            {
                api.RunResumeImpl = (_, _, _) => Task.FromResult(new RunningSurfaceSetResponse
                {
                    Ok = true, ETag = "v2", MesPhase = "RUNNING",
                });
            });

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='run-resume-btn']")));
        cut.Find("[data-testid='run-resume-btn']").Click();

        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        cut.WaitForAssertion(() =>
        {
            Assert.Single(api.RunResumeCalls);
            Assert.Equal("v1", api.RunResumeCalls[0].ETag);
        });
    }

    // ── Finish flow ────────────────────────────────────────────────
    // Wire-mirror: RunningSurfaceControllerTests.Run_finish_from_RUNNING_transitions_to_FQC_PENDING

    [Fact]
    public void Finish_button_disabled_when_QtyDoneCached_is_zero()
    {
        var cut = Render(this, View(etag: "v1", qtyDone: 0));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='run-finish-btn']")));
        Assert.True(cut.Find("[data-testid='run-finish-btn']").HasAttribute("disabled"));
    }

    [Fact]
    public void Finish_confirm_posts_with_If_Match()
    {
        var cut = Render(this, View(etag: "v1"), extraApiSetup: api =>
        {
            api.RunFinishImpl = (_, _, _) => Task.FromResult(new RunningSurfaceSetResponse
            {
                Ok = true, ETag = "v2", MesPhase = "FQC_PENDING",
            });
        });

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='run-finish-btn']")));
        cut.Find("[data-testid='run-finish-btn']").Click();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='finish-modal']")));
        cut.Find("[data-testid='finish-confirm-btn']").Click();

        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        cut.WaitForAssertion(() =>
        {
            Assert.Single(api.RunFinishCalls);
            Assert.Equal("v1", api.RunFinishCalls[0].ETag);
        });
    }

    // ── Correction flow ────────────────────────────────────────────
    // Wire-mirror: RunningSurfaceControllerTests.Run_qty_correct_happy_path_appends_new_entry_linked

    [Fact]
    public void Correct_button_disabled_when_no_recent_entries()
    {
        var cut = Render(this, View(etag: "v1",
            entries: Array.Empty<RunningQtyEntryRow>()));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='run-correct-btn']")));
        Assert.True(cut.Find("[data-testid='run-correct-btn']").HasAttribute("disabled"));
    }

    [Fact]
    public void Correct_modal_confirm_posts_with_linked_entry_and_reason()
    {
        var cut = Render(this, View(etag: "v1"), extraApiSetup: api =>
        {
            api.RunQtyCorrectImpl = (_, _, _, _) => Task.FromResult(new RunningSurfaceSetResponse
            {
                Ok = true, ETag = "v2", MesPhase = "RUNNING",
            });
        });

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='run-correct-btn']")));
        cut.Find("[data-testid='run-correct-btn']").Click();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='qty-correct-modal']")));

        cut.Find("[data-testid='qty-correct-entry-select']").Change("2");
        cut.Find("[data-testid='qty-correct-done-input']").Input("-50");
        cut.Find("[data-testid='qty-correct-reason-input']").Input("Đếm nhầm batch 2");

        cut.WaitForAssertion(() =>
            Assert.False(cut.Find("[data-testid='qty-correct-confirm-btn']").HasAttribute("disabled")));
        cut.Find("[data-testid='qty-correct-confirm-btn']").Click();

        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        cut.WaitForAssertion(() =>
        {
            Assert.Single(api.RunQtyCorrectCalls);
            var req = api.RunQtyCorrectCalls[0].Req;
            Assert.Equal(2L, req.LinkedEntryId);
            Assert.Equal(-50, req.QtyDoneDelta);
            Assert.Equal("Đếm nhầm batch 2", req.CorrectionReason);
        });
    }

    // ── 409 conflict ───────────────────────────────────────────────

    [Fact]
    public void Tap_qty_409_state_conflict_renders_set_error_banner()
    {
        var cut = Render(this, View(etag: "v1"), extraApiSetup: api =>
        {
            api.RunQtyAddImpl = (_, _, _, _) => Task.FromResult(new RunningSurfaceSetResponse
            {
                Ok = false,
                ErrorCode = "wo.state_conflict",
                ETag = "v2",
                MesPhase = "RUNNING",
            });
        });

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='tap-done-100']")));
        cut.Find("[data-testid='tap-done-100']").Click();

        cut.WaitForAssertion(() =>
        {
            var banner = cut.Find("[data-testid='running-set-error']");
            Assert.Contains("Một thao tác khác đã cập nhật WO này", banner.TextContent);
        });
    }
}
