using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWoTraceIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WoTraceIndexes",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WoId = table.Column<long>(nullable: false),
                    WoNo = table.Column<string>(nullable: false),
                    ProductCode = table.Column<string>(nullable: true),
                    ProductName = table.Column<string>(nullable: false),
                    Customer = table.Column<string>(nullable: true),
                    CurrentMesPhase = table.Column<string>(nullable: false),
                    FirstScannedAtUtc = table.Column<DateTime>(nullable: false),
                    LastScannedAtUtc = table.Column<DateTime>(nullable: false),
                    LastUpdatedAtUtc = table.Column<DateTime>(nullable: false),
                    ProductFrozen = table.Column<bool>(nullable: false),
                    IpqcFrozen = table.Column<bool>(nullable: false),
                    FqcFrozen = table.Column<bool>(nullable: false),
                    OqcFrozen = table.Column<bool>(nullable: false),
                    LatestFrozenAtUtc = table.Column<DateTime>(nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WoTraceIndexes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WoTraceIndexes_WoId",
                table: "WoTraceIndexes",
                column: "WoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WoTraceIndexes_WoNo",
                table: "WoTraceIndexes",
                column: "WoNo",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WoTraceIndexes");
        }
    }
}
