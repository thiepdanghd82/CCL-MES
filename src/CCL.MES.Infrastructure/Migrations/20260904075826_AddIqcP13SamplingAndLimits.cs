using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIqcP13SamplingAndLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LimitLabel",
                table: "IqcSpecItems",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "LimitLow",
                table: "IqcSpecItems",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "LimitNominal",
                table: "IqcSpecItems",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LimitParsed",
                table: "IqcSpecItems",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LimitUnit",
                table: "IqcSpecItems",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "LimitUp",
                table: "IqcSpecItems",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TearIsPass",
                table: "IqcSpecItems",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AutoVerdict",
                table: "IqcResultDetails",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AutoVerdictOffendingSeq",
                table: "IqcResultDetails",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AutoVerdictReason",
                table: "IqcResultDetails",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DefectCount",
                table: "IqcResultDetails",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OverriddenAt",
                table: "IqcResultDetails",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OverriddenBy",
                table: "IqcResultDetails",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OverrideReason",
                table: "IqcResultDetails",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TearObserved",
                table: "IqcResultDetails",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Approval",
                table: "IqcMaterialSpecs",
                maxLength: 16,
                nullable: false,
                defaultValue: "Approved");

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "IqcMaterialSpecs",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedBy",
                table: "IqcMaterialSpecs",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImportSource",
                table: "IqcMaterialSpecs",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TestMethod",
                table: "IqcMaterialSpecs",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LotQty",
                table: "IqcInspections",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SampleSizeOverrideReason",
                table: "IqcInspections",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SampleSizeSuggested",
                table: "IqcInspections",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "IqcCheckItemLibraries",
                maxLength: 16,
                nullable: false,
                defaultValue: "Any");

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "IqcCheckItemLibraries",
                maxLength: 16,
                nullable: false,
                defaultValue: "Verdict");

            migrationBuilder.AddColumn<int>(
                name: "MeasureCount",
                table: "IqcCheckItemLibraries",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "IqcResultMeasurements",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IqcResultDetailId = table.Column<long>(nullable: false),
                    Seq = table.Column<int>(nullable: false),
                    Value = table.Column<double>(nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IqcResultMeasurements", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IqcMaterialSpecs_Approval",
                table: "IqcMaterialSpecs",
                column: "Approval");

            migrationBuilder.CreateIndex(
                name: "IX_IqcCheckItemLibraries_Category_Kind",
                table: "IqcCheckItemLibraries",
                columns: new[] { "Category", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_IqcResultMeasurements_IqcResultDetailId_Seq",
                table: "IqcResultMeasurements",
                columns: new[] { "IqcResultDetailId", "Seq" },
                unique: true);

            // ── Backfill tường minh — lớp chặn THỨ HAI ────────────────────
            // EF sinh defaultValue = "" vì nó KHÔNG đọc giá trị khởi tạo của
            // property C#. "" không phải tên enum hợp lệ ⇒ 21 dòng thư viện và
            // 459 spec đang có sẽ nằm NGOÀI enum, và gate enum-integrity sẽ đỏ
            // — nhưng lúc đó dữ liệu đã hỏng. Đã bị đúng một lần khi làm bước
            // này; hai câu UPDATE dưới tốn vài mili-giây và bịt hẳn đường đó
            // kể cả khi ai đó sinh lại migration.
            //
            // 21 hạng mục cũ → "Any": chúng vốn là bộ CHUNG cho mọi nguyên
            // liệu; gán vào một nhóm cụ thể là viết lại lịch sử của phiếu đã
            // đóng băng theo chúng.
            // 459 spec cũ → "Approved": do người trong app tạo, không phải
            // hàng nhập từ file ngoài. Đẩy về PendingQc là dựng ra một hàng
            // chờ duyệt giả 459 mã mà chưa ai từng yêu cầu ai duyệt.
            migrationBuilder.Sql(
                "UPDATE IqcCheckItemLibraries SET Category = 'Any' WHERE Category IS NULL OR Category = '';");
            migrationBuilder.Sql(
                "UPDATE IqcCheckItemLibraries SET Kind = 'Verdict' WHERE Kind IS NULL OR Kind = '';");
            migrationBuilder.Sql(
                "UPDATE IqcMaterialSpecs SET Approval = 'Approved' WHERE Approval IS NULL OR Approval = '';");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IqcResultMeasurements");

            migrationBuilder.DropIndex(
                name: "IX_IqcMaterialSpecs_Approval",
                table: "IqcMaterialSpecs");

            migrationBuilder.DropIndex(
                name: "IX_IqcCheckItemLibraries_Category_Kind",
                table: "IqcCheckItemLibraries");

            migrationBuilder.DropColumn(
                name: "LimitLabel",
                table: "IqcSpecItems");

            migrationBuilder.DropColumn(
                name: "LimitLow",
                table: "IqcSpecItems");

            migrationBuilder.DropColumn(
                name: "LimitNominal",
                table: "IqcSpecItems");

            migrationBuilder.DropColumn(
                name: "LimitParsed",
                table: "IqcSpecItems");

            migrationBuilder.DropColumn(
                name: "LimitUnit",
                table: "IqcSpecItems");

            migrationBuilder.DropColumn(
                name: "LimitUp",
                table: "IqcSpecItems");

            migrationBuilder.DropColumn(
                name: "TearIsPass",
                table: "IqcSpecItems");

            migrationBuilder.DropColumn(
                name: "AutoVerdict",
                table: "IqcResultDetails");

            migrationBuilder.DropColumn(
                name: "AutoVerdictOffendingSeq",
                table: "IqcResultDetails");

            migrationBuilder.DropColumn(
                name: "AutoVerdictReason",
                table: "IqcResultDetails");

            migrationBuilder.DropColumn(
                name: "DefectCount",
                table: "IqcResultDetails");

            migrationBuilder.DropColumn(
                name: "OverriddenAt",
                table: "IqcResultDetails");

            migrationBuilder.DropColumn(
                name: "OverriddenBy",
                table: "IqcResultDetails");

            migrationBuilder.DropColumn(
                name: "OverrideReason",
                table: "IqcResultDetails");

            migrationBuilder.DropColumn(
                name: "TearObserved",
                table: "IqcResultDetails");

            migrationBuilder.DropColumn(
                name: "Approval",
                table: "IqcMaterialSpecs");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "IqcMaterialSpecs");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                table: "IqcMaterialSpecs");

            migrationBuilder.DropColumn(
                name: "ImportSource",
                table: "IqcMaterialSpecs");

            migrationBuilder.DropColumn(
                name: "TestMethod",
                table: "IqcMaterialSpecs");

            migrationBuilder.DropColumn(
                name: "LotQty",
                table: "IqcInspections");

            migrationBuilder.DropColumn(
                name: "SampleSizeOverrideReason",
                table: "IqcInspections");

            migrationBuilder.DropColumn(
                name: "SampleSizeSuggested",
                table: "IqcInspections");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "IqcCheckItemLibraries");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "IqcCheckItemLibraries");

            migrationBuilder.DropColumn(
                name: "MeasureCount",
                table: "IqcCheckItemLibraries");
        }
    }
}
