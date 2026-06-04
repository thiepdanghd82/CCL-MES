namespace CCL.MES.Shared.RecentScans;

/// <summary>
/// P10.6f — one row in the operator's "Quét gần đây" sidebar widget.
/// Mirrors SpecHub §11's <c>RECENT_SCANS</c> localStorage shape, ported
/// to MAUI Preferences. Each successful scan emits one entry; the
/// widget renders the newest <c>N</c> (default 5) and a click re-opens
/// the resolved route so the operator can keep working without
/// re-scanning a barcode.
///
/// Persistence keying choice: <see cref="Code"/> + <see cref="Context"/>
/// is the dedupe key — the same WO code captured a minute apart should
/// collapse to a single row whose <see cref="ScannedAt"/> is the latest
/// touch, NOT pile up as 4 rows that crowd the widget. Operators
/// frequently re-scan the same WO to re-confirm a screen.
///
/// Resolved fields are optional because the audit-log POST in
/// <c>WorkOrders.razor</c> fires BEFORE the lookup returns. The widget
/// renders entries with <see cref="ResolvedDisplay"/> non-null with a
/// rich title; entries that never resolved (network drop, 404) fall
/// back to <see cref="Code"/> so the row is still useful as a re-scan
/// trigger.
/// </summary>
public sealed record RecentScanEntry
{
    /// <summary>The raw scanned payload (WO number, spec id, etc.).
    /// Trimmed of leading/trailing whitespace — barcodes sometimes
    /// carry stray spaces from print rendering.</summary>
    public string Code { get; init; } = "";

    /// <summary>Symbology reported by the scanner (e.g. "Code128",
    /// "QRCode"). Empty when the scanner doesn't classify.</summary>
    public string Format { get; init; } = "";

    /// <summary>Workflow that captured the scan. Matches the
    /// <c>ScanLogRequest.Context</c> values already in use
    /// (e.g. "wo-accept"). Used both as the dedupe key half + the
    /// route hint when re-opening.</summary>
    public string Context { get; init; } = "";

    /// <summary>UTC timestamp of when the scan landed on the
    /// MAUI side. Stored UTC; the widget renders the locale-aware
    /// "x phút trước" relative label.</summary>
    public DateTime ScannedAt { get; init; }

    /// <summary>Human-readable display the widget renders. Set after
    /// the workflow's lookup succeeds — e.g. "WO-2026-00123" plus a
    /// short product name. Falls back to <see cref="Code"/> when
    /// null so unresolved scans are still tappable.</summary>
    public string? ResolvedDisplay { get; init; }

    /// <summary>Optional secondary line (customer name, step badge,
    /// etc.). Rendered smaller below <see cref="ResolvedDisplay"/>.
    /// </summary>
    public string? ResolvedSubtitle { get; init; }

    /// <summary>True when the scan resolved to a real entity. False
    /// (default) when the scan was logged but the lookup failed
    /// (404, network error). The widget shows a warning chip on
    /// unresolved rows so the operator knows tapping just re-opens
    /// the scan surface, not a known WO.</summary>
    public bool Resolved { get; init; }
}
