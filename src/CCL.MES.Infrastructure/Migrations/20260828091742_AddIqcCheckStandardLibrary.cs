using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIqcCheckStandardLibrary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IqcCheckItemLibraries",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ItemId = table.Column<string>(maxLength: 16, nullable: false),
                    GroupCode = table.Column<string>(maxLength: 8, nullable: false),
                    GroupLabelVi = table.Column<string>(maxLength: 64, nullable: false),
                    GroupLabelEn = table.Column<string>(maxLength: 64, nullable: true),
                    ItemVi = table.Column<string>(maxLength: 256, nullable: false),
                    ItemEn = table.Column<string>(maxLength: 256, nullable: true),
                    Sort = table.Column<int>(nullable: false),
                    Active = table.Column<bool>(nullable: false),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IqcCheckItemLibraries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IqcMaterialSpecs",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SpecNo = table.Column<string>(maxLength: 32, nullable: false),
                    MaterialCode = table.Column<string>(maxLength: 256, nullable: false),
                    MaterialCodeIfs = table.Column<string>(maxLength: 32, nullable: true),
                    SupplierName = table.Column<string>(maxLength: 256, nullable: true),
                    Revision = table.Column<string>(maxLength: 16, nullable: true),
                    Active = table.Column<bool>(nullable: false),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IqcMaterialSpecs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IqcSpecItems",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SpecNo = table.Column<string>(maxLength: 32, nullable: false),
                    ItemId = table.Column<string>(maxLength: 16, nullable: false),
                    Seq = table.Column<int>(nullable: false),
                    AcceptanceVi = table.Column<string>(maxLength: 1024, nullable: true),
                    AcceptanceEn = table.Column<string>(maxLength: 1024, nullable: true),
                    MethodVi = table.Column<string>(maxLength: 512, nullable: true),
                    MethodEn = table.Column<string>(maxLength: 512, nullable: true),
                    SourceFrequency = table.Column<string>(maxLength: 256, nullable: true),
                    Sort = table.Column<int>(nullable: false),
                    Active = table.Column<bool>(nullable: false),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IqcSpecItems", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IqcCheckItemLibraries_GroupCode",
                table: "IqcCheckItemLibraries",
                column: "GroupCode");

            migrationBuilder.CreateIndex(
                name: "IX_IqcCheckItemLibraries_ItemId",
                table: "IqcCheckItemLibraries",
                column: "ItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IqcMaterialSpecs_MaterialCode",
                table: "IqcMaterialSpecs",
                column: "MaterialCode");

            migrationBuilder.CreateIndex(
                name: "IX_IqcMaterialSpecs_MaterialCodeIfs",
                table: "IqcMaterialSpecs",
                column: "MaterialCodeIfs");

            migrationBuilder.CreateIndex(
                name: "IX_IqcMaterialSpecs_SpecNo",
                table: "IqcMaterialSpecs",
                column: "SpecNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IqcSpecItems_SpecNo",
                table: "IqcSpecItems",
                column: "SpecNo");

            migrationBuilder.CreateIndex(
                name: "IX_IqcSpecItems_SpecNo_ItemId_Seq",
                table: "IqcSpecItems",
                columns: new[] { "SpecNo", "ItemId", "Seq" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IqcCheckItemLibraries");

            migrationBuilder.DropTable(
                name: "IqcMaterialSpecs");

            migrationBuilder.DropTable(
                name: "IqcSpecItems");
        }
    }
}
