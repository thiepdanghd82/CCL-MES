using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWoMaterialIpqcWaiver : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "IpqcWaiverAt",
                table: "WoMaterials",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IpqcWaiverBy",
                table: "WoMaterials",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IpqcWaiverLotNo",
                table: "WoMaterials",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IpqcWaiverLotStatus",
                table: "WoMaterials",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IpqcWaiverReason",
                table: "WoMaterials",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IpqcWaiverAt",
                table: "WoMaterials");

            migrationBuilder.DropColumn(
                name: "IpqcWaiverBy",
                table: "WoMaterials");

            migrationBuilder.DropColumn(
                name: "IpqcWaiverLotNo",
                table: "WoMaterials");

            migrationBuilder.DropColumn(
                name: "IpqcWaiverLotStatus",
                table: "WoMaterials");

            migrationBuilder.DropColumn(
                name: "IpqcWaiverReason",
                table: "WoMaterials");
        }
    }
}
