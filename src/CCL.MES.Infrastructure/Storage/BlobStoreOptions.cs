namespace CCL.MES.Infrastructure.Storage;

/// <summary>
/// Phase 8 PR-D-5a — FilesystemBlobStore configuration.
///
/// All paths are absolute by the time options are bound (Program.cs resolves
/// MES_DATA_DIR via Path.GetFullPath before injecting). Defense against
/// Lesson 30 cwd-trap from Ops Control v1.2 — every File API call sees an
/// absolute path so process CWD changes can't redirect blob writes.
/// </summary>
public sealed class BlobStoreOptions
{
    /// <summary>
    /// Root data directory (matches Program.cs <c>dataDir</c> resolution).
    /// Blob files persist under <c>&lt;DataDir&gt;/blobs/</c>. Must be absolute.
    /// </summary>
    public string DataDir { get; set; } = "";

    /// <summary>
    /// Hard cap on bytes per single upload — protects against disk-full DoS +
    /// memory blowups on stream copy. Default 10 MiB; env-overridable via
    /// <c>MES_BLOB_MAX_BYTES</c>. Operator should bump for CAD/drawing PDFs
    /// larger than 10 MiB once the upload UI ships in PR-D-5b.
    /// </summary>
    public long MaxBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>
    /// Extension allowlist (lowercase, no leading dot). Anything else throws.
    /// Default = common drawing/artwork formats per CMES drawing kinds:
    /// pdf, png, jpg, jpeg, svg, gif, webp, dwg, dxf, ai.
    /// Override at boot via <c>MES_BLOB_ALLOWED_EXTENSIONS</c> as comma-separated list.
    /// </summary>
    public HashSet<string> AllowedExtensions { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "pdf", "png", "jpg", "jpeg", "svg", "gif", "webp", "dwg", "dxf", "ai",
    };
}
