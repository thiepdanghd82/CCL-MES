using System.Security.Cryptography;
using System.Text.RegularExpressions;
using CCL.MES.Application.Storage;
using Microsoft.Extensions.Logging;

namespace CCL.MES.Infrastructure.Storage;

/// <summary>
/// Phase 8 PR-D-5a — filesystem-backed <see cref="IBlobStore"/> implementation.
///
/// Path scheme (operator-readable + git-blame-friendly):
///   &lt;DataDir&gt;/blobs/drawings/&lt;revisionId&gt;/&lt;drawingId&gt;/v&lt;n&gt;_&lt;sha8&gt;.&lt;ext&gt;
///
/// Storage key returned by <see cref="PutAsync"/> is the RELATIVE path under
/// <c>&lt;DataDir&gt;/blobs/</c> — survives DataDir moves between dev/prod
/// without rewriting <see cref="CCL.MES.Domain.Entities.DrawingVersion.StorageKey"/>.
///
/// Security guards (every method validates):
///   1. <b>Suggested-key regex</b> on <see cref="PutAsync"/> —
///      <c>drawings/&lt;int&gt;/&lt;int&gt;/v&lt;int&gt;.&lt;ext&gt;</c> only;
///      blocks <c>..</c>, leading slash, drive letters, null bytes, NFD
///      tricks (regex token classes reject them).
///   2. <b>Stored-key regex</b> on <see cref="GetAsync"/> /
///      <see cref="ExistsAsync"/> / <see cref="DeleteAsync"/> — sha8 is
///      REQUIRED so callers can't probe for arbitrary keys.
///   3. <b>Extension allowlist</b> — only formats CMES drawing kinds use.
///   4. <b>Size limit</b> — bytes counted during stream copy; throws as
///      soon as the cap is exceeded so we never persist an oversize file.
///   5. <b>Containment check</b> — resolved absolute path MUST start with
///      <c>&lt;DataDir&gt;/blobs/</c> prefix; defends against symlink /
///      canonicalization tricks even if the regex were bypassed.
///   6. <b>Atomic rename</b> — write to <c>.tmp.&lt;guid&gt;</c> then
///      <see cref="File.Move(string, string)"/>; partial writes never
///      surface as readable blobs.
///   7. <b>Idempotency by content</b> — if the same (revision, drawing,
///      version, sha8, ext) already exists, the temp file is dropped and
///      the existing key returned. Two parallel uploads of the same byte
///      stream converge safely.
/// </summary>
public sealed class FilesystemBlobStore : IBlobStore
{
    private const string BlobsSubdir = "blobs";

    // Input key — sha8 NOT yet known (caller passes the version-no template).
    // ^drawings/<positive int>/<positive int>/v<positive int>.<1-8 ext char>$
    // No leading zeros allowed on ids so callers can't pass "001" as a path-traversal alias.
    private static readonly Regex SuggestedKeyRx = new(
        @"^drawings/([1-9]\d*)/([1-9]\d*)/v([1-9]\d*)\.([a-zA-Z0-9]{1,8})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Stored key — sha8 REQUIRED (this is the value persisted to DrawingVersion.StorageKey).
    private static readonly Regex StoredKeyRx = new(
        @"^drawings/([1-9]\d*)/([1-9]\d*)/v([1-9]\d*)_([0-9a-f]{8})\.([a-zA-Z0-9]{1,8})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly BlobStoreOptions _options;
    private readonly ILogger<FilesystemBlobStore>? _logger;
    private readonly string _blobRootAbs;

    public FilesystemBlobStore(BlobStoreOptions options, ILogger<FilesystemBlobStore>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_options.DataDir))
            throw new InvalidOperationException("BlobStoreOptions.DataDir must be set before use.");
        if (_options.MaxBytes <= 0)
            throw new InvalidOperationException("BlobStoreOptions.MaxBytes must be positive.");
        if (_options.AllowedExtensions.Count == 0)
            throw new InvalidOperationException("BlobStoreOptions.AllowedExtensions must be non-empty.");

        // Eagerly resolve absolute root so every subsequent containment check
        // compares against a stable canonical prefix.
        _blobRootAbs = Path.GetFullPath(Path.Combine(_options.DataDir, BlobsSubdir));
        Directory.CreateDirectory(_blobRootAbs);
    }

    public async Task<string> PutAsync(
        Stream content,
        string suggestedKey,
        string contentType,
        CancellationToken ct = default)
    {
        if (content is null) throw new ArgumentNullException(nameof(content));
        if (string.IsNullOrWhiteSpace(suggestedKey))
            throw new ArgumentException("suggestedKey is required.", nameof(suggestedKey));

        var m = SuggestedKeyRx.Match(suggestedKey);
        if (!m.Success)
        {
            throw new InvalidOperationException(
                "Invalid suggestedKey — must match 'drawings/<revisionId>/<drawingId>/v<n>.<ext>'.");
        }

        var revId = m.Groups[1].Value;
        var drwId = m.Groups[2].Value;
        var versionNo = m.Groups[3].Value;
        var ext = m.Groups[4].Value.ToLowerInvariant();

        if (!_options.AllowedExtensions.Contains(ext))
        {
            throw new InvalidOperationException(
                $"Extension '{ext}' not in allowlist (allowed: {string.Join(", ", _options.AllowedExtensions.OrderBy(s => s, StringComparer.Ordinal))}).");
        }

        var targetDir = Path.Combine(_blobRootAbs, "drawings", revId, drwId);
        // Containment check — paranoia even though regex already vetted ids.
        EnsureInsideBlobRoot(Path.GetFullPath(targetDir));
        Directory.CreateDirectory(targetDir);

        // Temp file in same dir so File.Move() is atomic on same filesystem.
        var tempFile = Path.Combine(targetDir, $".tmp.{Guid.NewGuid():N}");
        long bytesWritten = 0;
        string sha256Hex;

        try
        {
            using (var sha = SHA256.Create())
            using (var fs = new FileStream(
                tempFile, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                int n;
                while ((n = await content.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
                {
                    bytesWritten += n;
                    if (bytesWritten > _options.MaxBytes)
                    {
                        throw new InvalidOperationException(
                            $"Blob size {bytesWritten} bytes exceeds MaxBytes limit {_options.MaxBytes}.");
                    }
                    sha.TransformBlock(buffer, 0, n, null, 0);
                    await fs.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                }
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                await fs.FlushAsync(ct).ConfigureAwait(false);
                sha256Hex = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
            }

            var sha8 = sha256Hex[..8];
            var finalName = $"v{versionNo}_{sha8}.{ext}";
            var finalPath = Path.Combine(targetDir, finalName);
            // Containment check on final too (sha8 is hex per regex, but guard).
            EnsureInsideBlobRoot(Path.GetFullPath(finalPath));

            if (File.Exists(finalPath))
            {
                // Same content already stored — drop temp + return existing key.
                // This is the dedup convergence path for parallel uploads.
                File.Delete(tempFile);
            }
            else
            {
                File.Move(tempFile, finalPath);
            }

            var storageKey = $"drawings/{revId}/{drwId}/{finalName}";
            _logger?.LogInformation(
                "Blob stored: key={Key} bytes={Bytes} sha8={Sha8}",
                storageKey, bytesWritten, sha8);
            return storageKey;
        }
        catch
        {
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { /* best effort */ }
            }
            throw;
        }
    }

    public Task<Stream> GetAsync(string key, CancellationToken ct = default)
    {
        var abs = ResolveStoredKey(key, throwOnInvalid: true)!;
        Stream s = new FileStream(
            abs, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, useAsync: true);
        return Task.FromResult(s);
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        var abs = ResolveStoredKey(key, throwOnInvalid: false);
        return Task.FromResult(abs is not null && File.Exists(abs));
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var abs = ResolveStoredKey(key, throwOnInvalid: true)!;
        if (File.Exists(abs)) File.Delete(abs);
        return Task.CompletedTask;
    }

    private string? ResolveStoredKey(string key, bool throwOnInvalid)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            if (throwOnInvalid) throw new InvalidOperationException("key is required.");
            return null;
        }
        if (!StoredKeyRx.IsMatch(key))
        {
            if (throwOnInvalid)
                throw new InvalidOperationException(
                    "Invalid storage key — must match 'drawings/<revisionId>/<drawingId>/v<n>_<sha8>.<ext>'.");
            return null;
        }
        // Use Path.Combine + GetFullPath so OS handles separator conversion.
        var rel = key.Replace('/', Path.DirectorySeparatorChar);
        var abs = Path.GetFullPath(Path.Combine(_blobRootAbs, rel));
        if (!IsInsideBlobRoot(abs))
        {
            if (throwOnInvalid)
                throw new InvalidOperationException("Resolved path escapes blob root.");
            return null;
        }
        return abs;
    }

    private bool IsInsideBlobRoot(string absolutePath)
    {
        // OrdinalEqual check + trailing separator guard so "/foo/blobs2/x"
        // doesn't pass when root is "/foo/blobs".
        var rootWithSep = _blobRootAbs.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return absolutePath.StartsWith(rootWithSep, StringComparison.Ordinal)
            || string.Equals(absolutePath, _blobRootAbs, StringComparison.Ordinal);
    }

    private void EnsureInsideBlobRoot(string absolutePath)
    {
        if (!IsInsideBlobRoot(absolutePath))
            throw new InvalidOperationException("Resolved path escapes blob root.");
    }
}
