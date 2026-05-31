using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStructureExtendedFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<double>(
                name: "ScrapPct",
                table: "ManufacturingStructures",
                nullable: true,
                oldClrType: typeof(string),
                oldNullable: true);

            migrationBuilder.AlterColumn<double>(
                name: "Pitch",
                table: "ManufacturingStructures",
                nullable: true,
                oldClrType: typeof(string),
                oldNullable: true);

            migrationBuilder.AlterColumn<double>(
                name: "Cavity",
                table: "ManufacturingStructures",
                nullable: true,
                oldClrType: typeof(string),
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Alt",
                table: "ManufacturingStructures",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Effectivity",
                table: "ManufacturingStructures",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EffectivityDate",
                table: "ManufacturingStructures",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Planner",
                table: "ManufacturingStructures",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StructureType",
                table: "ManufacturingStructures",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Alt",
                table: "ManufacturingStructures");

            migrationBuilder.DropColumn(
                name: "Effectivity",
                table: "ManufacturingStructures");

            migrationBuilder.DropColumn(
                name: "EffectivityDate",
                table: "ManufacturingStructures");

            migrationBuilder.DropColumn(
                name: "Planner",
                table: "ManufacturingStructures");

            migrationBuilder.DropColumn(
                name: "StructureType",
                table: "ManufacturingStructures");

            migrationBuilder.AlterColumn<string>(
                name: "ScrapPct",
                table: "ManufacturingStructures",
                nullable: true,
                oldClrType: typeof(double),
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Pitch",
                table: "ManufacturingStructures",
                nullable: true,
                oldClrType: typeof(double),
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Cavity",
                table: "ManufacturingStructures",
                nullable: true,
                oldClrType: typeof(double),
                oldNullable: true);
        }
    }
}
