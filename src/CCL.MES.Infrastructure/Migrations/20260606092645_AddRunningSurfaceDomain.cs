using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRunningSurfaceDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "QtyDoneCached",
                table: "WorkOrders",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "QtyNgCached",
                table: "WorkOrders",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SettingDurationSec",
                table: "WorkOrders",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SettingEndAt",
                table: "WorkOrders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SettingStartAt",
                table: "WorkOrders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WoPauseEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WoId = table.Column<long>(type: "INTEGER", nullable: false),
                    RunSessionId = table.Column<long>(type: "INTEGER", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReasonCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    StartedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WoPauseEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WoQtyEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WoId = table.Column<long>(type: "INTEGER", nullable: false),
                    RunSessionId = table.Column<long>(type: "INTEGER", nullable: false),
                    Ts = table.Column<DateTime>(type: "TEXT", nullable: false),
                    QtyDoneDelta = table.Column<int>(type: "INTEGER", nullable: false),
                    QtyNgDelta = table.Column<int>(type: "INTEGER", nullable: false),
                    NgReasonCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    NgNote = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    LinkedEntryId = table.Column<long>(type: "INTEGER", nullable: true),
                    CorrectionReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    EnteredBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WoQtyEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WoRunSessions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WoId = table.Column<long>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    StartedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    EndedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "BLOB", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WoRunSessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WoPauseEvents_RunSessionId",
                table: "WoPauseEvents",
                column: "RunSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_WoPauseEvents_WoId",
                table: "WoPauseEvents",
                column: "WoId");

            migrationBuilder.CreateIndex(
                name: "IX_WoQtyEntries_LinkedEntryId",
                table: "WoQtyEntries",
                column: "LinkedEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_WoQtyEntries_RunSessionId",
                table: "WoQtyEntries",
                column: "RunSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_WoQtyEntries_WoId",
                table: "WoQtyEntries",
                column: "WoId");

            migrationBuilder.CreateIndex(
                name: "IX_WoQtyEntries_WoId_Ts",
                table: "WoQtyEntries",
                columns: new[] { "WoId", "Ts" });

            migrationBuilder.CreateIndex(
                name: "IX_WoRunSessions_WoId",
                table: "WoRunSessions",
                column: "WoId");

            migrationBuilder.CreateIndex(
                name: "IX_WoRunSessions_WoId_EndedAt",
                table: "WoRunSessions",
                columns: new[] { "WoId", "EndedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WoPauseEvents");

            migrationBuilder.DropTable(
                name: "WoQtyEntries");

            migrationBuilder.DropTable(
                name: "WoRunSessions");

            migrationBuilder.DropColumn(
                name: "QtyDoneCached",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "QtyNgCached",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "SettingDurationSec",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "SettingEndAt",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "SettingStartAt",
                table: "WorkOrders");
        }
    }
}
