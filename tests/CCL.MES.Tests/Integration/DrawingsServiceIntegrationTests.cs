using System.Security.Cryptography;
using CCL.MES.Application.Services;
using CCL.MES.Domain;
using CCL.MES.Infrastructure.Storage;
using CCL.MES.Tests.Integration._Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CCL.MES.Tests.Integration;

/// <summary>
/// Phase 9 T2a — Port of <c>scripts/VerifyDrawingsUpload</c> 18 cases:
/// PR-D-5b upload + PR-D-5c 3-role approval chain. Real
/// <see cref="DrawingsService"/> + <see cref="FilesystemBlobStore"/> +
/// EF SQLite under <c>/tmp</c>. Validates the high-risk preservation
/// guarantees verified by hand through Phase 8 PR-D-5b/c sprints.
///
/// <para>
/// Each <c>[Fact]</c> gets a fresh <see cref="IsolatedDbFixture"/> via
/// the xUnit constructor-per-test contract (the class implements
/// <see cref="IDisposable"/> directly). This makes every test
/// hermetic — no cross-test state, no order dependence. Cost: ~50 ms
/// per fixture boot (Migrate + minimal seed). Acceptable for ~18
/// cases (~1s total).
/// </para>
/// </summary>
public sealed class DrawingsServiceIntegrationTests : IDisposable
{
    private readonly IsolatedDbFixture _fx;
    private readonly string _blobRoot;
    private readonly FilesystemBlobStore _blobStore;
    private readonly InMemoryAuditWriter _audit;
    private readonly DrawingsService _svc;

    public DrawingsServiceIntegrationTests()
    {
        _fx = new IsolatedDbFixture();
        _blobRoot = Path.Combine(Path.GetTempPath(), $"ccl-drawings-blob-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_blobRoot);
        _blobStore = new FilesystemBlobStore(new BlobStoreOptions
        {
            DataDir  = _blobRoot,
            MaxBytes = 10 * 1024 * 1024,
        });
        _audit = new InMemoryAuditWriter();
        _svc = new DrawingsService(_fx.NewContext(), _blobStore, _audit);
    }

    public void Dispose()
    {
        _fx.Dispose();
        try { Directory.Delete(_blobRoot, recursive: true); } catch { /* best effort */ }
    }

    // ── 1. Upload large file (> Blazor InputFile 500 KB default) ──────

    [Fact]
    public async Task Upload_1_2_MiB_file_succeeds_end_to_end()
    {
        var (bytes, sha) = Payload(1_258_000);
        using var s = new MemoryStream(bytes);

        var r = await _svc.UploadAsync(
            revisionId:        _fx.SeedRevisionId,
            kind:              DrawingKind.CustomerDrawing,
            originalFileName:  "customer-original.pdf",
            contentType:       "application/pdf",
            content:           s,
            changeReason:      "Initial baseline upload",
            actor:             "engineer.demo",
            actorRole:         "Engineer");

        Assert.True(r.VersionId > 0);
        Assert.Equal(1, r.VersionNo);
        Assert.Equal(bytes.Length, r.SizeBytes);
        Assert.Equal(sha, r.Sha256Hex);
    }

    // ── 2 + 3. FileHash + FileSize match input ─────────────────────────

    [Fact]
    public async Task FileHash_and_FileSize_match_input_bytes()
    {
        var (bytes, sha) = Payload(700_000);
        using var s = new MemoryStream(bytes);
        var r = await _svc.UploadAsync(_fx.SeedRevisionId, DrawingKind.CustomerDrawing,
            "customer-original.pdf", "application/pdf", s, null, "engineer.demo", "Engineer");

        Assert.Equal(sha, r.Sha256Hex);
        Assert.Equal(bytes.Length, r.SizeBytes);
    }

    // ── 4. StorageKey resolves + download round-trip SHA match ─────────

    [Fact]
    public async Task GetForDownload_resolves_storage_key_and_bytes_round_trip()
    {
        var (bytes, sha) = Payload(50_000);
        using var s = new MemoryStream(bytes);
        var up = await _svc.UploadAsync(_fx.SeedRevisionId, DrawingKind.CustomerDrawing,
            "x.pdf", "application/pdf", s, null, "engineer.demo", "Engineer");

        var info = await _svc.GetForDownloadAsync(up.VersionId, _fx.SeedRevisionId);
        Assert.NotNull(info);
        using var blob = await _blobStore.GetAsync(info!.StorageKey);
        using var ms = new MemoryStream();
        await blob.CopyToAsync(ms);
        Assert.Equal(sha, Sha256Hex(ms.ToArray()));
    }

    // ── 5. Second upload to same kind creates v2 + bumps CurrentVersionId ─

    [Fact]
    public async Task Second_upload_creates_v2_and_bumps_drawing_CurrentVersionId()
    {
        var (b1, _) = Payload(50_000);
        using (var s = new MemoryStream(b1))
            await _svc.UploadAsync(_fx.SeedRevisionId, DrawingKind.CustomerDrawing,
                "v1.pdf", "application/pdf", s, null, "engineer.demo", "Engineer");

        var (b2, _) = Payload(60_000);
        DrawingUploadResult up2;
        using (var s = new MemoryStream(b2))
            up2 = await _svc.UploadAsync(_fx.SeedRevisionId, DrawingKind.CustomerDrawing,
                "v2.pdf", "application/pdf", s, "rev2", "engineer.demo", "Engineer");

        Assert.Equal(2, up2.VersionNo);

        using var db = _fx.NewContext();
        var dr = await db.Drawings.AsNoTracking()
            .FirstAsync(d => d.ProductRevisionId == _fx.SeedRevisionId && d.Kind == DrawingKind.CustomerDrawing);
        Assert.Equal(up2.VersionId, dr.CurrentVersionId);
    }

    // ── 6. Bad extension rejected + DB rolls back ──────────────────────

    [Fact]
    public async Task Bad_extension_rejected_and_no_orphan_rows_persist()
    {
        using var dbBefore = _fx.NewContext();
        var drawingsBefore = await dbBefore.Drawings.CountAsync();
        var versionsBefore = await dbBefore.DrawingVersions.CountAsync();

        using var s = new MemoryStream(new byte[500]);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _svc.UploadAsync(_fx.SeedRevisionId, DrawingKind.NpiPrintLayout,
                "malicious.exe", "application/octet-stream", s,
                null, "engineer.demo", "Engineer"));

        using var dbAfter = _fx.NewContext();
        Assert.Equal(drawingsBefore, await dbAfter.Drawings.CountAsync());
        Assert.Equal(versionsBefore, await dbAfter.DrawingVersions.CountAsync());
    }

    // ── 7. Unauthorised role rejected before touching the store ────────

    [Fact]
    public async Task Operator_role_rejected_with_UnauthorizedAccessException()
    {
        using var s = new MemoryStream(new byte[500]);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _svc.UploadAsync(_fx.SeedRevisionId, DrawingKind.FqcChecksheet,
                "checksheet.pdf", "application/pdf", s,
                null, "operator.demo", "Operator"));
    }

    // ── 8. Download scoped — wrong revision returns null ───────────────

    [Fact]
    public async Task GetForDownload_returns_null_when_revision_does_not_match()
    {
        var (b, _) = Payload(20_000);
        using var s = new MemoryStream(b);
        var up = await _svc.UploadAsync(_fx.SeedRevisionId, DrawingKind.CustomerDrawing,
            "x.pdf", "application/pdf", s, null, "engineer.demo", "Engineer");

        var info = await _svc.GetForDownloadAsync(up.VersionId, expectedRevisionId: 99_999);
        Assert.Null(info);
    }

    // ── 9. Upload creates 3 Pending approval rows (Npi + Prod + Qc) ────

    [Fact]
    public async Task Upload_creates_three_pending_approval_rows()
    {
        var up = await UploadFreshAsync(_fx.SeedRevisionId, 100, "approval-v1.pdf", "uploader", "Engineer");

        using var db = _fx.NewContext();
        var apCount = await db.DrawingApprovals.CountAsync(a => a.DrawingVersionId == up.VersionId);
        Assert.Equal(3, apCount);
    }

    // ── 10. NPI approve → version → PendingApproval ────────────────────

    [Fact]
    public async Task NPI_approve_advances_version_to_PendingApproval()
    {
        var up = await UploadFreshAsync(_fx.SeedRevisionId, 100, "v1.pdf", "uploader", "Engineer");
        var r = await _svc.DecideAsync(
            revisionId:       _fx.SeedRevisionId,
            versionId:        up.VersionId,
            role:             DrawingApprovalRole.Npi,
            decision:         DrawingApprovalStatus.Approved,
            comment:          "looks fine to NPI",
            actor:            "alice.npi",
            actorRole:        "Engineer",
            actorDepartment:  "npi");
        Assert.Equal(DrawingVersionStatus.PendingApproval, r.VersionStatus);
    }

    // ── 11. 2/3 approve still PendingApproval ──────────────────────────

    [Fact]
    public async Task Two_of_three_approves_keeps_version_PendingApproval()
    {
        var up = await UploadFreshAsync(_fx.SeedRevisionId, 100, "v1.pdf", "uploader", "Engineer");
        await _svc.DecideAsync(_fx.SeedRevisionId, up.VersionId, DrawingApprovalRole.Npi,
            DrawingApprovalStatus.Approved, null, "alice.npi", "Engineer", "npi");
        var r = await _svc.DecideAsync(_fx.SeedRevisionId, up.VersionId, DrawingApprovalRole.Production,
            DrawingApprovalStatus.Approved, null, "bob.prod", "Engineer", "production");
        Assert.Equal(DrawingVersionStatus.PendingApproval, r.VersionStatus);
    }

    // ── 12. 3/3 approve → Approved + Drawing.CurrentVersionId set ──────

    [Fact]
    public async Task Three_of_three_approves_promotes_version_to_Approved()
    {
        var up = await UploadFreshAsync(_fx.SeedRevisionId, 100, "v1.pdf", "uploader", "Engineer");
        await _svc.DecideAsync(_fx.SeedRevisionId, up.VersionId, DrawingApprovalRole.Npi,
            DrawingApprovalStatus.Approved, null, "alice.npi", "Engineer", "npi");
        await _svc.DecideAsync(_fx.SeedRevisionId, up.VersionId, DrawingApprovalRole.Production,
            DrawingApprovalStatus.Approved, null, "bob.prod", "Engineer", "production");
        var r = await _svc.DecideAsync(_fx.SeedRevisionId, up.VersionId, DrawingApprovalRole.Qc,
            DrawingApprovalStatus.Approved, null, "carol.qc", "Engineer", "qc");

        Assert.Equal(DrawingVersionStatus.Approved, r.VersionStatus);

        using var db = _fx.NewContext();
        var dr = await db.Drawings.AsNoTracking()
            .FirstAsync(d => d.ProductRevisionId == _fx.SeedRevisionId
                          && d.Kind == DrawingKind.NpiPrintLayout);
        Assert.Equal(up.VersionId, dr.CurrentVersionId);
        Assert.Equal(DrawingStatus.Approved, dr.Status);
    }

    // ── 13. Reject with empty comment throws ───────────────────────────

    [Fact]
    public async Task Reject_with_empty_comment_throws_InvalidOperationException()
    {
        var up = await UploadFreshAsync(_fx.SeedRevisionId, 100, "v1.pdf", "uploader", "Engineer");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _svc.DecideAsync(_fx.SeedRevisionId, up.VersionId, DrawingApprovalRole.Npi,
                DrawingApprovalStatus.Rejected, comment: "", "alice.npi", "Engineer", "npi"));
        Assert.Contains("Comment is required", ex.Message);
    }

    // ── 14. NPI reject with comment → version Rejected ─────────────────

    [Fact]
    public async Task NPI_reject_with_comment_marks_version_Rejected()
    {
        var up = await UploadFreshAsync(_fx.SeedRevisionId, 100, "v1.pdf", "uploader", "Engineer");
        var r = await _svc.DecideAsync(_fx.SeedRevisionId, up.VersionId, DrawingApprovalRole.Npi,
            DrawingApprovalStatus.Rejected, "needs revision", "alice.npi", "Engineer", "npi");
        Assert.Equal(DrawingVersionStatus.Rejected, r.VersionStatus);
    }

    // ── 15. Newly-approved v2 supersedes earlier non-rejected v1 ───────

    [Fact]
    public async Task Approved_v2_supersedes_earlier_non_rejected_v1()
    {
        var v1 = await UploadFreshAsync(_fx.SeedRevisionId, 100, "v1.pdf", "uploader", "Engineer");
        // Approve v1 fully so it becomes the active version first.
        await _svc.DecideAsync(_fx.SeedRevisionId, v1.VersionId, DrawingApprovalRole.Npi,        DrawingApprovalStatus.Approved, null, "alice.npi",  "Engineer", "npi");
        await _svc.DecideAsync(_fx.SeedRevisionId, v1.VersionId, DrawingApprovalRole.Production, DrawingApprovalStatus.Approved, null, "bob.prod",   "Engineer", "production");
        await _svc.DecideAsync(_fx.SeedRevisionId, v1.VersionId, DrawingApprovalRole.Qc,         DrawingApprovalStatus.Approved, null, "carol.qc",  "Engineer", "qc");

        // v2 fresh upload, approve fully — should supersede v1.
        var v2 = await UploadFreshAsync(_fx.SeedRevisionId, 200, "v2.pdf", "uploader", "Engineer");
        await _svc.DecideAsync(_fx.SeedRevisionId, v2.VersionId, DrawingApprovalRole.Npi,        DrawingApprovalStatus.Approved, null, "alice.npi",  "Engineer", "npi");
        await _svc.DecideAsync(_fx.SeedRevisionId, v2.VersionId, DrawingApprovalRole.Production, DrawingApprovalStatus.Approved, null, "bob.prod",   "Engineer", "production");
        var sup = await _svc.DecideAsync(_fx.SeedRevisionId, v2.VersionId, DrawingApprovalRole.Qc,
            DrawingApprovalStatus.Approved, null, "carol.qc", "Engineer", "qc");

        Assert.True(sup.SupersededCount >= 1);

        using var db = _fx.NewContext();
        var v1Refresh = await db.DrawingVersions.AsNoTracking().FirstAsync(v => v.Id == v1.VersionId);
        var dr = await db.Drawings.AsNoTracking()
            .FirstAsync(d => d.ProductRevisionId == _fx.SeedRevisionId && d.Kind == DrawingKind.NpiPrintLayout);
        Assert.Equal(DrawingVersionStatus.Superseded, v1Refresh.Status);
        Assert.Equal(v2.VersionId, dr.CurrentVersionId);
    }

    // ── 16. RBAC mismatch — Engineer + wrong dept cannot decide ────────

    [Fact]
    public async Task Engineer_with_wrong_department_cannot_decide_NPI_chip()
    {
        var up = await UploadFreshAsync(_fx.SeedRevisionId, 100, "rbac.pdf", "uploader", "Engineer");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _svc.DecideAsync(_fx.SeedRevisionId, up.VersionId, DrawingApprovalRole.Npi,
                DrawingApprovalStatus.Approved, null, "bob.prod", "Engineer", "production"));
    }

    // ── 17. Re-decide allowed (Approve → Reject flip) ──────────────────

    [Fact]
    public async Task Re_decide_can_flip_Approve_to_Reject()
    {
        var up = await UploadFreshAsync(_fx.SeedRevisionId, 100, "redecide.pdf", "uploader", "Engineer");
        await _svc.DecideAsync(_fx.SeedRevisionId, up.VersionId, DrawingApprovalRole.Npi,
            DrawingApprovalStatus.Approved, null, "alice.npi", "Engineer", "npi");
        var r = await _svc.DecideAsync(_fx.SeedRevisionId, up.VersionId, DrawingApprovalRole.Npi,
            DrawingApprovalStatus.Rejected, "changed my mind", "alice.npi", "Engineer", "npi");
        Assert.Equal(DrawingVersionStatus.Rejected, r.VersionStatus);
    }

    // ── 18. Admin override — no department needed on any chip ──────────

    [Fact]
    public async Task Admin_can_override_any_chip_without_department()
    {
        var up = await UploadFreshAsync(_fx.SeedRevisionId, 100, "admin.pdf", "uploader", "Engineer");
        var r = await _svc.DecideAsync(_fx.SeedRevisionId, up.VersionId, DrawingApprovalRole.Qc,
            DrawingApprovalStatus.Approved, null, "boss.admin", "Admin", actorDepartment: null);
        Assert.Equal(DrawingVersionStatus.PendingApproval, r.VersionStatus);
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private Task<DrawingUploadResult> UploadFreshAsync(
        long revId, int sizeBytes, string fname, string actor, string actorRole)
    {
        var (data, _) = Payload(sizeBytes);
        var s = new MemoryStream(data);
        return _svc.UploadAsync(
            revisionId:       revId,
            kind:             DrawingKind.NpiPrintLayout,
            originalFileName: fname,
            contentType:      "application/pdf",
            content:          s,
            changeReason:     null,
            actor:            actor,
            actorRole:        actorRole);
    }

    private static (byte[] bytes, string sha) Payload(int size)
    {
        var b = new byte[size];
        new Random(size).NextBytes(b);
        return (b, Sha256Hex(b));
    }

    private static string Sha256Hex(byte[] data)
        => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
