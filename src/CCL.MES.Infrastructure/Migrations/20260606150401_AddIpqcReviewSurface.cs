using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIpqcReviewSurface : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WoIpqcChecks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorkOrderId = table.Column<long>(type: "INTEGER", nullable: false),
                    MaterialStatus = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    MaterialNgReasonCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    MaterialNgNote = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    PrintAStatus = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    PrintANgReasonCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    PrintANgNote = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    PrintBStatus = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    PrintBNgReasonCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    PrintBNgNote = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    PrintCStatus = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    PrintCNgReasonCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    PrintCNgNote = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Judgment = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    SpecialAcceptReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IpqcSubmittedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    IpqcSubmittedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    QaOutcome = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    QaReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    QaApprovedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    QaApprovedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WoIpqcChecks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WoIpqcChecks_WorkOrderId",
                table: "WoIpqcChecks",
                column: "WorkOrderId",
                unique: true);

            // P10.7d-1 — idempotent backfill for WOs already past PREPRESS.
            // Lazy-materialise is the operational pattern (controller will
            // INSERT on first read), but legacy WOs that ALREADY moved past
            // SETTING into IPQC_WAIT / IPQC_APPROVED / QA_PENDING / RUNNING /
            // PAUSED / FQC_PENDING / OQC_PENDING via the 7c shim need a row
            // to render against — otherwise the IPQC dashboard renders empty
            // for an already-running WO. We INSERT a blank Pending row
            // where one's missing; the UNIQUE index makes the operation
            // idempotent on a second migration run (NOT EXISTS guard).
            migrationBuilder.Sql(@"
                INSERT INTO WoIpqcChecks
                    (WorkOrderId, MaterialStatus, PrintAStatus, PrintBStatus, PrintCStatus,
                     Judgment, QaOutcome, CreatedAt)
                SELECT wo.Id, 'Pending', 'Pending', 'Pending', 'Pending',
                       'Pending', 'Pending', datetime('now')
                FROM WorkOrders wo
                WHERE wo.MesPhase IN ('IPQC_WAIT', 'QA_PENDING', 'IPQC_APPROVED',
                                       'RUNNING', 'PAUSED', 'FQC_PENDING', 'OQC_PENDING')
                  AND NOT EXISTS (
                    SELECT 1 FROM WoIpqcChecks ipqc WHERE ipqc.WorkOrderId = wo.Id
                  );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WoIpqcChecks");
        }
    }
}
