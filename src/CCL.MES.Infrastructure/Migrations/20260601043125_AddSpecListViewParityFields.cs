using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <summary>
    /// Phase 8 PR #30 — ADD 4 nullable fields cho list-view parity SpecHub:
    ///   ProductRevision.RefNo            (string?, cột SpecHub `REF NO`)
    ///   ProductRevision.InspectionLevel  (string?, cột SpecHub `Spec`)
    ///   SpecPrint.Cavity                 (int?,    cột SpecHub `Cavity`)
    ///   SpecPrint.PitchMm                (double?, cột SpecHub `Pitch` mm)
    ///
    /// Provider-agnostic: KHÔNG có `type:` annotation (đã strip — Phase 7
    /// pattern). KHÔNG raw SQL guard như #28 vì ADD COLUMN nullable chạy chuẩn
    /// trên cả SQLite + SQL Server. Down() drop 4 columns ngược thứ tự.
    /// </summary>
    public partial class AddSpecListViewParityFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Cavity",
                table: "SpecPrints",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PitchMm",
                table: "SpecPrints",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InspectionLevel",
                table: "ProductRevisions",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefNo",
                table: "ProductRevisions",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Cavity",
                table: "SpecPrints");

            migrationBuilder.DropColumn(
                name: "PitchMm",
                table: "SpecPrints");

            migrationBuilder.DropColumn(
                name: "InspectionLevel",
                table: "ProductRevisions");

            migrationBuilder.DropColumn(
                name: "RefNo",
                table: "ProductRevisions");
        }
    }
}
