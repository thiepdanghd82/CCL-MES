using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSpecQcCaptureAndReasonCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReasonCodes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    LabelEn = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    LabelVi = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Active = table.Column<bool>(type: "INTEGER", nullable: false),
                    Sort = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReasonCodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SpecQcCaptures",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SpecQcWindowId = table.Column<long>(type: "INTEGER", nullable: false),
                    QcCriterionId = table.Column<long>(type: "INTEGER", nullable: false),
                    Result = table.Column<string>(type: "TEXT", nullable: false),
                    Measurement = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    NgReasonCode = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    Comment = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CapturedBy = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    CapturedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpecQcCaptures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpecQcCaptures_QcCriteria_QcCriterionId",
                        column: x => x.QcCriterionId,
                        principalTable: "QcCriteria",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SpecQcCaptures_SpecQcWindows_SpecQcWindowId",
                        column: x => x.SpecQcWindowId,
                        principalTable: "SpecQcWindows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReasonCodes_Code",
                table: "ReasonCodes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReasonCodes_Kind_Active_Sort",
                table: "ReasonCodes",
                columns: new[] { "Kind", "Active", "Sort" });

            migrationBuilder.CreateIndex(
                name: "IX_SpecQcCaptures_CapturedAt",
                table: "SpecQcCaptures",
                column: "CapturedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SpecQcCaptures_QcCriterionId",
                table: "SpecQcCaptures",
                column: "QcCriterionId");

            migrationBuilder.CreateIndex(
                name: "IX_SpecQcCaptures_SpecQcWindowId_QcCriterionId",
                table: "SpecQcCaptures",
                columns: new[] { "SpecQcWindowId", "QcCriterionId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReasonCodes");

            migrationBuilder.DropTable(
                name: "SpecQcCaptures");
        }
    }
}
