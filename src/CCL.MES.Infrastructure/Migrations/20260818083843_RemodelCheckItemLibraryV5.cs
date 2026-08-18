using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemodelCheckItemLibraryV5 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CheckItemLibraries_ProcessLine_QcStage",
                table: "CheckItemLibraries");

            migrationBuilder.DropColumn(
                name: "ParetoPct",
                table: "CheckItemLibraries");

            migrationBuilder.DropColumn(
                name: "QcStage",
                table: "CheckItemLibraries");

            migrationBuilder.DropColumn(
                name: "ShortForm",
                table: "CheckItemLibraries");

            migrationBuilder.AddColumn<bool>(
                name: "BlankLabel",
                table: "CheckItemLibraries",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "DrillHole",
                table: "CheckItemLibraries",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Flatbed",
                table: "CheckItemLibraries",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Flexo",
                table: "CheckItemLibraries",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Fqc",
                table: "CheckItemLibraries",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HpIndigo",
                table: "CheckItemLibraries",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Ipqc",
                table: "CheckItemLibraries",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Laminate",
                table: "CheckItemLibraries",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "LetterPress",
                table: "CheckItemLibraries",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Oqc",
                table: "CheckItemLibraries",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PunchHole",
                table: "CheckItemLibraries",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Rdc",
                table: "CheckItemLibraries",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SheetCut",
                table: "CheckItemLibraries",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SilkScreen",
                table: "CheckItemLibraries",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Slit",
                table: "CheckItemLibraries",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Zebra",
                table: "CheckItemLibraries",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_CheckItemLibraries_ProcessLine",
                table: "CheckItemLibraries",
                column: "ProcessLine");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CheckItemLibraries_ProcessLine",
                table: "CheckItemLibraries");

            migrationBuilder.DropColumn(
                name: "BlankLabel",
                table: "CheckItemLibraries");

            migrationBuilder.DropColumn(
                name: "DrillHole",
                table: "CheckItemLibraries");

            migrationBuilder.DropColumn(
                name: "Flatbed",
                table: "CheckItemLibraries");

            migrationBuilder.DropColumn(
                name: "Flexo",
                table: "CheckItemLibraries");

            migrationBuilder.DropColumn(
                name: "Fqc",
                table: "CheckItemLibraries");

            migrationBuilder.DropColumn(
                name: "HpIndigo",
                table: "CheckItemLibraries");

            migrationBuilder.DropColumn(
                name: "Ipqc",
                table: "CheckItemLibraries");

            migrationBuilder.DropColumn(
                name: "Laminate",
                table: "CheckItemLibraries");

            migrationBuilder.DropColumn(
                name: "LetterPress",
                table: "CheckItemLibraries");

            migrationBuilder.DropColumn(
                name: "Oqc",
                table: "CheckItemLibraries");

            migrationBuilder.DropColumn(
                name: "PunchHole",
                table: "CheckItemLibraries");

            migrationBuilder.DropColumn(
                name: "Rdc",
                table: "CheckItemLibraries");

            migrationBuilder.DropColumn(
                name: "SheetCut",
                table: "CheckItemLibraries");

            migrationBuilder.DropColumn(
                name: "SilkScreen",
                table: "CheckItemLibraries");

            migrationBuilder.DropColumn(
                name: "Slit",
                table: "CheckItemLibraries");

            migrationBuilder.DropColumn(
                name: "Zebra",
                table: "CheckItemLibraries");

            migrationBuilder.AddColumn<string>(
                name: "ParetoPct",
                table: "CheckItemLibraries",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QcStage",
                table: "CheckItemLibraries",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ShortForm",
                table: "CheckItemLibraries",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CheckItemLibraries_ProcessLine_QcStage",
                table: "CheckItemLibraries",
                columns: new[] { "ProcessLine", "QcStage" });
        }
    }
}
