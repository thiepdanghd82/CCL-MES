using System.Security.Cryptography;
using CCL.MES.Application.Storage;
using CCL.MES.Infrastructure.Storage;

// Phase 8 PR-D-5a — FilesystemBlobStore verification harness.
//
// 8 test cases — round-trip + idempotency + 6 security guards:
//   1. Round-trip:        put → get → SHA matches
//   2. Idempotency:       put same bytes twice → same key, dedup convergence
//   3. Path traversal A:  '../../etc/passwd' in suggestedKey rejected
//   4. Path traversal B:  '1/../2' inside ids rejected (regex-token check)
//   5. Oversize:          stream exceeding MaxBytes rejected during write
//   6. Extension allowlist: 'exe' rejected (not in CMES drawing kinds)
//   7. Probe-resistance:  Get with bad-format key rejected
//   8. Delete safety:     Delete with traversal key rejected
//
// Pass criterion: each test prints PASS; final summary prints "PASS N FAIL 0".
// Failures dump the exception name + message. Exit code = fail count.

var tmpRoot = Path.Combine(Path.GetTempPath(), $"ccl-blob-verify-{Guid.NewGuid():N}");
Directory.CreateDirectory(tmpRoot);

Console.WriteLine("PR-D-5a Verifier — FilesystemBlobStore round-trip + 6 security guards");
Console.WriteLine("──────────────────────────────────────────────────────────────────────");
Console.WriteLine($"  blob root: {tmpRoot}/blobs/");
Console.WriteLine();

int pass = 0, fail = 0;

void Pass(string label, string detail = "")
{
    Console.WriteLine($"  PASS  {label,-44}  {detail}");
    pass++;
}
void Fail(string label, string detail)
{
    Console.WriteLine($"  FAIL  {label,-44}  {detail}");
    fail++;
}

static byte[] RandomBytes(int n)
{
    var b = new byte[n];
    new Random(42).NextBytes(b);
    return b;
}

static string Sha256Hex(byte[] data)
    => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

var store = new FilesystemBlobStore(new BlobStoreOptions
{
    DataDir = tmpRoot,
    MaxBytes = 1024 * 1024,   // 1 MiB cap so we can force oversize easily
});

// ── 1. Round-trip ─────────────────────────────────────────────────────
try
{
    var content = RandomBytes(5000);
    var expectedSha = Sha256Hex(content);
    using var inStream = new MemoryStream(content);
    var key = await store.PutAsync(inStream, "drawings/1/2/v1.pdf", "application/pdf");

    if (!key.StartsWith("drawings/1/2/v1_") || !key.EndsWith(".pdf"))
    {
        Fail("1. Round-trip — key shape", $"unexpected key '{key}'");
    }
    else if (!key.Contains(expectedSha[..8]))
    {
        Fail("1. Round-trip — sha8 in key", $"expected {expectedSha[..8]} in '{key}'");
    }
    else
    {
        using var outStream = await store.GetAsync(key);
        using var ms = new MemoryStream();
        await outStream.CopyToAsync(ms);
        var actualSha = Sha256Hex(ms.ToArray());
        if (actualSha != expectedSha)
            Fail("1. Round-trip — sha mismatch", $"expected {expectedSha} got {actualSha}");
        else
            Pass("1. Round-trip put → get → sha match", $"key={key}");
    }
}
catch (Exception ex)
{
    Fail("1. Round-trip", $"{ex.GetType().Name}: {ex.Message}");
}

// ── 2. Idempotency — same content twice = same key, no duplicate file ──
try
{
    var content = RandomBytes(2048);
    using var s1 = new MemoryStream(content);
    var k1 = await store.PutAsync(s1, "drawings/3/4/v1.png", "image/png");
    using var s2 = new MemoryStream(content);
    var k2 = await store.PutAsync(s2, "drawings/3/4/v1.png", "image/png");
    if (k1 != k2)
        Fail("2. Idempotency — same content", $"k1='{k1}' k2='{k2}'");
    else
        Pass("2. Idempotency same content → same key", $"key={k1}");
}
catch (Exception ex)
{
    Fail("2. Idempotency", $"{ex.GetType().Name}: {ex.Message}");
}

// ── 3. Path traversal A — '..' in suggestedKey ────────────────────────
try
{
    using var s = new MemoryStream(RandomBytes(100));
    await store.PutAsync(s, "drawings/../../etc/passwd", "text/plain");
    Fail("3. Traversal '..' rejected", "store accepted dangerous key");
}
catch (InvalidOperationException ex)
{
    Pass("3. Traversal '..' rejected", $"throw OK: {ex.Message[..Math.Min(60, ex.Message.Length)]}");
}
catch (Exception ex)
{
    Fail("3. Traversal '..' rejected", $"wrong type {ex.GetType().Name}");
}

// ── 4. Path traversal B — '..' as id segment ──────────────────────────
try
{
    using var s = new MemoryStream(RandomBytes(100));
    await store.PutAsync(s, "drawings/1/../2/v1.pdf", "application/pdf");
    Fail("4. Traversal id-segment rejected", "store accepted dangerous key");
}
catch (InvalidOperationException ex)
{
    Pass("4. Traversal id-segment rejected", $"throw OK: {ex.Message[..Math.Min(60, ex.Message.Length)]}");
}
catch (Exception ex)
{
    Fail("4. Traversal id-segment rejected", $"wrong type {ex.GetType().Name}");
}

// ── 5. Oversize — stream exceeds MaxBytes (1 MiB) ──────────────────────
try
{
    var big = RandomBytes(1024 * 1024 + 1);   // 1 MiB + 1 byte
    using var s = new MemoryStream(big);
    await store.PutAsync(s, "drawings/5/6/v1.jpg", "image/jpeg");
    Fail("5. Oversize rejected (>MaxBytes)", "store accepted 1 MiB + 1 byte");
}
catch (InvalidOperationException ex) when (ex.Message.Contains("exceeds"))
{
    Pass("5. Oversize rejected (>MaxBytes)", "throw OK: size cap enforced");
}
catch (Exception ex)
{
    Fail("5. Oversize rejected", $"wrong type {ex.GetType().Name}: {ex.Message}");
}

// ── 6. Extension allowlist — 'exe' rejected ───────────────────────────
try
{
    using var s = new MemoryStream(RandomBytes(100));
    await store.PutAsync(s, "drawings/7/8/v1.exe", "application/octet-stream");
    Fail("6. Extension allowlist", "store accepted .exe");
}
catch (InvalidOperationException ex) when (ex.Message.Contains("not in allowlist"))
{
    Pass("6. Extension allowlist rejected", $"throw OK: 'exe' not in list");
}
catch (Exception ex)
{
    Fail("6. Extension allowlist", $"wrong type {ex.GetType().Name}: {ex.Message}");
}

// ── 7. Probe-resistance — Get/Exists with bad-format key ───────────────
try
{
    var bad = "drawings/../escape.pdf";
    var exists = await store.ExistsAsync(bad);
    if (exists)
    {
        Fail("7. Probe-resistance — ExistsAsync", "returned true for bad key");
    }
    else
    {
        // GetAsync should throw on bad key.
        try
        {
            using var s = await store.GetAsync(bad);
            Fail("7. Probe-resistance — GetAsync", "did not throw");
        }
        catch (InvalidOperationException)
        {
            Pass("7. Probe-resistance bad-key", "Exists=false + Get throws");
        }
    }
}
catch (Exception ex)
{
    Fail("7. Probe-resistance", $"{ex.GetType().Name}: {ex.Message}");
}

// ── 8. Delete safety — Delete with traversal key rejected ──────────────
try
{
    await store.DeleteAsync("drawings/../escape.pdf");
    Fail("8. Delete traversal-safe", "Delete did not throw on bad key");
}
catch (InvalidOperationException ex)
{
    Pass("8. Delete traversal-safe", $"throw OK: {ex.Message[..Math.Min(60, ex.Message.Length)]}");
}
catch (Exception ex)
{
    Fail("8. Delete traversal-safe", $"wrong type {ex.GetType().Name}");
}

// ── Containment audit — every persisted file lives under blob root ─────
var blobRoot = Path.Combine(tmpRoot, "blobs");
var stray = Directory.Exists(tmpRoot)
    ? Directory.EnumerateFileSystemEntries(tmpRoot, "*", SearchOption.AllDirectories)
        .Where(p => !p.StartsWith(blobRoot, StringComparison.Ordinal))
        .ToArray()
    : Array.Empty<string>();
if (stray.Length > 0)
{
    Fail("Audit — stray entries outside /blobs", $"{stray.Length} found");
}
else
{
    Pass("Audit — all entries inside /blobs root", $"scanned {tmpRoot}");
}

Console.WriteLine();
Console.WriteLine($"  Result: PASS {pass}  FAIL {fail}");

// Clean up temp dir.
try { Directory.Delete(tmpRoot, recursive: true); } catch { /* best effort */ }

return fail;
