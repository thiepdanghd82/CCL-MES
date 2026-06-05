using CCL.MES.Shared.WorkOrders;

namespace CCL.MES.Hybrid.Client.WorkOrders;

/// <summary>
/// P10.7a-1.3 — operator-facing advance flow lifted out of
/// <c>WorkOrders.razor</c> so the client xUnit suite can lock the
/// behaviour without booting MAUI/Blazor. Three responsibilities:
///
///   1. Double-tap guard — <see cref="IsBusy"/> stays <c>true</c> for
///      the entire RTT so a fast second tap is dropped before it
///      reaches the wire. The Razor button is already bound to this
///      via <c>disabled="@_advancing"</c>; the orchestrator surfaces
///      the same flag for tests + future non-Razor surfaces.
///   2. 409 ETag adoption — when the server reports a stale ETag,
///      the orchestrator copies the server's CURRENT ETag (in the 409
///      body) into the cached summary. The next tap reuses it; no
///      separate summary GET needed.
///   3. Success refresh — on 200, optionally refresh the summary via
///      a caller-supplied lookup delegate (the production Razor page
///      passes <c>Api.GetWorkOrderByNoAsync</c>).
/// </summary>
public sealed class AdvanceOrchestrator
{
    private readonly IAdvanceClient _api;
    private int _inFlight; // 0 = idle, 1 = busy. Interlocked guards
                            // the fast-tap path even off the UI thread.

    /// <summary>Production constructor — adapts <see cref="ICclApiClient"/>
    /// down to the narrow surface this orchestrator needs.</summary>
    public AdvanceOrchestrator(ICclApiClient api)
        : this(new CclApiClientAdvanceAdapter(api))
    {
    }

    /// <summary>Test constructor — the unit suite passes a hand-rolled
    /// <see cref="IAdvanceClient"/> stub without having to implement
    /// the full <see cref="ICclApiClient"/> surface.</summary>
    public AdvanceOrchestrator(IAdvanceClient api)
    {
        _api = api;
    }

    /// <summary>True while a tap is in flight. The Razor page binds
    /// this to <c>button.disabled</c>; non-Razor surfaces (future
    /// MAUI Shell pages) can poll it for a spinner.</summary>
    public bool IsBusy => Volatile.Read(ref _inFlight) == 1;

    /// <summary>
    /// Run the advance for <paramref name="summary"/>. Returns the
    /// post-advance summary the UI should display. On 409 the
    /// returned summary has the SERVER's current ETag adopted so a
    /// subsequent call from the UI uses the fresh value.
    /// <para/>
    /// If a tap is already in flight returns <c>null</c> + does
    /// nothing — the operator's second tap is a no-op (double-tap
    /// guard).
    /// </summary>
    /// <param name="summary">The current cached summary (must carry
    /// the ETag from the previous GET).</param>
    /// <param name="refreshSummary">Optional callback the
    /// orchestrator invokes on 200 success to fetch the new summary.
    /// Pass <c>null</c> if the caller will refresh on its own.</param>
    public async Task<AdvanceOutcome?> RunAsync(
        WorkOrderSummary summary,
        Func<string, CancellationToken, Task<WorkOrderSummary?>>? refreshSummary = null,
        CancellationToken ct = default)
    {
        // Double-tap guard. Interlocked.CompareExchange returns the
        // previous value; if it was already 1, another tap is racing
        // us and we drop this one immediately.
        if (Interlocked.CompareExchange(ref _inFlight, 1, 0) == 1)
        {
            return null;
        }

        try
        {
            var resp = await _api.AdvanceWorkOrderAsync(summary.Id, summary.ETag, ct);

            if (resp.Ok)
            {
                if (refreshSummary is not null)
                {
                    var refreshed = await refreshSummary(summary.WoNo, ct);
                    if (refreshed is not null)
                        return new AdvanceOutcome(refreshed, resp, AdvanceOutcomeKind.Success);
                }
                // Refresh skipped or returned null — synthesise an updated
                // summary from the advance response so the caller can at
                // least show the new step + new ETag.
                var synth = summary with
                {
                    CurrentStep = resp.CurrentStep,
                    ETag = resp.ETag,
                };
                return new AdvanceOutcome(synth, resp, AdvanceOutcomeKind.Success);
            }

            if (resp.ErrorCode == "wo.state_conflict" && !string.IsNullOrEmpty(resp.ETag))
            {
                // 409 path: adopt the server's CURRENT ETag so the
                // operator's next tap won't re-trip the conflict.
                var adopted = summary with { ETag = resp.ETag };
                return new AdvanceOutcome(adopted, resp, AdvanceOutcomeKind.StateConflict);
            }

            // Any other in-band failure (legacy state-machine guard:
            // RequiresSpecAndMaterials, IpqcNotPassed, ...). The
            // summary is unchanged; the caller renders the banner
            // from resp.ErrorCode.
            return new AdvanceOutcome(summary, resp, AdvanceOutcomeKind.DomainGuard);
        }
        finally
        {
            Volatile.Write(ref _inFlight, 0);
        }
    }
}

/// <summary>P10.7a-1.3 — minimal API surface
/// <see cref="AdvanceOrchestrator"/> needs. Defined as a separate
/// interface so the client xUnit suite can pass a hand-rolled stub
/// without implementing all of <see cref="ICclApiClient"/>.</summary>
public interface IAdvanceClient
{
    Task<CCL.MES.Shared.WorkOrders.AdvanceWorkOrderResponse>
        AdvanceWorkOrderAsync(long workOrderId, string ifMatchETag, CancellationToken ct);

    Task<CCL.MES.Shared.WorkOrders.WorkOrderSummary?>
        GetWorkOrderByNoAsync(string woNo, CancellationToken ct);
}

internal sealed class CclApiClientAdvanceAdapter : IAdvanceClient
{
    private readonly ICclApiClient _inner;
    public CclApiClientAdvanceAdapter(ICclApiClient inner) => _inner = inner;
    public Task<CCL.MES.Shared.WorkOrders.AdvanceWorkOrderResponse>
        AdvanceWorkOrderAsync(long workOrderId, string ifMatchETag, CancellationToken ct)
        => _inner.AdvanceWorkOrderAsync(workOrderId, ifMatchETag, ct);
    public Task<CCL.MES.Shared.WorkOrders.WorkOrderSummary?>
        GetWorkOrderByNoAsync(string woNo, CancellationToken ct)
        => _inner.GetWorkOrderByNoAsync(woNo, ct);
}

/// <summary>P10.7a-1.3 — outcome categorisation for the Razor page +
/// xUnit suite. Lets the page render a tone (success / warn /
/// neutral) without re-parsing the response code.</summary>
public enum AdvanceOutcomeKind
{
    /// <summary>200 + Ok=true. Summary refreshed.</summary>
    Success,
    /// <summary>409 stale ETag. Summary's ETag silently adopted from
    /// server's current value; banner shown to operator.</summary>
    StateConflict,
    /// <summary>200 + Ok=false (legacy domain guard like
    /// RequiresSpecAndMaterials). Summary unchanged.</summary>
    DomainGuard,
}

/// <summary>Bundle of post-advance state the orchestrator hands back
/// to the Razor page.</summary>
public sealed record AdvanceOutcome(
    WorkOrderSummary Summary,
    AdvanceWorkOrderResponse Response,
    AdvanceOutcomeKind Kind);
