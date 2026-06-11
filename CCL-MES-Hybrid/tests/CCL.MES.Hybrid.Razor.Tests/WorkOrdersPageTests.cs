using System.Net;
using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Client.Auth;
using CCL.MES.Hybrid.Client.Hardware;
using CCL.MES.Hybrid.Client.RecentScans;
using CCL.MES.Hybrid.Client.WorkOrders;
using CCL.MES.Hybrid.Razor.Pages;
using CCL.MES.Hybrid.Razor.Tests._Support;
using CCL.MES.Shared.Devices;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.Hardware;
using CCL.MES.Shared.RecentScans;
using CCL.MES.Shared.WorkOrders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// P10.7a-1.4 — bUnit render tests for <see cref="WorkOrders"/>.
/// Today's Catalyst checkpoint surfaced a wiring gap: the
/// AdvanceOrchestrator unit tests passed but the operator had no
/// visual feedback after a successful advance because the Razor
/// page didn't bind the success banner to the orchestrator outcome.
/// These tests render the REAL Razor page against a stubbed
/// <see cref="ICclApiClient"/> and assert on the actual DOM so the
/// next Razor↔orchestrator drift surfaces immediately in CI rather
/// than at the operator's tap.
/// </summary>
public sealed class WorkOrdersPageTests : TestContext
{
    public WorkOrdersPageTests()
    {
        // Register the page's DI deps. The hardware bits are stubbed
        // hard (no MAUI, no scanner). Defaults set HardwareOptions
        // ScanEnabled=true so the scan + manual entry UI render.
        var api = new RecordingApi();
        Services.AddSingleton<ICclApiClient>(api);
        Services.AddSingleton<IBarcodeScannerService, StubScannerService>();
        Services.AddSingleton<IDeviceSettingsLauncher, StubDeviceSettingsLauncher>();
        Services.AddSingleton<IRecentScansService, InMemoryRecentScansService>();
        // P10.7d-3 — QaApprovalDashboard injects IAuthSession for the Q3
        // dual-sig client guard. WorkOrders page hosts it on QA_PENDING so
        // the parent test context must register a session double too.
        Services.AddSingleton<IAuthSession>(new StubAuthSession());
        Services.AddSingleton<IOptions<HardwareOptions>>(
            Options.Create(new HardwareOptions { ScanEnabled = true }));
        Services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>),
            typeof(NullLogger<>));
        Services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance);
        // Bypass [Authorize] — bUnit's authorisation defaults render
        // the unauthorised body otherwise.
        this.AddTestAuthorization().SetAuthorized("test-user");
    }

    // ── Initial render — scan button + manual entry input both present ─

    [Fact]
    public void Initial_render_shows_scan_button_and_manual_entry_input()
    {
        var cut = RenderComponent<WorkOrders>();
        Assert.NotNull(cut.Find("button.wo-cta-primary"));
        Assert.NotNull(cut.Find("input.wo-manual-input"));
    }

    [Fact]
    public void Manual_entry_Find_button_disabled_until_three_chars()
    {
        var cut = RenderComponent<WorkOrders>();
        var findButton = cut.Find("div.wo-manual-row button");
        Assert.True(findButton.HasAttribute("disabled"),
            "Empty manual code → Find disabled.");

        // Type 2 chars — still disabled.
        cut.Find("input.wo-manual-input").Input("WO");
        findButton = cut.Find("div.wo-manual-row button");
        Assert.True(findButton.HasAttribute("disabled"),
            "2 chars → Find still disabled.");

        // Type 3 chars — enabled.
        cut.Find("input.wo-manual-input").Input("WO-");
        findButton = cut.Find("div.wo-manual-row button");
        Assert.False(findButton.HasAttribute("disabled"),
            "3 chars → Find enabled.");
    }

    [Fact]
    public void Manual_entry_lookup_renders_WO_card_on_success()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.SummaryImpl = (woNo, ct) => Task.FromResult<WorkOrderSummary?>(SampleSummary(woNo));

        var cut = RenderComponent<WorkOrders>();
        cut.Find("input.wo-manual-input").Input("WO-26-3684");
        cut.Find("div.wo-manual-row button").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("div.wo-card"));
            Assert.Contains("WO-26-3684", cut.Find("div.wo-card-wo").TextContent);
        });

        Assert.Single(api.SummaryCalls);
        Assert.Equal("WO-26-3684", api.SummaryCalls[0]);
        Assert.Single(api.ScanLogCalls); // manual entry still hits the audit endpoint
        Assert.Equal("MANUAL", api.ScanLogCalls[0].Format);
    }

    [Fact]
    public void Manual_entry_lookup_renders_not_found_VN_message_on_null()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.SummaryImpl = (_, _) => Task.FromResult<WorkOrderSummary?>(null);

        var cut = RenderComponent<WorkOrders>();
        cut.Find("input.wo-manual-input").Input("WO-26-9999");
        cut.Find("div.wo-manual-row button").Click();

        cut.WaitForAssertion(() =>
        {
            var err = cut.Find("div.scan-error");
            Assert.Contains("not found", err.TextContent);
            Assert.Contains("WO-26-9999", err.TextContent);
        });
    }

    // ── UX regression — success banner with from→to + auto-hide ──────

    [Fact]
    public void Advance_success_renders_green_banner_with_from_to_transition()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.SummaryImpl = (woNo, ct) => Task.FromResult<WorkOrderSummary?>(
            // P10.7c-3 — ReadyToRun + Running + IpqcApproval now dispatch
            // to RunningDashboard which OWNS the in-phase action buttons,
            // so the legacy Advance CTA is hidden on those steps. Test
            // uses "Fqc" — outside both the prepress + running surfaces —
            // so the legacy Advance flow still surfaces for wire coverage.
            SampleSummary(woNo) with { CurrentStep = "Fqc", ETag = "RV0" });
        api.AdvanceImpl = (id, ifMatch, ct) =>
        {
            api.SummaryImpl = (woNo, ct2) => Task.FromResult<WorkOrderSummary?>(
                SampleSummary(woNo) with { CurrentStep = "Oqc", ETag = "RV1" });
            return Task.FromResult(new AdvanceWorkOrderResponse
            {
                Ok = true, CurrentStep = "Oqc", ETag = "RV1"
            });
        };

        var cut = RenderComponent<WorkOrders>();
        cut.Find("input.wo-manual-input").Input("WO-26-3684");
        cut.Find("div.wo-manual-row button").Click();
        cut.WaitForElement("button.wo-cta-accept");
        cut.Find("button.wo-cta-accept").Click();

        // Closes the Catalyst feedback gap: the operator MUST see the
        // green banner with the FROM step + TO step the moment the
        // advance returns 200.
        cut.WaitForAssertion(() =>
        {
            var banner = cut.Find("[data-testid='advance-success-banner']");
            Assert.Contains("Step advanced:", banner.TextContent);
            Assert.Contains("Fqc", banner.TextContent);
            Assert.Contains("Oqc", banner.TextContent);
        });
    }

    [Fact]
    public void Advance_state_conflict_renders_yellow_warning_banner_not_success()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.SummaryImpl = (woNo, ct) => Task.FromResult<WorkOrderSummary?>(
            SampleSummary(woNo) with { CurrentStep = "Fqc", ETag = "STALE" });
        api.AdvanceImpl = (id, ifMatch, ct) => Task.FromResult(new AdvanceWorkOrderResponse
        {
            Ok = false,
            CurrentStep = "Fqc",
            ErrorCode = "wo.state_conflict",
            ETag = "FRESH",
        });

        var cut = RenderComponent<WorkOrders>();
        cut.Find("input.wo-manual-input").Input("WO-26-3684");
        cut.Find("div.wo-manual-row button").Click();
        cut.WaitForElement("button.wo-cta-accept");
        cut.Find("button.wo-cta-accept").Click();

        cut.WaitForAssertion(() =>
        {
            // The success banner MUST NOT render on 409 (operator
            // would otherwise think the advance succeeded).
            Assert.Empty(cut.FindAll("[data-testid='advance-success-banner']"));
            // The error banner MUST render the state-conflict text.
            var err = cut.Find("div.wo-card-error");
            Assert.Contains("Another operation", err.TextContent);
            Assert.Contains("Accept / Start", err.TextContent);
        });
    }

    [Fact]
    public void Advance_button_disabled_while_in_flight()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        var advanceGate = new TaskCompletionSource<AdvanceWorkOrderResponse>();
        api.SummaryImpl = (woNo, ct) => Task.FromResult<WorkOrderSummary?>(
            // P10.7c-3 — ReadyToRun + Running + IpqcApproval now dispatch
            // to RunningDashboard which OWNS the in-phase action buttons,
            // so the legacy Advance CTA is hidden on those steps. Test
            // uses "Fqc" — outside both the prepress + running surfaces —
            // so the legacy Advance flow still surfaces for wire coverage.
            SampleSummary(woNo) with { CurrentStep = "Fqc", ETag = "RV0" });
        api.AdvanceImpl = (id, ifMatch, ct) => advanceGate.Task;

        var cut = RenderComponent<WorkOrders>();
        cut.Find("input.wo-manual-input").Input("WO-26-3684");
        cut.Find("div.wo-manual-row button").Click();
        cut.WaitForElement("button.wo-cta-accept");
        cut.Find("button.wo-cta-accept").Click();

        // Mid-flight — the button MUST be disabled so a fast second
        // tap can't reach the orchestrator at all (defence in depth
        // on top of the orchestrator's Interlocked guard).
        cut.WaitForAssertion(() =>
        {
            var btn = cut.Find("button.wo-cta-accept");
            Assert.True(btn.HasAttribute("disabled"));
            Assert.Contains("Advancing step", btn.TextContent);
        });

        // Let the advance finish so the test fixture tears down cleanly.
        advanceGate.SetResult(new AdvanceWorkOrderResponse
        {
            Ok = true, CurrentStep = "Oqc", ETag = "RV1"
        });
        cut.WaitForElement("[data-testid='advance-success-banner']");
    }

    // ── P10.7c-3 BUG-FIX (Henry RCA on WO-26-3685) ────────────────────
    // Post-/setting/done divergence: WO carries MesPhase="IPQC_WAIT" but
    // legacy CurrentStep stays "OpSetting". Dispatch MUST key on canonical
    // MesPhase, not on the legacy projection. Otherwise SettingDashboard
    // dead-ends ("WO không ở SETTING — IPQC_WAIT") AND the legacy Advance
    // CTA + stale advance error chrome render alongside.

    [Fact]
    public void Divergence_MesPhase_IPQC_WAIT_routes_to_IpqcDashboard_not_SettingDashboard()
    {
        // P10.7d-3 — IPQC_WAIT now owns a real IpqcDashboard. Previously
        // (7c-3) it routed to RunningDashboard's placeholder card; 7d-3
        // narrowed IsRunningSurfacePhaseValue + added IsIpqcWaitPhase so
        // IPQC_WAIT lands on the dedicated dashboard. The L19 cause (legacy
        // CurrentStep="OpSetting" vs canonical MesPhase="IPQC_WAIT") still
        // applies — dispatch MUST key on MesPhase.
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.SummaryImpl = (woNo, ct) => Task.FromResult<WorkOrderSummary?>(
            SampleSummary(woNo) with { CurrentStep = "OpSetting", MesPhase = "IPQC_WAIT" });
        api.IpqcViewImpl = (_, _) => Task.FromResult(
            new CCL.MES.Shared.IpqcReview.IpqcView
            {
                WoId = 42, WoNo = "WO-26-3685", MesPhase = "IPQC_WAIT", ETag = "IV",
            });

        var cut = RenderComponent<WorkOrders>();
        cut.Find("input.wo-manual-input").Input("WO-26-3685");
        cut.Find("div.wo-manual-row button").Click();

        cut.WaitForAssertion(() =>
        {
            // IpqcDashboard MUST render — the real 7d-3 surface.
            Assert.NotNull(cut.Find("[data-testid='ipqc-dashboard']"));
            // RunningDashboard MUST NOT render — IPQC_WAIT is now off its
            // surface entirely (no placeholder).
            Assert.Empty(cut.FindAll("[data-testid='running-dashboard']"));
            // SettingDashboard MUST NOT render — that was the L19 dead-end bug.
            Assert.Empty(cut.FindAll("[data-testid='setting-dashboard']"));
        });
    }

    // ── L21 auto-refresh after phase change (Henry RCA on PR #119) ──
    // Each transition-emitting dashboard exposes OnPhaseChanged;
    // the parent re-fetches the summary so the dispatch re-evaluates
    // BEFORE the operator has to tap "Tìm" again. Without these the
    // operator stares at a stale dead-end card (the dashboard's own
    // _invalidPhase branch).

    [Fact]
    public void Auto_refresh_after_IPQC_judgment_routes_to_QaApprovalDashboard_without_manual_lookup()
    {
        // Initial summary: IPQC_WAIT → IpqcDashboard mounts.
        // After judgment: summary lookup returns QA_PENDING → dispatch
        // remounts to QaApprovalDashboard. Operator never tapped "Tìm".
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();

        var summaryCallCount = 0;
        api.SummaryImpl = (woNo, ct) =>
        {
            summaryCallCount++;
            // Call 1 = initial lookup (from manual entry). IPQC_WAIT.
            // Call 2 = HandleDashboardPhaseChangedAsync re-fetch after
            //          judgment. QA_PENDING (the phase advanced server-side).
            var phase = summaryCallCount == 1 ? "IPQC_WAIT" : "QA_PENDING";
            return Task.FromResult<WorkOrderSummary?>(
                SampleSummary(woNo) with { CurrentStep = "OpSetting", MesPhase = phase });
        };

        // IpqcDashboard's GET — initial render.
        var ipqcCallCount = 0;
        api.IpqcViewImpl = (_, _) =>
        {
            ipqcCallCount++;
            // Render 1 = IPQC_WAIT with all slots ready for judgment.
            // Renders after judgment = QA_PENDING (also returned for
            // QaApprovalDashboard's view fetch when it mounts).
            var phase = ipqcCallCount == 1 ? "IPQC_WAIT" : "QA_PENDING";
            return Task.FromResult(new CCL.MES.Shared.IpqcReview.IpqcView
            {
                WoId = 42, WoNo = "WO-26-3725", MesPhase = phase, ETag = "v1",
                MaterialStatus = "Ok", PrintAStatus = "Ok",
                PrintBStatus = "Ng", PrintCStatus = "Ok",
                IsReadyForJudgment = true, AllOk = false, AnyNg = true,
                IpqcSubmittedBy = phase == "QA_PENDING" ? "qc-alice" : null,
                IpqcSubmittedAt = phase == "QA_PENDING" ? DateTime.UtcNow : (DateTime?)null,
                SpecialAcceptReason = phase == "QA_PENDING" ? "Lô gấp" : null,
            });
        };
        api.PostIpqcJudgmentImpl = (_, _, _, _) => Task.FromResult(
            new CCL.MES.Shared.IpqcReview.IpqcSetResponse
            {
                Ok = true, ETag = "v2", MesPhase = "QA_PENDING",
            });

        var cut = RenderComponent<WorkOrders>();
        cut.Find("input.wo-manual-input").Input("WO-26-3725");
        cut.Find("div.wo-manual-row button").Click();

        // Step 1 — IpqcDashboard mounts (summary lookup #1 = IPQC_WAIT).
        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='ipqc-dashboard']"));
            Assert.Empty(cut.FindAll("[data-testid='qa-dashboard']"));
        });

        // Step 2 — type SpecialAccept reason + submit judgment.
        cut.Find("[data-testid='ipqc-special-accept-reason']").Input("Lô gấp, chấp nhận ΔE 2.3");
        cut.WaitForAssertion(() =>
            Assert.False(cut.Find("[data-testid='ipqc-judgment-specialaccept']").HasAttribute("disabled")));
        cut.Find("[data-testid='ipqc-judgment-specialaccept']").Click();

        // Step 3 — parent re-fetched the summary (call #2 = QA_PENDING),
        // dispatch re-evaluated, QaApprovalDashboard mounted. No
        // "Tìm" button needed. Stale IpqcDashboard unmounted.
        cut.WaitForAssertion(() =>
        {
            Assert.True(summaryCallCount >= 2,
                $"Parent MUST re-fetch summary after judgment. Got {summaryCallCount} lookups.");
            Assert.NotNull(cut.Find("[data-testid='qa-dashboard']"));
            Assert.Empty(cut.FindAll("[data-testid='ipqc-dashboard']"));
        });
    }

    [Fact]
    public void Auto_refresh_after_setting_done_routes_to_IpqcDashboard()
    {
        // SETTING → IPQC_WAIT transition via /setting/done.
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        var summaryCallCount = 0;
        api.SummaryImpl = (woNo, ct) =>
        {
            summaryCallCount++;
            var phase = summaryCallCount == 1 ? "SETTING" : "IPQC_WAIT";
            return Task.FromResult<WorkOrderSummary?>(
                SampleSummary(woNo) with { CurrentStep = "OpSetting", MesPhase = phase });
        };
        // SettingDashboard view (RunningSurfaceView) + IpqcDashboard view.
        api.RunningSurfaceViewImpl = (_, _) => Task.FromResult(
            new CCL.MES.Shared.RunningSurface.RunningSurfaceView
            {
                WoId = 42, WoNo = "WO-26-3801", MesPhase = "SETTING",
                ETag = "rv1", TargetQty = 1000,
                SettingStartAt = DateTime.UtcNow.AddMinutes(-3),
            });
        api.SettingDoneImpl = (_, _, _) => Task.FromResult(
            new CCL.MES.Shared.RunningSurface.RunningSurfaceSetResponse
            {
                Ok = true, ETag = "rv2", MesPhase = "IPQC_WAIT",
            });
        api.IpqcViewImpl = (_, _) => Task.FromResult(new CCL.MES.Shared.IpqcReview.IpqcView
        {
            WoId = 42, WoNo = "WO-26-3801", MesPhase = "IPQC_WAIT", ETag = "iv1",
        });

        var cut = RenderComponent<WorkOrders>();
        cut.Find("input.wo-manual-input").Input("WO-26-3801");
        cut.Find("div.wo-manual-row button").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='setting-dashboard']")));

        // Tick all 6 checklist items + click Hoàn tất Setting.
        for (var i = 0; i < 6; i++)
            cut.Find($"[data-testid='setting-check-{i}']").Change(true);
        cut.WaitForAssertion(() =>
            Assert.False(cut.Find("[data-testid='setting-done-btn']").HasAttribute("disabled")));
        cut.Find("[data-testid='setting-done-btn']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.True(summaryCallCount >= 2,
                $"Parent MUST re-fetch after /setting/done. Got {summaryCallCount}.");
            Assert.NotNull(cut.Find("[data-testid='ipqc-dashboard']"));
            Assert.Empty(cut.FindAll("[data-testid='setting-dashboard']"));
        });
    }

    [Fact]
    public void MesPhase_QA_PENDING_routes_to_QaApprovalDashboard()
    {
        // P10.7d-3 — QA_PENDING now owns a real QaApprovalDashboard.
        // Previously (7c-3) it routed to RunningDashboard's placeholder.
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.SummaryImpl = (woNo, ct) => Task.FromResult<WorkOrderSummary?>(
            SampleSummary(woNo) with { CurrentStep = "OpSetting", MesPhase = "QA_PENDING" });
        api.IpqcViewImpl = (_, _) => Task.FromResult(
            new CCL.MES.Shared.IpqcReview.IpqcView
            {
                WoId = 42, WoNo = "WO-26-3725", MesPhase = "QA_PENDING", ETag = "QV",
                IpqcSubmittedBy = "qc-alice",
                IpqcSubmittedAt = DateTime.UtcNow.AddMinutes(-3),
                SpecialAcceptReason = "Lô gấp",
                Judgment = "SpecialAccept",
                IsReadyForJudgment = true,
                AnyNg = true,
            });

        var cut = RenderComponent<WorkOrders>();
        cut.Find("input.wo-manual-input").Input("WO-26-3725");
        cut.Find("div.wo-manual-row button").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='qa-dashboard']"));
            Assert.Empty(cut.FindAll("[data-testid='ipqc-dashboard']"));
            Assert.Empty(cut.FindAll("[data-testid='running-dashboard']"));
        });
    }

    [Fact]
    public void Dashboard_owned_phase_hides_legacy_Advance_CTA_and_stale_error_banner()
    {
        // P10.7d-3 — IPQC_WAIT now routes to IpqcDashboard (real surface),
        // not the RunningDashboard placeholder. IsDashboardOwnedPhase
        // returns true via IsIpqcWaitPhase → legacy chrome stays hidden.
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.SummaryImpl = (woNo, ct) => Task.FromResult<WorkOrderSummary?>(
            SampleSummary(woNo) with { CurrentStep = "OpSetting", MesPhase = "IPQC_WAIT" });
        api.IpqcViewImpl = (_, _) => Task.FromResult(
            new CCL.MES.Shared.IpqcReview.IpqcView
            {
                WoId = 42, WoNo = "WO-26-3685", MesPhase = "IPQC_WAIT", ETag = "IV",
            });

        var cut = RenderComponent<WorkOrders>();
        cut.Find("input.wo-manual-input").Input("WO-26-3685");
        cut.Find("div.wo-manual-row button").Click();

        cut.WaitForAssertion(() =>
        {
            // Legacy Advance CTA MUST NOT render — dashboard owns the workflow.
            Assert.Empty(cut.FindAll("button.wo-cta-accept"));
            // Stale advance error banner (e.g. RequiresSetupConfirmed from
            // a prior failed advance) MUST NOT render either.
            Assert.Empty(cut.FindAll("div.wo-card-error"));
            // IpqcDashboard MUST be the owning surface.
            Assert.NotNull(cut.Find("[data-testid='ipqc-dashboard']"));
        });
    }

    [Fact]
    public void MesPhase_SETTING_routes_to_SettingDashboard_regardless_of_CurrentStep()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.SummaryImpl = (woNo, ct) => Task.FromResult<WorkOrderSummary?>(
            SampleSummary(woNo) with { CurrentStep = "OpSetting", MesPhase = "SETTING" });
        api.RunningSurfaceViewImpl = (_, _) => Task.FromResult(
            new CCL.MES.Shared.RunningSurface.RunningSurfaceView
            {
                WoId = 42, WoNo = "WO-26-3685", MesPhase = "SETTING",
                ETag = "RV", TargetQty = 12000,
                SettingStartAt = DateTime.UtcNow.AddMinutes(-1),
            });

        var cut = RenderComponent<WorkOrders>();
        cut.Find("input.wo-manual-input").Input("WO-26-3685");
        cut.Find("div.wo-manual-row button").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='setting-dashboard']"));
            Assert.Empty(cut.FindAll("[data-testid='running-dashboard']"));
            Assert.Empty(cut.FindAll("button.wo-cta-accept"));
        });
    }

    // ── L19-finalization (Henry RCA on WO-26-3686) ────────────────────
    // Card chip + "Trạng thái MES" row + placeholder card must ALL key on
    // canonical MesPhase. Previously the chip + step row rendered the
    // legacy CurrentStep (lying about the actual state); FQC_PENDING fell
    // through to RunningDashboard's "không ở giai đoạn chạy" dead-end.

    [Fact]
    public void Card_chip_renders_canonical_MesPhase_not_legacy_CurrentStep()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        // The exact WO-26-3686 divergence post-/run/finish.
        api.SummaryImpl = (woNo, ct) => Task.FromResult<WorkOrderSummary?>(
            SampleSummary(woNo) with { CurrentStep = "OpSetting", MesPhase = "FQC_PENDING" });
        api.RunningSurfaceViewImpl = (_, _) => Task.FromResult(
            new CCL.MES.Shared.RunningSurface.RunningSurfaceView
            {
                WoId = 42, WoNo = "WO-26-3686", MesPhase = "FQC_PENDING",
                ETag = "RV", TargetQty = 12000, QtyDoneCached = 12000,
            });

        var cut = RenderComponent<WorkOrders>();
        cut.Find("input.wo-manual-input").Input("WO-26-3686");
        cut.Find("div.wo-manual-row button").Click();

        cut.WaitForAssertion(() =>
        {
            var chip = cut.Find("[data-testid='wo-card-phase-chip']");
            Assert.Equal("FQC_PENDING", chip.TextContent.Trim());
            // Chip CSS class derived from canonical MesPhase, not server's
            // legacy BadgeCssClass (which was keyed on CurrentStep).
            Assert.Contains("wo-phase-fqc-pending", chip.GetAttribute("class") ?? "");

            var phaseRow = cut.Find("[data-testid='wo-card-phase-row']");
            Assert.Equal("FQC_PENDING", phaseRow.TextContent.Trim());
        });
    }

    [Fact]
    public void FQC_PENDING_routes_to_FqcDashboard_not_placeholder()
    {
        // P10.7e-3 — FQC_PENDING now has a real dashboard. The legacy
        // placeholder card was removed from RunningDashboard.DeferredPhaseInfo;
        // dispatch sits FqcDashboard BEFORE the running surface branch.
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.SummaryImpl = (woNo, ct) => Task.FromResult<WorkOrderSummary?>(
            SampleSummary(woNo) with { CurrentStep = "OpSetting", MesPhase = "FQC_PENDING" });
        api.WoQcViewImpl = (_, _, _) => Task.FromResult(
            new CCL.MES.Shared.WoQcReview.WoQcView
            {
                WoId = 42, WoNo = "WO-26-3686", MesPhase = "FQC_PENDING",
                ETag = "v1", QcKind = "FQC",
                Items = new List<CCL.MES.Shared.WoQcReview.WoQcViewItem>(),
            });

        var cut = RenderComponent<WorkOrders>();
        cut.Find("input.wo-manual-input").Input("WO-26-3686");
        cut.Find("div.wo-manual-row button").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='fqc-dashboard']"));
            Assert.Empty(cut.FindAll("[data-testid='running-deferred']"));
            Assert.Empty(cut.FindAll("button.wo-cta-accept"));
        });
    }

    [Theory]
    // P10.7e-3 — FQC_PENDING + OQC_PENDING + SHIPPED were REMOVED from
    // DeferredPhaseInfo (real dashboards now). Theory covers only DONE +
    // CANCELLED — the remaining terminal placeholders on RunningDashboard.
    [InlineData("DONE", "WO completed")]
    [InlineData("CANCELLED", "WO cancelled")]
    public void Deferred_phases_each_render_consistent_placeholder_card(string mesPhase, string expectedTitleFragment)
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.SummaryImpl = (woNo, ct) => Task.FromResult<WorkOrderSummary?>(
            SampleSummary(woNo) with { CurrentStep = "OpSetting", MesPhase = mesPhase });
        api.RunningSurfaceViewImpl = (_, _) => Task.FromResult(
            new CCL.MES.Shared.RunningSurface.RunningSurfaceView
            {
                WoId = 42, WoNo = "WO-26-3686", MesPhase = mesPhase,
                ETag = "RV", TargetQty = 12000,
            });

        var cut = RenderComponent<WorkOrders>();
        cut.Find("input.wo-manual-input").Input("WO-26-3686");
        cut.Find("div.wo-manual-row button").Click();

        cut.WaitForAssertion(() =>
        {
            var card = cut.Find("[data-testid='running-deferred']");
            Assert.Equal(mesPhase, card.GetAttribute("data-deferred-phase"));
            Assert.Contains(expectedTitleFragment, card.TextContent);
            // No dead-end error.
            Assert.Empty(cut.FindAll("[data-testid='running-invalid-phase']"));
            // No legacy Advance CTA — dashboard owns the workflow.
            Assert.Empty(cut.FindAll("button.wo-cta-accept"));
        });
    }

    [Fact]
    public void OQC_PENDING_routes_to_OqcDashboard()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.SummaryImpl = (woNo, ct) => Task.FromResult<WorkOrderSummary?>(
            SampleSummary(woNo) with { CurrentStep = "OpSetting", MesPhase = "OQC_PENDING" });
        api.WoQcViewImpl = (_, _, _) => Task.FromResult(
            new CCL.MES.Shared.WoQcReview.WoQcView
            {
                WoId = 42, WoNo = "WO-26-3686", MesPhase = "OQC_PENDING",
                ETag = "v1", QcKind = "OQC",
                Items = new List<CCL.MES.Shared.WoQcReview.WoQcViewItem>(),
            });

        var cut = RenderComponent<WorkOrders>();
        cut.Find("input.wo-manual-input").Input("WO-26-3686");
        cut.Find("div.wo-manual-row button").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='oqc-dashboard']"));
            Assert.Empty(cut.FindAll("[data-testid='running-deferred']"));
        });
    }

    [Fact]
    public void SHIPPED_routes_to_ShippedSummaryDashboard()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.SummaryImpl = (woNo, ct) => Task.FromResult<WorkOrderSummary?>(
            SampleSummary(woNo) with { CurrentStep = "OpSetting", MesPhase = "SHIPPED" });
        api.WoSummaryReportImpl = (_, _) => Task.FromResult(
            new CCL.MES.Shared.WoQcReview.WoSummaryReport
            {
                WoId = 42, WoNo = "WO-26-3686", MesPhase = "SHIPPED",
            });

        var cut = RenderComponent<WorkOrders>();
        cut.Find("input.wo-manual-input").Input("WO-26-3686");
        cut.Find("div.wo-manual-row button").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='shipped-summary-dashboard']"));
            Assert.Empty(cut.FindAll("[data-testid='running-deferred']"));
        });
    }

    [Fact]
    public void Legacy_CurrentStep_fallback_used_when_MesPhase_is_empty()
    {
        // Older summaries (cached / replayed) may not carry MesPhase.
        // Dispatch MUST gracefully fall back to legacy CurrentStep.
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.SummaryImpl = (woNo, ct) => Task.FromResult<WorkOrderSummary?>(
            SampleSummary(woNo) with { CurrentStep = "OpSetting", MesPhase = "" });
        api.RunningSurfaceViewImpl = (_, _) => Task.FromResult(
            new CCL.MES.Shared.RunningSurface.RunningSurfaceView
            {
                WoId = 42, WoNo = "WO-26-3685", MesPhase = "SETTING",
                ETag = "RV", TargetQty = 12000,
                SettingStartAt = DateTime.UtcNow.AddMinutes(-1),
            });

        var cut = RenderComponent<WorkOrders>();
        cut.Find("input.wo-manual-input").Input("WO-26-3685");
        cut.Find("div.wo-manual-row button").Click();

        cut.WaitForAssertion(() =>
        {
            // Falls back to CurrentStep="OpSetting" → SettingDashboard.
            Assert.NotNull(cut.Find("[data-testid='setting-dashboard']"));
        });
    }

    // ── helpers ──────────────────────────────────────────────────────

    private static WorkOrderSummary SampleSummary(string woNo) => new()
    {
        Id = 42,
        WoNo = woNo,
        CustomerName = "Brady Asia",
        ProductCode = "BRD-7656-D",
        ProductName = "PCB ID Label 20x8mm",
        MachineCode = "ACNC3",
        MachineName = "CNC 3-Heads",
        TargetQty = 12000,
        ProducedQty = 0,
        Uom = "pcs",
        CurrentStep = "ReadyToRun",
        BadgeLabelKey = "wo.status.ready_to_run",
        BadgeCssClass = "wo-status-running",
        ETag = "INITIAL",
    };
}
