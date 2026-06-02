using CCL.MES.Application;
using CCL.MES.Application.Services;
using CCL.MES.Domain;
using CCL.MES.Domain.Audit;
using CCL.MES.Tests.Integration._Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CCL.MES.Tests.Integration;

/// <summary>
/// Phase 9 T2a — Real-prod-logic integration tests for the Spec
/// lifecycle (PR-L1 Copy + Edit, PR-L2 Revise + Supersede, PR-L3
/// Trash + Restore). Exercises <see cref="SpecService"/> end-to-end
/// against an isolated /tmp SQLite — no mirror, no stub. Audit emits
/// captured via <see cref="InMemoryAuditWriter"/> for assertions on
/// the per-action audit row + JSON detail.
///
/// <para>
/// Per-test fresh fixture (xUnit creates a new class instance per
/// <c>[Fact]</c>) — every test has its own /tmp DB so failures are
/// hermetic and ordering-independent.
/// </para>
/// </summary>
public sealed class SpecLifecycleTests : IDisposable
{
    private readonly IsolatedDbFixture _fx;
    private readonly InMemoryAuditWriter _audit;
    private readonly SpecService _svc;

    public SpecLifecycleTests()
    {
        _fx = new IsolatedDbFixture();
        _audit = new InMemoryAuditWriter();
        _svc = new SpecService(_fx.NewContext(), _audit);
    }

    public void Dispose() => _fx.Dispose();

    // ── PR-L1 — CopyAsync ──────────────────────────────────────────────

    [Fact]
    public async Task Copy_to_new_product_succeeds_with_rev_A_and_audit_row()
    {
        // Seed a second product so the copy lands somewhere with no
        // prior revs → NextAvailableRev returns "A".
        var secondProductId = await SeedSecondProductAsync();
        var r = await _svc.CopyAsync(
            sourceRevisionId: _fx.SeedRevisionId,
            r: new CopySpecRequest { ProductId = secondProductId, SpecCode = "SPEC-COPY-001", Title = "Copy" },
            user: "engineer.demo");

        Assert.Equal(CopyResultKind.Ok, r.Kind);
        Assert.NotNull(r.Revision);
        Assert.Equal("A", r.Revision!.RevisionCode);
        Assert.Equal(secondProductId, r.Revision!.ProductId);

        // Audit row emitted.
        var audit = Assert.Single(_audit.ByAction(AuditAction.SpecCopy));
        Assert.Equal("engineer.demo", audit.Actor);
        Assert.Equal("ProductRevision", audit.TargetType);
        Assert.NotNull(audit.Detail);
    }

    [Fact]
    public async Task Copy_to_same_product_picks_next_available_rev_B()
    {
        // Seeded product already has rev A → next is B.
        var r = await _svc.CopyAsync(
            sourceRevisionId: _fx.SeedRevisionId,
            r: new CopySpecRequest { ProductId = _fx.SeedProductId, SpecCode = "SPEC-COPY-002", Title = "Copy" },
            user: "engineer.demo");

        Assert.Equal(CopyResultKind.Ok, r.Kind);
        Assert.Equal("B", r.Revision!.RevisionCode);
    }

    [Fact]
    public async Task Copy_with_duplicate_SpecCode_rejected()
    {
        // The seeded rev's SpecCode is "SPEC-TST-001" — re-use to force collision.
        var r = await _svc.CopyAsync(
            sourceRevisionId: _fx.SeedRevisionId,
            r: new CopySpecRequest { ProductId = _fx.SeedProductId, SpecCode = "SPEC-TST-001", Title = "Dup" },
            user: "engineer.demo");

        Assert.Equal(CopyResultKind.DuplicateCode, r.Kind);
        Assert.Null(r.Revision);
        Assert.Empty(_audit.ByAction(AuditAction.SpecCopy));
    }

    [Fact]
    public async Task Copy_with_missing_source_returns_SourceNotFound()
    {
        var r = await _svc.CopyAsync(
            sourceRevisionId: 99_999,
            r: new CopySpecRequest { ProductId = _fx.SeedProductId, SpecCode = "SPEC-COPY-X", Title = "X" },
            user: "engineer.demo");

        Assert.Equal(CopyResultKind.SourceNotFound, r.Kind);
    }

    // ── PR-L1 — UpdateAsync (Draft-only gate) ─────────────────────────

    [Fact]
    public async Task Update_succeeds_on_Draft_with_title_change()
    {
        var r = await _svc.UpdateAsync(
            revisionId: _fx.SeedRevisionId,
            r: new UpdateSpecRequest { Title = "Renamed spec" },
            user: "engineer.demo");

        Assert.Equal(UpdateResultKind.Ok, r.Kind);
        Assert.Equal("Renamed spec", r.Revision!.Title);
    }

    [Fact]
    public async Task Update_returns_NoChanges_when_no_fields_modified()
    {
        var r = await _svc.UpdateAsync(
            revisionId: _fx.SeedRevisionId,
            r: new UpdateSpecRequest(),                // all nullable, no diff
            user: "engineer.demo");

        Assert.Equal(UpdateResultKind.NoChanges, r.Kind);
    }

    [Fact]
    public async Task Update_blocked_on_Approved_status_with_ImmutableStatus()
    {
        // Promote seeded rev to Approved.
        await PromoteAsync(_fx.SeedRevisionId, ProductRevisionStatus.Approved);

        var r = await _svc.UpdateAsync(
            revisionId: _fx.SeedRevisionId,
            r: new UpdateSpecRequest { Title = "Should fail" },
            user: "engineer.demo");

        Assert.Equal(UpdateResultKind.ImmutableStatus, r.Kind);
        Assert.Equal(ProductRevisionStatus.Approved, r.CurrentStatus);
    }

    // ── PR-L2 — ReviseAsync ────────────────────────────────────────────

    [Fact]
    public async Task Revise_clones_source_and_auto_supersedes_it()
    {
        await PromoteAsync(_fx.SeedRevisionId, ProductRevisionStatus.Approved);

        var r = await _svc.ReviseAsync(
            sourceRevisionId: _fx.SeedRevisionId,
            r: new ReviseSpecRequest { Reason = "Customer requested adhesive switch" },
            user: "engineer.demo");

        Assert.Equal(ReviseResultKind.Ok, r.Kind);
        Assert.NotNull(r.Revision);
        Assert.Equal("B", r.Revision!.RevisionCode);
        Assert.Equal(_fx.SeedRevisionId, r.Revision!.ParentRevisionId);
        Assert.Equal("Customer requested adhesive switch", r.Revision!.ChangeSummary);

        // Source should be auto-superseded by the revise action.
        using var db = _fx.NewContext();
        var src = await db.ProductRevisions.AsNoTracking().FirstAsync(x => x.Id == _fx.SeedRevisionId);
        Assert.Equal(ProductRevisionStatus.Superseded, src.Status);
    }

    [Fact]
    public async Task Revise_with_short_reason_returns_ReasonRequired()
    {
        await PromoteAsync(_fx.SeedRevisionId, ProductRevisionStatus.Approved);

        var r = await _svc.ReviseAsync(
            sourceRevisionId: _fx.SeedRevisionId,
            r: new ReviseSpecRequest { Reason = "x" },
            user: "engineer.demo");

        Assert.Equal(ReviseResultKind.ReasonRequired, r.Kind);
    }

    [Fact]
    public async Task Revise_on_Draft_source_returns_InvalidSourceStatus()
    {
        // Seeded rev is Draft — Revise gate requires Approved/Released.
        var r = await _svc.ReviseAsync(
            sourceRevisionId: _fx.SeedRevisionId,
            r: new ReviseSpecRequest { Reason = "Valid reason text here" },
            user: "engineer.demo");

        Assert.Equal(ReviseResultKind.InvalidSourceStatus, r.Kind);
        Assert.Equal(ProductRevisionStatus.Draft, r.CurrentStatus);
    }

    // ── PR-L2 — SupersedeAsync ─────────────────────────────────────────

    [Fact]
    public async Task Supersede_with_correct_confirm_succeeds()
    {
        await PromoteAsync(_fx.SeedRevisionId, ProductRevisionStatus.Approved);

        var r = await _svc.SupersedeAsync(
            revisionId: _fx.SeedRevisionId,
            r: new SupersedeSpecRequest { ConfirmSpecCode = "SPEC-TST-001" },
            user: "engineer.demo");

        Assert.Equal(SupersedeResultKind.Ok, r.Kind);
        Assert.Equal(ProductRevisionStatus.Superseded, r.Revision!.Status);
    }

    [Fact]
    public async Task Supersede_with_mismatched_confirm_returns_ConfirmMismatch()
    {
        await PromoteAsync(_fx.SeedRevisionId, ProductRevisionStatus.Approved);

        var r = await _svc.SupersedeAsync(
            revisionId: _fx.SeedRevisionId,
            r: new SupersedeSpecRequest { ConfirmSpecCode = "WRONG" },
            user: "engineer.demo");

        Assert.Equal(SupersedeResultKind.ConfirmMismatch, r.Kind);

        // Spec stays Approved (not Superseded).
        using var db = _fx.NewContext();
        var rev = await db.ProductRevisions.AsNoTracking().FirstAsync(x => x.Id == _fx.SeedRevisionId);
        Assert.Equal(ProductRevisionStatus.Approved, rev.Status);
    }

    // ── PR-L3 — TrashAsync ─────────────────────────────────────────────

    [Fact]
    public async Task Trash_succeeds_when_no_active_WO()
    {
        var r = await _svc.TrashAsync(_fx.SeedRevisionId, user: "engineer.demo");
        Assert.Equal(TrashResultKind.Ok, r.Kind);
        Assert.True(r.Revision!.IsTrashed);
        Assert.NotNull(r.Revision!.TrashedAt);

        var audit = Assert.Single(_audit.ByAction(AuditAction.SpecTrash));
        Assert.Equal("engineer.demo", audit.Actor);
    }

    [Fact]
    public async Task Trash_blocked_when_active_WO_references_the_spec()
    {
        // Seed an InProgress WO bound to the seeded rev.
        await _fx.SeedWorkOrderAsync(_fx.SeedRevisionId, WoStatus.InProgress, "WO-BLOCK-1");

        var r = await _svc.TrashAsync(_fx.SeedRevisionId, user: "engineer.demo");

        Assert.Equal(TrashResultKind.ActiveWorkOrders, r.Kind);
        Assert.Equal(1, r.ActiveWoCount);
        Assert.Empty(_audit.ByAction(AuditAction.SpecTrash));

        // Spec NOT marked trashed.
        using var db = _fx.NewContext();
        var rev = await db.ProductRevisions.AsNoTracking().FirstAsync(x => x.Id == _fx.SeedRevisionId);
        Assert.False(rev.IsTrashed);
    }

    [Fact]
    public async Task Trash_idempotent_returns_AlreadyTrashed_on_second_call()
    {
        var first = await _svc.TrashAsync(_fx.SeedRevisionId, user: "engineer.demo");
        Assert.Equal(TrashResultKind.Ok, first.Kind);

        var second = await _svc.TrashAsync(_fx.SeedRevisionId, user: "engineer.demo");
        Assert.Equal(TrashResultKind.AlreadyTrashed, second.Kind);
    }

    // ── PR-L3 — RestoreAsync ───────────────────────────────────────────

    [Fact]
    public async Task Restore_succeeds_after_Trash()
    {
        await _svc.TrashAsync(_fx.SeedRevisionId, user: "engineer.demo");
        var r = await _svc.RestoreAsync(_fx.SeedRevisionId, user: "engineer.demo");

        Assert.Equal(RestoreResultKind.Ok, r.Kind);
        Assert.False(r.Revision!.IsTrashed);
        Assert.Null(r.Revision!.TrashedAt);

        Assert.Single(_audit.ByAction(AuditAction.SpecRestore));
    }

    [Fact]
    public async Task Restore_on_non_trashed_returns_NotTrashed()
    {
        var r = await _svc.RestoreAsync(_fx.SeedRevisionId, user: "engineer.demo");
        Assert.Equal(RestoreResultKind.NotTrashed, r.Kind);
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private async Task<long> SeedSecondProductAsync()
    {
        using var db = _fx.NewContext();
        var product = new CCL.MES.Domain.Entities.Product
        {
            ProductCode = "TST-002",
            Name        = "Second test product",
            CustomerId  = _fx.SeedCustomerId,
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product.Id;
    }

    private async Task PromoteAsync(long revId, ProductRevisionStatus status)
    {
        using var db = _fx.NewContext();
        var rev = await db.ProductRevisions.FirstAsync(x => x.Id == revId);
        rev.Status = status;
        await db.SaveChangesAsync();
    }
}
