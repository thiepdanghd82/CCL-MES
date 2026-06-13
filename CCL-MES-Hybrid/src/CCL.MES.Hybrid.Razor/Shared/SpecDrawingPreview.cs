namespace CCL.MES.Hybrid.Razor.Shared;

/// <summary>
/// P10.10 — one drawing preview rendered inside the spec showcard. Images +
/// PDFs carry a JS-created blob <see cref="ObjectUrl"/> (WKWebView renders both
/// reliably from a blob: URL); other types render as a labeled tile only.
/// </summary>
public sealed record SpecDrawingPreview(
    string Kind,
    string Name,
    bool IsImage,
    bool IsPdf,
    string? ObjectUrl);
