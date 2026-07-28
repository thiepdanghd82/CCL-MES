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

    /// <summary>Upper bound (cm) for the per-row vertical-fill search — a row
    /// never grows more than this even on a nearly-empty sheet, so a 1-row
    /// table doesn't balloon into one giant band.</summary>
    private const double MaxRowFillCm = 1.4;

    /// <summary>
    /// PR (detail-2page) — AUTO-FIT render. MigraDoc can't report page count
    /// before rendering, so render at the current compact step, read
    /// <see cref="PdfDocument.PageCount"/>, and if it exceeds 2 rebuild ONE
    /// step tighter (smaller body font + denser padding) and re-render — up to
    /// <see cref="SpecPdfDocumentBuilder.DetailLayout.MaxStep"/> times. Returns
    /// the first document at ≤ 2 pages (or the tightest attempt).
    /// </summary>
    public static PdfDocument RenderFitted(
        SpecDetailDto detail, SpecExportContext context, out int usedStep)
        => RenderFitted(detail, context, out usedStep, out _);

    /// <summary>
    /// As <see cref="RenderFitted(SpecDetailDto, SpecExportContext, out int)"/>,
    /// plus a SECOND phase — vertical AUTO-FILL. After fitting to ≤ 2 pages we
    /// binary-search <c>rowFillCm</c> (extra height on every Print-Process +
    /// Revision row) for the LARGEST value that keeps the same page count, so
    /// the sheet grows down to ~1cm off the bottom margin instead of leaving a
    /// big empty band. Bounded by <see cref="MaxRowFillCm"/> so a sparse sheet
    /// doesn't over-stretch. <paramref name="usedRowFillCm"/> exposes the
    /// chosen fill for tests.
    /// </summary>
    public static PdfDocument RenderFitted(
        SpecDetailDto detail, SpecExportContext context,
        out int usedStep, out double usedRowFillCm)
    {
        EnsureFontResolverInitialized();

        // ONE bounded mechanism (no competing loops). Rows sit at their NATURAL
        // height (rowFillCm = 0) — no forced tall minimum — so the FIT phase
        // measures the true content page count and never spills onto a blank
        // extra page.

        // ── Phase 1: FIT — smallest font tier whose natural render hits the
        // TARGET page count (prefer 1). A genuinely long spec that can't reach
        // 1 even at the smallest font settles at its minimum (2+). ───────────
        const int target = 1;
        PdfDocument fitted = null!;
        int fittedPages = int.MaxValue;
        usedStep = 0;
        for (int step = 0; step <= SpecPdfDocumentBuilder.DetailLayout.MaxStep; step++)
        {
            usedStep = step;
            fitted = Render(detail, context, step, rowFillCm: 0);
            fittedPages = fitted.PageCount;
            if (fittedPages <= target) break;   // fit the target → stop shrinking
        }

        // ── Phase 2: FILL — grow row height (Print Process + Revision) to the
        // LARGEST value that keeps the SAME page count, so content ends near
        // the bottom margin without ever adding a page. Bounded by
        // MaxRowFillCm so a sparse sheet doesn't balloon one giant row. ──────
        double lo = 0, hi = MaxRowFillCm;
        PdfDocument best = fitted;
        usedRowFillCm = 0;
        for (int iter = 0; iter < 8; iter++)
        {
            double mid = (lo + hi) / 2;
            var probe = Render(detail, context, usedStep, mid);
            if (probe.PageCount <= fittedPages)
            {
                lo = mid; best = probe; usedRowFillCm = mid; // still fits → keep, try taller
            }
            else
            {
                hi = mid;                                    // spilled → back off
            }
        }
        return best;
    }

    private static PdfDocument Render(
        SpecDetailDto detail, SpecExportContext context, int step, double rowFillCm)
    {
        var doc = SpecPdfDocumentBuilder.BuildDetailSheet(
            detail, context, compactStep: step, rowFillCm: rowFillCm);
        var renderer = new PdfDocumentRenderer { Document = doc };
        renderer.RenderDocument();
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
