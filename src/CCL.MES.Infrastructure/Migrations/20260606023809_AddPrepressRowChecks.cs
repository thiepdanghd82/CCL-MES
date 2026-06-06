using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPrepressRowChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WoCutterChecks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorkOrderId = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    CutterNo = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    NgReasonCode = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    NgNote = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CheckedBy = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    CheckedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WoCutterChecks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WoMaterials",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorkOrderId = table.Column<long>(type: "INTEGER", nullable: false),
                    BomLineIdx = table.Column<int>(type: "INTEGER", nullable: false),
                    MaterialCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    MaterialDescription = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    QtyRequired = table.Column<double>(type: "REAL", nullable: false),
                    Uom = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    QtyLoaded = table.Column<double>(type: "REAL", nullable: true),
                    LotNo = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    NgReasonCode = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    NgNote = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CheckedBy = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    CheckedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WoMaterials", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WoPlateChecks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorkOrderId = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    PlateNo = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    NgReasonCode = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    NgNote = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CheckedBy = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    CheckedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WoPlateChecks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WoCutterChecks_WorkOrderId",
                table: "WoCutterChecks",
                column: "WorkOrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WoMaterials_WorkOrderId",
                table: "WoMaterials",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_WoMaterials_WorkOrderId_BomLineIdx",
                table: "WoMaterials",
                columns: new[] { "WorkOrderId", "BomLineIdx" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WoPlateChecks_WorkOrderId",
                table: "WoPlateChecks",
                column: "WorkOrderId",
                unique: true);

            // ── P10.7b-1 backfill ────────────────────────────────────
            // Insert 1 PENDING WoPlateCheck + 1 PENDING WoCutterCheck per
            // existing PREPRESS-phase WO so the new endpoints have rows to
            // overwrite without forcing the operator to manually trigger a
            // materialise. wo_materials backfill: snapshot from
            // ManufacturingStructure via JOIN where ProductRevision +
            // Product link is intact; WOs whose ProductRevisionId is null
            // OR whose ProductCode has no MS rows get zero materials rows
            // (operator fills via 7b-2 endpoint after deploy).
            //
            // The backfill preserves legacy parity (Lesson: WorkOrder.MaterialsReady
            // bool stays authoritative for legacy Razor reads; the new
            // child-row rollup only governs the Hybrid client + future
            // PREPRESS→SETTING gate).
            migrationBuilder.Sql(@"
                INSERT INTO WoPlateChecks
                    (WorkOrderId, Status, CreatedAt)
                SELECT wo.Id, 'Pending', datetime('now')
                FROM WorkOrders wo
                WHERE wo.MesPhase = 'PREPRESS'
                  AND NOT EXISTS (
                    SELECT 1 FROM WoPlateChecks p WHERE p.WorkOrderId = wo.Id
                  );
            ");
            migrationBuilder.Sql(@"
                INSERT INTO WoCutterChecks
                    (WorkOrderId, Status, CreatedAt)
                SELECT wo.Id, 'Pending', datetime('now')
                FROM WorkOrders wo
                WHERE wo.MesPhase = 'PREPRESS'
                  AND NOT EXISTS (
                    SELECT 1 FROM WoCutterChecks c WHERE c.WorkOrderId = wo.Id
                  );
            ");
            migrationBuilder.Sql(@"
                INSERT INTO WoMaterials
                    (WorkOrderId, BomLineIdx, MaterialCode, MaterialDescription,
                     QtyRequired, Uom, Status, CreatedAt)
                SELECT
                    wo.Id,
                    (SELECT COUNT(*) FROM ManufacturingStructures ms2
                       WHERE ms2.ParentPart = p.ProductCode
                         AND ms2.Id < ms.Id) AS BomLineIdx,
                    ms.ComponentPart,
                    ms.ComponentDescription,
                    ms.QtyAssembly * wo.TargetQty,
                    ms.Uom,
                    'Pending',
                    datetime('now')
                FROM WorkOrders wo
                INNER JOIN ProductRevisions pr ON pr.Id = wo.ProductRevisionId
                INNER JOIN Products p ON p.Id = pr.ProductId
                INNER JOIN ManufacturingStructures ms ON ms.ParentPart = p.ProductCode
                WHERE wo.MesPhase = 'PREPRESS'
                  AND NOT EXISTS (
                    SELECT 1 FROM WoMaterials wm WHERE wm.WorkOrderId = wo.Id
                  );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WoCutterChecks");

            migrationBuilder.DropTable(
                name: "WoMaterials");

            migrationBuilder.DropTable(
                name: "WoPlateChecks");
        }
    }
}
