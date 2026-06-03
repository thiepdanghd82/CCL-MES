namespace CCL.MES.Shared.Hardware;

/// <summary>
/// One successful decode from any scanner source. Sent over the wire
/// to <c>POST /api/v2/devices/{id}/scan-log</c> (online-only per P10.3)
/// and surfaced in the in-process scan modal back to the operator.
///
/// The shape is INTENTIONALLY simple — no decode confidence, no raw
/// frame bytes, no per-segment payload split. Workflow code (WO Accept,
/// Sample Tracking, IPQC) parses <see cref="Code"/> against its own
/// rules. Centralising format expectations here pushes them out of
/// reach of operator-visible error messages.
/// </summary>
/// <param name="Code">
/// Raw payload as decoded from the symbol — no trimming, no
/// normalisation. UTF-8 string. Empty string is a valid decode (some
/// label vendors ship blank QR for non-product reference codes); the
/// workflow rejects, the hardware layer does not.
/// </param>
/// <param name="Format">
/// Standard barcode format name — <c>"QR"</c>, <c>"Code128"</c>,
/// <c>"EAN13"</c>, <c>"DataMatrix"</c>, <c>"Code39"</c>, <c>"PDF417"</c>,
/// or <c>"Unknown"</c>. Matches the AVFoundation
/// AVMetadataObjectType naming scheme (without the
/// <c>AVMetadataObjectTypeQRCode</c> prefix) and the ZXing BarcodeFormat
/// enum exactly so neither implementation needs a translation table.
/// </param>
/// <param name="CapturedAt">
/// Local wall-clock at decode time. Server normalises to UTC on
/// receive. We do NOT trust device clock for audit ordering — only for
/// display alongside the operator's session.
/// </param>
/// <param name="SourceDevice">
/// Free-form identifier of where the scan came from — e.g.
/// <c>"back-camera"</c> on Catalyst, <c>"front-camera"</c> on Win,
/// <c>"USB-HID:VID_05F9-PID_4204"</c> for a Datalogic raw-HID scanner.
/// Null for STUB / wedge-mode scans (wedge keystrokes can't be
/// distinguished from a real keyboard at the OS layer).
/// </param>
public sealed record ScanResult(
    string Code,
    string Format,
    DateTimeOffset CapturedAt,
    string? SourceDevice);
