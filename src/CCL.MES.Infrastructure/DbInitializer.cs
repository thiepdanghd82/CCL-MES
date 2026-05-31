using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

namespace CCL.MES.Infrastructure;

/// <summary>
/// Phase 5 — single DB init path for both SQLite (dev) and SQL Server (prod).
/// Replaces the prior <c>EnsureCreated()</c> / <c>Migrate()</c> branch in
/// Program.cs. See docs/PHASE5-STEP4-PLAN.md.
///
/// Behaviour:
///   1. New install (no tables) → <c>Migrate()</c> creates the schema from
///      the Init migration + records itself in <c>__EFMigrationsHistory</c>.
///   2. Existing install (tables present, history absent — the legacy
///      EnsureCreated() shape) → baseline-insert every known migration into
///      <c>__EFMigrationsHistory</c> first, then <c>Migrate()</c>. The
///      pending set is empty post-baseline, so no DDL runs and the 60k+
///      rows of NPI data stay intact.
///   3. Subsequent restarts (history present) → <c>Migrate()</c> applies
///      any genuinely-pending migrations (none on a freshly-baselined DB,
///      future migrations later).
///
/// Provider-agnostic: uses EF Core's <see cref="IHistoryRepository"/> for
/// the history-table DDL + INSERT SQL so SQLite + SQL Server both emit the
/// dialect-correct statements.
/// </summary>
public static class DbInitializer
{
    public static async Task InitializeAsync(MesDbContext db)
    {
        var creator = db.GetService<IRelationalDatabaseCreator>();
        var historyRepo = db.GetService<IHistoryRepository>();

        var hasTables = await creator.HasTablesAsync();
        var historyExists = await historyRepo.ExistsAsync();

        if (hasTables && !historyExists)
        {
            // Existing DB from EnsureCreated() era — baseline insert so the
            // upcoming Migrate() treats every known migration as already
            // applied. Otherwise CREATE TABLE would fail on existing tables
            // and the operator would lose access to ~60k rows.
            var createHistorySql = historyRepo.GetCreateScript();
            await db.Database.ExecuteSqlRawAsync(createHistorySql);

            var productVersion = typeof(MesDbContext).Assembly
                .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .Length > 0
                ? "10.0.8"  // pinned to EF Core package version
                : "10.0.8";

            foreach (var migrationId in db.Database.GetMigrations())
            {
                var row = new HistoryRow(migrationId, productVersion);
                var insertSql = historyRepo.GetInsertScript(row);
                await db.Database.ExecuteSqlRawAsync(insertSql);
            }
        }

        // After (optional) baseline: Migrate() is now a no-op on existing
        // DBs (pending = 0) and full-schema-creation on new DBs.
        await db.Database.MigrateAsync();
    }
}
