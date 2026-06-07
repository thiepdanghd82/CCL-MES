using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFqcOqcQualitySurface : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "QcProfileOverride",
                table: "Products",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WoQcChecks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorkOrderId = table.Column<long>(type: "INTEGER", nullable: false),
                    QcKind = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    ProfileSnapshotJson = table.Column<string>(type: "TEXT", nullable: false),
                    Judgment = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    JudgmentReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    InspectedBy = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    InspectedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReviewedBy = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ApprovedBy = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WoQcChecks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WoQcPhotos",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WoQcCheckItemId = table.Column<long>(type: "INTEGER", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    MimeType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    OriginalFileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    UploadedBy = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WoQcPhotos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WoQcCheckItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WoQcCheckId = table.Column<long>(type: "INTEGER", nullable: false),
                    ItemKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    NgReasonCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    NgNote = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    PhotoBlobId = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WoQcCheckItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WoQcCheckItems_WoQcChecks_WoQcCheckId",
                        column: x => x.WoQcCheckId,
                        principalTable: "WoQcChecks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WoQcCheckItems_WoQcCheckId_ItemKey",
                table: "WoQcCheckItems",
                columns: new[] { "WoQcCheckId", "ItemKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WoQcChecks_WorkOrderId_QcKind",
                table: "WoQcChecks",
                columns: new[] { "WorkOrderId", "QcKind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WoQcPhotos_Sha256",
                table: "WoQcPhotos",
                column: "Sha256");

            migrationBuilder.CreateIndex(
                name: "IX_WoQcPhotos_WoQcCheckItemId",
                table: "WoQcPhotos",
                column: "WoQcCheckItemId");

            // P10.7e-1 Q3 — idempotent backfill for legacy WOs already
            // past RUNNING. Mirrors the 7b BomSnapshot + 7d IPQC lazy-
            // materialise + backfill pattern. Controller will INSERT on
            // first read for new WOs; existing FQC_PENDING / OQC_PENDING
            // legacy rows get a blank Pending parent row here so the
            // FqcDashboard / OqcDashboard renders against a non-null
            // check on first load. ProfileSnapshotJson seeded as '{}'
            // so the dashboard surfaces "empty profile — admin must
            // seed profiles.json" inline instead of crashing on a null
            // JSON parse.
            //
            // The UNIQUE index on (WorkOrderId, QcKind) makes this
            // idempotent on a 2nd migration run (NOT EXISTS guard).
            // Two INSERTs — one per QcKind — because the unique index
            // pairs WO + kind, not WO alone.
            migrationBuilder.Sql(@"
                INSERT INTO WoQcChecks
                    (WorkOrderId, QcKind, ProfileSnapshotJson, Judgment, CreatedAt)
                SELECT wo.Id, 'FQC', '{}', 'Pending', datetime('now')
                FROM WorkOrders wo
                WHERE wo.MesPhase IN ('FQC_PENDING', 'OQC_PENDING')
                  AND NOT EXISTS (
                    SELECT 1 FROM WoQcChecks qc
                    WHERE qc.WorkOrderId = wo.Id AND qc.QcKind = 'FQC'
                  );
            ");
            migrationBuilder.Sql(@"
                INSERT INTO WoQcChecks
                    (WorkOrderId, QcKind, ProfileSnapshotJson, Judgment, CreatedAt)
                SELECT wo.Id, 'OQC', '{}', 'Pending', datetime('now')
                FROM WorkOrders wo
                WHERE wo.MesPhase = 'OQC_PENDING'
                  AND NOT EXISTS (
                    SELECT 1 FROM WoQcChecks qc
                    WHERE qc.WorkOrderId = wo.Id AND qc.QcKind = 'OQC'
                  );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WoQcCheckItems");

            migrationBuilder.DropTable(
                name: "WoQcPhotos");

            migrationBuilder.DropTable(
                name: "WoQcChecks");

            migrationBuilder.DropColumn(
                name: "QcProfileOverride",
                table: "Products");
        }
    }
}
