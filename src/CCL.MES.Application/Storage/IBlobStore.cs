namespace CCL.MES.Application.Storage;

/// <summary>
/// Phase 8 PR #28 — abstraction STUB cho Drawing blob storage. Implementation
/// thực ở PR #31 (FilesystemBlobStore + DATA_DIR/blobs/ path layout).
///
/// Mục đích định nghĩa contract sớm:
///   - PR #28 chỉ ADD interface + DI placeholder; KHÔNG implement
///   - PR #31 ADD FilesystemBlobStore : IBlobStore; DI swap registration
///   - Future PR có thể ADD SqliteBlobStore (single-file portable) hoặc
///     MinioBlobStore (cloud upgrade) — không cần đụng business code
///
/// Pattern mirror Ops Control v1.2 production blob storage convention:
///   blobs/drawings/&lt;revisionId&gt;/&lt;drawingId&gt;/v&lt;n&gt;_&lt;sha8&gt;.&lt;ext&gt;
/// Preview JPEG: cùng folder với `_preview.jpg` suffix.
///
/// Storage key trả về từ PutAsync sẽ được persist vào DrawingVersion.StorageKey.
/// </summary>
public interface IBlobStore
{
    /// <summary>
    /// Upload content + return storage key (immutable per write) AND the
    /// metadata callers need to persist alongside the blob (full SHA256 hex,
    /// byte count). Widened from <c>Task&lt;string&gt;</c> in PR-D-5b — callers
    /// were re-streaming the file just to compute SHA256, which doubled IO
    /// on every upload. The implementation already computes the full hash
    /// during the single write pass; this widens the return shape to expose
    /// it. See <see cref="BlobPutResult"/>.
    /// </summary>
    Task<BlobPutResult> PutAsync(Stream content, string suggestedKey, string contentType, CancellationToken ct = default);

    /// <summary>Stream content cho download. Caller dispose.</summary>
    Task<Stream> GetAsync(string key, CancellationToken ct = default);

    Task<bool> ExistsAsync(string key, CancellationToken ct = default);

    Task DeleteAsync(string key, CancellationToken ct = default);
}

/// <summary>
/// Phase 8 PR-D-5b — result of <see cref="IBlobStore.PutAsync"/>. Carries
/// the data callers must persist on <c>DrawingVersion</c> (StorageKey,
/// FileHash, FileSize) in one round-trip — no need to re-stream the file
/// to compute SHA256 after the write.
/// </summary>
/// <param name="Key">Relative path under <c>&lt;DataDir&gt;/blobs/</c>
///   — persists to <c>DrawingVersion.StorageKey</c>. Always contains the
///   <c>v&lt;n&gt;_&lt;sha8&gt;.&lt;ext&gt;</c> filename.</param>
/// <param name="Sha256Hex">Full lowercase SHA256 hex (64 chars) of the
///   uploaded content — persists to <c>DrawingVersion.FileHash</c>.</param>
/// <param name="SizeBytes">Total bytes written — persists to
///   <c>DrawingVersion.FileSize</c>.</param>
public sealed record BlobPutResult(string Key, string Sha256Hex, long SizeBytes);
