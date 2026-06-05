using System.Net;
using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client;
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
            Assert.Contains("Không tìm thấy", err.TextContent);
            Assert.Contains("WO-26-9999", err.TextContent);
        });
    }

    // ── UX regression — success banner with from→to + auto-hide ──────

    [Fact]
    public void Advance_success_renders_green_banner_with_from_to_transition()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.SummaryImpl = (woNo, ct) => Task.FromResult<WorkOrderSummary?>(
            SampleSummary(woNo) with { CurrentStep = "ReadyToRun", ETag = "RV0" });
        api.AdvanceImpl = (id, ifMatch, ct) =>
        {
            api.SummaryImpl = (woNo, ct2) => Task.FromResult<WorkOrderSummary?>(
                SampleSummary(woNo) with { CurrentStep = "Running", ETag = "RV1" });
            return Task.FromResult(new AdvanceWorkOrderResponse
            {
                Ok = true, CurrentStep = "Running", ETag = "RV1"
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
            Assert.Contains("Đã chuyển bước:", banner.TextContent);
            Assert.Contains("ReadyToRun", banner.TextContent);
            Assert.Contains("Running", banner.TextContent);
        });
    }

    [Fact]
    public void Advance_state_conflict_renders_yellow_warning_banner_not_success()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        api.SummaryImpl = (woNo, ct) => Task.FromResult<WorkOrderSummary?>(
            SampleSummary(woNo) with { CurrentStep = "ReadyToRun", ETag = "STALE" });
        api.AdvanceImpl = (id, ifMatch, ct) => Task.FromResult(new AdvanceWorkOrderResponse
        {
            Ok = false,
            CurrentStep = "ReadyToRun",
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
            // The error banner MUST render the VN state-conflict text.
            var err = cut.Find("div.wo-card-error");
            Assert.Contains("Một thao tác khác", err.TextContent);
            Assert.Contains("Nhận / Bắt đầu", err.TextContent);
        });
    }

    [Fact]
    public void Advance_button_disabled_while_in_flight()
    {
        var api = (RecordingApi)Services.GetRequiredService<ICclApiClient>();
        var advanceGate = new TaskCompletionSource<AdvanceWorkOrderResponse>();
        api.SummaryImpl = (woNo, ct) => Task.FromResult<WorkOrderSummary?>(
            SampleSummary(woNo) with { CurrentStep = "ReadyToRun", ETag = "RV0" });
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
            Assert.Contains("Đang chuyển bước", btn.TextContent);
        });

        // Let the advance finish so the test fixture tears down cleanly.
        advanceGate.SetResult(new AdvanceWorkOrderResponse
        {
            Ok = true, CurrentStep = "Running", ETag = "RV1"
        });
        cut.WaitForElement("[data-testid='advance-success-banner']");
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
