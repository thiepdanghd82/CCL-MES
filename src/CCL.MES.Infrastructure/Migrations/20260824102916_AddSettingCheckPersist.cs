using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSettingCheckPersist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Setting",
                table: "CheckItemLibraries",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "CheckItemDefectOptions",
                columns: table => new
                {
                    Id = table.Column<long>( nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ItemId = table.Column<string>( maxLength: 64, nullable: false),
                    DefectCode = table.Column<string>( maxLength: 64, nullable: false),
                    LabelVi = table.Column<string>( maxLength: 256, nullable: false),
                    LabelEn = table.Column<string>( maxLength: 256, nullable: false),
                    ProductCode = table.Column<string>( maxLength: 64, nullable: true),
                    Active = table.Column<bool>( nullable: false),
                    Sort = table.Column<int>( nullable: false),
                    CreatedAt = table.Column<DateTime>( nullable: false),
                    CreatedBy = table.Column<string>( nullable: true),
                    UpdatedAt = table.Column<DateTime>( nullable: true),
                    UpdatedBy = table.Column<string>( nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckItemDefectOptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WoSettingCheckItems",
                columns: table => new
                {
                    Id = table.Column<long>( nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorkOrderId = table.Column<long>( nullable: false),
                    ProcessKind = table.Column<string>( maxLength: 8, nullable: false),
                    ItemKey = table.Column<string>( maxLength: 64, nullable: false),
                    Label = table.Column<string>( maxLength: 512, nullable: true),
                    Standard = table.Column<string>( maxLength: 512, nullable: true),
                    GroupLabel = table.Column<string>( maxLength: 128, nullable: true),
                    Applicable = table.Column<bool>( nullable: false),
                    Status = table.Column<string>( maxLength: 16, nullable: false),
                    DefectCode = table.Column<string>( maxLength: 64, nullable: true),
                    NgNote = table.Column<string>( maxLength: 500, nullable: true),
                    AdHoc = table.Column<bool>( nullable: false),
                    ConfirmedBy = table.Column<string>( maxLength: 128, nullable: true),
                    ConfirmedAt = table.Column<DateTime>( nullable: true),
                    Sort = table.Column<int>( nullable: false),
                    CreatedAt = table.Column<DateTime>( nullable: false),
                    CreatedBy = table.Column<string>( nullable: true),
                    UpdatedAt = table.Column<DateTime>( nullable: true),
                    UpdatedBy = table.Column<string>( nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WoSettingCheckItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WoSettingCheckItems_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CheckItemDefectOptions_ItemId",
                table: "CheckItemDefectOptions",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_CheckItemDefectOptions_ItemId_DefectCode_ProductCode",
                table: "CheckItemDefectOptions",
                columns: new[] { "ItemId", "DefectCode", "ProductCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WoSettingCheckItems_WorkOrderId_ProcessKind_ItemKey",
                table: "WoSettingCheckItems",
                columns: new[] { "WorkOrderId", "ProcessKind", "ItemKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CheckItemDefectOptions");

            migrationBuilder.DropTable(
                name: "WoSettingCheckItems");

            migrationBuilder.DropColumn(
                name: "Setting",
                table: "CheckItemLibraries");
        }
    }
}
