using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Client.Auth;
using CCL.MES.Hybrid.Razor.Pages;
using CCL.MES.Hybrid.Razor.Tests._Support;
using CCL.MES.Shared.Home;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// P10.10 — bUnit render tests for the enriched <see cref="Home"/>
/// dashboard (SpecHub parity: greeting + live clock + role-gated
/// module quick-access grid).
///
/// The tiles are role-gated client-side off <see cref="IAuthSession"/>:
///   • NPI tiles (Engineer Spec + NPI Data) → Admin/Supervisor/Engineer/QC
///   • Production tile (Work Orders) → Admin/Supervisor/Operator/QC
///   • Settings tile → every authenticated user
/// These fixtures lock the gate so a future role-matrix drift surfaces
/// in CI rather than on the operator's home screen.
/// </summary>
public sealed class HomeTests : TestContext
{
    private readonly StubAuthSession _session;
    private readonly RecordingApi _api;

    public HomeTests()
    {
        _session = new StubAuthSession();
        _api = new RecordingApi();
        Services.AddSingleton<IAuthSession>(_session);
        Services.AddSingleton<ICclApiClient>(_api);
        // Home carries [Authorize]; bUnit renders the unauthorised body
        // otherwise.
        this.AddTestAuthorization().SetAuthorized("test-user");
    }

    [Fact]
    public void Engineer_sees_npi_tiles_and_settings_but_not_production()
    {
        _session.SetUser("eng-user", "Engineer");

        var cut = RenderComponent<Home>();
        var markup = cut.Markup;

        Assert.Contains("Engineer Spec", markup);
        Assert.Contains("NPI Data", markup);
        Assert.Contains("Cài đặt", markup);
        Assert.DoesNotContain("Sản xuất", markup);
        // Tile hrefs point at the real routes.
        Assert.Contains("href=\"/npi/specs\"", markup);
        Assert.Contains("href=\"/settings\"", markup);
    }

    [Fact]
    public void Operator_sees_production_and_settings_but_not_npi()
    {
        _session.SetUser("op-user", "Operator");

        var cut = RenderComponent<Home>();
        var markup = cut.Markup;

        Assert.Contains("Sản xuất", markup);
        Assert.Contains("href=\"/workorders\"", markup);
        Assert.Contains("Cài đặt", markup);
        Assert.DoesNotContain("Engineer Spec", markup);
        Assert.DoesNotContain("href=\"/npi/specs\"", markup);
    }

    [Fact]
    public void Renders_greeting_with_display_name_and_a_live_clock()
    {
        _session.SetUser("ada", "Admin");

        var cut = RenderComponent<Home>();
        var markup = cut.Markup;

        // Greeting is one of the 3 time-of-day variants + the username.
        Assert.Contains("ada", markup);
        Assert.True(
            markup.Contains("Chào buổi sáng")
            || markup.Contains("Chào buổi chiều")
            || markup.Contains("Chào buổi tối"),
            "Greeting must render a time-of-day variant.");
        // The live clock element is present.
        Assert.NotEmpty(cut.FindAll(".home-clock-time"));
    }

    [Fact]
    public void Renders_kpi_tiles_from_home_summary()
    {
        _session.SetUser("eng-user", "Engineer");
        _api.HomeSummary = new HomeSummaryDto
        {
            SpecsTotal = 42,
            PendingApprovals = 3,
            Drafts = 7,
            TodayActivity = 5,
        };

        var cut = RenderComponent<Home>();
        var nums = cut.FindAll(".home-kpi-num");

        Assert.Equal(4, nums.Count);
        var markup = cut.Markup;
        Assert.Contains("42", markup);   // specs total
        Assert.Contains("Spec trong thư viện", markup);
        Assert.Contains("Chờ duyệt", markup);
        Assert.Contains("Bản nháp", markup);
        Assert.Contains("Hoạt động hôm nay", markup);
        Assert.Equal(1, _api.HomeSummaryCalls);
    }

    [Fact]
    public void Kpi_tiles_show_dash_when_summary_unavailable()
    {
        _session.SetUser("op-user", "Operator");
        _api.HomeSummary = null; // simulates a failed/empty fetch

        var cut = RenderComponent<Home>();

        Assert.Contains("—", cut.Markup);
        Assert.Equal(4, cut.FindAll(".home-kpi-num").Count);
    }

    [Fact]
    public void Admin_sees_all_modules()
    {
        _session.SetUser("admin", "Admin");

        var markup = RenderComponent<Home>().Markup;

        Assert.Contains("Engineer Spec", markup);
        Assert.Contains("NPI Data", markup);
        Assert.Contains("Sản xuất", markup);
        Assert.Contains("Cài đặt", markup);
    }
}
