using CCL.MES.Hybrid.Client.Files;

namespace CCL.MES.Hybrid.Client.Tests.Files;

/// <summary>
/// P10.5e-1 — Pure-helper coverage for the extension → platform type
/// map. Pinned so the Catalyst / WinUI / Android picker bridges can
/// rely on the canonical UTType / extension / MIME triples without
/// re-deriving them.
/// </summary>
public sealed class FilePickerExtensionMapTests
{
    [Fact]
    public void DrawingAllowlist_matches_blob_store_default()
    {
        Assert.Equal(
            new[] { "pdf", "png", "jpg", "jpeg", "svg", "gif", "webp", "dwg", "dxf", "ai" },
            FilePickerExtensionMap.DrawingAllowlist);
    }

    [Theory]
    [InlineData("pdf",  "com.adobe.pdf")]
    [InlineData("png",  "public.png")]
    [InlineData("jpg",  "public.jpeg")]
    [InlineData("jpeg", "public.jpeg")]
    [InlineData("svg",  "public.svg-image")]
    [InlineData("gif",  "com.compuserve.gif")]
    [InlineData("webp", "org.webmproject.webp")]
    [InlineData("dwg",  "com.autodesk.dwg")]
    [InlineData("dxf",  "com.autodesk.dxf")]
    [InlineData("ai",   "com.adobe.illustrator.ai-image")]
    public void ToCatalystUtType_canonical_mapping(string ext, string expected)
    {
        Assert.Equal(expected, FilePickerExtensionMap.ToCatalystUtType(ext));
    }

    [Theory]
    [InlineData("PDF", "com.adobe.pdf")]
    [InlineData(".pdf", "com.adobe.pdf")]
    [InlineData("  Jpg  ", "public.jpeg")]
    public void ToCatalystUtType_is_normalization_tolerant(string ext, string expected)
    {
        Assert.Equal(expected, FilePickerExtensionMap.ToCatalystUtType(ext));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("")]
    [InlineData(null)]
    public void ToCatalystUtType_unknown_falls_back_to_public_data(string? ext)
    {
        Assert.Equal("public.data", FilePickerExtensionMap.ToCatalystUtType(ext!));
    }

    [Theory]
    [InlineData("pdf",  ".pdf")]
    [InlineData("png",  ".png")]
    [InlineData(".PNG", ".png")]
    [InlineData("",     ".*")]
    [InlineData(null,   ".*")]
    public void ToWindowsExtension_adds_leading_dot(string? ext, string expected)
    {
        Assert.Equal(expected, FilePickerExtensionMap.ToWindowsExtension(ext!));
    }

    [Theory]
    [InlineData("pdf",  "application/pdf")]
    [InlineData("png",  "image/png")]
    [InlineData("jpeg", "image/jpeg")]
    [InlineData("svg",  "image/svg+xml")]
    [InlineData("dwg",  "image/vnd.dwg")]
    [InlineData("dxf",  "image/vnd.dxf")]
    [InlineData("ai",   "application/postscript")]
    [InlineData("xx",   "application/octet-stream")]
    public void ToAndroidMime_canonical_mapping(string ext, string expected)
    {
        Assert.Equal(expected, FilePickerExtensionMap.ToAndroidMime(ext));
    }

    [Theory]
    [InlineData("pdf",  "pdf")]
    [InlineData(".PDF", "pdf")]
    [InlineData("  .PnG ", "png")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Normalize_strips_dot_and_lowercases(string? raw, string expected)
    {
        Assert.Equal(expected, FilePickerExtensionMap.Normalize(raw));
    }

    [Fact]
    public void MapCatalyst_deduplicates_jpg_and_jpeg_into_one_utype()
    {
        var input = new[] { "pdf", "jpg", "jpeg", "png" };
        var mapped = FilePickerExtensionMap.MapCatalyst(input);
        Assert.Equal(new[] { "com.adobe.pdf", "public.jpeg", "public.png" }, mapped);
    }

    [Fact]
    public void MapWindows_preserves_each_extension_distinctly()
    {
        var input = new[] { "pdf", "jpg", "jpeg" };
        var mapped = FilePickerExtensionMap.MapWindows(input);
        Assert.Equal(new[] { ".pdf", ".jpg", ".jpeg" }, mapped);
    }

    [Fact]
    public void Map_with_drawing_allowlist_produces_10_catalyst_types_collapsed_to_9()
    {
        // jpg + jpeg collapse to a single UTType so the operator-visible
        // 10-entry list reduces to 9 distinct picker types.
        var mapped = FilePickerExtensionMap.MapCatalyst(FilePickerExtensionMap.DrawingAllowlist);
        Assert.Equal(9, mapped.Count);
        Assert.Contains("com.adobe.pdf", mapped);
        Assert.Contains("public.jpeg", mapped);
        Assert.DoesNotContain("public.jpg", mapped); // not a real UTType
    }

    [Fact]
    public void Map_skips_blank_entries()
    {
        var input = new[] { "pdf", "", "  ", "png", null! };
        var mapped = FilePickerExtensionMap.MapCatalyst(input);
        Assert.Equal(new[] { "com.adobe.pdf", "public.png" }, mapped);
    }
}
