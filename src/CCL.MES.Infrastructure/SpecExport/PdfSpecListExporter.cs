using System.Runtime.InteropServices;
using CCL.MES.Application;
using CCL.MES.Application.SpecExport;
using MigraDoc.Rendering;
using PdfSharp.Fonts;

namespace CCL.MES.Infrastructure.SpecExport;

/// <summary>
/// Phase 8 PR #31c — PDF list exporter (PdfSharp + MigraDoc 6.2.4, MIT).
///
/// Pipeline:
///   <see cref="ProductRevisionListItem"/> rows
///   → <see cref="SpecPdfDocumentBuilder.BuildListView"/> MigraDoc DOM
///   → <see cref="PdfDocumentRenderer"/>
///   → MemoryStream → byte[]
///
/// Cross-platform: PDFsharp 6+ chạy pure managed code (no GDI / System.Drawing
/// fallback). Font resolution via <see cref="SystemFontResolver"/> tìm
/// Arial/DejaVu Sans theo platform-specific path. Linux deploy requires
/// `fonts-dejavu-core` apt package (~5MB, common preinstalled trên Debian/
/// Ubuntu/RHEL). Document trong LESSONS_LEARNED.
/// </summary>
public class PdfSpecListExporter : ISpecListExporter
{
    public string Format => "pdf";
    public string ContentType => "application/pdf";
    public string FileExtension => "pdf";

    private static readonly object _initLock = new();
    private static bool _initialized;

    static PdfSpecListExporter() => EnsureFontResolverInitialized();

    public byte[] Export(IReadOnlyList<ProductRevisionListItem> rows, SpecExportContext context)
    {
        EnsureFontResolverInitialized();

        var doc = SpecPdfDocumentBuilder.BuildListView(rows, context);
        var renderer = new PdfDocumentRenderer { Document = doc };
        renderer.RenderDocument();

        using var ms = new MemoryStream();
        renderer.PdfDocument.Save(ms, false);
        return ms.ToArray();
    }

    private static void EnsureFontResolverInitialized()
    {
        if (_initialized) return;
        lock (_initLock)
        {
            if (_initialized) return;
            try
            {
                if (GlobalFontSettings.FontResolver is null)
                    GlobalFontSettings.FontResolver = new SystemFontResolver();
            }
            catch
            {
                // FontResolver có thể đã set bởi callsite khác — bỏ qua silent.
            }
            _initialized = true;
        }
    }
}

/// <summary>
/// Phase 8 PR #31c — Cross-platform system font resolver. Tìm font TTF
/// theo platform-specific path; cache bytes trong dictionary để không re-IO
/// per request. Throws khi font missing với message gợi ý deploy step (apt
/// install fonts-dejavu-core / Windows font default / macOS default).
///
/// Reusable cho PR #33 detail sheet PDF (cùng GlobalFontSettings instance).
/// </summary>
public class SystemFontResolver : IFontResolver
{
    private static readonly Dictionary<string, byte[]> _cache = new(StringComparer.Ordinal);
    private static readonly object _cacheLock = new();

    public byte[]? GetFont(string faceName)
    {
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(faceName, out var cached)) return cached;

            var path = FindSystemFontPath(faceName);
            if (path is null || !File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"Font face '{faceName}' không tìm thấy trên hệ thống. " +
                    $"Linux deploy: cài `apt install fonts-dejavu-core` (~5MB). " +
                    $"macOS dev/prod: Arial.ttf + variants thường có sẵn. " +
                    $"Windows: Arial.ttf nằm trong %WINDIR%\\Fonts\\.");
            }
            var bytes = File.ReadAllBytes(path);
            _cache[faceName] = bytes;
            return bytes;
        }
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        // Map family request → face identifier dùng cho GetFont lookup.
        var normalized = (familyName ?? "").ToLowerInvariant();
        string face = normalized switch
        {
            "arial" or "helvetica" or "sans-serif" or "" => GetSansFace(isBold, isItalic),
            "courier" or "courier new" or "monospace" => GetMonoFace(isBold, isItalic),
            "times" or "times new roman" or "serif" => GetSerifFace(isBold, isItalic),
            _ => GetSansFace(isBold, isItalic),  // fallback sans-serif
        };
        return new FontResolverInfo(face);
    }

    private static string GetSansFace(bool bold, bool italic) => (bold, italic) switch
    {
        (true, true)  => "sans-bold-italic",
        (true, false) => "sans-bold",
        (false, true) => "sans-italic",
        _             => "sans-regular",
    };

    private static string GetMonoFace(bool bold, bool italic) => (bold, italic) switch
    {
        (true, true)  => "mono-bold-italic",
        (true, false) => "mono-bold",
        (false, true) => "mono-italic",
        _             => "mono-regular",
    };

    private static string GetSerifFace(bool bold, bool italic) => (bold, italic) switch
    {
        (true, true)  => "serif-bold-italic",
        (true, false) => "serif-bold",
        (false, true) => "serif-italic",
        _             => "serif-regular",
    };

    /// <summary>
    /// Tìm TTF path theo platform. Trả null nếu không tìm thấy.
    /// </summary>
    private static string? FindSystemFontPath(string face)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return MacOsFontPath(face);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return LinuxFontPath(face);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return WindowsFontPath(face);
        return null;
    }

    private static string? MacOsFontPath(string face) => face switch
    {
        "sans-regular"     => "/System/Library/Fonts/Supplemental/Arial.ttf",
        "sans-bold"        => "/System/Library/Fonts/Supplemental/Arial Bold.ttf",
        "sans-italic"      => "/System/Library/Fonts/Supplemental/Arial Italic.ttf",
        "sans-bold-italic" => "/System/Library/Fonts/Supplemental/Arial Bold Italic.ttf",
        "mono-regular"     => "/System/Library/Fonts/Supplemental/Courier New.ttf",
        "mono-bold"        => "/System/Library/Fonts/Supplemental/Courier New Bold.ttf",
        "mono-italic"      => "/System/Library/Fonts/Supplemental/Courier New Italic.ttf",
        "mono-bold-italic" => "/System/Library/Fonts/Supplemental/Courier New Bold Italic.ttf",
        "serif-regular"     => "/System/Library/Fonts/Supplemental/Times New Roman.ttf",
        "serif-bold"        => "/System/Library/Fonts/Supplemental/Times New Roman Bold.ttf",
        "serif-italic"      => "/System/Library/Fonts/Supplemental/Times New Roman Italic.ttf",
        "serif-bold-italic" => "/System/Library/Fonts/Supplemental/Times New Roman Bold Italic.ttf",
        _ => null,
    };

    private static string? LinuxFontPath(string face)
    {
        // DejaVu Sans family (fonts-dejavu-core package) — preinstalled trên
        // hầu hết Debian/Ubuntu/RHEL. Fallback Liberation Sans / Noto Sans.
        var dejaVuRoot = "/usr/share/fonts/truetype/dejavu";
        return face switch
        {
            "sans-regular"     => $"{dejaVuRoot}/DejaVuSans.ttf",
            "sans-bold"        => $"{dejaVuRoot}/DejaVuSans-Bold.ttf",
            "sans-italic"      => $"{dejaVuRoot}/DejaVuSans-Oblique.ttf",
            "sans-bold-italic" => $"{dejaVuRoot}/DejaVuSans-BoldOblique.ttf",
            "mono-regular"     => $"{dejaVuRoot}/DejaVuSansMono.ttf",
            "mono-bold"        => $"{dejaVuRoot}/DejaVuSansMono-Bold.ttf",
            "mono-italic"      => $"{dejaVuRoot}/DejaVuSansMono-Oblique.ttf",
            "mono-bold-italic" => $"{dejaVuRoot}/DejaVuSansMono-BoldOblique.ttf",
            "serif-regular"     => $"{dejaVuRoot}/DejaVuSerif.ttf",
            "serif-bold"        => $"{dejaVuRoot}/DejaVuSerif-Bold.ttf",
            "serif-italic"      => $"{dejaVuRoot}/DejaVuSerif-Italic.ttf",
            "serif-bold-italic" => $"{dejaVuRoot}/DejaVuSerif-BoldItalic.ttf",
            _ => null,
        };
    }

    private static string? WindowsFontPath(string face)
    {
        var fontsDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        return face switch
        {
            "sans-regular"     => Path.Combine(fontsDir, "arial.ttf"),
            "sans-bold"        => Path.Combine(fontsDir, "arialbd.ttf"),
            "sans-italic"      => Path.Combine(fontsDir, "ariali.ttf"),
            "sans-bold-italic" => Path.Combine(fontsDir, "arialbi.ttf"),
            "mono-regular"     => Path.Combine(fontsDir, "cour.ttf"),
            "mono-bold"        => Path.Combine(fontsDir, "courbd.ttf"),
            "mono-italic"      => Path.Combine(fontsDir, "couri.ttf"),
            "mono-bold-italic" => Path.Combine(fontsDir, "courbi.ttf"),
            "serif-regular"     => Path.Combine(fontsDir, "times.ttf"),
            "serif-bold"        => Path.Combine(fontsDir, "timesbd.ttf"),
            "serif-italic"      => Path.Combine(fontsDir, "timesi.ttf"),
            "serif-bold-italic" => Path.Combine(fontsDir, "timesbi.ttf"),
            _ => null,
        };
    }
}
