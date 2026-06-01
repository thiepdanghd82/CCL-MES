using CCL.MES.Application.SpecDetail;
using CCL.MES.Application.SpecExport;
using MigraDoc.Rendering;

namespace CCL.MES.Infrastructure.SpecExport;

/// <summary>
/// Phase 8 PR #31d — Single-spec detail sheet PDF exporter. Wrap
/// `SpecPdfDocumentBuilder.BuildDetailSheet` + PdfDocumentRenderer.
///
/// Reuse cùng SystemFontResolver từ PR #31c (PdfSpecListExporter); font
/// resolver được register 1 lần static lúc list exporter khởi tạo. Defensive
/// check ensure resolver registered nếu list chưa được dùng trước.
/// </summary>
public class PdfSpecSheetExporter
{
    private static readonly object _initLock = new();
    private static bool _initialized;

    static PdfSpecSheetExporter() => EnsureFontResolverInitialized();

    public string ContentType => "application/pdf";

    /// <summary>
    /// Build PDF bytes cho 1 spec detail. Filename pattern (caller decide):
    /// `SpecSheet_<RefNo>_Rev<RevCode>_<yyyyMMdd>.pdf`.
    /// </summary>
    public byte[] Export(SpecDetailDto detail, SpecExportContext context)
    {
        EnsureFontResolverInitialized();

        var doc = SpecPdfDocumentBuilder.BuildDetailSheet(detail, context);
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
                if (PdfSharp.Fonts.GlobalFontSettings.FontResolver is null)
                    PdfSharp.Fonts.GlobalFontSettings.FontResolver = new SystemFontResolver();
            }
            catch
            {
                // resolver có thể đã set bởi PdfSpecListExporter — bỏ qua silent.
            }
            _initialized = true;
        }
    }
}
