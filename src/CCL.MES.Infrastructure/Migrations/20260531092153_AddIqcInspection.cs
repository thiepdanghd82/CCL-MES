using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIqcInspection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IqcInspections",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RawMaterialId = table.Column<long>(nullable: true),
                    PartNo = table.Column<string>(nullable: false),
                    BatchNumber = table.Column<string>(nullable: false),
                    LotNumber = table.Column<string>(nullable: true),
                    ReceivedDate = table.Column<DateTime>(nullable: false),
                    SupplierName = table.Column<string>(nullable: true),
                    Quantity = table.Column<double>(nullable: false),
                    UomQty = table.Column<string>(nullable: true),
                    InspectorId = table.Column<string>(nullable: true),
                    SampleSize = table.Column<int>(nullable: false),
                    Result = table.Column<string>(nullable: false),
                    ApprovedBy = table.Column<string>(nullable: true),
                    ApprovedAt = table.Column<DateTime>(nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IqcInspections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IqcInspections_RawMaterials_RawMaterialId",
                        column: x => x.RawMaterialId,
                        principalTable: "RawMaterials",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "IqcResultDetails",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IqcInspectionId = table.Column<long>(nullable: false),
                    ItemName = table.Column<string>(nullable: false),
                    MeasuredValue = table.Column<string>(nullable: true),
                    Pass = table.Column<bool>(nullable: false),
                    DefectCode = table.Column<string>(nullable: true),
                    Qty = table.Column<int>(nullable: false),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IqcResultDetails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IqcResultDetails_IqcInspections_IqcInspectionId",
                        column: x => x.IqcInspectionId,
                        principalTable: "IqcInspections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IqcInspections_BatchNumber",
                table: "IqcInspections",
                column: "BatchNumber");

            migrationBuilder.CreateIndex(
                name: "IX_IqcInspections_PartNo",
                table: "IqcInspections",
                column: "PartNo");

            migrationBuilder.CreateIndex(
                name: "IX_IqcInspections_RawMaterialId",
                table: "IqcInspections",
                column: "RawMaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_IqcInspections_ReceivedDate",
                table: "IqcInspections",
                column: "ReceivedDate");

            migrationBuilder.CreateIndex(
                name: "IX_IqcResultDetails_IqcInspectionId",
                table: "IqcResultDetails",
                column: "IqcInspectionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IqcResultDetails");

            migrationBuilder.DropTable(
                name: "IqcInspections");
        }
    }
}
