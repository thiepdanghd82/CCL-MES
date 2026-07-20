using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWoTraceSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WoTraceSnapshots",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WoId = table.Column<long>(nullable: false),
                    WoNo = table.Column<string>(nullable: false),
                    Phase = table.Column<string>(nullable: false),
                    Version = table.Column<int>(nullable: false),
                    SchemaVersion = table.Column<int>(nullable: false),
                    FrozenAtUtc = table.Column<DateTime>(nullable: false),
                    FrozenBy = table.Column<string>(nullable: false),
                    ContentHash = table.Column<string>(nullable: false),
                    PayloadJson = table.Column<string>(nullable: false),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WoTraceSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WoTraceSnapshots_WoId",
                table: "WoTraceSnapshots",
                column: "WoId");

            migrationBuilder.CreateIndex(
                name: "IX_WoTraceSnapshots_WoId_Phase_Version",
                table: "WoTraceSnapshots",
                columns: new[] { "WoId", "Phase", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WoTraceSnapshots_WoNo",
                table: "WoTraceSnapshots",
                column: "WoNo");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WoTraceSnapshots");
        }
    }
}
