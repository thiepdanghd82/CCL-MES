using CCL.MES.Shared.Hardware;

namespace CCL.MES.Hybrid.Client.Hardware;

/// <summary>
/// Per-platform barcode + QR scanner. Real impls (W2 Catalyst AVFoundation,
/// W3 Windows MediaCapture+ZXing) land later in P10.3; W1 ships only the
/// <see cref="StubBarcodeScannerService"/> which reports
/// <see cref="HardwareAvailability"/> with reason <c>"not_implemented"</c>
/// + <c>"feature_disabled"</c> depending on the flag state.
///
/// <para>
/// Callers ALWAYS probe <see cref="IsAvailableAsync"/> before opening a
/// scan modal. The result is what the UI binds to for grey-out + banner
/// text. Calling <see cref="ScanOnceAsync"/> when IsAvailable is false
/// returns null + emits the same banner — never throws on the happy path.
/// </para>
/// </summary>
public interface IBarcodeScannerService
{
    /// <summary>
    /// Probe whether a scan is possible right now. Cheap call —
    /// caches the result for ~1 s so toggling the /hardware page
    /// doesn't hammer the permission API.
    /// </summary>
    Task<HardwareAvailability> IsAvailableAsync(CancellationToken ct = default);

    /// <summary>
    /// Opens a one-shot scan session. Returns the first decoded
    /// result. Returns null when the operator cancels (closes the
    /// modal) OR when <see cref="IsAvailableAsync"/> just transitioned
    /// to unavailable (e.g. permission revoked between probe + call).
    /// </summary>
    /// <param name="ct">
    /// Cancels the scan session. Caller wires this to the modal's
    /// close button + a per-call timeout. Recommended timeout: 60 s
    /// — long enough for operator to find the barcode, short enough
    /// to free the camera if the modal is forgotten.
    /// </param>
    Task<ScanResult?> ScanOnceAsync(CancellationToken ct = default);

    /// <summary>
    /// Continuous scan stream. For workflows that decode many codes
    /// in sequence without modal-open/close churn — e.g. batch
    /// receiving, IPQC sweep. The caller iterates with await foreach
    /// + cancels via the supplied CT. Out of scope for P10.3 ship;
    /// interface present so W4+ workflows can adopt without re-
    /// versioning the abstraction.
    /// </summary>
    IAsyncEnumerable<ScanResult> ScanStreamAsync(CancellationToken ct);
}
