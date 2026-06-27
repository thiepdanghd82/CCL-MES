namespace CCL.MES.Hybrid.Razor.Shared;

/// <summary>
/// P10.10 — one drawing preview rendered inside the spec showcard. Images +
/// PDFs carry a JS-created blob <see cref="ObjectUrl"/> (WKWebView renders both
/// reliably from a blob: URL); other types render as a labeled tile only.
/// <see cref="Group"/> buckets the preview into one of the collapsible
/// showcards rendered below the spec sheet (Customer drawing / Print layout /
/// Cut layout).
/// </summary>
public sealed record SpecDrawingPreview(
    string Kind,
    string Name,
    bool IsImage,
    bool IsPdf,
    string? ObjectUrl,
    SpecDrawingGroup Group);

/// <summary>The collapsible showcard a drawing preview belongs to.</summary>
public enum SpecDrawingGroup
{
    Customer = 0,
    Print = 1,
    Cut = 2,
}
