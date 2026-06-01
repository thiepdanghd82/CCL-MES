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
    /// <summary>Upload content + return storage key (cứng + immutable per write).</summary>
    Task<string> PutAsync(Stream content, string suggestedKey, string contentType, CancellationToken ct = default);

    /// <summary>Stream content cho download. Caller dispose.</summary>
    Task<Stream> GetAsync(string key, CancellationToken ct = default);

    Task<bool> ExistsAsync(string key, CancellationToken ct = default);

    Task DeleteAsync(string key, CancellationToken ct = default);
}
