using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIqcMaterialDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IqcMaterialDocuments",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MaterialCode = table.Column<string>(maxLength: 256, nullable: false),
                    DocType = table.Column<string>(maxLength: 64, nullable: false),
                    LabelVi = table.Column<string>(maxLength: 128, nullable: true),
                    LabelEn = table.Column<string>(maxLength: 128, nullable: true),
                    DocNumber = table.Column<string>(maxLength: 64, nullable: true),
                    IssueDate = table.Column<DateTime>(nullable: true),
                    ExpiryDate = table.Column<DateTime>(nullable: true),
                    StorageKey = table.Column<string>(maxLength: 512, nullable: true),
                    FileName = table.Column<string>(maxLength: 256, nullable: true),
                    FileSha256 = table.Column<string>(maxLength: 64, nullable: true),
                    FileSizeBytes = table.Column<long>(nullable: true),
                    Sort = table.Column<int>(nullable: false),
                    Active = table.Column<bool>(nullable: false),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IqcMaterialDocuments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IqcMaterialDocuments_MaterialCode",
                table: "IqcMaterialDocuments",
                column: "MaterialCode");

            migrationBuilder.CreateIndex(
                name: "IX_IqcMaterialDocuments_MaterialCode_DocType",
                table: "IqcMaterialDocuments",
                columns: new[] { "MaterialCode", "DocType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IqcMaterialDocuments");
        }
    }
}
