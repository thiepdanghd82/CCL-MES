namespace CCL.MES.Domain.Entities;

/// <summary>
/// P10.7a-1.2 — idempotency ledger per
/// <c>docs/P10.7-WO-STATE-CONTRACT.md</c> §6.2. One row per
/// <c>Idempotency-Key</c> the client sent. Same key replayed within
/// TTL returns the stored response; same key with different request
/// body returns 422 + <c>IDEMPOTENCY_REPLAY</c> audit. Concurrent
/// requests with the same key while the original is still
/// in-flight return 409 (not blocked / queued — the second client
/// should retry; production traffic on the same WO with the same
/// key in flight is a UI bug, not a normal flow).
///
/// Lifecycle:
///   1. Request arrives with header. Middleware INSERTs row with
///      <see cref="CompletedAtUtc"/> = null, <see cref="ResponseStatus"/> = 0,
///      <see cref="ResponseBody"/> = "". UNIQUE index on KeyValue +
///      ActorId catches concurrent inserts.
///   2. Downstream pipeline executes. Middleware buffers the
///      response stream.
///   3. On <c>OnStarting</c> / before flush, middleware UPDATEs the
///      row with ResponseStatus + ResponseBody + CompletedAtUtc = now.
///   4. Subsequent request with same KeyValue + same ActorId +
///      same BodySha256 within TTL: SELECT row, replay stored
///      response. No re-execute, no audit.
///   5. Different body: 422 + IDEMPOTENCY_REPLAY audit row.
///   6. In-flight collision (row exists, CompletedAtUtc null):
///      409 Conflict (do NOT block — caller MUST retry).
///
/// TTL sweep job is OUT OF SCOPE for 7a-1.2 — landing in 7a-2 along
/// with the audit archival background service. Until then,
/// IdempotencyKeys grows unbounded; non-issue in dev / UAT volume.
/// </summary>
public class IdempotencyKey : BaseEntity
{
    /// <summary>The client-supplied <c>Idempotency-Key</c> header
    /// value. UUID v4 is recommended (36 chars) but any opaque
    /// string ≤ 64 chars is accepted. (KeyValue + ActorId) is the
    /// natural-key unique index — two different actors can use the
    /// same key string without collision.</summary>
    public string KeyValue { get; set; } = "";

    /// <summary>FK to <c>Users.Id</c>. The actor who initiated the
    /// request. Server-derived from the auth context — NEVER trust
    /// a client-supplied actor field. Anonymous requests (rare for
    /// mutations) get <c>0</c>.</summary>
    public long ActorId { get; set; }

    /// <summary>The endpoint path the request hit, e.g.
    /// <c>"/api/v1/work-orders/42/advance"</c>. Captured so the
    /// replay-with-different-body check can require an exact path
    /// match — two different endpoints with the same key but
    /// different bodies is a UI bug, not a collision.</summary>
    public string EndpointPath { get; set; } = "";

    /// <summary>Hex SHA-256 of the canonical request body. Used to
    /// detect the "same key, different body" UI-bug case per
    /// §6.2 step 3. Lowercase hex, 64 chars.</summary>
    public string BodySha256 { get; set; } = "";

    /// <summary>HTTP status code of the stored response. 0 while
    /// the request is in-flight.</summary>
    public int ResponseStatus { get; set; }

    /// <summary>Serialized response body. Empty string while the
    /// request is in-flight. Capped at ~256 KB in the middleware to
    /// prevent runaway storage.</summary>
    public string ResponseBody { get; set; } = "";

    /// <summary>The content-type of the stored response so the
    /// replay can re-emit it without guessing (e.g. application/json,
    /// application/problem+json).</summary>
    public string ResponseContentType { get; set; } = "";

    /// <summary>UTC when the row was first inserted. Used for the
    /// in-flight stale-timeout check (if a row's CompletedAtUtc is
    /// null AND CreatedAtUtc is older than the in-flight grace
    /// window, the original request is presumed dead — replay is
    /// treated as a new request).</summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>UTC when the downstream pipeline finished + the
    /// response was buffered. Null while in-flight.</summary>
    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>UTC after which this row is eligible for the TTL
    /// sweep. Default ttl is 24h per breakdown §2.4; configurable
    /// via appsettings (Idempotency:TtlHours).</summary>
    public DateTime ExpiresAtUtc { get; set; }
}
