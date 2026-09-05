using System.Text.Json;
using CCL.MES.Api.Observability;
using CCL.MES.Application;
using CCL.MES.Application.Audit;
using CCL.MES.Domain.Entities;
using Microsoft.AspNetCore.Http;

namespace CCL.MES.Api.Audit;

/// <summary>
/// API-side implementation of <see cref="IAuditWriter"/>. Behaviour mirrors
/// the legacy <c>CCL.MES.Web.Services.AuditService</c> (Phase 6 Bước 5):
/// append-only writes to the <c>AuditLogs</c> table with remote IP picked
/// up from <see cref="IHttpContextAccessor"/>, Detail trimmed to 4 KB.
///
/// We DO NOT modify or import the legacy AuditService — keeping a
/// behaviour-identical copy here satisfies the "READ-ONLY legacy"
/// constraint and lets the API run without the Web project on its path.
/// <see cref="Source"/> defaults to <c>"Api"</c> so audit rows emitted via
/// HTTP are distinguishable from rows emitted via the legacy Web cookie path.
///
/// Đợt 1 C1 — this is also the single funnel every mutation already passes
/// through, so it is where observability is attached without touching a
/// single controller:
///   • <c>wo_no</c> from the canonical envelope lands in
///     <see cref="MesRequestContext"/> and therefore in every subsequent
///     log line of the request;
///   • an envelope whose <c>from_phase</c> differs from <c>to_phase</c> is,
///     by the definition in P10.7-WO-STATE-CONTRACT §7.2, a WO phase
///     transition — counted on <c>mes.wo.phase_transitions</c>.
/// Audit emission itself is unchanged; the trail is still append-only.
/// </summary>
public sealed class ApiAuditWriter : IAuditWriter
{
    /// <summary>Kênh mà writer này đại diện. Dùng khi caller không nói rõ —
    /// và đó là trường hợp thường gặp, vì service không biết nó đang chạy sau
    /// transport nào.</summary>
    private const string Transport = "Api";

    private const int MaxDetailLength = 4096;

    private readonly IMesDbContext _db;
    private readonly IHttpContextAccessor _http;
    private readonly MesRequestContext _obs;
    private readonly ILogger<ApiAuditWriter> _log;

    public ApiAuditWriter(
        IMesDbContext db,
        IHttpContextAccessor http,
        MesRequestContext obs,
        ILogger<ApiAuditWriter> log)
    {
        _db = db;
        _http = http;
        _obs = obs;
        _log = log;
    }

    public async Task EmitAsync(
        string action,
        string actor,
        string actorRole,
        string? targetType = null,
        string? targetId = null,
        string? detail = null,
        string? source = null)
    {
        var row = new AuditLog
        {
            Timestamp = DateTime.UtcNow,
            ActorUsername = string.IsNullOrWhiteSpace(actor) ? "anonymous" : actor,
            ActorRole = actorRole ?? "",
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Detail = TrimDetail(detail),
            IpAddress = ResolveIp(),
            // Writer BIẾT transport của mình; caller thì không. `null` = "cứ
            // dùng transport của anh", và đó là đường đi của gần như mọi lệnh
            // ghi. Truyền tường minh chỉ khi nguồn KHÁC transport (Console,
            // Scheduler).
            Source = string.IsNullOrWhiteSpace(source) ? Transport : source,
        };
        _db.AuditLogs.Add(row);
        await _db.SaveChangesAsync();

        // Observability runs AFTER the row is durable — a metric for a write
        // that then failed would be worse than no metric at all.
        ObserveEnvelope(action, detail);
    }

    /// <summary>
    /// Read <c>wo_no</c> / <c>from_phase</c> / <c>to_phase</c> out of the
    /// canonical audit envelope. Parses the untrimmed detail: 4 KB
    /// truncation can cut a JSON document mid-token.
    /// </summary>
    private void ObserveEnvelope(string action, string? detail)
    {
        if (detail is null) return;
        var trimmed = detail.AsSpan().TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '{') return;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(detail);
        }
        catch (JsonException ex)
        {
            // Not swallowed: an audit detail that is not valid JSON is a
            // contract break worth seeing. It must not fail the mutation
            // that already committed, so it is reported, not thrown.
            _log.LogDebug(ex, "Audit detail for {Action} is not valid JSON; skipping telemetry.", action);
            return;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;

            if (root.TryGetProperty("wo_no", out var woNo) && woNo.ValueKind == JsonValueKind.String)
                _obs.NoteWorkOrder(woNo.GetString());

            var from = ReadString(root, "from_phase");
            var to = ReadString(root, "to_phase");
            if (from is not null && to is not null && !string.Equals(from, to, StringComparison.Ordinal))
            {
                MesTelemetry.WoPhaseTransitions.Add(1,
                    new KeyValuePair<string, object?>("action", action),
                    new KeyValuePair<string, object?>("from_phase", from),
                    new KeyValuePair<string, object?>("to_phase", to));
            }
        }
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    private string? ResolveIp()
    {
        var ctx = _http.HttpContext;
        if (ctx is null) return null;
        var fwd = ctx.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrEmpty(fwd))
            return fwd.Split(',', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
        return ctx.Connection.RemoteIpAddress?.ToString();
    }

    private static string? TrimDetail(string? detail) =>
        detail is null ? null
        : detail.Length <= MaxDetailLength ? detail
        : detail[..MaxDetailLength];
}
