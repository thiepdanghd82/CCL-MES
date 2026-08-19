using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Claims;
using CCL.MES.Api.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CCL.MES.Api.Tests.Unit;

/// <summary>
/// Đợt 1 C1 — the middleware that makes every request observable.
///
/// Driven directly rather than through WebApplicationFactory so the two
/// counter branches (403 / 409) are deterministic instead of depending on
/// arranging a real RBAC denial and a real ETag collision. The end-to-end
/// proof that the wiring is live lives in
/// <c>RequestObservabilityEndToEndTests</c>.
/// </summary>
public sealed class RequestObservabilityMiddlewareTests
{
    // ── test doubles ──────────────────────────────────────────────

    /// <summary>Captures the scope objects the middleware pushes, and
    /// snapshots each one at the moment a line is logged — which is exactly
    /// how JsonConsoleFormatter reads them.</summary>
    private sealed class CapturingLogger : ILogger<RequestObservabilityMiddleware>
    {
        public readonly List<object?> ActiveScopes = new();
        public readonly List<Dictionary<string, object>> Snapshots = new();
        public readonly List<string> Messages = new();

        private sealed class Pop : IDisposable
        {
            public required Action OnDispose { get; init; }
            public void Dispose() => OnDispose();
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            ActiveScopes.Add(state);
            return new Pop { OnDispose = () => ActiveScopes.Remove(state) };
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var flat = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var scope in ActiveScopes)
            {
                if (scope is IEnumerable<KeyValuePair<string, object>> items)
                    foreach (var kv in items) flat[kv.Key] = kv.Value;
            }
            Snapshots.Add(flat);
            Messages.Add(formatter(state, exception));
        }
    }

    private static HttpContext BuildContext(int statusCode, ClaimsPrincipal? user = null)
    {
        var services = new ServiceCollection();
        services.AddScoped<MesRequestContext>();
        var provider = services.BuildServiceProvider();

        var ctx = new DefaultHttpContext
        {
            RequestServices = provider.CreateScope().ServiceProvider,
        };
        ctx.Request.Method = "GET";
        ctx.Request.Path = "/api/v2/work-orders/7/summary-report";
        ctx.Response.StatusCode = statusCode;
        if (user is not null) ctx.User = user;
        return ctx;
    }

    private static ClaimsPrincipal Authenticated(string username) =>
        new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, username) }, "TestAuth"));

    /// <summary>Collects a single counter by name across the whole test.</summary>
    private static (MeterListener Listener, List<long> Values) ListenTo(string instrumentName)
    {
        var values = new List<long>();
        var listener = new MeterListener
        {
            InstrumentPublished = (inst, l) =>
            {
                if (inst.Meter.Name == MesTelemetry.SourceName && inst.Name == instrumentName)
                    l.EnableMeasurementEvents(inst);
            },
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, _, _) =>
        {
            lock (values) values.Add(measurement);
        });
        listener.Start();
        return (listener, values);
    }

    // ── counters ──────────────────────────────────────────────────

    [Fact]
    public async Task Counts_rbac_denial_on_403()
    {
        var (listener, values) = ListenTo("mes.rbac_denials");
        using var _ = listener;

        var mw = new RequestObservabilityMiddleware(_ => Task.CompletedTask, new CapturingLogger());
        await mw.InvokeAsync(BuildContext(StatusCodes.Status403Forbidden));

        listener.RecordObservableInstruments();
        Assert.Single(values);
        Assert.Equal(1, values[0]);
    }

    [Fact]
    public async Task Counts_concurrency_conflict_on_409()
    {
        var (listener, values) = ListenTo("mes.concurrency_conflicts");
        using var _ = listener;

        var mw = new RequestObservabilityMiddleware(_ => Task.CompletedTask, new CapturingLogger());
        await mw.InvokeAsync(BuildContext(StatusCodes.Status409Conflict));

        Assert.Single(values);
    }

    [Fact]
    public async Task Does_not_count_401_as_an_rbac_denial()
    {
        // 401 is "no or expired token" — a different failure from "wrong
        // role". Conflating them makes the RBAC number useless.
        var (listener, values) = ListenTo("mes.rbac_denials");
        using var _ = listener;

        var mw = new RequestObservabilityMiddleware(_ => Task.CompletedTask, new CapturingLogger());
        await mw.InvokeAsync(BuildContext(StatusCodes.Status401Unauthorized));

        Assert.Empty(values);
    }

    [Fact]
    public async Task Counts_nothing_on_a_plain_200()
    {
        var (rbacListener, rbac) = ListenTo("mes.rbac_denials");
        var (conflictListener, conflicts) = ListenTo("mes.concurrency_conflicts");
        using var _ = rbacListener;
        using var __ = conflictListener;

        var mw = new RequestObservabilityMiddleware(_ => Task.CompletedTask, new CapturingLogger());
        await mw.InvokeAsync(BuildContext(StatusCodes.Status200OK));

        Assert.Empty(rbac);
        Assert.Empty(conflicts);
    }

    // ── log scope ─────────────────────────────────────────────────

    [Fact]
    public async Task Log_line_carries_trace_id_and_actor()
    {
        var log = new CapturingLogger();
        var mw = new RequestObservabilityMiddleware(_ => Task.CompletedTask, log);

        await mw.InvokeAsync(BuildContext(StatusCodes.Status200OK, Authenticated("qc.hai")));

        var snap = Assert.Single(log.Snapshots);
        Assert.True(snap.ContainsKey("trace_id"));
        Assert.False(string.IsNullOrWhiteSpace((string)snap["trace_id"]));
        Assert.Equal("qc.hai", snap["actor"]);
    }

    [Fact]
    public async Task Unauthenticated_request_logs_actor_anonymous()
    {
        var log = new CapturingLogger();
        var mw = new RequestObservabilityMiddleware(_ => Task.CompletedTask, log);

        await mw.InvokeAsync(BuildContext(StatusCodes.Status401Unauthorized));

        Assert.Equal("anonymous", Assert.Single(log.Snapshots)["actor"]);
    }

    /// <summary>
    /// The load-bearing property of <see cref="MesLogScope"/>: wo_no and
    /// work_center are not known when the scope is pushed, they are
    /// discovered mid-request. A snapshotting scope would silently drop
    /// them; a live-reading one picks them up.
    /// </summary>
    [Fact]
    public async Task Scope_picks_up_wo_no_and_work_center_discovered_mid_request()
    {
        var log = new CapturingLogger();
        var mw = new RequestObservabilityMiddleware(
            ctx =>
            {
                var obs = ctx.RequestServices.GetRequiredService<MesRequestContext>();
                obs.NoteWorkOrder("WO-26-2852");
                obs.NoteWorkCenter("SL-01");
                return Task.CompletedTask;
            },
            log);

        await mw.InvokeAsync(BuildContext(StatusCodes.Status200OK, Authenticated("qc.hai")));

        var snap = Assert.Single(log.Snapshots);
        Assert.Equal("WO-26-2852", snap["wo_no"]);
        Assert.Equal("SL-01", snap["work_center"]);
    }

    [Fact]
    public async Task Scope_omits_wo_no_and_work_center_when_unknown()
    {
        var log = new CapturingLogger();
        var mw = new RequestObservabilityMiddleware(_ => Task.CompletedTask, log);

        await mw.InvokeAsync(BuildContext(StatusCodes.Status200OK, Authenticated("qc.hai")));

        var snap = Assert.Single(log.Snapshots);
        Assert.False(snap.ContainsKey("wo_no"));
        Assert.False(snap.ContainsKey("work_center"));
    }

    [Fact]
    public async Task Activity_exists_so_trace_id_is_never_empty()
    {
        Activity? seen = null;
        var mw = new RequestObservabilityMiddleware(_ =>
        {
            seen = Activity.Current;
            return Task.CompletedTask;
        }, new CapturingLogger());

        await mw.InvokeAsync(BuildContext(StatusCodes.Status200OK));

        Assert.NotNull(seen);
        Assert.NotEqual(default, seen!.TraceId);
    }

    [Fact]
    public async Task Logs_and_counts_even_when_the_pipeline_throws()
    {
        // A 500 must still leave a trace line. The finally block is the
        // whole point — an exception is when you most want the log.
        var log = new CapturingLogger();
        var mw = new RequestObservabilityMiddleware(
            _ => throw new InvalidOperationException("boom"), log);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => mw.InvokeAsync(BuildContext(StatusCodes.Status200OK)));

        Assert.Single(log.Snapshots);
    }

    // ── regressions found during live verification ────────────────

    /// <summary>
    /// REGRESSION: this middleware runs ahead of UseAuthentication(), and
    /// UseAuthentication ASSIGNS a new ClaimsPrincipal to HttpContext.User.
    /// The first cut captured context.User in the scope constructor and
    /// therefore pinned the pre-auth empty principal — every line logged
    /// actor=anonymous, including RBAC denials, where the actor is the
    /// single most important field. Caught only by a live run; this test
    /// reproduces the ordering that exposed it.
    /// </summary>
    [Fact]
    public async Task Actor_resolves_when_authentication_runs_after_this_middleware()
    {
        var log = new CapturingLogger();
        var mw = new RequestObservabilityMiddleware(
            ctx =>
            {
                // Exactly what UseAuthentication does: replace the principal.
                ctx.User = Authenticated("qc.hai");
                return Task.CompletedTask;
            },
            log);

        // Context starts unauthenticated, as it really does at this point
        // in the pipeline.
        await mw.InvokeAsync(BuildContext(StatusCodes.Status200OK));

        Assert.Equal("qc.hai", Assert.Single(log.Snapshots)["actor"]);
    }

    /// <summary>
    /// REGRESSION: as the outermost middleware, we unwind BEFORE Kestrel
    /// stamps 500 on the response, so Response.StatusCode still reads 200.
    /// A log line claiming 200 for a request the caller saw fail is worse
    /// than no log line.
    /// </summary>
    [Fact]
    public async Task Unhandled_exception_is_logged_as_500_not_200()
    {
        var log = new CapturingLogger();
        var ctx = BuildContext(StatusCodes.Status200OK);
        var mw = new RequestObservabilityMiddleware(
            _ => throw new InvalidOperationException("boom"), log);

        await Assert.ThrowsAsync<InvalidOperationException>(() => mw.InvokeAsync(ctx));

        Assert.Contains(log.Messages, m => m.Contains("-> 500"));
        Assert.DoesNotContain(log.Messages, m => m.Contains("-> 200"));
    }
}
