using CCL.MES.Hybrid.Client.Files;

namespace CCL.MES.Hybrid.Client.Specs;

/// <summary>
/// Narrow surface the <see cref="SpecExportFlow"/> needs from the API
/// client. Mirrors the 2 download methods on <see cref="ICclApiClient"/>
/// so tests can sub a tiny fake without having to stand up the full
/// 30-method interface. Production wiring uses the convenience overload
/// that adapts <see cref="ICclApiClient"/> automatically.
/// </summary>
public interface ISpecExportDownloads
{
    Task<long> DownloadSpecListExportAsync(
        string format, string? search, string view, string? planner,
        string destinationFilePath, CancellationToken ct = default);

    Task<long> DownloadSpecSheetPdfAsync(
        long revisionId, string destinationFilePath, CancellationToken ct = default);

    Task<long> DownloadSpecSheetXlsxAsync(
        long revisionId, string destinationFilePath, CancellationToken ct = default);
}

/// <summary>
/// P10.5g — Pure orchestrator for the Spec export UX. Combines the
/// streamed download (<see cref="ICclApiClient"/>) with the sandbox-safe
/// download dir (<see cref="IFileOpener.GetSafeDownloadDirectory"/>),
/// the Save-As dialog (<see cref="IFileSaver"/>), and the "Open after
/// save" launcher (<see cref="IFileOpener.TryOpenAsync"/>) into a single
/// deterministic flow:
///
///   1. Download bytes → sandbox path (NpiSpecLibrary_… or SpecSheet_…).
///   2. Show native Save dialog with the server-stamped filename.
///   3. If the operator saved → optionally open at the destination.
///   4. If the operator cancelled → keep the sandbox copy + return the
///      sandbox path so the UI shell can offer "Open from sandbox".
///
/// The orchestrator is structured as a single pure helper so the Razor
/// page just kicks it off + renders the outcome — no UI-specific logic
/// lives here. Test hosts can sub the IFileSaver stub (returns
/// Cancelled) to pin the sandbox fallback branch.
/// </summary>
public sealed class SpecExportFlow
{
    private readonly ISpecExportDownloads _api;
    private readonly IFileOpener _opener;
    private readonly IFileSaver _saver;

    public SpecExportFlow(ISpecExportDownloads api, IFileOpener opener, IFileSaver saver)
    {
        _api = api;
        _opener = opener;
        _saver = saver;
    }

    /// <summary>Convenience overload — the production wiring passes the
    /// full <see cref="ICclApiClient"/> which already implements both
    /// download methods. Tests can sub a narrower fake via
    /// <see cref="ISpecExportDownloads"/>.</summary>
    public SpecExportFlow(ICclApiClient api, IFileOpener opener, IFileSaver saver)
        : this(new ApiAdapter(api), opener, saver) { }

    private sealed class ApiAdapter : ISpecExportDownloads
    {
        private readonly ICclApiClient _inner;
        public ApiAdapter(ICclApiClient inner) => _inner = inner;

        public Task<long> DownloadSpecListExportAsync(
            string format, string? search, string view, string? planner,
            string destinationFilePath, CancellationToken ct = default)
            => _inner.DownloadSpecListExportAsync(format, search, view, planner, destinationFilePath, ct);

        public Task<long> DownloadSpecSheetPdfAsync(
            long revisionId, string destinationFilePath, CancellationToken ct = default)
            => _inner.DownloadSpecSheetPdfAsync(revisionId, destinationFilePath, ct);

        public Task<long> DownloadSpecSheetXlsxAsync(
            long revisionId, string destinationFilePath, CancellationToken ct = default)
            => _inner.DownloadSpecSheetXlsxAsync(revisionId, destinationFilePath, ct);
    }

    /// <summary>
    /// Run the Spec list export flow. Returns the outcome describing
    /// every path: sandbox-only (Cancelled), saved-not-opened,
    /// saved-and-opened. Caller decides which banner to render.
    /// </summary>
    /// <param name="format">"csv" / "xlsx" / "pdf".</param>
    /// <param name="search">Same search the list page currently uses.</param>
    /// <param name="view">"Active" / "Trash" / "All" — matches the
    /// list page chip + the legacy <c>SpecListView</c> enum.</param>
    /// <param name="planner">"SILK" / "FLEXO" / "LETTER" / "INDIGO" /
    /// "DIECUT" — or null/empty for no chip filter.</param>
    /// <param name="openAfterSave">When true + operator saved → also
    /// hand the destination path to <see cref="IFileOpener.TryOpenAsync"/>
    /// so QuickLook / Excel opens the file in-place.</param>
    public async Task<SpecExportFlowOutcome> ExportListAsync(
        string format,
        string? search,
        string view,
        string? planner,
        bool openAfterSave,
        CancellationToken ct = default)
    {
        var f = (format ?? "").Trim().ToLowerInvariant();
        if (f != "csv" && f != "xlsx" && f != "pdf")
            throw new ArgumentOutOfRangeException(nameof(format),
                "Format must be one of: csv, xlsx, pdf.");

        var sandboxDir = _opener.GetSafeDownloadDirectory();
        var stampedName = StampedListFilename(f, DateTime.Now);
        var sandboxPath = Path.Combine(sandboxDir, stampedName);

        var bytes = await _api.DownloadSpecListExportAsync(
            f, search, view, planner, sandboxPath, ct);

        var save = await _saver.SaveAsync(sandboxPath, stampedName, ct);
        return await FinaliseAsync(save, sandboxPath, stampedName, openAfterSave, bytes, ct);
    }

    /// <summary>
    /// Per-spec sheet PDF flow — same shape as the list flow, just
    /// scoped to a single revision. Filename includes RefNo + Rev so
    /// the operator can tell two adjacent rev exports apart on disk.
    /// </summary>
    public async Task<SpecExportFlowOutcome> ExportSheetPdfAsync(
        long revisionId,
        string refNoOrSpecCode,
        string revisionCode,
        bool openAfterSave,
        CancellationToken ct = default)
    {
        var sandboxDir = _opener.GetSafeDownloadDirectory();
        var stampedName = StampedSheetFilename(refNoOrSpecCode, revisionCode, DateTime.Now);
        var sandboxPath = Path.Combine(sandboxDir, stampedName);

        var bytes = await _api.DownloadSpecSheetPdfAsync(revisionId, sandboxPath, ct);
        var save = await _saver.SaveAsync(sandboxPath, stampedName, ct);
        return await FinaliseAsync(save, sandboxPath, stampedName, openAfterSave, bytes, ct);
    }

    /// <summary>Per-spec sheet EXCEL flow — identical shape to
    /// <see cref="ExportSheetPdfAsync"/>, just the .xlsx endpoint + filename.</summary>
    public async Task<SpecExportFlowOutcome> ExportSheetXlsxAsync(
        long revisionId,
        string refNoOrSpecCode,
        string revisionCode,
        bool openAfterSave,
        CancellationToken ct = default)
    {
        var sandboxDir = _opener.GetSafeDownloadDirectory();
        var stampedName = StampedSheetFilename(refNoOrSpecCode, revisionCode, DateTime.Now, "xlsx");
        var sandboxPath = Path.Combine(sandboxDir, stampedName);

        var bytes = await _api.DownloadSpecSheetXlsxAsync(revisionId, sandboxPath, ct);
        var save = await _saver.SaveAsync(sandboxPath, stampedName, ct);
        return await FinaliseAsync(save, sandboxPath, stampedName, openAfterSave, bytes, ct);
    }

    private async Task<SpecExportFlowOutcome> FinaliseAsync(
        SaveOutcome save, string sandboxPath, string stampedName,
        bool openAfterSave, long downloadedBytes, CancellationToken ct)
    {
        if (save.Saved && !string.IsNullOrWhiteSpace(save.DestinationPath))
        {
            bool opened = false;
            if (openAfterSave)
                opened = await _opener.TryOpenAsync(save.DestinationPath!);
            return SpecExportFlowOutcome.Saved(
                sandboxPath, save.DestinationPath!, stampedName, downloadedBytes, opened);
        }

        // Cancel / failure → keep sandbox copy; optionally open from there.
        bool sandboxOpened = false;
        if (openAfterSave)
            sandboxOpened = await _opener.TryOpenAsync(sandboxPath);
        return SpecExportFlowOutcome.SandboxOnly(
            sandboxPath, stampedName, downloadedBytes, save.Error, sandboxOpened);
    }

    /// <summary>
    /// List-export filename composed from a local DateTime stamp. Pure
    /// so tests can pin both the format token and the timestamp shape.
    /// </summary>
    public static string StampedListFilename(string format, DateTime now)
    {
        var f = (format ?? "").Trim().ToLowerInvariant();
        var ts = now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        return $"NpiSpecLibrary_{ts}.{f}";
    }

    /// <summary>
    /// Sheet-PDF filename composed from RefNo (or SpecCode fallback) +
    /// Rev code + local stamp. Sanitises slashes + spaces so the
    /// Catalyst save dialog accepts the suggested name without
    /// rewriting it.
    /// </summary>
    public static string StampedSheetFilename(
        string refNoOrSpecCode, string revisionCode, DateTime now, string ext = "pdf")
    {
        var ts = now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var head = Sanitize(refNoOrSpecCode ?? "spec");
        var rev = Sanitize(revisionCode ?? "");
        return $"SpecSheet_{head}_Rev{rev}_{ts}.{ext}";
    }

    private static string Sanitize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "spec";
        var buf = new System.Text.StringBuilder(raw.Length);
        char prev = '\0';
        foreach (var ch in raw.Trim())
        {
            char clean = char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' || ch == '.'
                ? ch
                : '_';
            if (clean == '_' && prev == '_') continue;
            buf.Append(clean);
            prev = clean;
        }
        var result = buf.ToString().Trim('_', '.');
        return string.IsNullOrEmpty(result) ? "spec" : result;
    }
}

/// <summary>Outcome of one <see cref="SpecExportFlow"/> run. Three shapes:
/// Saved (operator picked a destination); SandboxOnly (cancelled or
/// failed but the sandbox copy is intact). <see cref="Opened"/> tells
/// the UI whether QuickLook / Excel fired so the banner can switch
/// between "Đã lưu + đã mở" and "Đã lưu — bấm Mở để xem".</summary>
public sealed record SpecExportFlowOutcome
{
    public bool DidSave { get; init; }
    public string SandboxPath { get; init; } = "";
    public string? DestinationPath { get; init; }
    public string SuggestedFileName { get; init; } = "";
    public long DownloadedBytes { get; init; }
    public bool Opened { get; init; }
    public string? Error { get; init; }

    public static SpecExportFlowOutcome Saved(
        string sandboxPath, string destinationPath, string suggestedFileName,
        long downloadedBytes, bool opened) => new()
    {
        DidSave = true,
        SandboxPath = sandboxPath,
        DestinationPath = destinationPath,
        SuggestedFileName = suggestedFileName,
        DownloadedBytes = downloadedBytes,
        Opened = opened,
    };

    public static SpecExportFlowOutcome SandboxOnly(
        string sandboxPath, string suggestedFileName,
        long downloadedBytes, string? error, bool opened) => new()
    {
        DidSave = false,
        SandboxPath = sandboxPath,
        SuggestedFileName = suggestedFileName,
        DownloadedBytes = downloadedBytes,
        Opened = opened,
        Error = error,
    };
}
