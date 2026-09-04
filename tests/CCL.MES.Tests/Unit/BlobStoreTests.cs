using System.Security.Cryptography;
using CCL.MES.Infrastructure.Storage;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// Phase 9 T1 — Port of <c>scripts/VerifyBlobStore</c> 8-case matrix
/// into xUnit. <see cref="FilesystemBlobStore"/> is exercised against
/// an isolated <c>/tmp/&lt;guid&gt;/</c> root per test class instance
/// so the tests are fully hermetic (no shared state, no prod blob
/// touch). EF + DbContext NOT involved — this is "filesystem-as-DB"
/// territory, kept as unit per <c>docs/PHASE9-TEST-FRAMEWORK-PLAN.md</c>
/// §4 P0 lock-in.
///
/// Locks in the 6 security guards documented in
/// <c>FilesystemBlobStore.cs:18-39</c>:
///   1. Suggested-key regex
///   2. Stored-key regex (sha8 mandatory)
///   3. Extension allowlist
///   4. Size limit
///   5. Containment check (relative path resolves under blob root)
///   6. Atomic rename + idempotency-by-content
/// </summary>
public sealed class BlobStoreTests : IDisposable
{
    private readonly string _tmpRoot;
    private readonly FilesystemBlobStore _store;

    public BlobStoreTests()
    {
        // Per-instance isolated root — xUnit creates a fresh instance per
        // [Fact]/[Theory] iteration, so /tmp pollution is bounded and
        // Dispose cleans each run.
        _tmpRoot = Path.Combine(Path.GetTempPath(), $"ccl-blob-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tmpRoot);
        _store = new FilesystemBlobStore(new BlobStoreOptions
        {
            DataDir  = _tmpRoot,
            MaxBytes = 1024 * 1024,   // 1 MiB cap — small so oversize is cheap to trigger
        });
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmpRoot, recursive: true); } catch { /* best effort */ }
    }

    // ── 1. Round-trip put → get → SHA match ────────────────────────────

    [Fact]
    public async Task Round_trip_persists_then_reads_back_with_matching_sha()
    {
        var content = RandomBytes(5000);
        var expectedSha = Sha256Hex(content);
        using var inStream = new MemoryStream(content);

        var result = await _store.PutAsync(inStream, "drawings/1/2/v1.pdf", "application/pdf");

        Assert.StartsWith("drawings/1/2/v1_", result.Key);
        Assert.EndsWith(".pdf", result.Key);
        Assert.Contains(expectedSha[..8], result.Key);
        Assert.Equal(expectedSha, result.Sha256Hex);
        Assert.Equal(content.Length, result.SizeBytes);

        using var outStream = await _store.GetAsync(result.Key);
        using var ms = new MemoryStream();
        await outStream.CopyToAsync(ms);
        Assert.Equal(expectedSha, Sha256Hex(ms.ToArray()));
    }

    // ── 2. Idempotency by content ──────────────────────────────────────

    [Fact]
    public async Task Idempotent_put_same_bytes_twice_yields_same_key()
    {
        var content = RandomBytes(2048);
        using var s1 = new MemoryStream(content);
        var r1 = await _store.PutAsync(s1, "drawings/3/4/v1.png", "image/png");
        using var s2 = new MemoryStream(content);
        var r2 = await _store.PutAsync(s2, "drawings/3/4/v1.png", "image/png");

        Assert.Equal(r1.Key, r2.Key);
        Assert.Equal(r1.Sha256Hex, r2.Sha256Hex);
    }

    // ── 3. Path traversal — '..' in suggested key ──────────────────────

    [Fact]
    public async Task Reject_path_traversal_dotdot_in_suggested_key()
    {
        using var s = new MemoryStream(RandomBytes(100));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _store.PutAsync(s, "drawings/../../etc/passwd", "text/plain"));
    }

    // ── 4. Path traversal — '..' as id segment ─────────────────────────

    [Fact]
    public async Task Reject_path_traversal_dotdot_as_id_segment()
    {
        using var s = new MemoryStream(RandomBytes(100));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _store.PutAsync(s, "drawings/1/../2/v1.pdf", "application/pdf"));
    }

    [Fact]
    public async Task Reject_leading_slash_in_suggested_key()
    {
        using var s = new MemoryStream(RandomBytes(100));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _store.PutAsync(s, "/drawings/1/2/v1.pdf", "application/pdf"));
    }

    [Fact]
    public async Task Reject_leading_zero_int_aliasing_traversal()
    {
        // Regex rejects "001" as drawingId — only [1-9]\d* allowed so callers
        // can't smuggle a path-traversal alias.
        using var s = new MemoryStream(RandomBytes(100));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _store.PutAsync(s, "drawings/001/2/v1.pdf", "application/pdf"));
    }

    // ── 5. Oversize cap enforced mid-stream ────────────────────────────

    [Fact]
    public async Task Reject_oversize_payload_exceeding_MaxBytes()
    {
        var big = RandomBytes(1024 * 1024 + 1);   // 1 MiB + 1 byte → cap is 1 MiB
        using var s = new MemoryStream(big);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _store.PutAsync(s, "drawings/5/6/v1.jpg", "image/jpeg"));
        Assert.Contains("exceeds", ex.Message);
    }

    // ── 6. Extension allowlist ─────────────────────────────────────────

    [Fact]
    public async Task Reject_extension_not_in_allowlist()
    {
        using var s = new MemoryStream(RandomBytes(100));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _store.PutAsync(s, "drawings/7/8/v1.exe", "application/octet-stream"));
        Assert.Contains("not in allowlist", ex.Message);
    }

    // ── 7. Probe-resistance — bad-format key on read/exists ────────────

    [Fact]
    public async Task ExistsAsync_returns_false_for_bad_format_key()
    {
        var bad = "drawings/../escape.pdf";
        var exists = await _store.ExistsAsync(bad);
        Assert.False(exists);
    }

    [Fact]
    public async Task GetAsync_throws_on_bad_format_key()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _store.GetAsync("drawings/../escape.pdf"));
    }

    // ── 8. Delete safety — traversal key rejected ──────────────────────

    [Fact]
    public async Task DeleteAsync_throws_on_traversal_key()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _store.DeleteAsync("drawings/../escape.pdf"));
    }

    // ── Containment audit — no stray writes outside <root>/blobs/ ──────

    [Fact]
    public async Task All_persisted_files_live_under_blob_root()
    {
        var content = RandomBytes(256);
        using var s = new MemoryStream(content);
        await _store.PutAsync(s, "drawings/9/10/v1.pdf", "application/pdf");

        var blobRoot = Path.Combine(_tmpRoot, "blobs");
        var stray = Directory.EnumerateFileSystemEntries(_tmpRoot, "*", SearchOption.AllDirectories)
            .Where(p => !p.StartsWith(blobRoot, StringComparison.Ordinal))
            .ToArray();
        Assert.Empty(stray);
    }

    // ── Options validation — constructor sanity ────────────────────────

    // ── P12 bước 4 — hình dạng thứ HAI (IQC/Documents) ───────────────────

    [Fact]
    public async Task Iqc_shape_round_trips_and_key_carries_sha8()
    {
        var r = await _store.PutAsync(new MemoryStream(RandomBytes(512)), "IQC/Documents/336T-AT1/336T-AT1_TDS.pdf",
            "application/pdf");

        Assert.StartsWith("IQC/Documents/336T-AT1/336T-AT1_TDS_", r.Key);
        Assert.EndsWith(".pdf", r.Key);
        Assert.True(await _store.ExistsAsync(r.Key));
        using var back = await _store.GetAsync(r.Key);
        Assert.NotNull(back);
    }

    [Theory]
    [InlineData("IQC/Documents/../../etc/passwd.pdf")]
    [InlineData("IQC/Documents/../secret/x.pdf")]
    [InlineData("IQC/Documents/a/../../b.pdf")]
    [InlineData("/IQC/Documents/a/b.pdf")]
    [InlineData("IQC/Documents/a/b/c.pdf")]          // sâu hơn 1 tầng mã
    [InlineData("IQC/Other/a/b.pdf")]                // sai tiền tố
    public async Task Iqc_shape_van_chan_traversal_va_tien_to_la(string key)
    {
        // Thêm caller thứ hai KHÔNG được nới guard #1 thành "đường dẫn nào cũng
        // được" — hình dạng mới vẫn là allowlist đóng.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _store.PutAsync(new MemoryStream(RandomBytes(64)), key, "application/pdf"));
    }

    [Fact]
    public async Task Iqc_stored_key_thieu_sha8_bi_tu_choi()
    {
        // Guard #2 giữ nguyên cho hình dạng mới: khoá không mang sha8 thì
        // không đọc được, nên không ai dò được khoá tuỳ ý.
        Assert.False(await _store.ExistsAsync("IQC/Documents/336T-AT1/336T-AT1_TDS.pdf"));
    }

    [Fact]
    public void Constructor_throws_when_DataDir_blank()
    {
        Assert.Throws<InvalidOperationException>(
            () => new FilesystemBlobStore(new BlobStoreOptions { DataDir = "", MaxBytes = 1024 }));
    }

    [Fact]
    public void Constructor_throws_when_MaxBytes_non_positive()
    {
        Assert.Throws<InvalidOperationException>(
            () => new FilesystemBlobStore(new BlobStoreOptions { DataDir = _tmpRoot, MaxBytes = 0 }));
    }

    [Fact]
    public void Constructor_throws_when_AllowedExtensions_empty()
    {
        Assert.Throws<InvalidOperationException>(
            () => new FilesystemBlobStore(new BlobStoreOptions
            {
                DataDir          = _tmpRoot,
                MaxBytes         = 1024,
                AllowedExtensions = new(),
            }));
    }

    // ── Successful round-trip across the 10 default extensions ────────

    [Theory]
    [InlineData("pdf",  "application/pdf")]
    [InlineData("png",  "image/png")]
    [InlineData("jpg",  "image/jpeg")]
    [InlineData("jpeg", "image/jpeg")]
    [InlineData("svg",  "image/svg+xml")]
    public async Task Accept_default_allowlist_extensions(string ext, string contentType)
    {
        var content = RandomBytes(128);
        using var s = new MemoryStream(content);
        var key = $"drawings/11/12/v1.{ext}";
        var res = await _store.PutAsync(s, key, contentType);
        Assert.EndsWith($".{ext}", res.Key);
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static byte[] RandomBytes(int n)
    {
        var b = new byte[n];
        // Deterministic seed per call so failures are reproducible.
        new Random(n).NextBytes(b);
        return b;
    }

    private static string Sha256Hex(byte[] data)
        => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
