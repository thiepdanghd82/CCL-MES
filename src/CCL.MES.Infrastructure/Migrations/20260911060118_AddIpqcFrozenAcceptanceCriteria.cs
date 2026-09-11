using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIpqcFrozenAcceptanceCriteria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Aql",
                table: "WoIpqcCheckItems",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CavityCount",
                table: "WoIpqcCheckItems",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CavitySource",
                table: "WoIpqcCheckItems",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Sampling",
                table: "WoIpqcCheckItems",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Aql",
                table: "WoIpqcCheckItems");

            migrationBuilder.DropColumn(
                name: "CavityCount",
                table: "WoIpqcCheckItems");

            migrationBuilder.DropColumn(
                name: "CavitySource",
                table: "WoIpqcCheckItems");

            migrationBuilder.DropColumn(
                name: "Sampling",
                table: "WoIpqcCheckItems");
        }
    }
}
