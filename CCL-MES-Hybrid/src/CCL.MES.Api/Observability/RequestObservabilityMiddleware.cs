using System.Diagnostics;

namespace CCL.MES.Api.Observability;

/// <summary>
/// Đợt 1 C1 — one Activity + one logging scope + one structured line per
/// API request, and the two status-code counters.
///
/// Sits FIRST in the pipeline, ahead of routing and authentication, so it
/// also wraps the requests that never reach a controller: a 403 from the
/// authorization middleware is exactly the event we want counted, and it
/// happens before any controller code runs.
///
/// The scope is created before <c>User</c> is populated, which is fine —
/// <see cref="MesLogScope"/> reads the principal live, so by the time any
/// line is written the actor is resolved.
///
/// Nothing here belongs in a controller. Per <c>cmes-thin-controller</c> a
/// controller binds, authorizes, calls a service and maps errors; it does
/// not instrument itself. Keeping this in middleware is also why the
/// instrumentation cannot be forgotten on the next endpoint somebody adds.
/// </summary>
public sealed class RequestObservabilityMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestObservabilityMiddleware> _log;

    public RequestObservabilityMiddleware(
        RequestDelegate next,
        ILogger<RequestObservabilityMiddleware> log)
    {
        _next = next;
        _log = log;
        MesTelemetry.EnsureActivityListener();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // MesRequestContext is scoped — resolve from the request scope, not
        // from the (singleton) middleware constructor. ValidateScopes = true
        // in Program.cs would throw on the latter.
        var ctx = context.RequestServices.GetRequiredService<MesRequestContext>();

        var method = context.Request.Method;
        var path = context.Request.Path.HasValue ? context.Request.Path.Value! : "/";

        using var activity = MesTelemetry.Source.StartActivity(
            $"{method} {path}", ActivityKind.Server);
        activity?.SetTag("http.request.method", method);
        activity?.SetTag("url.path", path);

        using var scope = _log.BeginScope(
            new MesLogScope(activity ?? Activity.Current, context, ctx));

        var started = Stopwatch.GetTimestamp();
        Exception? failure = null;
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            // Not swallowed — rethrown below. Captured only so the status we
            // log matches what the caller receives: this middleware is
            // outermost, so Kestrel stamps 500 on the response only AFTER we
            // unwind, and reading Response.StatusCode here would report the
            // untouched 200.
            failure = ex;
            throw;
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var status = failure is null
                ? context.Response.StatusCode
                : StatusCodes.Status500InternalServerError;

            // 409 is reserved in this API for optimistic-concurrency loss
            // (stale If-Match on a WO write) — see the 7d/7e controllers.
            if (status == StatusCodes.Status409Conflict)
                MesTelemetry.ConcurrencyConflicts.Add(1, new KeyValuePair<string, object?>("path", path));

            // 403 = authenticated but refused by policy. 401 (absent or
            // expired token) is a different failure and stays uncounted.
            if (status == StatusCodes.Status403Forbidden)
                MesTelemetry.RbacDenials.Add(1, new KeyValuePair<string, object?>("path", path));

            activity?.SetTag("http.response.status_code", status);
            if (failure is not null) activity?.SetStatus(ActivityStatusCode.Error, failure.Message);

            // WO number, work center and actor are attached by the scope,
            // which reads them live while the request ran.
            _log.LogInformation(
                "api_request {Method} {Path} -> {Status} in {ElapsedMs:F1}ms",
                method, path, status, elapsedMs);
        }
    }
}
