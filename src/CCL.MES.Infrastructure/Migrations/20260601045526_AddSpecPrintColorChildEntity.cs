using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <summary>
    /// Phase 8 PR #31a — ADD SpecPrintColors child table cho silkscreen print
    /// rows (mirror SpecHub `spec.printRows[]` 20-field shape).
    ///
    /// PURELY ADDITIVE — không đụng table existing. EF emit `CreateTable` +
    /// `CreateIndex` chuẩn ở cả SQLite + SqlServer providers (KHÔNG cần
    /// `ActiveProvider` guard như PR #28). Foreign key Cascade: xóa SpecPrint
    /// master sẽ xóa toàn bộ color rows con (orphan-safe).
    ///
    /// A→B→C SAFE apply:
    ///   A. Backup `cclmes.db` + SHA256 → `db-backups/`
    ///   B. Test apply trên `/tmp/spec-printcolor-design.db` (isolated copy)
    ///      → verify ProductRevisions/SpecPrints/ProcessCatalogs counts
    ///      unchanged + new SpecPrintColors empty
    ///   C. Apply LIVE → re-verify counts + new table exists
    ///
    /// Rollback: `dotnet ef migrations remove` HOẶC `DROP TABLE SpecPrintColors`.
    /// </summary>
    public partial class AddSpecPrintColorChildEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SpecPrintColors",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SpecPrintId = table.Column<long>(type: "INTEGER", nullable: false),
                    Seq = table.Column<int>(type: "INTEGER", nullable: false),
                    Surface = table.Column<string>(type: "TEXT", nullable: true),
                    Color = table.Column<string>(type: "TEXT", nullable: true),
                    InkName = table.Column<string>(type: "TEXT", nullable: true),
                    InkCode = table.Column<string>(type: "TEXT", nullable: true),
                    Maker = table.Column<string>(type: "TEXT", nullable: true),
                    Retarder = table.Column<string>(type: "TEXT", nullable: true),
                    Viscosity = table.Column<double>(type: "REAL", nullable: true),
                    Speed = table.Column<double>(type: "REAL", nullable: true),
                    Squeegee = table.Column<string>(type: "TEXT", nullable: true),
                    Dry = table.Column<string>(type: "TEXT", nullable: true),
                    TemperatureC = table.Column<double>(type: "REAL", nullable: true),
                    TimeMin = table.Column<int>(type: "INTEGER", nullable: true),
                    Uv = table.Column<string>(type: "TEXT", nullable: true),
                    EmulsionUm = table.Column<double>(type: "REAL", nullable: true),
                    PlateSize = table.Column<string>(type: "TEXT", nullable: true),
                    Mesh = table.Column<string>(type: "TEXT", nullable: true),
                    AngleDeg = table.Column<double>(type: "REAL", nullable: true),
                    PlateCode = table.Column<string>(type: "TEXT", nullable: true),
                    ControlNo = table.Column<int>(type: "INTEGER", nullable: true),
                    Remark = table.Column<string>(type: "TEXT", nullable: true),
                    ExtraJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpecPrintColors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpecPrintColors_SpecPrints_SpecPrintId",
                        column: x => x.SpecPrintId,
                        principalTable: "SpecPrints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SpecPrintColors_InkCode",
                table: "SpecPrintColors",
                column: "InkCode");

            migrationBuilder.CreateIndex(
                name: "IX_SpecPrintColors_PlateCode",
                table: "SpecPrintColors",
                column: "PlateCode");

            migrationBuilder.CreateIndex(
                name: "IX_SpecPrintColors_SpecPrintId_Seq",
                table: "SpecPrintColors",
                columns: new[] { "SpecPrintId", "Seq" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SpecPrintColors");
        }
    }
}
