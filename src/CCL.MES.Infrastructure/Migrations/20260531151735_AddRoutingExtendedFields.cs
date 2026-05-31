using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRoutingExtendedFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<double>(
                name: "MachineSetupTime",
                table: "RoutingOperations",
                nullable: true,
                oldClrType: typeof(double));

            migrationBuilder.AlterColumn<double>(
                name: "MachineRunTime",
                table: "RoutingOperations",
                nullable: true,
                oldClrType: typeof(double));

            migrationBuilder.AlterColumn<double>(
                name: "LaborSetupTime",
                table: "RoutingOperations",
                nullable: true,
                oldClrType: typeof(double));

            migrationBuilder.AlterColumn<double>(
                name: "LaborRunTime",
                table: "RoutingOperations",
                nullable: true,
                oldClrType: typeof(double));

            migrationBuilder.AddColumn<string>(
                name: "Alt",
                table: "RoutingOperations",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Crew",
                table: "RoutingOperations",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Effectivity",
                table: "RoutingOperations",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Efficiency",
                table: "RoutingOperations",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LaborClass",
                table: "RoutingOperations",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Planner",
                table: "RoutingOperations",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RoutingType",
                table: "RoutingOperations",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "SetupCrew",
                table: "RoutingOperations",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Site",
                table: "RoutingOperations",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "RoutingOperations",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Alt",
                table: "RoutingOperations");

            migrationBuilder.DropColumn(
                name: "Crew",
                table: "RoutingOperations");

            migrationBuilder.DropColumn(
                name: "Effectivity",
                table: "RoutingOperations");

            migrationBuilder.DropColumn(
                name: "Efficiency",
                table: "RoutingOperations");

            migrationBuilder.DropColumn(
                name: "LaborClass",
                table: "RoutingOperations");

            migrationBuilder.DropColumn(
                name: "Planner",
                table: "RoutingOperations");

            migrationBuilder.DropColumn(
                name: "RoutingType",
                table: "RoutingOperations");

            migrationBuilder.DropColumn(
                name: "SetupCrew",
                table: "RoutingOperations");

            migrationBuilder.DropColumn(
                name: "Site",
                table: "RoutingOperations");

            migrationBuilder.DropColumn(
                name: "Unit",
                table: "RoutingOperations");

            migrationBuilder.AlterColumn<double>(
                name: "MachineSetupTime",
                table: "RoutingOperations",
                nullable: false,
                defaultValue: 0.0,
                oldClrType: typeof(double),
                oldNullable: true);

            migrationBuilder.AlterColumn<double>(
                name: "MachineRunTime",
                table: "RoutingOperations",
                nullable: false,
                defaultValue: 0.0,
                oldClrType: typeof(double),
                oldNullable: true);

            migrationBuilder.AlterColumn<double>(
                name: "LaborSetupTime",
                table: "RoutingOperations",
                nullable: false,
                defaultValue: 0.0,
                oldClrType: typeof(double),
                oldNullable: true);

            migrationBuilder.AlterColumn<double>(
                name: "LaborRunTime",
                table: "RoutingOperations",
                nullable: false,
                defaultValue: 0.0,
                oldClrType: typeof(double),
                oldNullable: true);
        }
    }
}
