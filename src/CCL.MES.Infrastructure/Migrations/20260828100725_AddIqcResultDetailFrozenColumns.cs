using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIqcResultDetailFrozenColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "Pass",
                table: "IqcResultDetails",
                nullable: true,
                oldClrType: typeof(bool),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<string>(
                name: "AcceptanceEn",
                table: "IqcResultDetails",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AcceptanceUnspecified",
                table: "IqcResultDetails",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AcceptanceVi",
                table: "IqcResultDetails",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "FromDefaultMatrix",
                table: "IqcResultDetails",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "GroupCode",
                table: "IqcResultDetails",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GroupLabelEn",
                table: "IqcResultDetails",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GroupLabelVi",
                table: "IqcResultDetails",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ItemKey",
                table: "IqcResultDetails",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LabelEn",
                table: "IqcResultDetails",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LabelVi",
                table: "IqcResultDetails",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MethodEn",
                table: "IqcResultDetails",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MethodVi",
                table: "IqcResultDetails",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Seq",
                table: "IqcResultDetails",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SourceFrequency",
                table: "IqcResultDetails",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpecNo",
                table: "IqcResultDetails",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcceptanceEn",
                table: "IqcResultDetails");

            migrationBuilder.DropColumn(
                name: "AcceptanceUnspecified",
                table: "IqcResultDetails");

            migrationBuilder.DropColumn(
                name: "AcceptanceVi",
                table: "IqcResultDetails");

            migrationBuilder.DropColumn(
                name: "FromDefaultMatrix",
                table: "IqcResultDetails");

            migrationBuilder.DropColumn(
                name: "GroupCode",
                table: "IqcResultDetails");

            migrationBuilder.DropColumn(
                name: "GroupLabelEn",
                table: "IqcResultDetails");

            migrationBuilder.DropColumn(
                name: "GroupLabelVi",
                table: "IqcResultDetails");

            migrationBuilder.DropColumn(
                name: "ItemKey",
                table: "IqcResultDetails");

            migrationBuilder.DropColumn(
                name: "LabelEn",
                table: "IqcResultDetails");

            migrationBuilder.DropColumn(
                name: "LabelVi",
                table: "IqcResultDetails");

            migrationBuilder.DropColumn(
                name: "MethodEn",
                table: "IqcResultDetails");

            migrationBuilder.DropColumn(
                name: "MethodVi",
                table: "IqcResultDetails");

            migrationBuilder.DropColumn(
                name: "Seq",
                table: "IqcResultDetails");

            migrationBuilder.DropColumn(
                name: "SourceFrequency",
                table: "IqcResultDetails");

            migrationBuilder.DropColumn(
                name: "SpecNo",
                table: "IqcResultDetails");

            migrationBuilder.AlterColumn<bool>(
                name: "Pass",
                table: "IqcResultDetails",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "INTEGER",
                oldNullable: true);
        }
    }
}
