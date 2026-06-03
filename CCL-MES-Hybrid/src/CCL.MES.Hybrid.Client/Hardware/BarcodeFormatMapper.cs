namespace CCL.MES.Hybrid.Client.Hardware;

/// <summary>
/// Maps platform-specific barcode format identifiers to the stable
/// <see cref="CCL.MES.Shared.Hardware.ScanResult.Format"/> strings the
/// rest of the app uses. Lives in the shared client lib so the same
/// mapping table powers both the Catalyst impl (W2 AVFoundation) and
/// the future Windows impl (W3 ZXing) without each impl reinventing
/// the lookup.
///
/// <para>
/// Pure — no AVFoundation / ZXing references — so it can be unit-tested
/// from the cross-platform xUnit project. The Catalyst impl calls
/// <see cref="FromAVRawIdentifier"/> with the
/// <c>AVMetadataObject.Type.ToString()</c> value (Apple ships these as
/// reverse-DNS strings like <c>"org.iso.QRCode"</c>). The Windows impl
/// will call <see cref="FromZxingBarcodeFormat"/> with the ZXing enum
/// name (<c>"QR_CODE"</c>, <c>"CODE_128"</c>, …).
/// </para>
/// </summary>
public static class BarcodeFormatMapper
{
    /// <summary>
    /// The set of canonical Format strings exposed via
    /// <see cref="CCL.MES.Shared.Hardware.ScanResult.Format"/>. Kept
    /// short + ASCII so they sit cleanly in logs, audit rows, and
    /// API payloads.
    /// </summary>
    public const string FormatQR = "QR";
    public const string FormatCode128 = "Code128";
    public const string FormatCode39 = "Code39";
    public const string FormatCode93 = "Code93";
    public const string FormatEAN13 = "EAN13";
    public const string FormatEAN8 = "EAN8";
    public const string FormatUPCA = "UPCA";
    public const string FormatUPCE = "UPCE";
    public const string FormatDataMatrix = "DataMatrix";
    public const string FormatPDF417 = "PDF417";
    public const string FormatAztec = "Aztec";
    public const string FormatITF = "ITF";
    public const string FormatUnknown = "Unknown";

    /// <summary>
    /// Maps an AVFoundation metadata-object type identifier (the raw
    /// reverse-DNS string Apple ships on every
    /// <c>AVMetadataObject.Type</c>) to a canonical format string.
    /// Unknown identifiers map to <see cref="FormatUnknown"/> — the
    /// raw payload is still returned to the caller, just without a
    /// recognised format label.
    /// </summary>
    public static string FromAVRawIdentifier(string? raw) => raw switch
    {
        // Common 2D
        "org.iso.QRCode"     => FormatQR,
        "org.iso.DataMatrix" => FormatDataMatrix,
        "org.iso.PDF417"     => FormatPDF417,
        "org.iso.Aztec"      => FormatAztec,
        // 1D ISO
        "org.iso.Code128"    => FormatCode128,
        "org.iso.Code39"     => FormatCode39,
        // Code39 with mod-43 checksum is a separate type ID but the
        // payload format is identical from the operator's POV.
        "org.iso.Code39Mod43" => FormatCode39,
        "com.intermec.Code93" => FormatCode93,
        // GS1 retail
        "org.gs1.EAN-13"     => FormatEAN13,
        "org.gs1.EAN-8"      => FormatEAN8,
        "org.gs1.UPC-E"      => FormatUPCE,
        // UPC-A is not a separate AVMetadataObjectType — it surfaces
        // as EAN-13 with a leading zero. The Catalyst impl detects
        // the leading-zero case and overrides the format.
        // ITF (industrial 2-of-5)
        "org.ansi.Interleaved2of5" => FormatITF,
        "org.gs1.ITF14"            => FormatITF,
        // Unrecognised
        null or "" => FormatUnknown,
        _          => FormatUnknown,
    };

    /// <summary>
    /// Same job for the Windows-side ZXing.Net BarcodeFormat enum.
    /// Lands in W3; the W2 mapping table is already in place so the
    /// W3 PR doesn't touch shared client code at all.
    /// </summary>
    public static string FromZxingBarcodeFormat(string? raw) => raw switch
    {
        "QR_CODE"          => FormatQR,
        "DATA_MATRIX"      => FormatDataMatrix,
        "PDF_417"          => FormatPDF417,
        "AZTEC"            => FormatAztec,
        "CODE_128"         => FormatCode128,
        "CODE_39"          => FormatCode39,
        "CODE_93"          => FormatCode93,
        "EAN_13"           => FormatEAN13,
        "EAN_8"            => FormatEAN8,
        "UPC_A"            => FormatUPCA,
        "UPC_E"            => FormatUPCE,
        "ITF"              => FormatITF,
        null or ""         => FormatUnknown,
        _                  => FormatUnknown,
    };

    /// <summary>
    /// Some AVFoundation builds report UPC-A barcodes under the EAN-13
    /// type because UPC-A is an EAN-13 with a leading zero. The
    /// Catalyst impl calls this AFTER it has the payload to upgrade
    /// the format label when the leading zero is present.
    /// </summary>
    public static string DisambiguateEAN13UpcA(string format, string payload)
    {
        if (format != FormatEAN13) return format;
        if (string.IsNullOrEmpty(payload)) return format;
        // UPC-A is 12 digits; AVFoundation pads to 13 with leading 0.
        return payload.Length == 13 && payload[0] == '0' ? FormatUPCA : FormatEAN13;
    }
}
