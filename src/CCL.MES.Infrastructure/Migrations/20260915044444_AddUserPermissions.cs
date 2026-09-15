using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PermApproveProduction",
                table: "Users",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PermApproveQc",
                table: "Users",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PermEditData",
                table: "Users",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PermExportReport",
                table: "Users",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PermManageUsers",
                table: "Users",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PermSpecialAccept",
                table: "Users",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PermSystemConfig",
                table: "Users",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PermViewData",
                table: "Users",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PermApproveProduction",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PermApproveQc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PermEditData",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PermExportReport",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PermManageUsers",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PermSpecialAccept",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PermSystemConfig",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PermViewData",
                table: "Users");
        }
    }
}
