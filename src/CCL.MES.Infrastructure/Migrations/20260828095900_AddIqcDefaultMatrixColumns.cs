using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIqcDefaultMatrixColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultAcceptanceEn",
                table: "IqcCheckItemLibraries",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultAcceptanceVi",
                table: "IqcCheckItemLibraries",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultMethodEn",
                table: "IqcCheckItemLibraries",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultMethodVi",
                table: "IqcCheckItemLibraries",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "InDefaultMatrix",
                table: "IqcCheckItemLibraries",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultAcceptanceEn",
                table: "IqcCheckItemLibraries");

            migrationBuilder.DropColumn(
                name: "DefaultAcceptanceVi",
                table: "IqcCheckItemLibraries");

            migrationBuilder.DropColumn(
                name: "DefaultMethodEn",
                table: "IqcCheckItemLibraries");

            migrationBuilder.DropColumn(
                name: "DefaultMethodVi",
                table: "IqcCheckItemLibraries");

            migrationBuilder.DropColumn(
                name: "InDefaultMatrix",
                table: "IqcCheckItemLibraries");
        }
    }
}
