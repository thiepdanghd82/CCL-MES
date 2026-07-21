using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWoMaterialPartScan : Migration
    {
        // Two nullable columns on WoMaterials so a scanned/manually-entered part
        // code + its BOM-resolved description survive to the Product freeze
        // snapshot (previously part_scan lived only in audit detail). Additive,
        // no backfill — existing rows read NULL → "—". Type-affinity strings
        // stripped per CLAUDE.md §4.5 (SQL Server provider gate).
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PartScan",
                table: "WoMaterials",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PartScanDescription",
                table: "WoMaterials",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PartScan",
                table: "WoMaterials");

            migrationBuilder.DropColumn(
                name: "PartScanDescription",
                table: "WoMaterials");
        }
    }
}
