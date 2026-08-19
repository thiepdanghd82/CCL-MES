using System.Collections;
using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace CCL.MES.Api.Observability;

/// <summary>
/// Đợt 1 C1 — the logging scope pushed around every API request.
///
/// Reads its values <b>live</b> rather than snapshotting them at
/// <c>BeginScope</c> time. That matters: <c>wo_no</c> and
/// <c>work_center</c> are not known when the request enters the pipeline,
/// they are discovered by the audit writer or the OEE read path partway
/// through. Because <c>JsonConsoleFormatter</c> enumerates the scope once
/// per log record, a live-reading scope means every line written after the
/// discovery carries the identifiers — including the request-completed
/// line the middleware writes last.
///
/// Implements <see cref="IReadOnlyList{T}"/> of
/// <see cref="KeyValuePair{TKey,TValue}"/> with <c>object</c> values,
/// which is the exact shape <c>JsonConsoleFormatter</c> unpacks into named
/// JSON properties. Anything else lands as a flat <c>ToString()</c> blob.
/// </summary>
public sealed class MesLogScope : IReadOnlyList<KeyValuePair<string, object>>
{
    private readonly Activity? _activity;
    private readonly HttpContext _http;
    private readonly MesRequestContext _ctx;

    /// <param name="http">The live context, NOT a snapshot of
    /// <c>context.User</c>. This middleware runs ahead of
    /// <c>UseAuthentication()</c>, which ASSIGNS a brand-new
    /// <see cref="ClaimsPrincipal"/> to <c>HttpContext.User</c>. Capturing
    /// the principal in the constructor pins the pre-auth empty one, and
    /// every line — including RBAC denials, where the actor matters most —
    /// logs "anonymous". Hold the context, read User per enumeration.</param>
    public MesLogScope(Activity? activity, HttpContext http, MesRequestContext ctx)
    {
        _activity = activity;
        _http = http;
        _ctx = ctx;
    }

    private string TraceId => _activity?.TraceId.ToString() ?? "";

    /// <summary>Username from the validated JWT. Never the token itself —
    /// <c>gate-audit-emit.sh</c> hard-fails on credential material reaching
    /// any exported surface, and logs are an exported surface.</summary>
    private string Actor
    {
        get
        {
            var user = _http.User;
            return user?.Identity?.IsAuthenticated == true
                ? (user.FindFirstValue(ClaimTypes.Name)
                   ?? user.FindFirstValue("sub")
                   ?? user.Identity.Name
                   ?? "anonymous")
                : "anonymous";
        }
    }

    public int Count => 2
        + (_ctx.WoNo is null ? 0 : 1)
        + (_ctx.WorkCenter is null ? 0 : 1);

    public KeyValuePair<string, object> this[int index]
    {
        get
        {
            switch (index)
            {
                case 0: return new KeyValuePair<string, object>("trace_id", TraceId);
                case 1: return new KeyValuePair<string, object>("actor", Actor);
            }
            var i = 2;
            if (_ctx.WoNo is string wo)
            {
                if (index == i) return new KeyValuePair<string, object>("wo_no", wo);
                i++;
            }
            if (_ctx.WorkCenter is string wc && index == i)
                return new KeyValuePair<string, object>("work_center", wc);

            throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
    {
        for (var i = 0; i < Count; i++) yield return this[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Fallback rendering for the plain-text console formatter.</summary>
    public override string ToString()
    {
        var parts = new List<string>(4);
        foreach (var kv in this) parts.Add($"{kv.Key}={kv.Value}");
        return string.Join(' ', parts);
    }
}
