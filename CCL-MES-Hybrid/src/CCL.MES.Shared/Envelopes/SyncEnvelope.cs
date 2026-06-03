namespace CCL.MES.Shared.Envelopes;

/// <summary>
/// Reserved envelope for the P10.4 offline-sync engine. Carries one outbox
/// operation between MAUI client and API. Defined here so the wire shape
/// is fixed early — clients can compile against it from P10.1 even though
/// no endpoint accepts it until P10.4 lands.
///
/// Henry's Q4 lock (2026-06-03): Phase 10 ships ONLY append-only
/// operations (<c>EntityType</c> ∈ {<c>ProductionLog</c>, <c>QcCapture</c>,
/// <c>Scan</c>, <c>OeeEvent</c>}). Stateful entities (WorkOrder advance,
/// Spec approve, master data edits) MUST go through their normal REST
/// endpoint and wait for reconnect — never through SyncEnvelope.
/// </summary>
public sealed record SyncEnvelope<T>
{
    /// <summary>Client-generated GUID v7 (time-sortable). Becomes the
    /// server-side <c>Idempotency-Key</c> header. Same value across retries.</summary>
    public Guid OpId { get; init; }

    /// <summary>Discriminator for the payload type. Server routes to the
    /// matching append handler. Allowed values reserved for P10.4:
    /// <c>"ProductionLog"</c>, <c>"QcCapture"</c>, <c>"Scan"</c>,
    /// <c>"OeeEvent"</c>.</summary>
    public string EntityType { get; init; } = "";

    /// <summary>UTC instant the client created the operation. NOT the
    /// instant the server applied it — server stamps its own
    /// <c>applied_at</c> in the idempotency ledger.</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>The append-only payload itself. Shape per
    /// <c>EntityType</c>.</summary>
    public T Payload { get; init; } = default!;
}
