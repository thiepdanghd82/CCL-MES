using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessLineMap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProcessLineMaps",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MatchType = table.Column<string>(maxLength: 32, nullable: false),
                    MatchValue = table.Column<string>(maxLength: 128, nullable: false),
                    QcLine = table.Column<string>(maxLength: 16, nullable: false),
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
                    table.PrimaryKey("PK_ProcessLineMaps", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessLineMaps_MatchType_MatchValue",
                table: "ProcessLineMaps",
                columns: new[] { "MatchType", "MatchValue" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProcessLineMaps");
        }
    }
}
