using CCL.MES.Application.Services;
using CCL.MES.Domain;
using CCL.MES.Domain.Auth;
using CCL.MES.Infrastructure.Storage;
using CCL.MES.Tests.Integration._Support;
using Xunit;

namespace CCL.MES.Tests.Integration;

/// <summary>
/// Phase 9 T2b — Integration-level RBAC matrix for the 4 services that
/// gate mutations through a private <c>_editorRoles</c> HashSet +
/// <c>RequireEditorRole</c> static throw helper. Complements T1's
/// <see cref="CCL.MES.Tests.Unit.DrawingsService_CanActAsTests"/> by
/// exercising the SERVICE entry points end-to-end on a real EF context
/// + isolated /tmp SQLite — proves the guard fires BEFORE any DB touch.
///
/// <para>
/// Editor-role matrix locked in:
/// <list type="bullet">
/// <item><see cref="IqcService"/>            — Admin / Supervisor / QC</item>
/// <item><see cref="SpecQcCaptureService"/>  — Admin / Engineer</item>
/// <item><see cref="SpecQcWindowService"/>   — Admin / Engineer</item>
/// <item><see cref="DrawingsService"/>       — Admin / Engineer</item>
/// </list>
/// </para>
/// </summary>
public sealed class RbacServiceTests : IDisposable
{
    private readonly IsolatedDbFixture _fx;
    private readonly InMemoryAuditWriter _audit;
    private readonly string _blobRoot;
    private readonly FilesystemBlobStore _blobStore;

    public RbacServiceTests()
    {
        _fx = new IsolatedDbFixture();
        _audit = new InMemoryAuditWriter();
        _blobRoot = Path.Combine(Path.GetTempPath(), $"ccl-rbac-blob-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_blobRoot);
        _blobStore = new FilesystemBlobStore(new BlobStoreOptions
        {
            DataDir  = _blobRoot,
            MaxBytes = 1024 * 1024,
        });
    }

    public void Dispose()
    {
        _fx.Dispose();
        try { Directory.Delete(_blobRoot, recursive: true); } catch { /* best effort */ }
    }

    // ── IqcService — editor set {Admin, Supervisor, QC} ───────────────

    [Theory]
    [InlineData(UserRole.Engineer)]
    [InlineData(UserRole.Operator)]
    [InlineData("")]                                          // anonymous
    [InlineData("HACKER")]                                    // unknown role
    public async Task IqcService_CreateAsync_rejects_role_not_in_editor_set(string badRole)
    {
        var svc = new IqcService(_fx.NewContext(), _audit);
        var req = MinimalIqcRequest();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            svc.CreateAsync(req, actor: "rbac.demo", actorRole: badRole));
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Supervisor)]
    [InlineData(UserRole.Qc)]
    public async Task IqcService_CreateAsync_passes_role_in_editor_set(string allowedRole)
    {
        var svc = new IqcService(_fx.NewContext(), _audit);
        var req = MinimalIqcRequest();

        var insp = await svc.CreateAsync(req, actor: "rbac.demo", actorRole: allowedRole);

        Assert.NotNull(insp);
        Assert.Equal(QcResult.Pending, insp.Result);
    }

    [Theory]
    [InlineData(UserRole.Engineer)]
    [InlineData(UserRole.Operator)]
    public async Task IqcService_ApproveAsync_rejects_role_not_in_editor_set(string badRole)
    {
        // Seed an inspection first using a privileged role so we have a real id.
        var svc = new IqcService(_fx.NewContext(), _audit);
        var seed = await svc.CreateAsync(MinimalIqcRequest(), actor: "admin", actorRole: UserRole.Admin);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            svc.ApproveAsync(seed.Id, pass: true, actor: "rbac.demo", actorRole: badRole));
    }

    // ── SpecQcWindowService — editor set {Admin, Engineer} ────────────

    [Theory]
    [InlineData(UserRole.Supervisor)]
    [InlineData(UserRole.Qc)]
    [InlineData(UserRole.Operator)]
    [InlineData("")]
    public async Task SpecQcWindowService_UpsertStageAsync_rejects_role_not_in_editor_set(string badRole)
    {
        var svc = new SpecQcWindowService(_fx.NewContext(), _audit);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            svc.UpsertStageAsync(
                revisionId: _fx.SeedRevisionId,
                stage:      QcStage.IpqcPrint,
                rows:       new List<QcCriterionRow>(),
                actor:      "rbac.demo",
                actorRole:  badRole));
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Engineer)]
    public async Task SpecQcWindowService_UpsertStageAsync_passes_role_in_editor_set(string allowedRole)
    {
        var svc = new SpecQcWindowService(_fx.NewContext(), _audit);

        var window = await svc.UpsertStageAsync(
            revisionId: _fx.SeedRevisionId,
            stage:      QcStage.IpqcPrint,
            rows:       new List<QcCriterionRow>(),
            actor:      "rbac.demo",
            actorRole:  allowedRole);

        Assert.NotNull(window);
    }

    // ── SpecQcCaptureService — editor set {Admin, Engineer} ───────────

    [Theory]
    [InlineData(UserRole.Supervisor)]
    [InlineData(UserRole.Qc)]
    [InlineData(UserRole.Operator)]
    [InlineData("")]
    public async Task SpecQcCaptureService_CaptureAsync_rejects_role_not_in_editor_set(string badRole)
    {
        var svc = new SpecQcCaptureService(_fx.NewContext(), _audit);

        var req = new QcCaptureRequest(
            CriterionId: 1,
            Result:      QcCaptureResult.Pass,
            Measurement: "1.0",
            NgReasonCode: null,
            Comment:     null);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            svc.CaptureAsync(
                revisionId: _fx.SeedRevisionId,
                request:    req,
                actor:      "rbac.demo",
                actorRole:  badRole));
    }

    // ── DrawingsService — editor set {Admin, Engineer} ────────────────

    [Theory]
    [InlineData(UserRole.Supervisor)]
    [InlineData(UserRole.Qc)]
    [InlineData(UserRole.Operator)]
    [InlineData("")]
    public async Task DrawingsService_UploadAsync_rejects_role_not_in_editor_set(string badRole)
    {
        var svc = new DrawingsService(_fx.NewContext(), _blobStore, _audit);
        using var s = new MemoryStream(new byte[100]);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            svc.UploadAsync(
                revisionId:       _fx.SeedRevisionId,
                kind:             DrawingKind.CustomerDrawing,
                originalFileName: "rbac.pdf",
                contentType:      "application/pdf",
                content:          s,
                changeReason:     null,
                actor:            "rbac.demo",
                actorRole:        badRole));
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Engineer)]
    public async Task DrawingsService_UploadAsync_passes_role_in_editor_set(string allowedRole)
    {
        var svc = new DrawingsService(_fx.NewContext(), _blobStore, _audit);
        using var s = new MemoryStream(new byte[100]);

        var r = await svc.UploadAsync(
            revisionId:       _fx.SeedRevisionId,
            kind:             DrawingKind.CustomerDrawing,
            originalFileName: "rbac.pdf",
            contentType:      "application/pdf",
            content:          s,
            changeReason:     null,
            actor:            "rbac.demo",
            actorRole:        allowedRole);

        Assert.True(r.VersionId > 0);
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static CreateIqcRequest MinimalIqcRequest() => new(
        PartNo:        "PN-IQC-TEST",
        BatchNumber:   "B-001",
        LotNumber:     null,
        ReceivedDate:  new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        SupplierName:  "Test Supplier",
        Quantity:      100,
        UomQty:        "pcs",
        InspectorId:   "rbac.demo",
        SampleSize:    8,
        Details:       new List<CreateIqcDetail>());
}
