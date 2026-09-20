using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWoPhaseSpan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WoPhaseSpans",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WoId = table.Column<long>(nullable: false),
                    Phase = table.Column<string>(maxLength: 32, nullable: false),
                    VisitNo = table.Column<int>(nullable: false),
                    StartedAt = table.Column<DateTime>(nullable: false),
                    EndedAt = table.Column<DateTime>(nullable: true),
                    StartedBy = table.Column<string>(maxLength: 128, nullable: false),
                    EndedBy = table.Column<string>(maxLength: 128, nullable: true),
                    WoLegId = table.Column<long>(nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WoPhaseSpans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WoPhaseSpans_WorkOrders_WoId",
                        column: x => x.WoId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WoPhaseSpans_WoId_Phase",
                table: "WoPhaseSpans",
                columns: new[] { "WoId", "Phase" });

            migrationBuilder.CreateIndex(
                name: "IX_WoPhaseSpans_WoId_StartedAt",
                table: "WoPhaseSpans",
                columns: new[] { "WoId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WoPhaseSpans_WoLegId",
                table: "WoPhaseSpans",
                column: "WoLegId");

            migrationBuilder.CreateIndex(
                name: "UX_WoPhaseSpans_OpenPerLeg",
                table: "WoPhaseSpans",
                columns: new[] { "WoId", "WoLegId" },
                unique: true,
                filter: "\"EndedAt\" IS NULL AND \"WoLegId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_WoPhaseSpans_OpenPerWo",
                table: "WoPhaseSpans",
                column: "WoId",
                unique: true,
                filter: "\"EndedAt\" IS NULL AND \"WoLegId\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WoPhaseSpans");
        }
    }
}
