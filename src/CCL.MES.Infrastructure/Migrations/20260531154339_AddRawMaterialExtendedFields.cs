using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRawMaterialExtendedFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "TypeDesc",
                table: "RawMaterials",
                newName: "TaxCodeDescription");

            migrationBuilder.RenameColumn(
                name: "Type",
                table: "RawMaterials",
                newName: "TaxCode");

            migrationBuilder.RenameColumn(
                name: "Grp",
                table: "RawMaterials",
                newName: "SupplierPartNo");

            migrationBuilder.RenameColumn(
                name: "CatalogGroup",
                table: "RawMaterials",
                newName: "SupplierPartDescription");

            migrationBuilder.RenameColumn(
                name: "CatalogDesc",
                table: "RawMaterials",
                newName: "StatusCode");

            migrationBuilder.AlterColumn<double>(
                name: "Price",
                table: "RawMaterials",
                nullable: true,
                oldClrType: typeof(double));

            migrationBuilder.AddColumn<string>(
                name: "AcquisitionType",
                table: "RawMaterials",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ConversionFactor",
                table: "RawMaterials",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CountryOfOrigin",
                table: "RawMaterials",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InventoryUom",
                table: "RawMaterials",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "MinimumQuantity",
                table: "RawMaterials",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "NetWeight",
                table: "RawMaterials",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NetWeightUom",
                table: "RawMaterials",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NextOrderDate",
                table: "RawMaterials",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "RawMaterials",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PriceInclTax",
                table: "RawMaterials",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PurchUom",
                table: "RawMaterials",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Site",
                table: "RawMaterials",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SiteDescription",
                table: "RawMaterials",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "StandardPackSize",
                table: "RawMaterials",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "StdMultipleQty",
                table: "RawMaterials",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "SupplierLeadtimeDays",
                table: "RawMaterials",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcquisitionType",
                table: "RawMaterials");

            migrationBuilder.DropColumn(
                name: "ConversionFactor",
                table: "RawMaterials");

            migrationBuilder.DropColumn(
                name: "CountryOfOrigin",
                table: "RawMaterials");

            migrationBuilder.DropColumn(
                name: "InventoryUom",
                table: "RawMaterials");

            migrationBuilder.DropColumn(
                name: "MinimumQuantity",
                table: "RawMaterials");

            migrationBuilder.DropColumn(
                name: "NetWeight",
                table: "RawMaterials");

            migrationBuilder.DropColumn(
                name: "NetWeightUom",
                table: "RawMaterials");

            migrationBuilder.DropColumn(
                name: "NextOrderDate",
                table: "RawMaterials");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "RawMaterials");

            migrationBuilder.DropColumn(
                name: "PriceInclTax",
                table: "RawMaterials");

            migrationBuilder.DropColumn(
                name: "PurchUom",
                table: "RawMaterials");

            migrationBuilder.DropColumn(
                name: "Site",
                table: "RawMaterials");

            migrationBuilder.DropColumn(
                name: "SiteDescription",
                table: "RawMaterials");

            migrationBuilder.DropColumn(
                name: "StandardPackSize",
                table: "RawMaterials");

            migrationBuilder.DropColumn(
                name: "StdMultipleQty",
                table: "RawMaterials");

            migrationBuilder.DropColumn(
                name: "SupplierLeadtimeDays",
                table: "RawMaterials");

            migrationBuilder.RenameColumn(
                name: "TaxCodeDescription",
                table: "RawMaterials",
                newName: "TypeDesc");

            migrationBuilder.RenameColumn(
                name: "TaxCode",
                table: "RawMaterials",
                newName: "Type");

            migrationBuilder.RenameColumn(
                name: "SupplierPartNo",
                table: "RawMaterials",
                newName: "Grp");

            migrationBuilder.RenameColumn(
                name: "SupplierPartDescription",
                table: "RawMaterials",
                newName: "CatalogGroup");

            migrationBuilder.RenameColumn(
                name: "StatusCode",
                table: "RawMaterials",
                newName: "CatalogDesc");

            migrationBuilder.AlterColumn<double>(
                name: "Price",
                table: "RawMaterials",
                nullable: false,
                defaultValue: 0.0,
                oldClrType: typeof(double),
                oldNullable: true);
        }
    }
}
