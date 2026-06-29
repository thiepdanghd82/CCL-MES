namespace CCL.MES.Hybrid.Client.Printing;

/// <summary>
/// Native "print directly" bridge — opens the OS print panel (printer
/// chooser) for a ready-made PDF whose page is already the chosen paper
/// size + orientation (the server spec-sheet PDF). Printing the PDF rather
/// than the live WebView gives a deterministic, full-page-fit result and a
/// real printer chooser — web <c>window.print()</c> is a no-op inside the
/// maccatalyst WKWebView.
///
/// <para>
/// The default <see cref="NoopDocumentPrintService"/> reports
/// <see cref="IsSupported"/> = false so callers fall back to opening the PDF
/// in the system viewer (where the operator can print) on platforms without
/// a native bridge.
/// </para>
/// </summary>
public interface IDocumentPrintService
{
    /// <summary>True when a native print panel is wired on this platform.</summary>
    bool IsSupported { get; }

    /// <summary>
    /// Present the OS print panel (printer chooser) for the PDF at
    /// <paramref name="pdfFilePath"/>. The PDF page already encodes the paper
    /// size + orientation, so it prints at full page size. Returns false when
    /// no native bridge is available. Never throws on the happy path.
    /// </summary>
    Task<bool> PrintPdfFileAsync(string pdfFilePath, string? jobName = null);
}

/// <summary>Fallback for tests / platforms without a native print bridge.</summary>
public sealed class NoopDocumentPrintService : IDocumentPrintService
{
    public bool IsSupported => false;

    public Task<bool> PrintPdfFileAsync(string pdfFilePath, string? jobName = null)
        => Task.FromResult(false);
}
