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
    // EF change tracker still holds the rolled-back Drawing in Added state;
    // clear so subsequent tests do not double-insert it.
    db.ChangeTracker.Clear();
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

// ── PR-D-5c — Approval chain test cases (9-16) ────────────────────────

// Use the v1 + v2 customer-drawing chain that PR-D-5b tests created.
// Their approval rows were inserted by UploadAsync.

// Fresh helper: bootstrap a new revision so PR-D-5b's v1/v2 don't pollute.
var revision2 = new ProductRevision
{
    ProductId = product.Id,
    SpecCode = "SPEC-TST-002",
    Title = "Approval-test spec",
    RevisionCode = "B",
    Status = ProductRevisionStatus.Draft,
};
db.ProductRevisions.Add(revision2);
await db.SaveChangesAsync();

// Helper to upload v(n) as engineer for npi department.
async Task<DrawingUploadResult> UploadFreshAsync(long revId, int sizeBytes, string fname)
{
    var data = new byte[sizeBytes];
    new Random(sizeBytes).NextBytes(data);
    using var s = new MemoryStream(data);
    return await drawingsSvc.UploadAsync(
        revisionId: revId,
        kind: DrawingKind.NpiPrintLayout,
        originalFileName: fname,
        contentType: "application/pdf",
        content: s,
        changeReason: null,
        actor: "uploader",
        actorRole: "Engineer");
}

DrawingUploadResult? v1 = null;
try
{
    v1 = await UploadFreshAsync(revision2.Id, 100, "approval-test-v1.pdf");
    // Verify 3 approval rows created at upload time.
    var apCount = await db.DrawingApprovals.CountAsync(a => a.DrawingVersionId == v1.VersionId);
    if (apCount != 3)
        Fail("9. Upload creates 3 approval rows", $"expected 3 got {apCount}");
    else
        Pass("9. Upload creates 3 approval rows", "Npi+Production+Qc all Pending");
}
catch (Exception ex)
{
    Fail("9. Upload + approval row creation", $"{ex.GetType().Name}: {ex.Message}");
}

// ── 10. Decide NPI Approve → version → PendingApproval ──
if (v1 is not null)
{
    try
    {
        var r = await drawingsSvc.DecideAsync(
            revisionId: revision2.Id,
            versionId: v1.VersionId,
            role: DrawingApprovalRole.Npi,
            decision: DrawingApprovalStatus.Approved,
            comment: "looks fine to NPI",
            actor: "alice.npi",
            actorRole: "Engineer",
            actorDepartment: "npi");
        if (r.VersionStatus != DrawingVersionStatus.PendingApproval)
            Fail("10. NPI Approve → PendingApproval", $"got {r.VersionStatus}");
        else
            Pass("10. NPI Approve → PendingApproval", $"drawing_status={r.DrawingStatus}");
    }
    catch (Exception ex) { Fail("10. NPI Approve", $"{ex.GetType().Name}: {ex.Message}"); }
}

// ── 11. Decide Production Approve → still PendingApproval ──
if (v1 is not null)
{
    try
    {
        var r = await drawingsSvc.DecideAsync(
            revisionId: revision2.Id,
            versionId: v1.VersionId,
            role: DrawingApprovalRole.Production,
            decision: DrawingApprovalStatus.Approved,
            comment: null,
            actor: "bob.prod",
            actorRole: "Engineer",
            actorDepartment: "production");
        if (r.VersionStatus != DrawingVersionStatus.PendingApproval)
            Fail("11. 2/3 approved still PendingApproval", $"got {r.VersionStatus}");
        else
            Pass("11. 2/3 approved → PendingApproval", "");
    }
    catch (Exception ex) { Fail("11. Production Approve", $"{ex.GetType().Name}: {ex.Message}"); }
}

// ── 12. Decide QC Approve → all 3 OK → Approved + Drawing.CurrentVersionId set ──
if (v1 is not null)
{
    try
    {
        var r = await drawingsSvc.DecideAsync(
            revisionId: revision2.Id,
            versionId: v1.VersionId,
            role: DrawingApprovalRole.Qc,
            decision: DrawingApprovalStatus.Approved,
            comment: null,
            actor: "carol.qc",
            actorRole: "Engineer",
            actorDepartment: "qc");
        var dr = await db.Drawings.FirstAsync(d => d.ProductRevisionId == revision2.Id && d.Kind == DrawingKind.NpiPrintLayout);
        if (r.VersionStatus != DrawingVersionStatus.Approved)
            Fail("12. 3/3 → Approved", $"version status got {r.VersionStatus}");
        else if (dr.CurrentVersionId != v1.VersionId)
            Fail("12. CurrentVersionId set to v1", $"got {dr.CurrentVersionId}");
        else if (dr.Status != DrawingStatus.Approved)
            Fail("12. Drawing.Status = Approved", $"got {dr.Status}");
        else
            Pass("12. 3/3 Approved → Drawing.Approved + CurrentVersionId", $"v{v1.VersionNo}");
    }
    catch (Exception ex) { Fail("12. QC Approve cascade", $"{ex.GetType().Name}: {ex.Message}"); }
}

// ── 13. Reject with empty comment → throws ──
if (v1 is not null)
{
    // Use a different version so we don't poison v1; upload v2.
    try
    {
        var v2 = await UploadFreshAsync(revision2.Id, 200, "approval-test-v2.pdf");
        try
        {
            await drawingsSvc.DecideAsync(
                revisionId: revision2.Id,
                versionId: v2.VersionId,
                role: DrawingApprovalRole.Npi,
                decision: DrawingApprovalStatus.Rejected,
                comment: "",
                actor: "alice.npi",
                actorRole: "Engineer",
                actorDepartment: "npi");
            Fail("13. Reject with empty comment", "did not throw");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Comment is required"))
        {
            Pass("13. Reject with empty comment → throws", "");
        }

        // ── 14. NPI Reject with comment → version → Rejected ──
        var rj = await drawingsSvc.DecideAsync(
            revisionId: revision2.Id,
            versionId: v2.VersionId,
            role: DrawingApprovalRole.Npi,
            decision: DrawingApprovalStatus.Rejected,
            comment: "needs revision before production approval",
            actor: "alice.npi",
            actorRole: "Engineer",
            actorDepartment: "npi");
        if (rj.VersionStatus != DrawingVersionStatus.Rejected)
            Fail("14. 1 Reject → version.Status=Rejected", $"got {rj.VersionStatus}");
        else
            Pass("14. 1 Reject → version=Rejected (drawing.Status stays Approved from v1)", "");

        // ── 15. Upload v3 + 3-chip approve → v1 superseded ──
        var v3 = await UploadFreshAsync(revision2.Id, 300, "approval-test-v3.pdf");
        await drawingsSvc.DecideAsync(revision2.Id, v3.VersionId, DrawingApprovalRole.Npi,        DrawingApprovalStatus.Approved, null, "alice.npi",  "Engineer", "npi");
        await drawingsSvc.DecideAsync(revision2.Id, v3.VersionId, DrawingApprovalRole.Production, DrawingApprovalStatus.Approved, null, "bob.prod",   "Engineer", "production");
        var rSup = await drawingsSvc.DecideAsync(revision2.Id, v3.VersionId, DrawingApprovalRole.Qc, DrawingApprovalStatus.Approved, null, "carol.qc", "Engineer", "qc");

        // Reload v1 from DB to check status flip.
        var v1Refresh = await db.DrawingVersions.AsNoTracking().FirstAsync(v => v.Id == v1.VersionId);
        var v2Refresh = await db.DrawingVersions.AsNoTracking().FirstAsync(v => v.Id == v2.VersionId);
        var drRefresh = await db.Drawings.AsNoTracking().FirstAsync(d => d.ProductRevisionId == revision2.Id && d.Kind == DrawingKind.NpiPrintLayout);

        if (v1Refresh.Status != DrawingVersionStatus.Superseded)
            Fail("15. v1 superseded by v3 Approved", $"v1.Status={v1Refresh.Status}");
        else if (v2Refresh.Status != DrawingVersionStatus.Rejected)
            Fail("15. v2 stays Rejected (not Superseded)", $"v2.Status={v2Refresh.Status}");
        else if (drRefresh.CurrentVersionId != v3.VersionId)
            Fail("15. CurrentVersionId → v3", $"got {drRefresh.CurrentVersionId}");
        else
            Pass("15. v3 Approved → v1 Superseded + CurrentVersionId=v3 + v2 stays Rejected", $"superseded_count={rSup.SupersededCount}");
    }
    catch (Exception ex)
    {
        Fail("13-15. Reject/Supersede chain", $"{ex.GetType().Name}: {ex.Message}");
    }
}

// ── 16. RBAC: Engineer + Department=production cannot action NPI chip ──
try
{
    var vrr = await UploadFreshAsync(revision2.Id, 150, "rbac-test.pdf");
    await drawingsSvc.DecideAsync(
        revisionId: revision2.Id,
        versionId: vrr.VersionId,
        role: DrawingApprovalRole.Npi,
        decision: DrawingApprovalStatus.Approved,
        comment: null,
        actor: "bob.prod",
        actorRole: "Engineer",
        actorDepartment: "production");
    Fail("16. RBAC: Eng+Dept=production action NPI chip rejected", "did not throw");
}
catch (UnauthorizedAccessException)
{
    Pass("16. RBAC: Eng+Dept=production action NPI chip rejected", "throw OK");
}
catch (Exception ex) { Fail("16. RBAC mismatch reject", $"wrong type {ex.GetType().Name}"); }

// ── 17. Re-decide allowed: flip Approve → Reject ──
try
{
    var vrd = await UploadFreshAsync(revision2.Id, 175, "redecide-test.pdf");
    await drawingsSvc.DecideAsync(revision2.Id, vrd.VersionId, DrawingApprovalRole.Npi, DrawingApprovalStatus.Approved, null, "alice.npi", "Engineer", "npi");
    var r = await drawingsSvc.DecideAsync(revision2.Id, vrd.VersionId, DrawingApprovalRole.Npi, DrawingApprovalStatus.Rejected, "actually need changes", "alice.npi", "Engineer", "npi");
    if (r.VersionStatus != DrawingVersionStatus.Rejected)
        Fail("17. Re-decide Approve→Reject flips version status", $"got {r.VersionStatus}");
    else
        Pass("17. Re-decide Approve→Reject allowed (flip OK)", "");
}
catch (Exception ex) { Fail("17. Re-decide", $"{ex.GetType().Name}: {ex.Message}"); }

// ── 18. Admin overrides any chip + department ──
try
{
    var vad = await UploadFreshAsync(revision2.Id, 125, "admin-override.pdf");
    var r = await drawingsSvc.DecideAsync(revision2.Id, vad.VersionId, DrawingApprovalRole.Qc, DrawingApprovalStatus.Approved, null, "boss.admin", "Admin", null);
    if (r.VersionStatus != DrawingVersionStatus.PendingApproval)
        Fail("18. Admin override QC chip", $"got {r.VersionStatus}");
    else
        Pass("18. Admin override QC chip (no department needed)", "");
}
catch (Exception ex) { Fail("18. Admin override", $"{ex.GetType().Name}: {ex.Message}"); }

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
