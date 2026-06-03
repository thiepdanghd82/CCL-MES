using CCL.MES.Hybrid.Client.Hardware;

namespace CCL.MES.Hybrid.Client.Tests.Hardware;

public sealed class BarcodeFormatMapperTests
{
    // ── AVFoundation reverse-DNS ────────────────────────────────────

    [Theory]
    [InlineData("org.iso.QRCode",       BarcodeFormatMapper.FormatQR)]
    [InlineData("org.iso.DataMatrix",   BarcodeFormatMapper.FormatDataMatrix)]
    [InlineData("org.iso.PDF417",       BarcodeFormatMapper.FormatPDF417)]
    [InlineData("org.iso.Aztec",        BarcodeFormatMapper.FormatAztec)]
    [InlineData("org.iso.Code128",      BarcodeFormatMapper.FormatCode128)]
    [InlineData("org.iso.Code39",       BarcodeFormatMapper.FormatCode39)]
    [InlineData("org.iso.Code39Mod43",  BarcodeFormatMapper.FormatCode39)]
    [InlineData("com.intermec.Code93",  BarcodeFormatMapper.FormatCode93)]
    [InlineData("org.gs1.EAN-13",       BarcodeFormatMapper.FormatEAN13)]
    [InlineData("org.gs1.EAN-8",        BarcodeFormatMapper.FormatEAN8)]
    [InlineData("org.gs1.UPC-E",        BarcodeFormatMapper.FormatUPCE)]
    [InlineData("org.ansi.Interleaved2of5", BarcodeFormatMapper.FormatITF)]
    [InlineData("org.gs1.ITF14",        BarcodeFormatMapper.FormatITF)]
    public void AV_known_identifiers_map_to_canonical_format(string raw, string expected)
        => Assert.Equal(expected, BarcodeFormatMapper.FromAVRawIdentifier(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("org.iso.NotARealFormat")]
    [InlineData("nonsense")]
    public void AV_unknown_identifiers_map_to_Unknown(string? raw)
        => Assert.Equal(BarcodeFormatMapper.FormatUnknown,
            BarcodeFormatMapper.FromAVRawIdentifier(raw));

    // ── ZXing.Net BarcodeFormat enum names (W3 use) ──────────────────

    [Theory]
    [InlineData("QR_CODE",      BarcodeFormatMapper.FormatQR)]
    [InlineData("DATA_MATRIX",  BarcodeFormatMapper.FormatDataMatrix)]
    [InlineData("PDF_417",      BarcodeFormatMapper.FormatPDF417)]
    [InlineData("AZTEC",        BarcodeFormatMapper.FormatAztec)]
    [InlineData("CODE_128",     BarcodeFormatMapper.FormatCode128)]
    [InlineData("CODE_39",      BarcodeFormatMapper.FormatCode39)]
    [InlineData("CODE_93",      BarcodeFormatMapper.FormatCode93)]
    [InlineData("EAN_13",       BarcodeFormatMapper.FormatEAN13)]
    [InlineData("EAN_8",        BarcodeFormatMapper.FormatEAN8)]
    [InlineData("UPC_A",        BarcodeFormatMapper.FormatUPCA)]
    [InlineData("UPC_E",        BarcodeFormatMapper.FormatUPCE)]
    [InlineData("ITF",          BarcodeFormatMapper.FormatITF)]
    public void ZXing_known_formats_map_to_canonical_format(string raw, string expected)
        => Assert.Equal(expected, BarcodeFormatMapper.FromZxingBarcodeFormat(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("CODABAR")] // Real ZXing format but not in our table
    public void ZXing_unknown_formats_map_to_Unknown(string? raw)
        => Assert.Equal(BarcodeFormatMapper.FormatUnknown,
            BarcodeFormatMapper.FromZxingBarcodeFormat(raw));

    // ── EAN13 → UPCA disambiguation ──────────────────────────────────

    [Fact]
    public void Disambiguate_13_digits_leading_zero_becomes_UPCA()
    {
        var fmt = BarcodeFormatMapper.DisambiguateEAN13UpcA(
            BarcodeFormatMapper.FormatEAN13, "0123456789012");
        Assert.Equal(BarcodeFormatMapper.FormatUPCA, fmt);
    }

    [Fact]
    public void Disambiguate_13_digits_non_zero_lead_stays_EAN13()
    {
        var fmt = BarcodeFormatMapper.DisambiguateEAN13UpcA(
            BarcodeFormatMapper.FormatEAN13, "8934567890123");
        Assert.Equal(BarcodeFormatMapper.FormatEAN13, fmt);
    }

    [Fact]
    public void Disambiguate_only_applies_to_EAN13_format()
    {
        var fmt = BarcodeFormatMapper.DisambiguateEAN13UpcA(
            BarcodeFormatMapper.FormatQR, "0123456789012");
        Assert.Equal(BarcodeFormatMapper.FormatQR, fmt);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Disambiguate_empty_payload_is_safe(string? payload)
    {
        var fmt = BarcodeFormatMapper.DisambiguateEAN13UpcA(
            BarcodeFormatMapper.FormatEAN13, payload!);
        Assert.Equal(BarcodeFormatMapper.FormatEAN13, fmt);
    }

    [Fact]
    public void Disambiguate_wrong_length_payload_stays_EAN13()
    {
        var fmt = BarcodeFormatMapper.DisambiguateEAN13UpcA(
            BarcodeFormatMapper.FormatEAN13, "012345"); // too short
        Assert.Equal(BarcodeFormatMapper.FormatEAN13, fmt);
    }
}
