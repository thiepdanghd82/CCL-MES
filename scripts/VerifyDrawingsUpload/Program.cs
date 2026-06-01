using System.Security.Cryptography;
using CCL.MES.Application;
using CCL.MES.Application.Audit;
using CCL.MES.Application.Services;
using CCL.MES.Application.Storage;
using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

// Phase 8 PR-D-5b end-to-end integration test for DrawingsService.
//
// Bootstraps a temp SQLite DB + temp blob root, seeds a minimal
// ProductRevision, runs 1.2 MiB synthetic content through the full
// DrawingsService.UploadAsync → DrawingsService.GetForDownloadAsync
// → IBlobStore.GetAsync pipeline, and verifies:
//   1. Upload of a 1.2 MiB file (> Blazor InputFile default 500 KB cap)
//      succeeds end-to-end and persists DrawingVersion metadata.
//   2. Persisted FileHash matches SHA256 of the original content.
//   3. Persisted FileSize matches the byte count.
//   4. Persisted StorageKey resolves back to the blob; downloaded bytes
//      reproduce the original (round-trip SHA match).
//   5. Subsequent upload to the same kind creates v2 (chain advances)
//      and updates Drawing.CurrentVersionId.
//   6. Upload with a non-allowed extension (.exe) is rejected at the
//      store boundary and the DB rolls back (no orphan rows).
//   7. Upload with an unauthorised role (Operator) is rejected before
//      touching the store (RBAC server-side gate).
//   8. GetForDownloadAsync(versionId, wrongRevisionId) returns null
//      (revision-scoped download).

var tmpRoot = Path.Combine(Path.GetTempPath(), $"ccl-drawings-verify-{Guid.NewGuid():N}");
Directory.CreateDirectory(tmpRoot);
var dbPath = Path.Combine(tmpRoot, "test.db");
var blobOpts = new BlobStoreOptions { DataDir = tmpRoot, MaxBytes = 10 * 1024 * 1024 };

Console.WriteLine("PR-D-5b Verifier — DrawingsService upload + download integration");
Console.WriteLine("──────────────────────────────────────────────────────────────────");
Console.WriteLine($"  blob root: {tmpRoot}/blobs/");
Console.WriteLine($"  db path  : {dbPath}");
Console.WriteLine();

int pass = 0, fail = 0;
void Pass(string label, string detail = "") { Console.WriteLine($"  PASS  {label,-50}  {detail}"); pass++; }
void Fail(string label, string detail) { Console.WriteLine($"  FAIL  {label,-50}  {detail}"); fail++; }

static string Sha256Hex(byte[] data)
    => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

// Bootstrap a minimal EF context.
var options = new DbContextOptionsBuilder<MesDbContext>()
    .UseSqlite($"Data Source={dbPath}")
    .Options;
await using var db = new MesDbContext(options);
await db.Database.MigrateAsync();

// Seed a ProductRevision so FK works.
var customer = new Customer { Code = "TST", Name = "Test Co" };
var product = new Product { ProductCode = "TST-001", Name = "Test Product", Customer = customer };
customer.Products.Add(product);
db.Customers.Add(customer);
await db.SaveChangesAsync();
var revision = new ProductRevision
{
    ProductId = product.Id,
    SpecCode = "SPEC-TST-001",
    Title = "Test spec",
    RevisionCode = "A",
    Status = ProductRevisionStatus.Draft,
};
db.ProductRevisions.Add(revision);
await db.SaveChangesAsync();

// Wire IBlobStore + DrawingsService.
var blobStore = new FilesystemBlobStore(blobOpts);
var auditWriter = new InMemoryAuditWriter();
var drawingsSvc = new DrawingsService(db, blobStore, auditWriter);

// ── 1. Upload 1.2 MiB file (> Blazor InputFile 500 KB default cap) ────
var bigBytes = new byte[1_258_000];   // 1.2 MiB-ish
new Random(7).NextBytes(bigBytes);
var bigSha = Sha256Hex(bigBytes);
DrawingUploadResult? upRes = null;
try
{
    using var s = new MemoryStream(bigBytes);
    upRes = await drawingsSvc.UploadAsync(
        revisionId: revision.Id,
        kind: DrawingKind.CustomerDrawing,
        originalFileName: "customer-original.pdf",
        contentType: "application/pdf",
        content: s,
        changeReason: "Initial baseline upload",
        actor: "engineer.demo",
        actorRole: "Engineer");
    Pass("1. Upload 1.2 MiB file (> InputFile 500 KB default)",
        $"v{upRes.VersionNo} size={upRes.SizeBytes}");
}
catch (Exception ex)
{
    Fail("1. Upload 1.2 MiB file", $"{ex.GetType().Name}: {ex.Message}");
}

// ── 2. Persisted FileHash matches original SHA ──────────────────────────
if (upRes is not null)
{
    if (upRes.Sha256Hex != bigSha)
        Fail("2. FileHash == SHA256(input)", $"expected {bigSha} got {upRes.Sha256Hex}");
    else
        Pass("2. FileHash == SHA256(input)", $"sha={bigSha[..16]}...");
}

// ── 3. Persisted FileSize matches byte count ────────────────────────────
if (upRes is not null)
{
    if (upRes.SizeBytes != bigBytes.Length)
        Fail("3. FileSize == bytes.Length", $"expected {bigBytes.Length} got {upRes.SizeBytes}");
    else
        Pass("3. FileSize == bytes.Length", $"{upRes.SizeBytes} bytes");
}

// ── 4. StorageKey resolves + downloaded bytes reproduce original ────────
if (upRes is not null)
{
    try
    {
        var info = await drawingsSvc.GetForDownloadAsync(upRes.VersionId, revision.Id);
        if (info is null)
        {
            Fail("4. GetForDownload + bytes round-trip", "GetForDownloadAsync returned null");
        }
        else
        {
            using var blob = await blobStore.GetAsync(info.StorageKey);
            using var ms = new MemoryStream();
            await blob.CopyToAsync(ms);
            var actualSha = Sha256Hex(ms.ToArray());
            if (actualSha != bigSha)
                Fail("4. Download SHA match", $"expected {bigSha} got {actualSha}");
            else
                Pass("4. Download SHA match (1.2 MiB round-trip)",
                    $"key={info.StorageKey}");
        }
    }
    catch (Exception ex)
    {
        Fail("4. Download SHA match", $"{ex.GetType().Name}: {ex.Message}");
    }
}

// ── 5. Second upload to same kind creates v2 + advances current ─────────
try
{
    var bytes2 = new byte[700_000];   // 700 KB, different content
    new Random(13).NextBytes(bytes2);
    using var s = new MemoryStream(bytes2);
    var up2 = await drawingsSvc.UploadAsync(
        revisionId: revision.Id,
        kind: DrawingKind.CustomerDrawing,
        originalFileName: "customer-revised.pdf",
        contentType: "application/pdf",
        content: s,
        changeReason: "Updated die outline",
        actor: "engineer.demo",
        actorRole: "Engineer");

    if (up2.VersionNo != 2)
    {
        Fail("5. Second upload → v2", $"expected v2 got v{up2.VersionNo}");
    }
    else
    {
        var dr = await db.Drawings
            .FirstOrDefaultAsync(d => d.ProductRevisionId == revision.Id && d.Kind == DrawingKind.CustomerDrawing);
        if (dr is null || dr.CurrentVersionId != up2.VersionId)
            Fail("5. CurrentVersionId advanced to v2",
                $"expected {up2.VersionId} got {dr?.CurrentVersionId?.ToString() ?? "null"}");
        else
            Pass("5. Second upload → v2 + CurrentVersionId advanced",
                $"v2.id={up2.VersionId}");
    }
}
catch (Exception ex)
{
    Fail("5. Second upload + advance", $"{ex.GetType().Name}: {ex.Message}");
}

// ── 6. Bad extension rejected + DB rolls back ──────────────────────────
var drawingCountBefore = await db.Drawings.CountAsync();
var versionCountBefore = await db.DrawingVersions.CountAsync();
try
{
    using var s = new MemoryStream(new byte[500]);
    await drawingsSvc.UploadAsync(
        revisionId: revision.Id,
        kind: DrawingKind.NpiPrintLayout,
        originalFileName: "malicious.exe",
        contentType: "application/octet-stream",
        content: s,
        changeReason: null,
        actor: "engineer.demo",
        actorRole: "Engineer");
    Fail("6. Bad extension rejected + rollback", ".exe upload was accepted");
}
catch (InvalidOperationException ex) when (ex.Message.Contains("not in allowlist"))
{
    var drawingCountAfter = await db.Drawings.CountAsync();
    var versionCountAfter = await db.DrawingVersions.CountAsync();
    if (drawingCountAfter != drawingCountBefore || versionCountAfter != versionCountBefore)
        Fail("6. Bad extension rejected + rollback",
            $"orphan rows: drawings {drawingCountBefore}->{drawingCountAfter}, versions {versionCountBefore}->{versionCountAfter}");
    else
        Pass("6. Bad extension rejected + rollback", "no orphan Drawing or Version row");
}
catch (Exception ex)
{
    Fail("6. Bad extension rejected", $"wrong type {ex.GetType().Name}: {ex.Message}");
}

// ── 7. Unauthorised role rejected (Operator) ───────────────────────────
try
{
    using var s = new MemoryStream(new byte[500]);
    await drawingsSvc.UploadAsync(
        revisionId: revision.Id,
        kind: DrawingKind.FqcChecksheet,
        originalFileName: "checksheet.pdf",
        contentType: "application/pdf",
        content: s,
        changeReason: null,
        actor: "operator.demo",
        actorRole: "Operator");
    Fail("7. RBAC reject Operator role", "Operator upload was accepted");
}
catch (UnauthorizedAccessException)
{
    Pass("7. RBAC reject Operator role", "throw OK");
}
catch (Exception ex)
{
    Fail("7. RBAC reject Operator role", $"wrong type {ex.GetType().Name}");
}

// ── 8. GetForDownload with wrong revision returns null (scoped check) ──
if (upRes is not null)
{
    var info = await drawingsSvc.GetForDownloadAsync(upRes.VersionId, expectedRevisionId: 9999);
    if (info is not null)
        Fail("8. Download scope: wrong revisionId rejected", "service returned the blob");
    else
        Pass("8. Download scope: wrong revisionId rejected", "returned null");
}

Console.WriteLine();
Console.WriteLine($"  Result: PASS {pass}  FAIL {fail}");

// Clean up.
try { db.Dispose(); } catch { }
try { Directory.Delete(tmpRoot, recursive: true); } catch { }
return fail;


// ─────────────────────────────────────────────────────────────────────
sealed class InMemoryAuditWriter : IAuditWriter
{
    public Task EmitAsync(string action, string actor, string actorRole,
        string? targetType = null, string? targetId = null,
        string? detail = null, string source = "Web")
    {
        // No-op for test purposes — service just needs the call to succeed.
        return Task.CompletedTask;
    }
}
