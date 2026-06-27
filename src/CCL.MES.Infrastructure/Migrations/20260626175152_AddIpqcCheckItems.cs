using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIpqcCheckItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ItemsProfileSnapshotJson",
                table: "WoIpqcChecks",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolvedLines",
                table: "WoIpqcChecks",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WoIpqcCheckItems",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WoIpqcCheckId = table.Column<long>(nullable: false),
                    ItemKey = table.Column<string>(maxLength: 64, nullable: false),
                    ProcessLine = table.Column<string>(maxLength: 16, nullable: true),
                    GroupLabel = table.Column<string>(maxLength: 128, nullable: true),
                    Label = table.Column<string>(maxLength: 512, nullable: true),
                    AcceptanceCriteria = table.Column<string>(maxLength: 512, nullable: true),
                    Method = table.Column<string>(maxLength: 256, nullable: true),
                    Severity = table.Column<string>(maxLength: 64, nullable: true),
                    DefectCode = table.Column<string>(maxLength: 64, nullable: true),
                    Status = table.Column<string>(maxLength: 16, nullable: false),
                    NgReasonCode = table.Column<string>(maxLength: 64, nullable: true),
                    NgNote = table.Column<string>(maxLength: 500, nullable: true),
                    Sort = table.Column<int>(nullable: false),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WoIpqcCheckItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WoIpqcCheckItems_WoIpqcChecks_WoIpqcCheckId",
                        column: x => x.WoIpqcCheckId,
                        principalTable: "WoIpqcChecks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WoIpqcCheckItems_WoIpqcCheckId_ItemKey",
                table: "WoIpqcCheckItems",
                columns: new[] { "WoIpqcCheckId", "ItemKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WoIpqcCheckItems");

            migrationBuilder.DropColumn(
                name: "ItemsProfileSnapshotJson",
                table: "WoIpqcChecks");

            migrationBuilder.DropColumn(
                name: "ResolvedLines",
                table: "WoIpqcChecks");
        }
    }
}
