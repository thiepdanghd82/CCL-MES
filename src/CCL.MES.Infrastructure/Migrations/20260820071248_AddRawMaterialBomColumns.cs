using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRawMaterialBomColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccountingGroupDescription",
                table: "RawMaterials",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DimensionQuality",
                table: "RawMaterials",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LeadTimeCode",
                table: "RawMaterials",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotherCode",
                table: "RawMaterials",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PartType",
                table: "RawMaterials",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Planner",
                table: "RawMaterials",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductFamily",
                table: "RawMaterials",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductFamilyDescription",
                table: "RawMaterials",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Thickness",
                table: "RawMaterials",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TypeDesignation",
                table: "RawMaterials",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "WidthMm",
                table: "RawMaterials",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccountingGroupDescription",
                table: "RawMaterials");

            migrationBuilder.DropColumn(
                name: "DimensionQuality",
                table: "RawMaterials");

            migrationBuilder.DropColumn(
                name: "LeadTimeCode",
                table: "RawMaterials");

            migrationBuilder.DropColumn(
                name: "MotherCode",
                table: "RawMaterials");

            migrationBuilder.DropColumn(
                name: "PartType",
                table: "RawMaterials");

            migrationBuilder.DropColumn(
                name: "Planner",
                table: "RawMaterials");

            migrationBuilder.DropColumn(
                name: "ProductFamily",
                table: "RawMaterials");

            migrationBuilder.DropColumn(
                name: "ProductFamilyDescription",
                table: "RawMaterials");

            migrationBuilder.DropColumn(
                name: "Thickness",
                table: "RawMaterials");

            migrationBuilder.DropColumn(
                name: "TypeDesignation",
                table: "RawMaterials");

            migrationBuilder.DropColumn(
                name: "WidthMm",
                table: "RawMaterials");
        }
    }
}
