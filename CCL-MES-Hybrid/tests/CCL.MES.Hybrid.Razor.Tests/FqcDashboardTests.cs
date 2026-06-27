using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Razor.Shared;
using CCL.MES.Hybrid.Razor.Tests._Support;
using CCL.MES.Shared.ReasonCodes;
using CCL.MES.Shared.WoQcReview;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// P10.7e-3 — bUnit render tests for <see cref="FqcDashboard"/>.
///
/// Rule 4 — every input/button/select rendered here is a plain HTML
/// element (no &lt;InputText&gt;/&lt;EditForm&gt;).
/// Rule 7.3 wire-mirror: every wire-path probe has a paired
/// integration test in WoQcReviewControllerTests.
/// </summary>
public sealed class FqcDashboardTests : TestContext
{
    public FqcDashboardTests()
    {
        var api = new RecordingApi();
        Services.AddSingleton<ICclApiClient>(api);
        Services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>),
            typeof(NullLogger<>));
        Services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance);
        this.AddTestAuthorization().SetAuthorized("inspector-1");
    }

    private static WoQcView View(
        string etag = "v1",
        string phase = "FQC_PENDING",
        IEnumerable<(string Key, string Status)>? items = null,
        bool? overrideReady = null,
        bool? overrideAllOk = null,
        bool? overrideAnyNg = null,
        string? inspectedBy = null)
    {
        items ??= new[]
        {
            ("trim-width", "Pending"),
            ("trim-length", "Pending"),
            ("color-de", "Pending"),
        };
        var arr = items.ToArray();
        var ready = overrideReady ?? arr.All(i => i.Status != "Pending");
        var allOk = overrideAllOk ?? (ready && arr.All(i => i.Status == "Ok"));
        var anyNg = overrideAnyNg ?? arr.Any(i => i.Status == "Ng");
        return new WoQcView
        {
            WoId = 42,
            WoNo = "WO-26-3683",
            MesPhase = phase,
            ETag = etag,
            QcKind = "FQC",
            Items = arr.Select(x => new WoQcViewItem
            {
                ItemKey = x.Key,
                Status = x.Status,
                NgReasonCode = x.Status == "Ng" ? "SC-COLOR" : null,
                NgNote = x.Status == "Ng" ? "drift" : null,
            }).ToList(),
            IsReadyForJudgment = ready,
            AllOk = allOk,
            AnyNg = anyNg,
            InspectedBy = inspectedBy,
        };
    }

    private static List<ReasonCodeOption> Scraps() => new()
    {
        new() { Code = "SC-COLOR", LabelEn = "Colour off", LabelVi = "Lệch màu", Kind = "Scrap", Sort = 1 },
    };

    // ── Initial render ─────────────────────────────────────────────

    [Fact]
    public void Initial_render_shows_data_driven_items()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.WoQcViewImpl = (_, _, _) => Task.FromResult(View());

        var cut = RenderComponent<FqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 42L)
            .Add(d => d.ScrapReasons, Scraps()));

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='fqc-dashboard']"));
            Assert.NotNull(cut.Find("[data-testid='fqc-item-trim-width']"));
            Assert.NotNull(cut.Find("[data-testid='fqc-item-trim-length']"));
            Assert.NotNull(cut.Find("[data-testid='fqc-item-color-de']"));
            Assert.NotNull(cut.Find("[data-testid='fqc-judgment']"));
        });
        Assert.Single(api.WoQcViewCalls);
        Assert.Equal((42L, "fqc"), api.WoQcViewCalls[0]);
    }

    [Fact]
    public void Invalid_phase_renders_dead_end_banner()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.WoQcViewImpl = (_, _, _) => Task.FromResult(View(phase: "RUNNING"));

        var cut = RenderComponent<FqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 42L)
            .Add(d => d.ScrapReasons, Scraps()));

        cut.WaitForAssertion(() =>
            Assert.NotNull(cut.Find("[data-testid='fqc-invalid-phase']")));
    }

    // ── Pass judgment happy path (L21 OnPhaseChanged) ─────────────

    [Fact]
    public async Task Pass_judgment_invokes_OnPhaseChanged()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        var items = new[]
        {
            ("trim-width", "Ok"),
            ("trim-length", "Ok"),
            ("color-de", "Ok"),
        };
        api.WoQcViewImpl = (_, _, _) => Task.FromResult(View(items: items));
        api.PostFqcJudgmentImpl = (id, etag, _, _) =>
            Task.FromResult(new WoQcSetResponse { Ok = true, ETag = "v2", MesPhase = "OQC_PENDING" });

        var phaseChanged = false;
        var cut = RenderComponent<FqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 42L)
            .Add(d => d.ScrapReasons, Scraps())
            .Add(d => d.OnPhaseChanged,
                Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this,
                    () => phaseChanged = true)));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='fqc-judgment-pass']")));
        await cut.Find("[data-testid='fqc-judgment-pass']").ClickAsync(new());

        cut.WaitForAssertion(() => Assert.True(phaseChanged));
        Assert.Single(api.PostFqcJudgmentCalls);
        Assert.Equal("Pass", api.PostFqcJudgmentCalls[0].Req.Judgment);
    }

    // ── Reject judgment requires reason ───────────────────────────

    [Fact]
    public void Reject_button_disabled_until_reason_typed()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        var items = new[]
        {
            ("trim-width", "Ok"),
            ("trim-length", "Ng"),
            ("color-de", "Ok"),
        };
        api.WoQcViewImpl = (_, _, _) => Task.FromResult(View(items: items));

        var cut = RenderComponent<FqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 42L)
            .Add(d => d.ScrapReasons, Scraps()));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='fqc-judgment-reject']")));
        var rejectBtn = cut.Find("[data-testid='fqc-judgment-reject']");
        Assert.True(rejectBtn.HasAttribute("disabled"));

        // Type reason — button enables.
        var reasonInput = cut.Find("[data-testid='fqc-reject-reason']");
        reasonInput.Input("Color drift past tolerance");
        cut.WaitForAssertion(() =>
            Assert.False(cut.Find("[data-testid='fqc-judgment-reject']").HasAttribute("disabled")));
    }
}
