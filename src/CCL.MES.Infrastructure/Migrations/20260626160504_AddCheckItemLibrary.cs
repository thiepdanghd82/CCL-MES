using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckItemLibrary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CheckItemLibraries",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ItemId = table.Column<string>(nullable: false),
                    ProcessLine = table.Column<string>(nullable: false),
                    ProductCode = table.Column<string>(nullable: true),
                    QcStage = table.Column<string>(nullable: false),
                    GroupLabel = table.Column<string>(nullable: false),
                    Code = table.Column<string>(nullable: false),
                    ItemVi = table.Column<string>(nullable: false),
                    ItemEn = table.Column<string>(nullable: false),
                    AcceptanceVi = table.Column<string>(nullable: false),
                    AcceptanceEn = table.Column<string>(nullable: false),
                    Method = table.Column<string>(nullable: true),
                    Severity = table.Column<string>(nullable: true),
                    Aql = table.Column<string>(nullable: true),
                    Sampling = table.Column<string>(nullable: true),
                    CheckType = table.Column<string>(nullable: true),
                    DefectCode = table.Column<string>(nullable: true),
                    ParetoPct = table.Column<string>(nullable: true),
                    ShortForm = table.Column<string>(nullable: true),
                    IsoRef = table.Column<string>(nullable: true),
                    AppliesWhen = table.Column<string>(nullable: true),
                    Note = table.Column<string>(nullable: true),
                    Active = table.Column<bool>(nullable: false),
                    Sort = table.Column<int>(nullable: false),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckItemLibraries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CheckItemLibraries_ItemId",
                table: "CheckItemLibraries",
                column: "ItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CheckItemLibraries_ProcessLine_QcStage",
                table: "CheckItemLibraries",
                columns: new[] { "ProcessLine", "QcStage" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CheckItemLibraries");
        }
    }
}
