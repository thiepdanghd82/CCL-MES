using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <summary>
    /// P10.7a-1 — adds the canonical 12-state <c>MesPhase</c> column +
    /// optimistic-concurrency <c>RowVersion</c> column to
    /// <c>WorkOrders</c>, plus the SQLite trigger that bumps RowVersion
    /// on every UPDATE (SQL Server auto-bumps; SQLite needs help).
    ///
    /// Backfill rule (one-way projection legacy CurrentStep → MesPhase):
    /// legacy rows in <c>PrePressCheck</c> land in <c>PREPRESS</c>
    /// (the active member of the collapsed NEW/PREPRESS pair), legacy
    /// <c>Running</c> lands in <c>RUNNING</c> (PAUSED is operator-event
    /// only — legacy never recorded it), etc. The migration is
    /// idempotent on a fresh DB (no existing rows) and on an
    /// already-migrated DB (column-exists check prevents double-apply
    /// at the EF level).
    ///
    /// Down() is symmetric: drop trigger → drop columns. Tested via
    /// <c>scripts/verify-p10.7a-1.sh</c> apply → down → re-apply on a
    /// copy of <c>data/ccl_mes.db</c> per Henry condition 2.
    /// </summary>
    public partial class AddWorkOrderRowVersionAndMesPhase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EF default behaviour: defaultValueSql "" for new non-null
            // string column. Caller-side WorkOrder.cs initializer is
            // "NEW", so any row created post-migration via the C#
            // entity path gets MesPhase = "NEW" out of the box. The
            // backfill SQL below replaces the EF-default "" with the
            // projected MesPhase for rows that existed BEFORE this
            // migration ran.
            migrationBuilder.AddColumn<string>(
                name: "MesPhase",
                table: "WorkOrders",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "NEW");

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "WorkOrders",
                type: "BLOB",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            // Backfill MesPhase for existing rows via reverse projection
            // of CurrentStep (the legacy ProcessStepCode column is
            // HasConversion<string> in MesDbContext, so the values are
            // the enum NAMES not integers — see breakdown §3.2 for the
            // full mapping table).
            migrationBuilder.Sql(@"
                UPDATE WorkOrders
                SET MesPhase = CASE CurrentStep
                    WHEN 'PrePressCheck' THEN 'PREPRESS'
                    WHEN 'OpSetting'     THEN 'SETTING'
                    WHEN 'IpqcApproval'  THEN 'IPQC_WAIT'
                    WHEN 'ReadyToRun'    THEN 'IPQC_APPROVED'
                    WHEN 'Running'       THEN 'RUNNING'
                    WHEN 'Fqc'           THEN 'FQC_PENDING'
                    WHEN 'Oqc'           THEN 'OQC_PENDING'
                    WHEN 'Closed'        THEN 'DONE'
                    ELSE                      'NEW'
                END
                WHERE MesPhase = 'NEW';
            ");

            // Backfill RowVersion with a per-row randomblob(8). Without
            // this, every existing row starts at the EF default empty
            // byte[], which collides across all pre-migration rows and
            // breaks optimistic-concurrency on first edit.
            migrationBuilder.Sql(@"
                UPDATE WorkOrders
                SET RowVersion = randomblob(8)
                WHERE length(RowVersion) = 0;
            ");

            // SQLite RowVersion trigger — bumps RowVersion to a fresh
            // randomblob(8) on every UPDATE where the application
            // didn't explicitly set it. SQL Server's TIMESTAMP column
            // auto-bumps; SQLite has no equivalent so the trigger
            // fills the gap. EF Core's IsRowVersion() instructs EF
            // to compare the OLD value in the WHERE clause + reload
            // the NEW value post-save; the trigger guarantees the
            // NEW value differs from the OLD one.
            //
            // Guard "WHEN NEW.RowVersion = OLD.RowVersion" prevents
            // the trigger from re-firing in a loop when the app
            // explicitly sets the value (e.g. tests overwriting).
            migrationBuilder.Sql(@"
                CREATE TRIGGER IF NOT EXISTS WorkOrders_RowVersion_OnUpdate
                AFTER UPDATE ON WorkOrders
                FOR EACH ROW
                WHEN NEW.RowVersion = OLD.RowVersion
                BEGIN
                    UPDATE WorkOrders
                    SET RowVersion = randomblob(8)
                    WHERE rowid = NEW.rowid;
                END;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop trigger BEFORE columns — SQLite refuses to drop
            // a column referenced by a trigger.
            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS WorkOrders_RowVersion_OnUpdate;
            ");

            migrationBuilder.DropColumn(
                name: "MesPhase",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "WorkOrders");
        }
    }
}
