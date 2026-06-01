using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <summary>
    /// Phase 8 PR #31d — ADD 4 nullable columns on SpecPrints cho detail sheet
    /// parity (Q6 + Q7):
    ///   - ProductSizeWmm / ProductSizeHmm (double?, mm) — silk + flexo product size
    ///   - RemarksText (text?) — silk single blob / flexo print remarks
    ///   - RemarksCutText (text?) — flexo cut remarks (silk = null)
    ///
    /// PURELY ADDITIVE — chỉ AddColumn nullable trên bảng existing. EF emit
    /// chuẩn ở cả SQLite + SqlServer providers (KHÔNG cần ActiveProvider
    /// guard). Backfill: NULL cho mọi rev existing — parser PR #31a/b đã
    /// capture nhưng SaveAsync chưa persist; future import (Refresh Samples
    /// hoặc Create Spec modal) sẽ populate. Detail sheet render "—" cho NULL.
    ///
    /// A→B→C SAFE apply:
    ///   A. Backup `data/ccl_mes.db` + SHA256 → `db-backups/`
    ///   B. Test apply trên `/tmp/spec-detail-design.db` (isolated copy)
    ///      → verify baseline + PR #31a/b/c counts unchanged + 4 new cols NULL
    ///   C. Apply LIVE → re-verify
    ///
    /// Rollback: `dotnet ef migrations remove` HOẶC manual `ALTER TABLE
    /// SpecPrints DROP COLUMN ProductSizeWmm; ProductSizeHmm; RemarksText;
    /// RemarksCutText;` (SQLite cần table-rebuild trick).
    /// </summary>
    public partial class AddSpecPrintDetailSheetFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "ProductSizeHmm",
                table: "SpecPrints",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ProductSizeWmm",
                table: "SpecPrints",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RemarksCutText",
                table: "SpecPrints",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RemarksText",
                table: "SpecPrints",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProductSizeHmm",
                table: "SpecPrints");

            migrationBuilder.DropColumn(
                name: "ProductSizeWmm",
                table: "SpecPrints");

            migrationBuilder.DropColumn(
                name: "RemarksCutText",
                table: "SpecPrints");

            migrationBuilder.DropColumn(
                name: "RemarksText",
                table: "SpecPrints");
        }
    }
}
