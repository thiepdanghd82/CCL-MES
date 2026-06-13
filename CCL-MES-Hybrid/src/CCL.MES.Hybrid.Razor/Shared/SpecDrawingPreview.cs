namespace CCL.MES.Hybrid.Razor.Shared;

/// <summary>
/// P10.10 — one drawing preview rendered inside the spec showcard. Image
/// types carry a base64 <see cref="ImageDataUrl"/> for an inline thumbnail;
/// non-image types (e.g. PDF/DWG) render as a labeled tile only.
/// </summary>
public sealed record SpecDrawingPreview(
    string Kind,
    string Name,
    bool IsImage,
    string? ImageDataUrl);
