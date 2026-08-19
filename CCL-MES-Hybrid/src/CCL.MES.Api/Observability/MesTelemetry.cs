using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace CCL.MES.Api.Observability;

/// <summary>
/// Đợt 1 C1 — in-box telemetry primitives (PA-1). Zero new NuGet packages:
/// <see cref="ActivitySource"/> and <see cref="Meter"/> ship with the
/// runtime, and <c>AddJsonConsole</c> ships with
/// Microsoft.Extensions.Logging.Console. OpenTelemetry export is
/// deliberately NOT part of this batch — when it lands it subscribes to
/// exactly these two names and no callsite here changes.
///
/// The counters answer the three questions we could not answer during the
/// 2026-06-05 "500 on login" and the P10.7a advance-soak incidents:
///   • how often does a WO actually change phase?
///   • how often do two operators collide on the same WO (409)?
///   • how often is somebody bounced by RBAC (403)?
/// </summary>
public static class MesTelemetry
{
    public const string SourceName = "CCL.MES.Api";
    private const string Version = "1.0.0";

    /// <summary>Wraps every API request so <c>trace_id</c> is stable across
    /// middleware → controller → application service → audit writer.</summary>
    public static readonly ActivitySource Source = new(SourceName, Version);

    private static readonly Meter Meter = new(SourceName, Version);

    /// <summary>Incremented once per audit row whose canonical envelope
    /// shows <c>from_phase != to_phase</c>. Tagged with the audit action so
    /// ADVANCE / SPLIT / REJECT can be told apart.</summary>
    public static readonly Counter<long> WoPhaseTransitions = Meter.CreateCounter<long>(
        "mes.wo.phase_transitions", unit: "{transition}",
        description: "WO phase transitions observed on the audit trail.");

    /// <summary>Incremented on every HTTP 409. That status is reserved in
    /// this API for optimistic-concurrency loss (stale If-Match).</summary>
    public static readonly Counter<long> ConcurrencyConflicts = Meter.CreateCounter<long>(
        "mes.concurrency_conflicts", unit: "{conflict}",
        description: "HTTP 409 — optimistic concurrency loss on a WO write.");

    /// <summary>Incremented on every HTTP 403 — authenticated caller, wrong
    /// role. 401 (no/expired token) is a different problem and is not
    /// counted here.</summary>
    public static readonly Counter<long> RbacDenials = Meter.CreateCounter<long>(
        "mes.rbac_denials", unit: "{denial}",
        description: "HTTP 403 — authenticated caller refused by an RBAC policy.");

    private static int _listenerInstalled;

    /// <summary>
    /// <see cref="ActivitySource.StartActivity()"/> returns null when nobody
    /// is listening, which would leave <c>trace_id</c> empty on a stock
    /// deployment. Install an always-sample listener scoped to our own
    /// source so an Activity always exists. Idempotent — safe to call from
    /// every <c>WebApplicationFactory</c> a test class spins up.
    /// </summary>
    public static void EnsureActivityListener()
    {
        if (Interlocked.Exchange(ref _listenerInstalled, 1) == 1) return;

        ActivitySource.AddActivityListener(new ActivityListener
        {
            ShouldListenTo = s => s.Name == SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
        });
    }
}
