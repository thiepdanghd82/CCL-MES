using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPerLegPartialUniqueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WoPlateChecks_WorkOrderId",
                table: "WoPlateChecks");

            migrationBuilder.DropIndex(
                name: "IX_WoMaterials_WorkOrderId_BomLineIdx",
                table: "WoMaterials");

            migrationBuilder.DropIndex(
                name: "IX_WoIpqcChecks_WorkOrderId",
                table: "WoIpqcChecks");

            migrationBuilder.DropIndex(
                name: "IX_WoCutterChecks_WorkOrderId",
                table: "WoCutterChecks");

            migrationBuilder.CreateIndex(
                name: "IX_WoPlateChecks_WorkOrderId",
                table: "WoPlateChecks",
                column: "WorkOrderId",
                unique: true,
                filter: "\"WoLegId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WoPlateChecks_WorkOrderId_WoLegId",
                table: "WoPlateChecks",
                columns: new[] { "WorkOrderId", "WoLegId" },
                unique: true,
                filter: "\"WoLegId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WoMaterials_WorkOrderId_BomLineIdx",
                table: "WoMaterials",
                columns: new[] { "WorkOrderId", "BomLineIdx" },
                unique: true,
                filter: "\"WoLegId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WoMaterials_WorkOrderId_WoLegId_BomLineIdx",
                table: "WoMaterials",
                columns: new[] { "WorkOrderId", "WoLegId", "BomLineIdx" },
                unique: true,
                filter: "\"WoLegId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WoIpqcChecks_WorkOrderId",
                table: "WoIpqcChecks",
                column: "WorkOrderId",
                unique: true,
                filter: "\"WoLegId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WoIpqcChecks_WorkOrderId_WoLegId",
                table: "WoIpqcChecks",
                columns: new[] { "WorkOrderId", "WoLegId" },
                unique: true,
                filter: "\"WoLegId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WoCutterChecks_WorkOrderId",
                table: "WoCutterChecks",
                column: "WorkOrderId",
                unique: true,
                filter: "\"WoLegId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WoCutterChecks_WorkOrderId_WoLegId",
                table: "WoCutterChecks",
                columns: new[] { "WorkOrderId", "WoLegId" },
                unique: true,
                filter: "\"WoLegId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WoPlateChecks_WorkOrderId",
                table: "WoPlateChecks");

            migrationBuilder.DropIndex(
                name: "IX_WoPlateChecks_WorkOrderId_WoLegId",
                table: "WoPlateChecks");

            migrationBuilder.DropIndex(
                name: "IX_WoMaterials_WorkOrderId_BomLineIdx",
                table: "WoMaterials");

            migrationBuilder.DropIndex(
                name: "IX_WoMaterials_WorkOrderId_WoLegId_BomLineIdx",
                table: "WoMaterials");

            migrationBuilder.DropIndex(
                name: "IX_WoIpqcChecks_WorkOrderId",
                table: "WoIpqcChecks");

            migrationBuilder.DropIndex(
                name: "IX_WoIpqcChecks_WorkOrderId_WoLegId",
                table: "WoIpqcChecks");

            migrationBuilder.DropIndex(
                name: "IX_WoCutterChecks_WorkOrderId",
                table: "WoCutterChecks");

            migrationBuilder.DropIndex(
                name: "IX_WoCutterChecks_WorkOrderId_WoLegId",
                table: "WoCutterChecks");

            migrationBuilder.CreateIndex(
                name: "IX_WoPlateChecks_WorkOrderId",
                table: "WoPlateChecks",
                column: "WorkOrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WoMaterials_WorkOrderId_BomLineIdx",
                table: "WoMaterials",
                columns: new[] { "WorkOrderId", "BomLineIdx" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WoIpqcChecks_WorkOrderId",
                table: "WoIpqcChecks",
                column: "WorkOrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WoCutterChecks_WorkOrderId",
                table: "WoCutterChecks",
                column: "WorkOrderId",
                unique: true);
        }
    }
}
