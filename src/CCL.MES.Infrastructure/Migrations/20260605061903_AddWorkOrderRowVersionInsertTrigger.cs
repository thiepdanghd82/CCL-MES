using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <summary>
    /// P10.7a-1.3 — fix-up trigger so newly-inserted WorkOrders get a
    /// fresh RowVersion automatically. The P10.7a-1.1 migration only
    /// added the AFTER UPDATE trigger; AFTER INSERT was missed because
    /// EF Core's default for byte[] is an empty array and SQLite has
    /// no automatic RowVersion semantic.
    ///
    /// Without this trigger, a freshly-seeded WO has RowVersion =
    /// empty bytes → ETag is empty → the client cannot send a valid
    /// If-Match header → 428/409. This migration backfills RowVersion
    /// for any existing WOs that still have empty bytes (the 1-row
    /// real data slipped through this gap) and installs the INSERT
    /// trigger for all future rows.
    /// </summary>
    public partial class AddWorkOrderRowVersionInsertTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill any rows that came in via direct insert (not the
            // legacy backfill SQL in the parent migration).
            migrationBuilder.Sql(@"
                UPDATE WorkOrders
                SET RowVersion = randomblob(8)
                WHERE length(RowVersion) = 0;
            ");

            // INSERT trigger — fires for every new row + populates
            // RowVersion when the caller didn't explicitly set it.
            // Same guard pattern as the UPDATE trigger (no re-fire if
            // the app already populated the column).
            migrationBuilder.Sql(@"
                CREATE TRIGGER IF NOT EXISTS WorkOrders_RowVersion_OnInsert
                AFTER INSERT ON WorkOrders
                FOR EACH ROW
                WHEN length(NEW.RowVersion) = 0
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
            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS WorkOrders_RowVersion_OnInsert;
            ");
        }
    }
}
