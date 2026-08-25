using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIpqcMaterialCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CheckType",
                table: "WoIpqcCheckItems",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MeasuredValue",
                table: "WoIpqcCheckItems",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WoIpqcMaterialChecks",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorkOrderId = table.Column<long>(nullable: false),
                    WoIpqcCheckId = table.Column<long>(nullable: true),
                    BomLineIdx = table.Column<int>(nullable: false),
                    MaterialCode = table.Column<string>(maxLength: 64, nullable: false),
                    MaterialDescription = table.Column<string>(maxLength: 256, nullable: true),
                    SourceIqcReceiptNo = table.Column<string>(maxLength: 64, nullable: true),
                    ExpectedPartNo = table.Column<string>(maxLength: 64, nullable: true),
                    ActualLotNo = table.Column<string>(maxLength: 64, nullable: true),
                    MaterialLotStatusSnapshot = table.Column<string>(maxLength: 32, nullable: true),
                    IqcResultSnapshot = table.Column<string>(maxLength: 16, nullable: true),
                    HasShadowFk = table.Column<bool>(nullable: false),
                    DivergenceFlags = table.Column<int>(nullable: false),
                    DivergenceKind = table.Column<string>(maxLength: 24, nullable: false),
                    Status = table.Column<string>(maxLength: 16, nullable: false),
                    NgReasonCode = table.Column<string>(maxLength: 64, nullable: true),
                    NgNote = table.Column<string>(maxLength: 500, nullable: true),
                    ConfirmedBy = table.Column<string>(maxLength: 128, nullable: true),
                    ConfirmedAt = table.Column<DateTime>(nullable: true),
                    DivergenceApprovalStatus = table.Column<string>(maxLength: 20, nullable: false),
                    ApprovedBy = table.Column<string>(maxLength: 128, nullable: true),
                    ApprovedAt = table.Column<DateTime>(nullable: true),
                    ApprovalReason = table.Column<string>(maxLength: 500, nullable: true),
                    Sort = table.Column<int>(nullable: false),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WoIpqcMaterialChecks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WoIpqcMaterialChecks_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WoIpqcMaterialChecks_WorkOrderId",
                table: "WoIpqcMaterialChecks",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_WoIpqcMaterialChecks_WorkOrderId_BomLineIdx",
                table: "WoIpqcMaterialChecks",
                columns: new[] { "WorkOrderId", "BomLineIdx" },
                unique: true);

            // Idempotent backfill: materialise a MATERIAL (SYSTEM) row for every
            // WO-level (WoLegId IS NULL) BOM line of WOs currently awaiting IPQC,
            // so existing IPQC_WAIT WOs show the panel without waiting for the
            // lazy GET materialise (7d pattern). Divergence snapshot columns stay
            // NULL until first confirm (freeze-at-confirm, Q4). WHERE NOT EXISTS
            // makes re-runs 0-touch. Per-leg (WoLegId set) rows are out of scope.
            migrationBuilder.Sql(@"
                INSERT INTO WoIpqcMaterialChecks
                    (WorkOrderId, BomLineIdx, MaterialCode, MaterialDescription,
                     HasShadowFk, DivergenceFlags, DivergenceKind, Status,
                     DivergenceApprovalStatus, Sort, CreatedAt)
                SELECT m.WorkOrderId, m.BomLineIdx, m.MaterialCode, m.MaterialDescription,
                       0, 0, 'None', 'Pending', 'NotRequired', m.BomLineIdx,
                       strftime('%Y-%m-%d %H:%M:%S', 'now')
                FROM WoMaterials m
                JOIN WorkOrders w ON w.Id = m.WorkOrderId
                WHERE w.MesPhase = 'IPQC_WAIT'
                  AND m.WoLegId IS NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM WoIpqcMaterialChecks x
                      WHERE x.WorkOrderId = m.WorkOrderId
                        AND x.BomLineIdx = m.BomLineIdx);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WoIpqcMaterialChecks");

            migrationBuilder.DropColumn(
                name: "CheckType",
                table: "WoIpqcCheckItems");

            migrationBuilder.DropColumn(
                name: "MeasuredValue",
                table: "WoIpqcCheckItems");
        }
    }
}
