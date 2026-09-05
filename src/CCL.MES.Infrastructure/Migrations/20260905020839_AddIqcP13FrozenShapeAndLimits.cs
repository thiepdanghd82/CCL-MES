using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIqcP13FrozenShapeAndLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "IqcResultDetails",
                maxLength: 16,
                nullable: false,
                // 20 dòng kết quả có sẵn là hạng mục người bấm đạt/không đạt —
                // "Verdict" MÔ TẢ ĐÚNG chúng. Để "" thì mọi dòng cũ mang một
                // giá trị ngoài enum và EF ném lúc đọc.
                defaultValue: "Verdict");

            migrationBuilder.AddColumn<string>(
                name: "LimitLabel",
                table: "IqcResultDetails",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "LimitLow",
                table: "IqcResultDetails",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LimitUnit",
                table: "IqcResultDetails",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "LimitUp",
                table: "IqcResultDetails",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MeasureCount",
                table: "IqcResultDetails",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "TearIsPass",
                table: "IqcResultDetails",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MaterialCategory",
                table: "IqcInspections",
                maxLength: 16,
                nullable: false,
                // 26 phiếu cũ mở trước khi có luật nhóm. "Any" = KHÔNG BIẾT,
                // đúng sự thật; gán bừa "Roll" là bịa lịch sử.
                defaultValue: "Any");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Kind",
                table: "IqcResultDetails");

            migrationBuilder.DropColumn(
                name: "LimitLabel",
                table: "IqcResultDetails");

            migrationBuilder.DropColumn(
                name: "LimitLow",
                table: "IqcResultDetails");

            migrationBuilder.DropColumn(
                name: "LimitUnit",
                table: "IqcResultDetails");

            migrationBuilder.DropColumn(
                name: "LimitUp",
                table: "IqcResultDetails");

            migrationBuilder.DropColumn(
                name: "MeasureCount",
                table: "IqcResultDetails");

            migrationBuilder.DropColumn(
                name: "TearIsPass",
                table: "IqcResultDetails");

            migrationBuilder.DropColumn(
                name: "MaterialCategory",
                table: "IqcInspections");
        }
    }
}
