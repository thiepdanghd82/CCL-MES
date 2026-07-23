using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRoutingLegDag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "WoLegId",
                table: "WoRunSessions",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "WoLegId",
                table: "WoQtyEntries",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "WoLegId",
                table: "WoPlateChecks",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "WoLegId",
                table: "WoPauseEvents",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "WoLegId",
                table: "WoMaterials",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "WoLegId",
                table: "WoIpqcChecks",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "WoLegId",
                table: "WoIpqcCheckItems",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "WoLegId",
                table: "WoCutterChecks",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProcessLegMaps",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MatchType = table.Column<string>(maxLength: 32, nullable: false),
                    MatchValue = table.Column<string>(maxLength: 128, nullable: false),
                    LegKind = table.Column<string>(maxLength: 16, nullable: false),
                    Method = table.Column<string>(maxLength: 32, nullable: false),
                    ProcessLine = table.Column<string>(maxLength: 16, nullable: false),
                    Sort = table.Column<int>(nullable: false),
                    Active = table.Column<bool>(nullable: false),
                    Note = table.Column<string>(maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessLegMaps", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WoLegDependencies",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorkOrderId = table.Column<long>(nullable: false),
                    LegId = table.Column<long>(nullable: false),
                    DependsOnLegId = table.Column<long>(nullable: false),
                    DependencyGate = table.Column<string>(maxLength: 8, nullable: false),
                    RequiredQty = table.Column<int>(nullable: false),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WoLegDependencies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WoLegDependencies_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WoLegs",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorkOrderId = table.Column<long>(nullable: false),
                    Sequence = table.Column<int>(nullable: false),
                    LegKind = table.Column<string>(maxLength: 16, nullable: false),
                    Method = table.Column<string>(maxLength: 32, nullable: false),
                    ProcessLine = table.Column<string>(maxLength: 16, nullable: false),
                    SpecRevisionId = table.Column<long>(nullable: true),
                    SurfaceProfile = table.Column<string>(maxLength: 8, nullable: false),
                    InputSource = table.Column<string>(maxLength: 16, nullable: false),
                    LegPhase = table.Column<string>(maxLength: 16, nullable: false),
                    RowVersion = table.Column<byte[]>(rowVersion: true, nullable: false),
                    QtyDoneCached = table.Column<int>(nullable: false),
                    QtyNgCached = table.Column<int>(nullable: false),
                    LegDoneAt = table.Column<DateTime>(nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WoLegs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WoLegs_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WoRunSessions_WoLegId",
                table: "WoRunSessions",
                column: "WoLegId");

            migrationBuilder.CreateIndex(
                name: "IX_WoQtyEntries_WoLegId",
                table: "WoQtyEntries",
                column: "WoLegId");

            migrationBuilder.CreateIndex(
                name: "IX_WoPlateChecks_WoLegId",
                table: "WoPlateChecks",
                column: "WoLegId");

            migrationBuilder.CreateIndex(
                name: "IX_WoPauseEvents_WoLegId",
                table: "WoPauseEvents",
                column: "WoLegId");

            migrationBuilder.CreateIndex(
                name: "IX_WoMaterials_WoLegId",
                table: "WoMaterials",
                column: "WoLegId");

            migrationBuilder.CreateIndex(
                name: "IX_WoIpqcChecks_WoLegId",
                table: "WoIpqcChecks",
                column: "WoLegId");

            migrationBuilder.CreateIndex(
                name: "IX_WoIpqcCheckItems_WoLegId",
                table: "WoIpqcCheckItems",
                column: "WoLegId");

            migrationBuilder.CreateIndex(
                name: "IX_WoCutterChecks_WoLegId",
                table: "WoCutterChecks",
                column: "WoLegId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessLegMaps_MatchType_MatchValue",
                table: "ProcessLegMaps",
                columns: new[] { "MatchType", "MatchValue" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WoLegDependencies_WorkOrderId",
                table: "WoLegDependencies",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_WoLegDependencies_WorkOrderId_LegId_DependsOnLegId",
                table: "WoLegDependencies",
                columns: new[] { "WorkOrderId", "LegId", "DependsOnLegId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WoLegs_WorkOrderId",
                table: "WoLegs",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_WoLegs_WorkOrderId_Sequence",
                table: "WoLegs",
                columns: new[] { "WorkOrderId", "Sequence" },
                unique: true);

            // P11-1 — per-leg optimistic concurrency. SQLite có no auto
            // RowVersion semantic (EF IsRowVersion() chỉ tự bump ở SQL
            // Server); 2 trigger sinh randomblob(8) khi app không tự set,
            // đúng pattern WorkOrders_RowVersion_On{Insert,Update}.
            migrationBuilder.Sql(@"
                CREATE TRIGGER IF NOT EXISTS WoLegs_RowVersion_OnInsert
                AFTER INSERT ON WoLegs
                FOR EACH ROW
                WHEN length(NEW.RowVersion) = 0
                BEGIN
                    UPDATE WoLegs
                    SET RowVersion = randomblob(8)
                    WHERE rowid = NEW.rowid;
                END;
            ");
            migrationBuilder.Sql(@"
                CREATE TRIGGER IF NOT EXISTS WoLegs_RowVersion_OnUpdate
                AFTER UPDATE ON WoLegs
                FOR EACH ROW
                WHEN NEW.RowVersion = OLD.RowVersion
                BEGIN
                    UPDATE WoLegs
                    SET RowVersion = randomblob(8)
                    WHERE rowid = NEW.rowid;
                END;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS WoLegs_RowVersion_OnInsert;");
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS WoLegs_RowVersion_OnUpdate;");

            migrationBuilder.DropTable(
                name: "ProcessLegMaps");

            migrationBuilder.DropTable(
                name: "WoLegDependencies");

            migrationBuilder.DropTable(
                name: "WoLegs");

            migrationBuilder.DropIndex(
                name: "IX_WoRunSessions_WoLegId",
                table: "WoRunSessions");

            migrationBuilder.DropIndex(
                name: "IX_WoQtyEntries_WoLegId",
                table: "WoQtyEntries");

            migrationBuilder.DropIndex(
                name: "IX_WoPlateChecks_WoLegId",
                table: "WoPlateChecks");

            migrationBuilder.DropIndex(
                name: "IX_WoPauseEvents_WoLegId",
                table: "WoPauseEvents");

            migrationBuilder.DropIndex(
                name: "IX_WoMaterials_WoLegId",
                table: "WoMaterials");

            migrationBuilder.DropIndex(
                name: "IX_WoIpqcChecks_WoLegId",
                table: "WoIpqcChecks");

            migrationBuilder.DropIndex(
                name: "IX_WoIpqcCheckItems_WoLegId",
                table: "WoIpqcCheckItems");

            migrationBuilder.DropIndex(
                name: "IX_WoCutterChecks_WoLegId",
                table: "WoCutterChecks");

            migrationBuilder.DropColumn(
                name: "WoLegId",
                table: "WoRunSessions");

            migrationBuilder.DropColumn(
                name: "WoLegId",
                table: "WoQtyEntries");

            migrationBuilder.DropColumn(
                name: "WoLegId",
                table: "WoPlateChecks");

            migrationBuilder.DropColumn(
                name: "WoLegId",
                table: "WoPauseEvents");

            migrationBuilder.DropColumn(
                name: "WoLegId",
                table: "WoMaterials");

            migrationBuilder.DropColumn(
                name: "WoLegId",
                table: "WoIpqcChecks");

            migrationBuilder.DropColumn(
                name: "WoLegId",
                table: "WoIpqcCheckItems");

            migrationBuilder.DropColumn(
                name: "WoLegId",
                table: "WoCutterChecks");
        }
    }
}
