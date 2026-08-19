using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIqcGroupColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // feat/iqc-module-tabs — ADDITIVE. NOT NULL an toàn nhờ
            // defaultValue: SQLite AddColumn set mọi dòng cũ = "Materials"
            // ngay khi áp (backfill tự động). Type-affinity đã strip (§4.5).
            migrationBuilder.AddColumn<string>(
                name: "Group",
                table: "IqcInspections",
                maxLength: 20,
                nullable: false,
                defaultValue: "Materials");

            migrationBuilder.CreateIndex(
                name: "IX_IqcInspections_Group",
                table: "IqcInspections",
                column: "Group");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IqcInspections_Group",
                table: "IqcInspections");

            migrationBuilder.DropColumn(
                name: "Group",
                table: "IqcInspections");
        }
    }
}
