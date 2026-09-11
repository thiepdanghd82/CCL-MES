using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIpqcProductLimitsLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "LimitLow",
                table: "WoIpqcCheckItems",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "LimitNominal",
                table: "WoIpqcCheckItems",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LimitSourceWindowId",
                table: "WoIpqcCheckItems",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LimitUnit",
                table: "WoIpqcCheckItems",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "LimitUp",
                table: "WoIpqcCheckItems",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LibraryItemKey",
                table: "QcCriteria",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LimitLow",
                table: "WoIpqcCheckItems");

            migrationBuilder.DropColumn(
                name: "LimitNominal",
                table: "WoIpqcCheckItems");

            migrationBuilder.DropColumn(
                name: "LimitSourceWindowId",
                table: "WoIpqcCheckItems");

            migrationBuilder.DropColumn(
                name: "LimitUnit",
                table: "WoIpqcCheckItems");

            migrationBuilder.DropColumn(
                name: "LimitUp",
                table: "WoIpqcCheckItems");

            migrationBuilder.DropColumn(
                name: "LibraryItemKey",
                table: "QcCriteria");
        }
    }
}
