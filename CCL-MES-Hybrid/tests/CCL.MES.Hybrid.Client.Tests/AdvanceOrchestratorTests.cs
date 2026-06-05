using CCL.MES.Hybrid.Client.WorkOrders;
using CCL.MES.Shared.WorkOrders;
using Xunit;

namespace CCL.MES.Hybrid.Client.Tests;

/// <summary>
/// P10.7a-1.3 — locks the operator-facing advance flow that the
/// Razor <c>OnAdvance</c> handler delegates to. These tests REPLACE
/// the manual "tap Accept twice fast" + "tap Accept after make-stale"
/// portions of the original Catalyst checkpoint with hard assertions
/// the agent can re-run on every CI build.
///
/// Specifically:
///   - <see cref="DoubleTap_guard_drops_second_concurrent_run"/>
///     proves the in-flight guard on a real double-tap.
///   - <see cref="StateConflict_adopts_server_ETag"/> proves the
///     409 flow updates the cached summary's ETag in place.
///   - <see cref="Success_path_refreshes_summary"/> proves the
///     happy path consumes the refresh delegate.
/// </summary>
public sealed class AdvanceOrchestratorTests
{
    // ── Double-tap guard ─────────────────────────────────────────────

    [Fact]
    public async Task IsBusy_false_when_idle()
    {
        var (orch, _) = Build();
        Assert.False(orch.IsBusy);
    }

    [Fact]
    public async Task IsBusy_true_while_advance_in_flight()
    {
        var gate = new TaskCompletionSource<AdvanceWorkOrderResponse>();
        var api = new ScriptedApi
        {
            AdvanceImpl = async (id, etag, ct) =>
            {
                // Block until the test releases — emulates a slow LAN.
                return await gate.Task;
            },
        };
        var orch = new AdvanceOrchestrator(api);

        var task = orch.RunAsync(SampleSummary(), refreshSummary: null);
        // Yield a tick so the orchestrator enters RunAsync.
        await Task.Delay(20);
        Assert.True(orch.IsBusy);

        gate.SetResult(new AdvanceWorkOrderResponse
        {
            Ok = true, CurrentStep = "Running", ETag = "RV1"
        });
        await task;
        Assert.False(orch.IsBusy);
    }

    [Fact]
    public async Task DoubleTap_guard_drops_second_concurrent_run()
    {
        var gate = new TaskCompletionSource<AdvanceWorkOrderResponse>();
        var callCount = 0;
        var api = new ScriptedApi
        {
            AdvanceImpl = async (id, etag, ct) =>
            {
                Interlocked.Increment(ref callCount);
                return await gate.Task;
            },
        };
        var orch = new AdvanceOrchestrator(api);

        var summary = SampleSummary();
        // Tap #1 enters + blocks waiting on the API.
        var first = orch.RunAsync(summary);
        await Task.Delay(20);

        // Tap #2 should be dropped immediately + return null without
        // calling the API. By this point tap #1 has already reached
        // the API (callCount = 1) and is blocked at the gate; tap #2
        // never makes it past the orchestrator's in-flight guard.
        var second = await orch.RunAsync(summary);
        Assert.Null(second);
        Assert.Equal(1, callCount); // tap #1 only; tap #2 dropped at guard

        gate.SetResult(new AdvanceWorkOrderResponse
        {
            Ok = true, CurrentStep = "Running", ETag = "RV1"
        });
        var firstResult = await first;
        Assert.NotNull(firstResult);
        Assert.Equal(1, callCount); // STILL exactly one wire call from two taps
    }

    [Fact]
    public async Task DoubleTap_guard_clears_after_first_finishes()
    {
        var api = new ScriptedApi
        {
            AdvanceImpl = (id, etag, ct) => Task.FromResult(new AdvanceWorkOrderResponse
            {
                Ok = true, CurrentStep = "Running", ETag = "RV-NEW"
            }),
        };
        var orch = new AdvanceOrchestrator(api);

        var first = await orch.RunAsync(SampleSummary());
        Assert.NotNull(first);
        Assert.False(orch.IsBusy);

        // A second tap after the first finishes IS allowed (operator's
        // legitimate "advance again" intent).
        var second = await orch.RunAsync(first!.Summary);
        Assert.NotNull(second);
    }

    // ── 409 ETag adoption ────────────────────────────────────────────

    [Fact]
    public async Task StateConflict_adopts_server_ETag()
    {
        var api = new ScriptedApi
        {
            AdvanceImpl = (id, etag, ct) => Task.FromResult(new AdvanceWorkOrderResponse
            {
                Ok = false,
                CurrentStep = "PrePressCheck",
                ErrorCode = "wo.state_conflict",
                ETag = "SERVER_FRESH_ETAG=",
            }),
        };
        var orch = new AdvanceOrchestrator(api);

        var staleSummary = SampleSummary() with { ETag = "STALE=" };
        var outcome = await orch.RunAsync(staleSummary);

        Assert.NotNull(outcome);
        Assert.Equal(AdvanceOutcomeKind.StateConflict, outcome!.Kind);
        // The cached summary now carries the SERVER's ETag — the next
        // operator tap won't re-trip the same conflict.
        Assert.Equal("SERVER_FRESH_ETAG=", outcome.Summary.ETag);
        // Other summary fields untouched (the 409 doesn't change the
        // displayed step / customer / etc.).
        Assert.Equal(staleSummary.Id, outcome.Summary.Id);
        Assert.Equal(staleSummary.WoNo, outcome.Summary.WoNo);
        Assert.Equal(staleSummary.CurrentStep, outcome.Summary.CurrentStep);
    }

    [Fact]
    public async Task StateConflict_does_not_replace_summary_when_server_ETag_empty()
    {
        // Defensive: if the server returns 409 but the body's ETag
        // field is missing/empty, we DON'T silently wipe the cached
        // ETag. The outcome flags the conflict; the cached summary's
        // ETag stays — operator must re-scan to recover.
        var api = new ScriptedApi
        {
            AdvanceImpl = (id, etag, ct) => Task.FromResult(new AdvanceWorkOrderResponse
            {
                Ok = false,
                CurrentStep = "PrePressCheck",
                ErrorCode = "wo.state_conflict",
                ETag = "", // server didn't surface a current ETag
            }),
        };
        var orch = new AdvanceOrchestrator(api);

        var staleSummary = SampleSummary() with { ETag = "ORIGINAL=" };
        var outcome = await orch.RunAsync(staleSummary);

        Assert.NotNull(outcome);
        Assert.Equal("ORIGINAL=", outcome!.Summary.ETag);
    }

    // ── Success path ─────────────────────────────────────────────────

    [Fact]
    public async Task Success_path_refreshes_summary_via_callback()
    {
        var refreshedSummary = SampleSummary() with
        {
            CurrentStep = "Running",
            ETag = "REFRESHED_ETAG=",
        };
        var api = new ScriptedApi
        {
            AdvanceImpl = (id, etag, ct) => Task.FromResult(new AdvanceWorkOrderResponse
            {
                Ok = true, CurrentStep = "Running", ETag = "BUMPED="
            }),
        };
        var orch = new AdvanceOrchestrator(api);

        var refreshCalls = 0;
        var outcome = await orch.RunAsync(
            SampleSummary(),
            refreshSummary: (wo, ct) =>
            {
                Interlocked.Increment(ref refreshCalls);
                return Task.FromResult<WorkOrderSummary?>(refreshedSummary);
            });

        Assert.NotNull(outcome);
        Assert.Equal(AdvanceOutcomeKind.Success, outcome!.Kind);
        Assert.Equal(1, refreshCalls);
        // The refreshed summary is the source of truth (server-derived
        // fields like BadgeCssClass are populated).
        Assert.Equal("REFRESHED_ETAG=", outcome.Summary.ETag);
        Assert.Equal("Running", outcome.Summary.CurrentStep);
    }

    [Fact]
    public async Task Success_path_without_refresh_callback_synthesises_summary()
    {
        var api = new ScriptedApi
        {
            AdvanceImpl = (id, etag, ct) => Task.FromResult(new AdvanceWorkOrderResponse
            {
                Ok = true, CurrentStep = "Running", ETag = "BUMPED="
            }),
        };
        var orch = new AdvanceOrchestrator(api);

        var outcome = await orch.RunAsync(SampleSummary(), refreshSummary: null);

        Assert.NotNull(outcome);
        Assert.Equal(AdvanceOutcomeKind.Success, outcome!.Kind);
        // Synthesised summary carries the bumped ETag + new step from
        // the advance response.
        Assert.Equal("BUMPED=", outcome.Summary.ETag);
        Assert.Equal("Running", outcome.Summary.CurrentStep);
    }

    // ── Domain guard (legacy 200 + Ok=false) ─────────────────────────

    [Fact]
    public async Task DomainGuard_leaves_summary_unchanged()
    {
        var api = new ScriptedApi
        {
            AdvanceImpl = (id, etag, ct) => Task.FromResult(new AdvanceWorkOrderResponse
            {
                Ok = false,
                CurrentStep = "PrePressCheck",
                ErrorCode = "RequiresSpecAndMaterials",
                ETag = "SAME_ETAG=",
            }),
        };
        var orch = new AdvanceOrchestrator(api);

        var original = SampleSummary() with { ETag = "ORIGINAL=" };
        var outcome = await orch.RunAsync(original);

        Assert.NotNull(outcome);
        Assert.Equal(AdvanceOutcomeKind.DomainGuard, outcome!.Kind);
        // Domain guard: nothing changed server-side. The cached
        // summary is returned as-is so the banner explains WHY the
        // tap was rejected without overwriting the displayed state.
        Assert.Same(original, outcome.Summary);
        Assert.Equal("RequiresSpecAndMaterials", outcome.Response.ErrorCode);
    }

    // ── helpers ──────────────────────────────────────────────────────

    private static WorkOrderSummary SampleSummary() => new()
    {
        Id = 42,
        WoNo = "WO-TEST-100",
        CustomerName = "Brady Asia",
        ProductCode = "BRD-001",
        ProductName = "PCB Label 20x8mm",
        MachineCode = "ACNC3",
        MachineName = "CNC 3-Heads",
        TargetQty = 1000,
        ProducedQty = 0,
        Uom = "pcs",
        CurrentStep = "ReadyToRun",
        BadgeLabelKey = "wo.status.in_progress",
        BadgeCssClass = "wo-status-running",
        ETag = "INITIAL_ETAG=",
    };

    private static (AdvanceOrchestrator orch, ScriptedApi api) Build()
    {
        var api = new ScriptedApi();
        var orch = new AdvanceOrchestrator(api);
        return (orch, api);
    }
}
