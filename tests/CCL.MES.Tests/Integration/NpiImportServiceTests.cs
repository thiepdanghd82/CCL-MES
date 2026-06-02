using System.Security.Claims;
using CCL.MES.Application.Services.NpiImport;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Auth;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Tests.Integration._Support;
using CCL.MES.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CCL.MES.Tests.Integration;

/// <summary>
/// Phase 9 T2b — Integration coverage for
/// <see cref="NpiImportService.ApplyAsync"/> across the 4 concrete CSV
/// targets (Structure / Routine / RawMaterial / WorkCenter). Validates
/// the replace-all-atomic contract end-to-end against a real
/// <see cref="BackupService"/> snapshot on disk, real EF cascade behavior
/// on isolated /tmp SQLite, and audit-emit detail JSON shape.
///
/// <para>
/// Each <c>[Fact]</c> gets a fresh fixture (xUnit ctor-per-test) so no
/// order dependence on backup-file timestamps. SpecImport (5th target)
/// is intentionally out of scope — NpiImportService is Phase 7's NPI
/// replace-all engine; Spec lifecycle uses
/// <see cref="CCL.MES.Application.Services.SpecService"/> directly, not
/// the CSV pipeline.
/// </para>
/// </summary>
public sealed class NpiImportServiceTests : IDisposable
{
    private readonly IsolatedDbFixture _fx;
    private readonly InMemoryAuditWriter _audit;
    private readonly BackupService _backup;
    private readonly NpiImportService _svc;

    public NpiImportServiceTests()
    {
        _fx = new IsolatedDbFixture();
        _audit = new InMemoryAuditWriter();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("ConnectionStrings:Default", $"Data Source={_fx.DbPath}"),
                new KeyValuePair<string, string?>("Database:Provider", "Sqlite"),
            })
            .Build();

        _backup = new BackupService(config, _audit);
        _svc = new NpiImportService(_fx.NewContext(), _backup, _audit);
    }

    public void Dispose() => _fx.Dispose();

    // ── Target 1: WorkCenter replace-all ──────────────────────────────

    [Fact]
    public async Task Apply_WorkCenter_replaces_all_and_emits_audit_plus_backup()
    {
        await SeedWorkCenterAsync("PRE-OLD", "obsolete row");
        var newRows = new[]
        {
            new WorkCenter { Code = "PRT-01", Description = "Print line 1" },
            new WorkCenter { Code = "CUT-01", Description = "Cut line 1" },
            new WorkCenter { Code = "PCK-01", Description = "Pack line 1" },
        };

        var result = await _svc.ApplyAsync<WorkCenter>(
            rows: newRows, target: new WorkCenterCsvTarget(),
            actor: AdminPrincipal(), skipped: 0);

        Assert.Equal("WorkCenters", result.Table);
        Assert.Equal(1, result.OldCount);
        Assert.Equal(3, result.NewCount);
        Assert.NotNull(result.BackupFile);
        Assert.NotEmpty(result.BackupFile);

        await AssertCountAsync(db => db.WorkCenters.CountAsync(), expected: 3);
        await AssertBackupFileExistsAsync(result.BackupFile);

        var audit = Assert.Single(_audit.ByAction(AuditAction.NpiImport));
        Assert.Equal("admin.demo", audit.Actor);
        Assert.Equal(UserRole.Admin, audit.ActorRole);
        Assert.Equal("WorkCenters", audit.TargetType);
        Assert.NotNull(audit.Detail);
        Assert.Contains("\"new_count\":3", audit.Detail);
        Assert.Contains("\"old_count\":1", audit.Detail);
    }

    // ── Target 2: RawMaterial replace-all ─────────────────────────────

    [Fact]
    public async Task Apply_RawMaterial_replaces_all_and_emits_audit()
    {
        var newRows = new[]
        {
            new RawMaterial { PartNo = "PN-MAT-01", PartDescription = "Adhesive A" },
            new RawMaterial { PartNo = "PN-MAT-02", PartDescription = "PET film 100um" },
        };

        var result = await _svc.ApplyAsync<RawMaterial>(
            rows: newRows, target: new RawMaterialCsvTarget(),
            actor: AdminPrincipal(), skipped: 0);

        Assert.Equal("RawMaterials", result.Table);
        Assert.Equal(2, result.NewCount);
        await AssertCountAsync(db => db.RawMaterials.CountAsync(), expected: 2);
        Assert.Single(_audit.ByAction(AuditAction.NpiImport));
    }

    // ── Target 3: RoutingOperation replace-all ────────────────────────

    [Fact]
    public async Task Apply_RoutingOperation_replaces_all()
    {
        var newRows = new[]
        {
            new RoutingOperation { PartNo = "PN-ROUTE-01", OpNo = "010" },
            new RoutingOperation { PartNo = "PN-ROUTE-01", OpNo = "020" },
            new RoutingOperation { PartNo = "PN-ROUTE-02", OpNo = "010" },
        };

        var result = await _svc.ApplyAsync<RoutingOperation>(
            rows: newRows, target: new RoutineCsvTarget(),
            actor: AdminPrincipal(), skipped: 0);

        Assert.Equal("RoutingOperations", result.Table);
        Assert.Equal(3, result.NewCount);
        await AssertCountAsync(db => db.RoutingOperations.CountAsync(), expected: 3);
    }

    // ── Target 4: ManufacturingStructure replace-all ──────────────────

    [Fact]
    public async Task Apply_ManufacturingStructure_replaces_all()
    {
        var newRows = new[]
        {
            new ManufacturingStructure { ParentPart = "PARENT-A", ComponentPart = "COMP-1" },
            new ManufacturingStructure { ParentPart = "PARENT-A", ComponentPart = "COMP-2" },
        };

        var result = await _svc.ApplyAsync<ManufacturingStructure>(
            rows: newRows, target: new StructureCsvTarget(),
            actor: AdminPrincipal(), skipped: 0);

        Assert.Equal("ManufacturingStructures", result.Table);
        Assert.Equal(2, result.NewCount);
        await AssertCountAsync(db => db.ManufacturingStructures.CountAsync(), expected: 2);
    }

    // ── Idempotent — re-apply same rows yields same row count ────────

    [Fact]
    public async Task Reapply_with_same_rows_does_not_double_count()
    {
        var rows = new[]
        {
            new WorkCenter { Code = "WC-IDEM-1", Description = "Idempotent test 1" },
            new WorkCenter { Code = "WC-IDEM-2", Description = "Idempotent test 2" },
        };

        // First import — fresh DB has 0 rows.
        var r1 = await _svc.ApplyAsync<WorkCenter>(rows, new WorkCenterCsvTarget(), AdminPrincipal(), 0);
        Assert.Equal(0, r1.OldCount);
        Assert.Equal(2, r1.NewCount);

        // Second import — DB had 2 rows from r1; replace-all wipes + re-inserts.
        // Need fresh entity instances because EF tracks identity by primary key
        // (the same in-memory rows would now have Id assigned from r1).
        var rowsRound2 = new[]
        {
            new WorkCenter { Code = "WC-IDEM-1", Description = "Idempotent test 1" },
            new WorkCenter { Code = "WC-IDEM-2", Description = "Idempotent test 2" },
        };
        var r2 = await _svc.ApplyAsync<WorkCenter>(rowsRound2, new WorkCenterCsvTarget(), AdminPrincipal(), 0);
        Assert.Equal(2, r2.OldCount);
        Assert.Equal(2, r2.NewCount);                          // NOT 4

        await AssertCountAsync(db => db.WorkCenters.CountAsync(), expected: 2);
        Assert.Equal(2, _audit.ByAction(AuditAction.NpiImport).Count());
    }

    // ── RBAC — forbidden role throws CsvImportException with key ─────

    [Theory]
    [InlineData(UserRole.Operator)]
    [InlineData(UserRole.Supervisor)]
    [InlineData(UserRole.Qc)]
    [InlineData("")]                                            // anonymous
    public async Task Apply_with_forbidden_role_throws_with_forbidden_role_key(string role)
    {
        var ex = await Assert.ThrowsAsync<CsvImportException>(() =>
            _svc.ApplyAsync<WorkCenter>(
                rows:   new[] { new WorkCenter { Code = "X" } },
                target: new WorkCenterCsvTarget(),
                actor:  PrincipalWithRole("badactor", role),
                skipped: 0));

        Assert.Equal("forbidden_role", ex.ErrorKey);
        await AssertCountAsync(db => db.WorkCenters.CountAsync(), expected: 0);
        Assert.Empty(_audit.ByAction(AuditAction.NpiImport));   // no audit on RBAC reject
    }

    [Fact]
    public async Task Apply_with_engineer_role_passes_RBAC()
    {
        var r = await _svc.ApplyAsync<WorkCenter>(
            rows:   new[] { new WorkCenter { Code = "WC-ENG", Description = "Engineer-uploaded" } },
            target: new WorkCenterCsvTarget(),
            actor:  PrincipalWithRole("engineer.demo", UserRole.Engineer),
            skipped: 0);

        Assert.Equal(1, r.NewCount);
    }

    // ── Empty rows — throws with no_rows key ──────────────────────────

    [Fact]
    public async Task Apply_with_zero_rows_throws_no_rows_key()
    {
        var ex = await Assert.ThrowsAsync<CsvImportException>(() =>
            _svc.ApplyAsync<WorkCenter>(
                rows:    Array.Empty<WorkCenter>(),
                target:  new WorkCenterCsvTarget(),
                actor:   AdminPrincipal(),
                skipped: 0));

        Assert.Equal("no_rows", ex.ErrorKey);
        Assert.Empty(_audit.ByAction(AuditAction.NpiImport));
    }

    // ── Skip count propagated through to audit + result ───────────────

    [Fact]
    public async Task Apply_propagates_skipped_count_into_audit_and_result()
    {
        var rows = new[] { new WorkCenter { Code = "WC-SK-1", Description = "kept" } };

        var r = await _svc.ApplyAsync<WorkCenter>(rows, new WorkCenterCsvTarget(), AdminPrincipal(), skipped: 7);

        Assert.Equal(7, r.Skipped);
        var audit = Assert.Single(_audit.ByAction(AuditAction.NpiImport));
        Assert.Contains("\"skipped\":7", audit.Detail);
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static ClaimsPrincipal AdminPrincipal() =>
        PrincipalWithRole("admin.demo", UserRole.Admin);

    private static ClaimsPrincipal PrincipalWithRole(string name, string role)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, name) };
        if (!string.IsNullOrEmpty(role))
            claims.Add(new Claim(ClaimTypes.Role, role));
        var identity = new ClaimsIdentity(claims, authenticationType: "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    private async Task SeedWorkCenterAsync(string code, string description)
    {
        using var db = _fx.NewContext();
        db.WorkCenters.Add(new WorkCenter { Code = code, Description = description });
        await db.SaveChangesAsync();
    }

    private async Task AssertCountAsync(Func<MesDbContext, Task<int>> count, int expected)
    {
        using var db = _fx.NewContext();
        Assert.Equal(expected, await count(db));
    }

    private async Task AssertBackupFileExistsAsync(string backupFileName)
    {
        var (backupDir, _) = _backup.ResolveSqlitePaths();
        var full = Path.Combine(backupDir, backupFileName);
        Assert.True(File.Exists(full), $"Expected backup file at '{full}' but it was not present.");
        var info = new FileInfo(full);
        Assert.True(info.Length > 0, "Backup file is empty.");
        await Task.CompletedTask;
    }
}
