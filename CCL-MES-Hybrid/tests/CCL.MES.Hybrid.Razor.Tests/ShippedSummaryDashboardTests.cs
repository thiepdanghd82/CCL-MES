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
/// P10.7e-3 Q8 — bUnit render tests for <see cref="ShippedSummaryDashboard"/>.
/// Read-only surface; tests assert structural blocks render against a
/// representative WoSummaryReport payload.
/// </summary>
public sealed class ShippedSummaryDashboardTests : TestContext
{
    public ShippedSummaryDashboardTests()
    {
        var api = new RecordingApi();
        Services.AddSingleton<ICclApiClient>(api);
        Services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>),
            typeof(NullLogger<>));
        Services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance);
    }

    private static WoSummaryReport Report() => new()
    {
        WoId = 7,
        WoNo = "WO-26-3684",
        MesPhase = "SHIPPED",
        ShippedAt = new DateTime(2026, 6, 7, 12, 34, 56, DateTimeKind.Utc),
        Totals = new() { QtyTarget = 1000, QtyDone = 980, QtyNg = 20 },
        Runtime = new() { RunSeconds = 7200, PauseSeconds = 600, SessionCount = 2 },
        Oee = new() { Availability = 0.92, Performance = 0.85, Quality = 0.98, Oee = 0.766 },
        PausePareto = new[]
        {
            new WoSummaryParetoRow { ReasonCode = "PA-CHANGE", Count = 2, TotalSeconds = 400 },
            new WoSummaryParetoRow { ReasonCode = "PA-BREAK",  Count = 1, TotalSeconds = 200 },
        },
        QcSummary = new()
        {
            Ipqc = new() { Judgment = "GoRun", SubmittedBy = "alice" },
            Fqc  = new() { Judgment = "Pass",  SubmittedBy = "alice" },
            Oqc  = new() { Judgment = "Pass",  SubmittedBy = "alice", Reviewer = "bob", Approver = "charlie" },
        },
    };

    [Fact]
    public void Renders_all_blocks_for_shipped_wo()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.WoSummaryReportImpl = (_, _) => Task.FromResult(Report());

        var cut = RenderComponent<ShippedSummaryDashboard>(p => p
            .Add(d => d.WorkOrderId, 7L));

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='shipped-summary-dashboard']"));
            Assert.NotNull(cut.Find("[data-testid='shipped-totals']"));
            Assert.NotNull(cut.Find("[data-testid='shipped-runtime']"));
            Assert.NotNull(cut.Find("[data-testid='shipped-oee']"));
            Assert.NotNull(cut.Find("[data-testid='shipped-pareto']"));
            Assert.NotNull(cut.Find("[data-testid='shipped-qc-ipqc']"));
            Assert.NotNull(cut.Find("[data-testid='shipped-qc-fqc']"));
            Assert.NotNull(cut.Find("[data-testid='shipped-qc-oqc']"));
        });

        Assert.Single(api.WoSummaryReportCalls);
        Assert.Equal(7L, api.WoSummaryReportCalls[0]);
    }

    [Fact]
    public void Phase_chip_shows_SHIPPED()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.WoSummaryReportImpl = (_, _) => Task.FromResult(Report());

        var cut = RenderComponent<ShippedSummaryDashboard>(p => p
            .Add(d => d.WorkOrderId, 7L));

        cut.WaitForAssertion(() =>
        {
            var chip = cut.Find("[data-testid='shipped-phase-chip']");
            Assert.Contains("SHIPPED", chip.TextContent);
        });
    }

    [Fact]
    public void Oee_total_renders_percentage()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.WoSummaryReportImpl = (_, _) => Task.FromResult(Report());

        var cut = RenderComponent<ShippedSummaryDashboard>(p => p
            .Add(d => d.WorkOrderId, 7L));

        cut.WaitForAssertion(() =>
        {
            var oee = cut.Find("[data-testid='shipped-oee-total']");
            Assert.Contains("76.6%", oee.TextContent);
        });
    }
}
