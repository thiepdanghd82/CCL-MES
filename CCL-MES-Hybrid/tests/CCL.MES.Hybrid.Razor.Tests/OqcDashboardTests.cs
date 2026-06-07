using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Client.Auth;
using CCL.MES.Hybrid.Razor.Shared;
using CCL.MES.Hybrid.Razor.Tests._Support;
using CCL.MES.Shared.ReasonCodes;
using CCL.MES.Shared.WoQcReview;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// P10.7e-3 — bUnit render tests for <see cref="OqcDashboard"/>.
/// Covers Q5 client guards (Reviewer ≠ Inspector, Approver ≠
/// {Inspector, Reviewer}) + 3-sig stage transitions + L21 OnPhaseChanged.
/// Rule 7.3: wire-mirror in WoQcReviewControllerTests.
/// </summary>
public sealed class OqcDashboardTests : TestContext
{
    private readonly StubAuthSession _session;

    public OqcDashboardTests()
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

    private static WoQcView View(
        string etag = "v1",
        string phase = "OQC_PENDING",
        IEnumerable<(string Key, string Status)>? items = null,
        bool? overrideReady = null,
        bool? overrideAllOk = null,
        bool? overrideAnyNg = null,
        string? inspectedBy = null,
        string? reviewedBy = null,
        string? approvedBy = null)
    {
        items ??= new[]
        {
            ("pack-count", "Ok"),
            ("ship-label", "Ok"),
        };
        var arr = items.ToArray();
        var ready = overrideReady ?? arr.All(i => i.Status != "Pending");
        var allOk = overrideAllOk ?? (ready && arr.All(i => i.Status == "Ok"));
        var anyNg = overrideAnyNg ?? arr.Any(i => i.Status == "Ng");
        return new WoQcView
        {
            WoId = 42,
            WoNo = "WO-26-3684",
            MesPhase = phase,
            ETag = etag,
            QcKind = "OQC",
            Items = arr.Select(x => new WoQcViewItem
            {
                ItemKey = x.Key,
                Status = x.Status,
            }).ToList(),
            IsReadyForJudgment = ready,
            AllOk = allOk,
            AnyNg = anyNg,
            InspectedBy = inspectedBy,
            ReviewedBy = reviewedBy,
            ApprovedBy = approvedBy,
        };
    }

    private static List<ReasonCodeOption> Scraps() => new()
    {
        new() { Code = "SC-COLOR", LabelEn = "Colour off", LabelVi = "Lệch màu", Kind = "Scrap", Sort = 1 },
    };

    // ── Stage transitions ──────────────────────────────────────────

    [Fact]
    public void Inspector_signing_stage_shows_inspector_button()
    {
        _session.SetUser("inspector-1");
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.WoQcViewImpl = (_, _, _) => Task.FromResult(View());

        var cut = RenderComponent<OqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 42L)
            .Add(d => d.ScrapReasons, Scraps()));

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='oqc-stage-inspector']"));
            Assert.NotNull(cut.Find("[data-testid='oqc-btn-inspector-sign']"));
        });
    }

    [Fact]
    public void Reviewer_stage_disables_when_same_as_inspector()
    {
        _session.SetUser("alice");
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        // Inspector is alice — current user is alice → Q5 same-user guard.
        api.WoQcViewImpl = (_, _, _) => Task.FromResult(View(inspectedBy: "alice"));

        var cut = RenderComponent<OqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 42L)
            .Add(d => d.ScrapReasons, Scraps()));

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='oqc-q5-banner-inspector']"));
            Assert.True(cut.Find("[data-testid='oqc-btn-reviewer-sign']").HasAttribute("disabled"));
        });
    }

    [Fact]
    public void Reviewer_stage_enables_when_distinct_user()
    {
        _session.SetUser("bob");
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.WoQcViewImpl = (_, _, _) => Task.FromResult(View(inspectedBy: "alice"));

        var cut = RenderComponent<OqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 42L)
            .Add(d => d.ScrapReasons, Scraps()));

        cut.WaitForAssertion(() =>
        {
            Assert.False(cut.Find("[data-testid='oqc-btn-reviewer-sign']").HasAttribute("disabled"));
            Assert.Throws<Bunit.ElementNotFoundException>(() =>
                cut.Find("[data-testid='oqc-q5-banner-inspector']"));
        });
    }

    [Fact]
    public void Approver_stage_disables_when_same_as_reviewer()
    {
        _session.SetUser("bob");
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.WoQcViewImpl = (_, _, _) =>
            Task.FromResult(View(inspectedBy: "alice", reviewedBy: "bob"));

        var cut = RenderComponent<OqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 42L)
            .Add(d => d.ScrapReasons, Scraps()));

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='oqc-q5-banner-reviewer']"));
            Assert.True(cut.Find("[data-testid='oqc-btn-approve']").HasAttribute("disabled"));
        });
    }

    [Fact]
    public void Approver_stage_disables_when_same_as_inspector()
    {
        _session.SetUser("alice");
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.WoQcViewImpl = (_, _, _) =>
            Task.FromResult(View(inspectedBy: "alice", reviewedBy: "bob"));

        var cut = RenderComponent<OqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 42L)
            .Add(d => d.ScrapReasons, Scraps()));

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='oqc-q5-banner-approver-inspector']"));
            Assert.True(cut.Find("[data-testid='oqc-btn-approve']").HasAttribute("disabled"));
        });
    }

    [Fact]
    public async Task Approve_happy_invokes_OnPhaseChanged()
    {
        _session.SetUser("charlie");
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.WoQcViewImpl = (_, _, _) =>
            Task.FromResult(View(inspectedBy: "alice", reviewedBy: "bob"));
        api.PostOqcApproveImpl = (id, etag, req, _) =>
            Task.FromResult(new WoQcSetResponse { Ok = true, ETag = "v2", MesPhase = "SHIPPED" });

        var phaseChanged = false;
        var cut = RenderComponent<OqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 42L)
            .Add(d => d.ScrapReasons, Scraps())
            .Add(d => d.OnPhaseChanged,
                Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this,
                    () => phaseChanged = true)));

        cut.WaitForAssertion(() =>
            Assert.False(cut.Find("[data-testid='oqc-btn-approve']").HasAttribute("disabled")));
        await cut.Find("[data-testid='oqc-btn-approve']").ClickAsync(new());

        cut.WaitForAssertion(() => Assert.True(phaseChanged));
        Assert.Single(api.PostOqcApproveCalls);
        Assert.Equal("Approve", api.PostOqcApproveCalls[0].Req.Outcome);
    }
}
