using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIqcTicketFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // §4.5 — type-affinity strip (giữ cổng SQL Server provider-agnostic).
            // collation NOCASE trên ReceiptNo GIỮ LẠI: đó là ngữ nghĩa, không phải
            // affinity SQLite. Mirror pattern MaterialLot.LotNo.
            migrationBuilder.AddColumn<string>(
                name: "CodeIfs",
                table: "IqcInspections",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IfsDescription",
                table: "IqcInspections",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MakerName",
                table: "IqcInspections",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ManufactureDate",
                table: "IqcInspections",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MaterialDescription",
                table: "IqcInspections",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReceiptNo",
                table: "IqcInspections",
                maxLength: 32,
                nullable: true,
                collation: "NOCASE");

            migrationBuilder.CreateIndex(
                name: "IX_IqcInspections_ReceiptNo_Unique",
                table: "IqcInspections",
                column: "ReceiptNo",
                unique: true,
                filter: "\"ReceiptNo\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IqcInspections_ReceiptNo_Unique",
                table: "IqcInspections");

            migrationBuilder.DropColumn(
                name: "CodeIfs",
                table: "IqcInspections");

            migrationBuilder.DropColumn(
                name: "IfsDescription",
                table: "IqcInspections");

            migrationBuilder.DropColumn(
                name: "MakerName",
                table: "IqcInspections");

            migrationBuilder.DropColumn(
                name: "ManufactureDate",
                table: "IqcInspections");

            migrationBuilder.DropColumn(
                name: "MaterialDescription",
                table: "IqcInspections");

            migrationBuilder.DropColumn(
                name: "ReceiptNo",
                table: "IqcInspections");
        }
    }
}
