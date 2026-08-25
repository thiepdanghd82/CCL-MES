using System.Net;
using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Razor.Shared;
using CCL.MES.Hybrid.Razor.Tests._Support;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.IpqcReview;
using CCL.MES.Shared.ReasonCodes;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// P10.7d-3 — bUnit render tests for <see cref="IpqcDashboard"/>.
///
/// Rule 4 — every input/button/select rendered here is a plain HTML
/// element (no &lt;InputText&gt; / &lt;EditForm&gt;).
/// Rule 7.3 wire-mirror: every wire-path probe has a paired
/// integration test in IpqcReviewControllerTests hitting the same
/// endpoint via TestServer. Each fixture comment names its mirror.
/// </summary>
public sealed class IpqcDashboardTests : TestContext
{
    public IpqcDashboardTests()
    {
        var api = new RecordingApi();
        Services.AddSingleton<ICclApiClient>(api);
        var session = new StubAuthSession();
        session.SetUser("test-user", "QC");
        Services.AddSingleton<CCL.MES.Hybrid.Client.Auth.IAuthSession>(session);
        Services.AddI18n();
        Services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>),
            typeof(NullLogger<>));
        Services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance);
        this.AddTestAuthorization().SetAuthorized("test-user");
    }

    private static IpqcView View(
        string etag = "v1",
        string phase = "IPQC_WAIT",
        string material = "Pending",
        string printA = "Pending",
        string printB = "Pending",
        string printC = "Pending",
        bool? overrideReady = null,
        bool? overrideAllOk = null,
        bool? overrideAnyNg = null)
    {
        var statuses = new[] { material, printA, printB, printC };
        var ready = overrideReady ?? statuses.All(s => s != "Pending");
        var allOk = overrideAllOk ?? (ready && statuses.All(s => s == "Ok"));
        var anyNg = overrideAnyNg ?? statuses.Any(s => s == "Ng");
        return new IpqcView
        {
            WoId = 7,
            WoNo = "WO-26-3725",
            MesPhase = phase,
            ETag = etag,
            MaterialStatus = material,
            PrintAStatus = printA,
            PrintBStatus = printB,
            PrintCStatus = printC,
            IsReadyForJudgment = ready,
            AllOk = allOk,
            AnyNg = anyNg,
        };
    }

    private static List<ReasonCodeOption> Scraps() => new()
    {
        new() { Code = "SC-COLOR", LabelEn = "Colour off", LabelVi = "Lệch màu", Kind = "Scrap", Sort = 1 },
        new() { Code = "SC-REG",   LabelEn = "Registration", LabelVi = "Chồng màu lệch", Kind = "Scrap", Sort = 2 },
    };

    // ── Initial render ─────────────────────────────────────────────

    [Fact]
    public void Initial_render_shows_loading_then_4_slot_cards()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.IpqcViewImpl = (_, _) => Task.FromResult(View());

        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 7L)
            .Add(d => d.ScrapReasons, Scraps()));

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='ipqc-dashboard']"));
            Assert.NotNull(cut.Find("[data-testid='ipqc-slot-material']"));
            Assert.NotNull(cut.Find("[data-testid='ipqc-slot-print-a']"));
            Assert.NotNull(cut.Find("[data-testid='ipqc-slot-print-b']"));
            Assert.NotNull(cut.Find("[data-testid='ipqc-slot-print-c']"));
            Assert.NotNull(cut.Find("[data-testid='ipqc-judgment']"));
        });
        Assert.Single(api.IpqcViewCalls);
        Assert.Equal(7L, api.IpqcViewCalls[0]);
    }

    [Fact]
    public void Initial_load_404_shows_localised_error_banner()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.IpqcViewImpl = (_, _) => Task.FromException<IpqcView>(
            new ApiException((int)HttpStatusCode.NotFound,
                new ApiError { Code = "wo.not_found", MessageEn = "no wo" }));

        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 7L)
            .Add(d => d.ScrapReasons, Scraps()));

        cut.WaitForAssertion(() =>
        {
            var banner = cut.Find("[data-testid='ipqc-initial-error']");
            Assert.Contains("WO not found", banner.TextContent);
        });
    }

    [Fact]
    public void Invalid_phase_banner_renders_when_phase_is_not_IPQC_WAIT()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.IpqcViewImpl = (_, _) => Task.FromResult(View(phase: "RUNNING"));

        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 7L)
            .Add(d => d.ScrapReasons, Scraps()));

        cut.WaitForAssertion(() =>
        {
            var banner = cut.Find("[data-testid='ipqc-invalid-phase']");
            Assert.Contains("Lệnh SX không ở công đoạn IPQC_WAIT", banner.TextContent);
        });
    }

    // ── Slot OK happy path ─────────────────────────────────────────
    // Wire-mirror: IpqcReviewControllerTests.PutMaterialSlot_returns_200_*

    [Fact]
    public void Slot_OK_click_posts_status_Ok_with_If_Match_and_reloads()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        var calls = 0;
        api.IpqcViewImpl = (_, _) =>
        {
            calls++;
            return Task.FromResult(calls == 1
                ? View(etag: "v1")
                : View(etag: "v2", material: "Ok"));
        };
        api.PutIpqcMaterialImpl = (_, _, _, _) => Task.FromResult(new IpqcSetResponse
        {
            Ok = true,
            ETag = "v2",
            MesPhase = "IPQC_WAIT",
            IsReadyForJudgment = false,
        });

        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 7L)
            .Add(d => d.ScrapReasons, Scraps()));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='ipqc-slot-material-ok']")));
        cut.Find("[data-testid='ipqc-slot-material-ok']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(api.PutIpqcMaterialCalls);
            Assert.Equal("v1", api.PutIpqcMaterialCalls[0].ETag);
            Assert.Equal("Ok", api.PutIpqcMaterialCalls[0].Req.Status);
        });
    }

    // ── Slot NG sub-form gating ───────────────────────────────────

    [Fact]
    public void Slot_NG_button_opens_inline_form_with_reason_select_and_note()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.IpqcViewImpl = (_, _) => Task.FromResult(View());

        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 7L)
            .Add(d => d.ScrapReasons, Scraps()));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='ipqc-slot-print-b-ng']")));
        cut.Find("[data-testid='ipqc-slot-print-b-ng']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='ipqc-slot-print-b-ng-form']"));
            Assert.NotNull(cut.Find("[data-testid='ipqc-slot-print-b-ng-reason']"));
            Assert.NotNull(cut.Find("[data-testid='ipqc-slot-print-b-ng-note']"));
            // Submit disabled until reason + note set.
            var submit = cut.Find("[data-testid='ipqc-slot-print-b-ng-submit']");
            Assert.True(submit.HasAttribute("disabled"));
        });
    }

    [Fact]
    public void Slot_NG_submit_posts_status_Ng_with_reason_and_note()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        var calls = 0;
        api.IpqcViewImpl = (_, _) =>
        {
            calls++;
            return Task.FromResult(calls == 1
                ? View(etag: "v1")
                : View(etag: "v2", printB: "Ng"));
        };
        api.PutIpqcPrintBImpl = (_, _, _, _) => Task.FromResult(new IpqcSetResponse
        {
            Ok = true,
            ETag = "v2",
            MesPhase = "IPQC_WAIT",
        });

        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 7L)
            .Add(d => d.ScrapReasons, Scraps()));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='ipqc-slot-print-b-ng']")));
        cut.Find("[data-testid='ipqc-slot-print-b-ng']").Click();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='ipqc-slot-print-b-ng-form']")));

        cut.Find("[data-testid='ipqc-slot-print-b-ng-reason']").Change("SC-COLOR");
        cut.Find("[data-testid='ipqc-slot-print-b-ng-note']").Input("Lệch màu nhẹ giữa cuộn 3 và 4");

        cut.WaitForAssertion(() =>
        {
            var submit = cut.Find("[data-testid='ipqc-slot-print-b-ng-submit']");
            Assert.False(submit.HasAttribute("disabled"));
        });
        cut.Find("[data-testid='ipqc-slot-print-b-ng-submit']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(api.PutIpqcPrintBCalls);
            Assert.Equal("v1", api.PutIpqcPrintBCalls[0].ETag);
            Assert.Equal("Ng", api.PutIpqcPrintBCalls[0].Req.Status);
            Assert.Equal("SC-COLOR", api.PutIpqcPrintBCalls[0].Req.NgReasonCode);
            Assert.Equal("Lệch màu nhẹ giữa cuộn 3 và 4", api.PutIpqcPrintBCalls[0].Req.NgNote);
        });
    }

    // ── Judgment gating ───────────────────────────────────────────

    [Fact]
    public void Judgment_row_buttons_disabled_when_NotReadyForJudgment()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.IpqcViewImpl = (_, _) => Task.FromResult(View(material: "Ok", printA: "Ok"));

        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 7L)
            .Add(d => d.ScrapReasons, Scraps()));

        cut.WaitForAssertion(() =>
        {
            Assert.True(cut.Find("[data-testid='ipqc-judgment-gorun']").HasAttribute("disabled"));
            Assert.True(cut.Find("[data-testid='ipqc-judgment-stopline']").HasAttribute("disabled"));
            Assert.True(cut.Find("[data-testid='ipqc-judgment-specialaccept']").HasAttribute("disabled"));
        });
    }

    [Fact]
    public void Judgment_GoRun_enabled_only_when_AllOk_and_no_NG()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.IpqcViewImpl = (_, _) => Task.FromResult(View(
            material: "Ok", printA: "Ok", printB: "Ok", printC: "Ok",
            overrideReady: true, overrideAllOk: true, overrideAnyNg: false));

        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 7L)
            .Add(d => d.ScrapReasons, Scraps()));

        cut.WaitForAssertion(() =>
        {
            Assert.False(cut.Find("[data-testid='ipqc-judgment-gorun']").HasAttribute("disabled"));
            // Stop Line + Special Accept disabled when AllOk: only Go Run path valid.
            Assert.True(cut.Find("[data-testid='ipqc-judgment-stopline']").HasAttribute("disabled"));
            Assert.True(cut.Find("[data-testid='ipqc-judgment-specialaccept']").HasAttribute("disabled"));
        });
    }

    [Fact]
    public void Judgment_GoRun_disabled_when_AnyNg_and_StopLine_enabled()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.IpqcViewImpl = (_, _) => Task.FromResult(View(
            material: "Ok", printA: "Ok", printB: "Ng", printC: "Ok",
            overrideReady: true, overrideAllOk: false, overrideAnyNg: true));

        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 7L)
            .Add(d => d.ScrapReasons, Scraps()));

        cut.WaitForAssertion(() =>
        {
            Assert.True(cut.Find("[data-testid='ipqc-judgment-gorun']").HasAttribute("disabled"));
            Assert.False(cut.Find("[data-testid='ipqc-judgment-stopline']").HasAttribute("disabled"));
            // Special Accept still gated by reason field — empty by default → disabled.
            Assert.True(cut.Find("[data-testid='ipqc-judgment-specialaccept']").HasAttribute("disabled"));
        });
    }

    [Fact]
    public void SpecialAccept_button_enables_only_when_reason_set()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.IpqcViewImpl = (_, _) => Task.FromResult(View(
            material: "Ok", printA: "Ok", printB: "Ng", printC: "Ok",
            overrideReady: true, overrideAllOk: false, overrideAnyNg: true));

        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 7L)
            .Add(d => d.ScrapReasons, Scraps()));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='ipqc-special-accept-reason']")));
        cut.Find("[data-testid='ipqc-special-accept-reason']").Input("Lô gấp giao trong ngày, ΔE 2.3 chấp nhận được");

        cut.WaitForAssertion(() =>
            Assert.False(cut.Find("[data-testid='ipqc-judgment-specialaccept']").HasAttribute("disabled")));
    }

    // ── Judgment submit ───────────────────────────────────────────
    // Wire-mirror: IpqcReviewControllerTests.SubmitJudgment_SpecialAccept_advances_to_QA_PENDING

    [Fact]
    public void Submit_judgment_SpecialAccept_posts_with_reason_and_If_Match()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        var calls = 0;
        api.IpqcViewImpl = (_, _) =>
        {
            calls++;
            return Task.FromResult(calls == 1
                ? View(etag: "v3", material: "Ok", printA: "Ok", printB: "Ng", printC: "Ok",
                       overrideReady: true, overrideAllOk: false, overrideAnyNg: true)
                : View(etag: "v4", phase: "QA_PENDING", material: "Ok", printA: "Ok", printB: "Ng", printC: "Ok",
                       overrideReady: true, overrideAllOk: false, overrideAnyNg: true));
        };
        api.PostIpqcJudgmentImpl = (_, _, _, _) => Task.FromResult(new IpqcSetResponse
        {
            Ok = true,
            ETag = "v4",
            MesPhase = "QA_PENDING",
        });

        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 7L)
            .Add(d => d.ScrapReasons, Scraps()));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='ipqc-special-accept-reason']")));
        cut.Find("[data-testid='ipqc-special-accept-reason']").Input("Lô gấp, chấp nhận ΔE 2.3");
        cut.WaitForAssertion(() =>
            Assert.False(cut.Find("[data-testid='ipqc-judgment-specialaccept']").HasAttribute("disabled")));

        cut.Find("[data-testid='ipqc-judgment-specialaccept']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(api.PostIpqcJudgmentCalls);
            Assert.Equal("v3", api.PostIpqcJudgmentCalls[0].ETag);
            Assert.Equal("SpecialAccept", api.PostIpqcJudgmentCalls[0].Req.Judgment);
            Assert.Equal("Lô gấp, chấp nhận ΔE 2.3", api.PostIpqcJudgmentCalls[0].Req.SpecialAcceptReason);
        });
    }

    // ── L21 auto-refresh (Henry RCA on PR #119) ────────────────────
    // Phase-changing actions MUST invoke OnPhaseChanged so the parent
    // re-fetches the summary + dispatch picks the new phase. Slot
    // PUTs DON'T change MesPhase (stays IPQC_WAIT) so they DON'T
    // bubble; only judgment submit does.

    [Fact]
    public void Judgment_submit_success_invokes_OnPhaseChanged()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.IpqcViewImpl = (_, _) => Task.FromResult(View(
            material: "Ok", printA: "Ok", printB: "Ng", printC: "Ok",
            overrideReady: true, overrideAllOk: false, overrideAnyNg: true));
        api.PostIpqcJudgmentImpl = (_, _, _, _) => Task.FromResult(new IpqcSetResponse
        {
            Ok = true, ETag = "v4", MesPhase = "QA_PENDING",
        });

        var phaseChangedCount = 0;
        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 7L)
            .Add(d => d.ScrapReasons, Scraps())
            .Add(d => d.OnPhaseChanged, EventCallback.Factory.Create(this, () => phaseChangedCount++)));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='ipqc-special-accept-reason']")));
        cut.Find("[data-testid='ipqc-special-accept-reason']").Input("Lô gấp");
        cut.WaitForAssertion(() =>
            Assert.False(cut.Find("[data-testid='ipqc-judgment-specialaccept']").HasAttribute("disabled")));
        cut.Find("[data-testid='ipqc-judgment-specialaccept']").Click();

        cut.WaitForAssertion(() => Assert.Equal(1, phaseChangedCount));
    }

    [Fact]
    public void Slot_PUT_success_does_NOT_invoke_OnPhaseChanged()
    {
        // Slots stay in IPQC_WAIT — no parent re-route needed.
        // Bubbling here would just churn a wasted summary GET per tap.
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        var calls = 0;
        api.IpqcViewImpl = (_, _) =>
        {
            calls++;
            return Task.FromResult(calls == 1 ? View(etag: "v1") : View(etag: "v2", material: "Ok"));
        };
        api.PutIpqcMaterialImpl = (_, _, _, _) => Task.FromResult(new IpqcSetResponse
        {
            Ok = true, ETag = "v2", MesPhase = "IPQC_WAIT",
        });

        var phaseChangedCount = 0;
        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 7L)
            .Add(d => d.ScrapReasons, Scraps())
            .Add(d => d.OnPhaseChanged, EventCallback.Factory.Create(this, () => phaseChangedCount++)));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='ipqc-slot-material-ok']")));
        cut.Find("[data-testid='ipqc-slot-material-ok']").Click();

        cut.WaitForAssertion(() =>
            Assert.Single(api.PutIpqcMaterialCalls));
        Assert.Equal(0, phaseChangedCount);
    }

    [Fact]
    public void Judgment_submit_409_conflict_does_NOT_invoke_OnPhaseChanged()
    {
        // 409 means the action did NOT change phase server-side — only
        // a successful Ok=true bubble. State drifts get a banner.
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.IpqcViewImpl = (_, _) => Task.FromResult(View(
            material: "Ok", printA: "Ok", printB: "Ng", printC: "Ok",
            overrideReady: true, overrideAllOk: false, overrideAnyNg: true));
        api.PostIpqcJudgmentImpl = (_, _, _, _) => Task.FromResult(new IpqcSetResponse
        {
            Ok = false, ErrorCode = "wo.state_conflict", ETag = "v9", MesPhase = "IPQC_WAIT",
        });

        var phaseChangedCount = 0;
        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 7L)
            .Add(d => d.ScrapReasons, Scraps())
            .Add(d => d.OnPhaseChanged, EventCallback.Factory.Create(this, () => phaseChangedCount++)));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='ipqc-special-accept-reason']")));
        cut.Find("[data-testid='ipqc-special-accept-reason']").Input("Lô gấp");
        cut.WaitForAssertion(() =>
            Assert.False(cut.Find("[data-testid='ipqc-judgment-specialaccept']").HasAttribute("disabled")));
        cut.Find("[data-testid='ipqc-judgment-specialaccept']").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='ipqc-set-error']")));
        Assert.Equal(0, phaseChangedCount);
    }

    // ── 409 conflict path (optimistic revert via reload) ───────────
    // Wire-mirror: IpqcReviewControllerTests.PutSlot_with_stale_If_Match_returns_409

    [Fact]
    public void Slot_PUT_409_state_conflict_renders_banner_and_reloads_view()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        var calls = 0;
        api.IpqcViewImpl = (_, _) =>
        {
            calls++;
            return Task.FromResult(View(etag: calls == 1 ? "v1" : "v2"));
        };
        api.PutIpqcMaterialImpl = (_, _, _, _) => Task.FromResult(new IpqcSetResponse
        {
            Ok = false,
            ErrorCode = "wo.state_conflict",
            ETag = "v2",
            MesPhase = "IPQC_WAIT",
        });

        var cut = RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 7L)
            .Add(d => d.ScrapReasons, Scraps()));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='ipqc-slot-material-ok']")));
        cut.Find("[data-testid='ipqc-slot-material-ok']").Click();

        cut.WaitForAssertion(() =>
        {
            var banner = cut.Find("[data-testid='ipqc-set-error']");
            Assert.Contains("already updated this WO", banner.TextContent);
            // Reload count: initial + after-conflict refetch = 2.
            Assert.Equal(2, api.IpqcViewCalls.Count);
        });
    }
}
