using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <summary>
    /// Phase 8 PR #31b — ADD 2 flexo child tables:
    ///   - SpecFlexoCuttingRows (14 fields/row) — die cut + lamination + cutter
    ///   - SpecFlexoInkRows (10 fields/row) — per-color ink + anilox + UV/IR
    ///
    /// Mirror SpecHub `flexoData.cuttingRows[]` + `inkRows[]` (HTML:11707-
    /// 11751). PrintingRows (substrate/cylinder/tension) tiếp tục fold vào
    /// SpecPrint.ExtraJson per Q3 (ít column-wise query).
    ///
    /// PURELY ADDITIVE — không đụng table existing (kể cả SpecPrintColors từ
    /// PR #31a). EF emit `CreateTable` + `CreateIndex` chuẩn cả 2 provider
    /// (SQLite + SqlServer); KHÔNG cần ActiveProvider guard. FK Cascade khớp
    /// pattern SpecPrintColor — xóa SpecPrint master sẽ wipe 3 dạng child rows
    /// (Colors + FlexoCutting + FlexoInk).
    ///
    /// A→B→C SAFE apply:
    ///   A. Backup `data/ccl_mes.db` + SHA256 → `db-backups/`
    ///   B. Test apply trên `/tmp/spec-flexo-design.db` (isolated copy)
    ///      → verify baseline + PR #31a counts unchanged + 2 new tables empty
    ///   C. Apply LIVE → re-verify
    ///
    /// Rollback: `dotnet ef migrations remove` HOẶC
    /// `DROP TABLE SpecFlexoInkRows; DROP TABLE SpecFlexoCuttingRows;`.
    /// </summary>
    public partial class AddSpecFlexoChildEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SpecFlexoCuttingRows",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SpecPrintId = table.Column<long>(type: "INTEGER", nullable: false),
                    Seq = table.Column<int>(type: "INTEGER", nullable: false),
                    Process = table.Column<string>(type: "TEXT", nullable: true),
                    Lamination = table.Column<string>(type: "TEXT", nullable: true),
                    Size = table.Column<string>(type: "TEXT", nullable: true),
                    CutterLot = table.Column<string>(type: "TEXT", nullable: true),
                    CutterName = table.Column<string>(type: "TEXT", nullable: true),
                    PcsPerSheet = table.Column<int>(type: "INTEGER", nullable: true),
                    CuttingCavity = table.Column<int>(type: "INTEGER", nullable: true),
                    PitchMm = table.Column<double>(type: "REAL", nullable: true),
                    Packing = table.Column<string>(type: "TEXT", nullable: true),
                    PaperSpeed = table.Column<double>(type: "REAL", nullable: true),
                    CuttingSpeed = table.Column<double>(type: "REAL", nullable: true),
                    CuttingPressure = table.Column<double>(type: "REAL", nullable: true),
                    HeadTension = table.Column<double>(type: "REAL", nullable: true),
                    RollTension = table.Column<double>(type: "REAL", nullable: true),
                    ExtraJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpecFlexoCuttingRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpecFlexoCuttingRows_SpecPrints_SpecPrintId",
                        column: x => x.SpecPrintId,
                        principalTable: "SpecPrints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SpecFlexoInkRows",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SpecPrintId = table.Column<long>(type: "INTEGER", nullable: false),
                    Seq = table.Column<int>(type: "INTEGER", nullable: false),
                    Color = table.Column<string>(type: "TEXT", nullable: true),
                    InkCode = table.Column<string>(type: "TEXT", nullable: true),
                    InkDescription = table.Column<string>(type: "TEXT", nullable: true),
                    Brand = table.Column<string>(type: "TEXT", nullable: true),
                    Anilox = table.Column<string>(type: "TEXT", nullable: true),
                    PlateCode = table.Column<string>(type: "TEXT", nullable: true),
                    Pressure = table.Column<double>(type: "REAL", nullable: true),
                    UvPowerW = table.Column<double>(type: "REAL", nullable: true),
                    IrPowerW = table.Column<double>(type: "REAL", nullable: true),
                    ExtraJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpecFlexoInkRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpecFlexoInkRows_SpecPrints_SpecPrintId",
                        column: x => x.SpecPrintId,
                        principalTable: "SpecPrints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SpecFlexoCuttingRows_SpecPrintId_Seq",
                table: "SpecFlexoCuttingRows",
                columns: new[] { "SpecPrintId", "Seq" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpecFlexoInkRows_InkCode",
                table: "SpecFlexoInkRows",
                column: "InkCode");

            migrationBuilder.CreateIndex(
                name: "IX_SpecFlexoInkRows_PlateCode",
                table: "SpecFlexoInkRows",
                column: "PlateCode");

            migrationBuilder.CreateIndex(
                name: "IX_SpecFlexoInkRows_SpecPrintId_Seq",
                table: "SpecFlexoInkRows",
                columns: new[] { "SpecPrintId", "Seq" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SpecFlexoCuttingRows");

            migrationBuilder.DropTable(
                name: "SpecFlexoInkRows");
        }
    }
}
