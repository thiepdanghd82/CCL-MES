using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIqcNgClaim : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IqcNgRecords",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IqcInspectionId = table.Column<long>(nullable: true),
                    MaterialLotId = table.Column<long>(nullable: true),
                    PartNo = table.Column<string>(maxLength: 32, nullable: true),
                    SupplierLotNo = table.Column<string>(maxLength: 64, nullable: true),
                    SupplierName = table.Column<string>(maxLength: 200, nullable: true),
                    MaterialName = table.Column<string>(maxLength: 300, nullable: true),
                    PoNo = table.Column<string>(maxLength: 64, nullable: true),
                    DetectedAt = table.Column<DateTime>(nullable: false),
                    DetectedStage = table.Column<string>(maxLength: 16, nullable: false),
                    DefectName = table.Column<string>(maxLength: 256, nullable: true),
                    DefectCode = table.Column<string>(maxLength: 32, nullable: true),
                    NgQty = table.Column<double>(nullable: true),
                    NgUom = table.Column<string>(maxLength: 16, nullable: true),
                    NgAreaM2 = table.Column<double>(nullable: true),
                    NgRolls = table.Column<int>(nullable: true),
                    Status = table.Column<string>(maxLength: 24, nullable: false),
                    ClaimedAt = table.Column<DateTime>(nullable: true),
                    ClaimRef = table.Column<string>(maxLength: 128, nullable: true),
                    Settlement = table.Column<string>(maxLength: 16, nullable: false),
                    SettledAt = table.Column<DateTime>(nullable: true),
                    SupplierNote = table.Column<string>(maxLength: 512, nullable: true),
                    Remark = table.Column<string>(maxLength: 512, nullable: true),
                    ImportSource = table.Column<string>(maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IqcNgRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IqcNgRecords_DetectedAt",
                table: "IqcNgRecords",
                column: "DetectedAt");

            migrationBuilder.CreateIndex(
                name: "IX_IqcNgRecords_IqcInspectionId",
                table: "IqcNgRecords",
                column: "IqcInspectionId");

            migrationBuilder.CreateIndex(
                name: "IX_IqcNgRecords_PartNo",
                table: "IqcNgRecords",
                column: "PartNo");

            migrationBuilder.CreateIndex(
                name: "IX_IqcNgRecords_Status",
                table: "IqcNgRecords",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IqcNgRecords");
        }
    }
}
