using CCL.MES.Application.SpecDetail;
using CCL.MES.Application.SpecExport;
using MigraDoc.Rendering;
using PdfSharp.Pdf;

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
        using var pdf = RenderFitted(detail, context, out _);
        using var ms = new MemoryStream();
        pdf.Save(ms, false);
        return ms.ToArray();
    }

    /// <summary>
    /// PR (detail-2page) — AUTO-FIT render. MigraDoc can't report page count
    /// before rendering, so render at the current compact step, read
    /// <see cref="PdfDocument.PageCount"/>, and if it exceeds 2 rebuild ONE
    /// step tighter (smaller body font + denser padding) and re-render — up to
    /// <see cref="SpecPdfDocumentBuilder.DetailLayout.MaxStep"/> times. Returns
    /// the first document at ≤ 2 pages (or the tightest attempt). Exposed for
    /// tests to assert the fitted page count without re-implementing the loop.
    /// </summary>
    public static PdfDocument RenderFitted(
        SpecDetailDto detail, SpecExportContext context, out int usedStep)
    {
        EnsureFontResolverInitialized();

        PdfDocumentRenderer renderer = null!;
        usedStep = 0;
        for (int step = 0; step <= SpecPdfDocumentBuilder.DetailLayout.MaxStep; step++)
        {
            usedStep = step;
            var doc = SpecPdfDocumentBuilder.BuildDetailSheet(detail, context, compactStep: step);
            renderer = new PdfDocumentRenderer { Document = doc };
            renderer.RenderDocument();
            if (renderer.PdfDocument.PageCount <= 2)
                break;
        }
        return renderer.PdfDocument;
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
