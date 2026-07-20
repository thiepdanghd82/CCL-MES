namespace CCL.MES.Shared.Quality;

/// <summary>Phase tokens stored in <c>WoTraceSnapshot.Phase</c> + payload.</summary>
public static class TracePhase
{
    public const string Product = "Product";
    public const string Ipqc = "Ipqc";
    public const string Fqc = "Fqc";
    public const string Oqc = "Oqc";

    public static readonly string[] All = { Product, Ipqc, Fqc, Oqc };
}

// ── Self-describing frozen payload (stored verbatim in PayloadJson) ──────
//
// A generic renderer walks Header (key-value) + Items (dynamic rows) so the
// SAME UI renders all 4 phases and any product variant with zero per-phase
// hardcoding. Only items actually inspected for that WO/variant appear.

public sealed record TraceKv
{
    public string Label { get; init; } = "";
    public string? Value { get; init; }
}

public sealed record TraceItem
{
    public string Key { get; init; } = "";
    public string Label { get; init; } = "";
    public string? Status { get; init; }      // Ok | Ng | Pending | null
    public string? NgReason { get; init; }
    public string? NgNote { get; init; }
    /// <summary>Free-form extension bag for variant-specific fields —
    /// grows without a schema change. NEVER contains image bytes/paths;
    /// at most a "photoCount" string.</summary>
    public Dictionary<string, string?>? Extra { get; init; }
}

public sealed record TracePayload
{
    public string Phase { get; init; } = "";
    public string WoNo { get; init; } = "";
    public string? Variant { get; init; }
    public DateTime FrozenAtUtc { get; init; }
    public string FrozenBy { get; init; } = "";
    public List<TraceKv> Header { get; init; } = new();
    public List<TraceItem> Items { get; init; } = new();
}

// ── Read models (merged detail + list) ──────────────────────────────────

/// <summary>One frozen phase as served to the detail dialog: the payload
/// plus its row metadata. Null in the merged detail = not yet frozen.</summary>
public sealed record TracePhaseDto
{
    public int Version { get; init; }
    public int SchemaVersion { get; init; }
    public DateTime FrozenAtUtc { get; init; }
    public string FrozenBy { get; init; } = "";
    public TracePayload Payload { get; init; } = new();
}

/// <summary>Merged read for the detail dialog — newest version of each of the
/// 4 phases. A null phase renders an empty-state tab (never a live fallback).</summary>
public sealed record TraceabilityDetailDto
{
    public string WoNo { get; init; } = "";
    public string ProductName { get; init; } = "";
    public TracePhaseDto? Product { get; init; }
    public TracePhaseDto? Ipqc { get; init; }
    public TracePhaseDto? Fqc { get; init; }
    public TracePhaseDto? Oqc { get; init; }
}

/// <summary>One row of the real-time Traceability list — projected from the
/// MUTABLE WoTraceIndex. A WO appears here the moment it is scanned/found,
/// even before any phase is frozen (FrozenPhases empty).</summary>
public sealed record TraceListRow
{
    public long WoId { get; init; }
    public string WoNo { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string? ProductCode { get; init; }
    public string? Customer { get; init; }
    public string CurrentMesPhase { get; init; } = "";
    public DateTime LastScannedAtUtc { get; init; }
    public DateTime? LatestFrozenAtUtc { get; init; }
    /// <summary>Which of the 4 phases have ≥1 frozen snapshot (for chips).</summary>
    public List<string> FrozenPhases { get; init; } = new();
}
