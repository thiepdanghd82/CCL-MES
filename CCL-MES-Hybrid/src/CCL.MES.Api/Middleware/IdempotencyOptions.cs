namespace CCL.MES.Api.Middleware;

/// <summary>
/// P10.7a-1.2 — strongly-typed options for the idempotency middleware.
/// Bound from <c>appsettings.json</c> section <c>Idempotency</c>.
/// </summary>
public sealed class IdempotencyOptions
{
    /// <summary>Hours after <c>CreatedAtUtc</c> at which an
    /// <c>IdempotencyKey</c> row becomes eligible for TTL sweep.
    /// Default 24h per breakdown §2.4 (matches Ops Control MES-2
    /// convention). The TTL sweep job itself is out of scope for
    /// 7a-1.2 — that lands in 7a-2 along with audit archival.</summary>
    public int TtlHours { get; set; } = 24;

    /// <summary>Maximum bytes of the downstream response that
    /// the middleware will store for replay. Bigger responses are
    /// truncated. Default 256 KB — fits every plausible WO-advance
    /// or admin-action envelope; not big enough for a future
    /// xlsx-export reply (export endpoints SHOULD NOT use
    /// idempotency anyway).</summary>
    public int MaxStoredResponseBytes { get; set; } = 256 * 1024;

    /// <summary>Maximum bytes of the request body the middleware
    /// reads into memory to compute the SHA-256 + replay match.
    /// Bigger bodies fail-closed with 413 Payload Too Large. Default
    /// 256 KB; matches MaxStoredResponseBytes.</summary>
    public int MaxRequestBodyBytes { get; set; } = 256 * 1024;
}
