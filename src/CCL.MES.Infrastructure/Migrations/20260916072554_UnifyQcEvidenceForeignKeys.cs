using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UnifyQcEvidenceForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WoIpqcChecks_WorkOrders_WorkOrderId",
                table: "WoIpqcChecks");

            migrationBuilder.AddForeignKey(
                name: "FK_SemiAllocations_WorkOrders_WorkOrderId",
                table: "SemiAllocations",
                column: "WorkOrderId",
                principalTable: "WorkOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_WoIpqcChecks_WorkOrders_WorkOrderId",
                table: "WoIpqcChecks",
                column: "WorkOrderId",
                principalTable: "WorkOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WoMaterials_WorkOrders_WorkOrderId",
                table: "WoMaterials",
                column: "WorkOrderId",
                principalTable: "WorkOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_WoPauseEvents_WorkOrders_WoId",
                table: "WoPauseEvents",
                column: "WoId",
                principalTable: "WorkOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_WoQcChecks_WorkOrders_WorkOrderId",
                table: "WoQcChecks",
                column: "WorkOrderId",
                principalTable: "WorkOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WoQcPhotos_WoQcCheckItems_WoQcCheckItemId",
                table: "WoQcPhotos",
                column: "WoQcCheckItemId",
                principalTable: "WoQcCheckItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WoQtyEntries_WorkOrders_WoId",
                table: "WoQtyEntries",
                column: "WoId",
                principalTable: "WorkOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_WoRunSessions_WorkOrders_WoId",
                table: "WoRunSessions",
                column: "WoId",
                principalTable: "WorkOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SemiAllocations_WorkOrders_WorkOrderId",
                table: "SemiAllocations");

            migrationBuilder.DropForeignKey(
                name: "FK_WoIpqcChecks_WorkOrders_WorkOrderId",
                table: "WoIpqcChecks");

            migrationBuilder.DropForeignKey(
                name: "FK_WoMaterials_WorkOrders_WorkOrderId",
                table: "WoMaterials");

            migrationBuilder.DropForeignKey(
                name: "FK_WoPauseEvents_WorkOrders_WoId",
                table: "WoPauseEvents");

            migrationBuilder.DropForeignKey(
                name: "FK_WoQcChecks_WorkOrders_WorkOrderId",
                table: "WoQcChecks");

            migrationBuilder.DropForeignKey(
                name: "FK_WoQcPhotos_WoQcCheckItems_WoQcCheckItemId",
                table: "WoQcPhotos");

            migrationBuilder.DropForeignKey(
                name: "FK_WoQtyEntries_WorkOrders_WoId",
                table: "WoQtyEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_WoRunSessions_WorkOrders_WoId",
                table: "WoRunSessions");

            migrationBuilder.AddForeignKey(
                name: "FK_WoIpqcChecks_WorkOrders_WorkOrderId",
                table: "WoIpqcChecks",
                column: "WorkOrderId",
                principalTable: "WorkOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
