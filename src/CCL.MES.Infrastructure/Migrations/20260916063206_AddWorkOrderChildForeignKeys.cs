using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkOrderChildForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "FK_WoCutterChecks_WorkOrders_WorkOrderId",
                table: "WoCutterChecks",
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
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_WoPlateChecks_WorkOrders_WorkOrderId",
                table: "WoPlateChecks",
                column: "WorkOrderId",
                principalTable: "WorkOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_WoTraceIndexes_WorkOrders_WoId",
                table: "WoTraceIndexes",
                column: "WoId",
                principalTable: "WorkOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_WoTraceSnapshots_WorkOrders_WoId",
                table: "WoTraceSnapshots",
                column: "WoId",
                principalTable: "WorkOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WoCutterChecks_WorkOrders_WorkOrderId",
                table: "WoCutterChecks");

            migrationBuilder.DropForeignKey(
                name: "FK_WoIpqcChecks_WorkOrders_WorkOrderId",
                table: "WoIpqcChecks");

            migrationBuilder.DropForeignKey(
                name: "FK_WoPlateChecks_WorkOrders_WorkOrderId",
                table: "WoPlateChecks");

            migrationBuilder.DropForeignKey(
                name: "FK_WoTraceIndexes_WorkOrders_WoId",
                table: "WoTraceIndexes");

            migrationBuilder.DropForeignKey(
                name: "FK_WoTraceSnapshots_WorkOrders_WoId",
                table: "WoTraceSnapshots");
        }
    }
}
