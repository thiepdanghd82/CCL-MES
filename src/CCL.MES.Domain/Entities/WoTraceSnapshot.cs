namespace CCL.MES.Domain.Entities;

/// <summary>
/// Append-only, immutable frozen trace snapshot for one Work Order phase.
/// The whole point is a DEAD copy: at each confirm point (Product/IPQC/FQC/
/// OQC) the literal values are serialised into <see cref="PayloadJson"/> and
/// never read back from the live source again. Editing master data or the
/// source entity afterwards MUST NOT change a stored snapshot.
///
/// Re-freezing (a later re-confirm) appends a NEW row with Version++ — rows
/// are never updated in place. The newest Version per (WoId, Phase) is what
/// Traceability shows. No hard FK to WoMaterial/WoQcCheck/etc. — the snapshot
/// deliberately outlives and de-couples from its source.
/// </summary>
public class WoTraceSnapshot : BaseEntity
{
    public long WoId { get; set; }
    public string WoNo { get; set; } = "";

    /// <summary>Product | Ipqc | Fqc | Oqc (see <c>TracePhase</c> constants).</summary>
    public string Phase { get; set; } = "";

    /// <summary>1-based; a re-freeze bumps this rather than overwriting.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Payload structure version — lets a future renderer stay
    /// forward-compatible when the JSON shape changes.</summary>
    public int SchemaVersion { get; set; } = 1;

    public DateTime FrozenAtUtc { get; set; }
    public string FrozenBy { get; set; } = "";

    /// <summary>sha256 hex of <see cref="PayloadJson"/> — a re-freeze whose
    /// content is byte-identical is a NOOP (idempotent confirm/retry).</summary>
    public string ContentHash { get; set; } = "";

    /// <summary>Self-describing literal snapshot (see Shared <c>TracePayload</c>).
    /// Schemaless TEXT — the generic renderer walks header[]/items[].</summary>
    public string PayloadJson { get; set; } = "";
}
