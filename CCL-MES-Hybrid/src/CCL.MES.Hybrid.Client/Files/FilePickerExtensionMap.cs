namespace CCL.MES.Hybrid.Client.Files;

/// <summary>
/// P10.5e-1 — Pure helper that maps a lowercase extension (no dot) to
/// the platform-specific picker type ID. Catalyst + iOS use UTType
/// uniform identifiers; WinUI takes the extension with a leading dot;
/// Android takes the MIME type. Lives in the platform-agnostic client
/// lib so xUnit can pin the mapping table without spinning up a MAUI
/// host.
///
/// Drawing allowlist (mirrors <c>BlobStoreOptions.AllowedExtensions</c>):
/// pdf / png / jpg / jpeg / svg / gif / webp / dwg / dxf / ai.
/// </summary>
public static class FilePickerExtensionMap
{
    /// <summary>The drawing allowlist canonicalised + de-duped. Caller
    /// passes the operator-visible list as-is; this is the shape the
    /// MAUI picker map keys off.</summary>
    public static readonly IReadOnlyList<string> DrawingAllowlist = new[]
    {
        "pdf", "png", "jpg", "jpeg", "svg", "gif", "webp", "dwg", "dxf", "ai",
    };

    /// <summary>UTType id for Catalyst / iOS pickers. Returns
    /// <c>"public.data"</c> for unknown extensions so the picker stays
    /// open but doesn't crash.</summary>
    public static string ToCatalystUtType(string extension) => Normalize(extension) switch
    {
        "pdf"  => "com.adobe.pdf",
        "png"  => "public.png",
        "jpg" or "jpeg" => "public.jpeg",
        "svg"  => "public.svg-image",
        "gif"  => "com.compuserve.gif",
        "webp" => "org.webmproject.webp",
        "dwg"  => "com.autodesk.dwg",
        "dxf"  => "com.autodesk.dxf",
        "ai"   => "com.adobe.illustrator.ai-image",
        _      => "public.data",
    };

    /// <summary>WinUI passes extensions with the leading dot. Returns
    /// <c>".*"</c> for unknown so the picker stays open.</summary>
    public static string ToWindowsExtension(string extension)
    {
        var ext = Normalize(extension);
        return string.IsNullOrEmpty(ext) ? ".*" : "." + ext;
    }

    /// <summary>Android MIME type. Returns <c>"application/octet-stream"</c>
    /// for unknown extensions.</summary>
    public static string ToAndroidMime(string extension) => Normalize(extension) switch
    {
        "pdf"  => "application/pdf",
        "png"  => "image/png",
        "jpg" or "jpeg" => "image/jpeg",
        "svg"  => "image/svg+xml",
        "gif"  => "image/gif",
        "webp" => "image/webp",
        "dwg"  => "image/vnd.dwg",
        "dxf"  => "image/vnd.dxf",
        "ai"   => "application/postscript",
        _      => "application/octet-stream",
    };

    /// <summary>Normalise a caller-supplied extension to lowercase, no
    /// leading dot, trimmed.</summary>
    public static string Normalize(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return "";
        var e = extension.Trim().ToLowerInvariant();
        return e.StartsWith('.') ? e[1..] : e;
    }

    /// <summary>Map a whole allowlist to Catalyst UTType ids, preserving
    /// order and de-duplicating. Useful when constructing the
    /// FilePickerFileType dictionary at the MAUI bridge.</summary>
    public static IReadOnlyList<string> MapCatalyst(IReadOnlyList<string> allowedExtensions) =>
        MapDistinct(allowedExtensions, ToCatalystUtType);

    public static IReadOnlyList<string> MapWindows(IReadOnlyList<string> allowedExtensions) =>
        MapDistinct(allowedExtensions, ToWindowsExtension);

    public static IReadOnlyList<string> MapAndroid(IReadOnlyList<string> allowedExtensions) =>
        MapDistinct(allowedExtensions, ToAndroidMime);

    private static IReadOnlyList<string> MapDistinct(
        IReadOnlyList<string> source, Func<string, string> map)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>(source.Count);
        foreach (var raw in source)
        {
            var ext = Normalize(raw);
            if (string.IsNullOrEmpty(ext)) continue;
            var mapped = map(ext);
            if (seen.Add(mapped)) result.Add(mapped);
        }
        return result;
    }
}
