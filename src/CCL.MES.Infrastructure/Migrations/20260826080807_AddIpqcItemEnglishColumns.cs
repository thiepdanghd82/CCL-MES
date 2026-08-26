using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIpqcItemEnglishColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AcceptanceCriteriaEn",
                table: "WoIpqcCheckItems",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GroupLabelEn",
                table: "WoIpqcCheckItems",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LabelEn",
                table: "WoIpqcCheckItems",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MethodEn",
                table: "WoIpqcCheckItems",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GroupLabelEn",
                table: "CheckItemLibraries",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MethodEn",
                table: "CheckItemLibraries",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcceptanceCriteriaEn",
                table: "WoIpqcCheckItems");

            migrationBuilder.DropColumn(
                name: "GroupLabelEn",
                table: "WoIpqcCheckItems");

            migrationBuilder.DropColumn(
                name: "LabelEn",
                table: "WoIpqcCheckItems");

            migrationBuilder.DropColumn(
                name: "MethodEn",
                table: "WoIpqcCheckItems");

            migrationBuilder.DropColumn(
                name: "GroupLabelEn",
                table: "CheckItemLibraries");

            migrationBuilder.DropColumn(
                name: "MethodEn",
                table: "CheckItemLibraries");
        }
    }
}
