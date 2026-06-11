using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Client.Auth;
using CCL.MES.Hybrid.Razor.Shared;
using CCL.MES.Hybrid.Razor.Tests._Support;
using CCL.MES.Shared.IpqcReview;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// P10.7d-3 — bUnit render tests for <see cref="QaApprovalDashboard"/>.
///
/// Q3 dual-sig client guard is the focus: the Approve button must
/// disable client-side when the signed-in operator matches the IPQC
/// submitter, surfacing IpqcReviewErrorLocaliser.Q3SameUserBanner.
/// The server-side guard (422 qa.same_user_as_ipqc_submitter +
/// WO_QA_APPROVE_DENIED audit) is the defence-in-depth layer; the
/// client gate is "be helpful + don't bounce the user."
///
/// Rule 4 — plain &lt;input&gt; + &lt;button&gt; only.
/// Rule 7.3 wire-mirror — every wire probe pairs with a TestServer
/// fixture in IpqcReviewControllerTests.
/// </summary>
public sealed class QaApprovalDashboardTests : TestContext
{
    private readonly StubAuthSession _session;

    public QaApprovalDashboardTests()
    {
        var api = new RecordingApi();
        _session = new StubAuthSession();
        Services.AddSingleton<ICclApiClient>(api);
        Services.AddSingleton<IAuthSession>(_session);
        Services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>),
            typeof(NullLogger<>));
        Services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance);
        this.AddTestAuthorization().SetAuthorized("test-user");
    }

    private static IpqcView View(
        string etag = "v5",
        string phase = "QA_PENDING",
        string ipqcSubmittedBy = "qc-alice",
        string? printBStatus = "Ng",
        string? specialAcceptReason = "Lô gấp, chấp nhận ΔE 2.3") => new()
    {
        WoId = 7,
        WoNo = "WO-26-3726",
        MesPhase = phase,
        ETag = etag,
        MaterialStatus = "Ok",
        PrintAStatus = "Ok",
        PrintBStatus = printBStatus ?? "Ok",
        PrintCStatus = "Ok",
        Judgment = "SpecialAccept",
        IpqcSubmittedBy = ipqcSubmittedBy,
        IpqcSubmittedAt = DateTime.UtcNow.AddMinutes(-3),
        SpecialAcceptReason = specialAcceptReason,
        IsReadyForJudgment = true,
        AllOk = false,
        AnyNg = printBStatus == "Ng",
    };

    // ── Initial render ─────────────────────────────────────────────

    [Fact]
    public void Initial_render_shows_QA_dashboard_with_submitter_pill()
    {
        _session.SetUser("qc-bob");
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.IpqcViewImpl = (_, _) => Task.FromResult(View(ipqcSubmittedBy: "qc-alice"));

        var cut = RenderComponent<QaApprovalDashboard>(p => p.Add(d => d.WorkOrderId, 7L));

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='qa-dashboard']"));
            var submitter = cut.Find("[data-testid='qa-submitter']");
            Assert.Contains("qc-alice", submitter.TextContent);
            Assert.NotNull(cut.Find("[data-testid='qa-special-reason']"));
        });
    }

    [Fact]
    public void Invalid_phase_banner_renders_when_phase_is_not_QA_PENDING()
    {
        _session.SetUser("qc-bob");
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.IpqcViewImpl = (_, _) => Task.FromResult(View(phase: "RUNNING"));

        var cut = RenderComponent<QaApprovalDashboard>(p => p.Add(d => d.WorkOrderId, 7L));

        cut.WaitForAssertion(() =>
        {
            var banner = cut.Find("[data-testid='qa-invalid-phase']");
            Assert.Contains("not in the QA_PENDING phase", banner.TextContent);
        });
    }

    // ── Q3 client guard — same-user gate ──────────────────────────

    [Fact]
    public void Q3_same_user_renders_warning_banner_and_disables_Approve()
    {
        _session.SetUser("qc-alice"); // SAME as submitter below
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.IpqcViewImpl = (_, _) => Task.FromResult(View(ipqcSubmittedBy: "qc-alice"));

        var cut = RenderComponent<QaApprovalDashboard>(p => p.Add(d => d.WorkOrderId, 7L));

        cut.WaitForAssertion(() =>
        {
            // Banner present.
            var banner = cut.Find("[data-testid='qa-q3-banner']");
            Assert.Contains("dual-sig", banner.TextContent);
            Assert.Contains("một người khác duyệt", banner.TextContent);
            // Hint near the buttons.
            Assert.NotNull(cut.Find("[data-testid='qa-hint-q3']"));
            // Approve disabled.
            Assert.True(cut.Find("[data-testid='qa-approve-btn']").HasAttribute("disabled"));
        });
    }

    [Fact]
    public void Q3_same_user_case_insensitive_match()
    {
        // Ordinal IgnoreCase per the server-side enforcement.
        _session.SetUser("QC-Alice"); // mixed case vs submitter "qc-alice"
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.IpqcViewImpl = (_, _) => Task.FromResult(View(ipqcSubmittedBy: "qc-alice"));

        var cut = RenderComponent<QaApprovalDashboard>(p => p.Add(d => d.WorkOrderId, 7L));

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='qa-q3-banner']"));
            Assert.True(cut.Find("[data-testid='qa-approve-btn']").HasAttribute("disabled"));
        });
    }

    [Fact]
    public void Q3_distinct_user_no_banner_and_Approve_enabled()
    {
        _session.SetUser("qc-bob"); // DIFFERENT from submitter
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.IpqcViewImpl = (_, _) => Task.FromResult(View(ipqcSubmittedBy: "qc-alice"));

        var cut = RenderComponent<QaApprovalDashboard>(p => p.Add(d => d.WorkOrderId, 7L));

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("[data-testid='qa-q3-banner']"));
            Assert.False(cut.Find("[data-testid='qa-approve-btn']").HasAttribute("disabled"));
        });
    }

    // ── Approve happy path ────────────────────────────────────────
    // Wire-mirror: IpqcReviewControllerTests.PostQaApprove_distinct_user_advances_to_IPQC_APPROVED

    [Fact]
    public void Distinct_user_clicks_Approve_posts_QA_approve_with_If_Match()
    {
        _session.SetUser("qc-bob");
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        var calls = 0;
        api.IpqcViewImpl = (_, _) =>
        {
            calls++;
            return Task.FromResult(View(etag: calls == 1 ? "v5" : "v6", phase: calls == 1 ? "QA_PENDING" : "IPQC_APPROVED"));
        };
        api.PostQaApproveImpl = (_, _, _, _) => Task.FromResult(new IpqcSetResponse
        {
            Ok = true,
            ETag = "v6",
            MesPhase = "IPQC_APPROVED",
        });

        var cut = RenderComponent<QaApprovalDashboard>(p => p.Add(d => d.WorkOrderId, 7L));

        cut.WaitForAssertion(() =>
            Assert.False(cut.Find("[data-testid='qa-approve-btn']").HasAttribute("disabled")));
        cut.Find("[data-testid='qa-approve-btn']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(api.PostQaApproveCalls);
            Assert.Equal("v5", api.PostQaApproveCalls[0].ETag);
            Assert.Equal("Approve", api.PostQaApproveCalls[0].Req.Outcome);
        });
    }

    // ── Reject gating — requires QA reason ────────────────────────

    [Fact]
    public void Reject_button_disabled_until_QA_reason_set()
    {
        _session.SetUser("qc-bob");
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.IpqcViewImpl = (_, _) => Task.FromResult(View(ipqcSubmittedBy: "qc-alice"));

        var cut = RenderComponent<QaApprovalDashboard>(p => p.Add(d => d.WorkOrderId, 7L));

        cut.WaitForAssertion(() =>
            Assert.True(cut.Find("[data-testid='qa-reject-btn']").HasAttribute("disabled")));

        cut.Find("[data-testid='qa-reason-input']").Input("Không chấp nhận sai số ΔE — lô re-print");

        cut.WaitForAssertion(() =>
            Assert.False(cut.Find("[data-testid='qa-reject-btn']").HasAttribute("disabled")));
    }

    // ── Reject same-user STILL allowed (matches OnQaDecisionAsync) ─

    [Fact]
    public void Q3_same_user_Reject_button_still_enabled_when_reason_present()
    {
        // Reject == StopLine semantics: the IPQC submitter cancelling
        // their own SPECIAL_ACCEPT request is operationally fine.
        // Only Approve is dual-sig gated.
        _session.SetUser("qc-alice"); // SAME as submitter
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.IpqcViewImpl = (_, _) => Task.FromResult(View(ipqcSubmittedBy: "qc-alice"));

        var cut = RenderComponent<QaApprovalDashboard>(p => p.Add(d => d.WorkOrderId, 7L));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='qa-reason-input']")));
        cut.Find("[data-testid='qa-reason-input']").Input("Rút lại Special Accept, sản xuất lại");

        cut.WaitForAssertion(() =>
        {
            Assert.True(cut.Find("[data-testid='qa-approve-btn']").HasAttribute("disabled"));
            // Reject NOT gated by Q3 — same operator can reverse their own submit.
            Assert.False(cut.Find("[data-testid='qa-reject-btn']").HasAttribute("disabled"));
        });
    }

    // ── L21 auto-refresh (Henry RCA on PR #119) ────────────────────

    [Fact]
    public void Approve_success_invokes_OnPhaseChanged()
    {
        _session.SetUser("qc-bob"); // distinct from submitter
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.IpqcViewImpl = (_, _) => Task.FromResult(View(ipqcSubmittedBy: "qc-alice"));
        api.PostQaApproveImpl = (_, _, _, _) => Task.FromResult(new IpqcSetResponse
        {
            Ok = true, ETag = "v6", MesPhase = "IPQC_APPROVED",
        });

        var phaseChangedCount = 0;
        var cut = RenderComponent<QaApprovalDashboard>(p => p
            .Add(d => d.WorkOrderId, 7L)
            .Add(d => d.OnPhaseChanged, EventCallback.Factory.Create(this, () => phaseChangedCount++)));

        cut.WaitForAssertion(() =>
            Assert.False(cut.Find("[data-testid='qa-approve-btn']").HasAttribute("disabled")));
        cut.Find("[data-testid='qa-approve-btn']").Click();

        cut.WaitForAssertion(() => Assert.Equal(1, phaseChangedCount));
    }

    [Fact]
    public void Reject_success_invokes_OnPhaseChanged()
    {
        _session.SetUser("qc-bob");
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.IpqcViewImpl = (_, _) => Task.FromResult(View(ipqcSubmittedBy: "qc-alice"));
        api.PostQaApproveImpl = (_, _, _, _) => Task.FromResult(new IpqcSetResponse
        {
            Ok = true, ETag = "v7", MesPhase = "PREPRESS",
        });

        var phaseChangedCount = 0;
        var cut = RenderComponent<QaApprovalDashboard>(p => p
            .Add(d => d.WorkOrderId, 7L)
            .Add(d => d.OnPhaseChanged, EventCallback.Factory.Create(this, () => phaseChangedCount++)));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='qa-reason-input']")));
        cut.Find("[data-testid='qa-reason-input']").Input("Không chấp nhận sai số ΔE");
        cut.WaitForAssertion(() =>
            Assert.False(cut.Find("[data-testid='qa-reject-btn']").HasAttribute("disabled")));
        cut.Find("[data-testid='qa-reject-btn']").Click();

        cut.WaitForAssertion(() => Assert.Equal(1, phaseChangedCount));
    }

    [Fact]
    public void Server_422_same_user_does_NOT_invoke_OnPhaseChanged()
    {
        _session.SetUser("qc-bob");
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.IpqcViewImpl = (_, _) => Task.FromResult(View(ipqcSubmittedBy: "qc-alice"));
        api.PostQaApproveImpl = (_, _, _, _) => Task.FromResult(new IpqcSetResponse
        {
            Ok = false, ErrorCode = "qa.same_user_as_ipqc_submitter",
            ETag = "v5", MesPhase = "QA_PENDING",
        });

        var phaseChangedCount = 0;
        var cut = RenderComponent<QaApprovalDashboard>(p => p
            .Add(d => d.WorkOrderId, 7L)
            .Add(d => d.OnPhaseChanged, EventCallback.Factory.Create(this, () => phaseChangedCount++)));

        cut.WaitForAssertion(() =>
            Assert.False(cut.Find("[data-testid='qa-approve-btn']").HasAttribute("disabled")));
        cut.Find("[data-testid='qa-approve-btn']").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='qa-set-error']")));
        Assert.Equal(0, phaseChangedCount);
    }

    // ── Server-side Q3 422 path (curl bypass) renders banner ──────
    // Wire-mirror: IpqcReviewControllerTests.Same_user_QaApprove_returns_422

    [Fact]
    public void Server_returns_422_same_user_renders_localised_banner()
    {
        _session.SetUser("qc-bob"); // appears distinct on client (Q3 client guard passes)
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.IpqcViewImpl = (_, _) => Task.FromResult(View(ipqcSubmittedBy: "qc-alice"));
        // Server still rejects — eg. cookie/JWT identity drifted from the
        // session snapshot (token rotation race). Client surfaces the
        // VN banner via IpqcReviewErrorLocaliser.
        api.PostQaApproveImpl = (_, _, _, _) => Task.FromResult(new IpqcSetResponse
        {
            Ok = false,
            ErrorCode = "qa.same_user_as_ipqc_submitter",
            ETag = "v5",
            MesPhase = "QA_PENDING",
        });

        var cut = RenderComponent<QaApprovalDashboard>(p => p.Add(d => d.WorkOrderId, 7L));

        cut.WaitForAssertion(() =>
            Assert.False(cut.Find("[data-testid='qa-approve-btn']").HasAttribute("disabled")));
        cut.Find("[data-testid='qa-approve-btn']").Click();

        cut.WaitForAssertion(() =>
        {
            var banner = cut.Find("[data-testid='qa-set-error']");
            Assert.Contains("dual-sig", banner.TextContent);
        });
    }
}
