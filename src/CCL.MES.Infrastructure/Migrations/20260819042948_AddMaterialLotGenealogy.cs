using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <summary>
    /// A1 — mạch lô nguyên vật liệu. Hai bảng MỚI + một cột MỚI nullable.
    ///
    /// <para><b>Đã strip type-affinity</b> (<c>type: "TEXT|INTEGER|REAL|BLOB"</c>)
    /// theo §4 hợp đồng: giữ <c>maxLength</c> / <c>nullable</c> / <c>collation</c>
    /// / <c>Sqlite:Autoincrement</c>. Affinity cứng làm migration chỉ chạy được
    /// trên SQLite; bỏ đi thì cùng file này áp được lên SQL Server sau này.</para>
    ///
    /// <para><b>Trigger RowVersion viết TAY</b> ở cuối <c>Up()</c> (mirror
    /// <c>20260723081701_AddSemiStock.cs</c>) — <c>IsRowVersion()</c> của EF chỉ
    /// tự bump trên SQL Server, SQLite không có gì tương đương.</para>
    ///
    /// <para><b>ĐO ĐƯỢC trên bản copy dữ liệu thật ở /tmp (2026-08-19):</b>
    /// <c>AddForeignKey</c> trên <c>WoMaterials</c> khiến SQLite rebuild bảng
    /// (EF phát <c>PRAGMA foreign_keys = 0</c> ngoài transaction). Rủi ro §3.3
    /// cảnh báo là rebuild XOÁ mất hai partial unique index
    /// <c>IX_WoMaterials_WorkOrderId_BomLineIdx</c> và
    /// <c>..._WoLegId_BomLineIdx</c>. Đã kiểm chứng: EF dựng lại CẢ HAI với SQL
    /// y hệt, 6/6 trigger còn nguyên, 82/82 dòng còn nguyên. <b>Nhưng</b> thao
    /// tác này KHÔNG chạy trong transaction — nếu tiến trình chết giữa chừng thì
    /// DB nằm ở trạng thái nửa vời và phải khôi phục thủ công. Vì thế Phase C
    /// (áp lên live) BẮT BUỘC: backup + SHA256 trước, và kiểm lại đúng hai
    /// index này ngay sau khi áp.</para>
    /// </summary>
    public partial class AddMaterialLotGenealogy : Migration
    {
        /// <summary>
        /// THỨ TỰ CÓ CHỦ Ý — đừng sắp xếp lại cho "gọn".
        ///
        /// <para>Hai bảng mới + trigger phải đứng TRƯỚC mọi thao tác chạm
        /// <c>WoMaterials</c>. Lý do đo được: <c>AddForeignKey</c> trên
        /// <c>WoMaterials</c> đặt bảng đó vào hàng đợi rebuild của EF, và mọi
        /// <c>migrationBuilder.Sql(...)</c> đứng SAU đó bị cảnh báo
        /// <i>"An operation of type 'SqlOperation' will be attempted while a
        /// rebuild of table 'WoMaterials' is pending — the database may not be
        /// in an expected state"</i>. Bản sinh tự động của EF đặt
        /// <c>AddColumn</c> lên đầu và đã phát đúng cảnh báo đó; đảo lại thứ tự
        /// làm nó biến mất. Trên live thì "may not be in an expected state"
        /// không phải thứ được phép bỏ qua.</para>
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MaterialLots",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LotNo = table.Column<string>(maxLength: 64, nullable: false, collation: "NOCASE"),
                    RawMaterialId = table.Column<long>(nullable: true),
                    PartNo = table.Column<string>(maxLength: 64, nullable: false, collation: "NOCASE"),
                    IqcInspectionId = table.Column<long>(nullable: true),
                    SupplierName = table.Column<string>(maxLength: 120, nullable: true),
                    SupplierLotNo = table.Column<string>(maxLength: 64, nullable: true, collation: "NOCASE"),
                    ReceivedAt = table.Column<DateTime>(nullable: false),
                    ExpiryAt = table.Column<DateTime>(nullable: true),
                    QtyReceived = table.Column<double>(nullable: false),
                    QtyAvailable = table.Column<double>(nullable: false),
                    Uom = table.Column<string>(maxLength: 16, nullable: true),
                    Status = table.Column<string>(maxLength: 16, nullable: false),
                    StatusReason = table.Column<string>(maxLength: 500, nullable: true),
                    StatusChangedBy = table.Column<string>(maxLength: 80, nullable: true),
                    StatusChangedAt = table.Column<DateTime>(nullable: true),
                    RetestedAt = table.Column<DateTime>(nullable: true),
                    RetestedBy = table.Column<string>(maxLength: 80, nullable: true),
                    ExpiryExtendedTo = table.Column<DateTime>(nullable: true),
                    ExpiryExtendedBy = table.Column<string>(maxLength: 80, nullable: true),
                    RowVersion = table.Column<byte[]>(nullable: false),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialLots", x => x.Id);
                    table.CheckConstraint("CK_MaterialLots_LotNo_Trimmed", "\"LotNo\" = TRIM(\"LotNo\") AND LENGTH(\"LotNo\") > 0");
                    table.CheckConstraint("CK_MaterialLots_PartNo_Trimmed", "\"PartNo\" = TRIM(\"PartNo\") AND LENGTH(\"PartNo\") > 0");
                });

            migrationBuilder.CreateTable(
                name: "WoMaterialConsumptions",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WoId = table.Column<long>(nullable: false),
                    LegId = table.Column<long>(nullable: true),
                    WoMaterialId = table.Column<long>(nullable: false),
                    MaterialLotId = table.Column<long>(nullable: false),
                    QtyUsed = table.Column<double>(nullable: false),
                    Uom = table.Column<string>(maxLength: 16, nullable: true),
                    ScannedBy = table.Column<string>(maxLength: 80, nullable: false),
                    ScannedAt = table.Column<DateTime>(nullable: false),
                    ReversedAt = table.Column<DateTime>(nullable: true),
                    ReversedBy = table.Column<string>(maxLength: 80, nullable: true),
                    ReversedReason = table.Column<string>(maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WoMaterialConsumptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WoMaterialConsumptions_MaterialLots_MaterialLotId",
                        column: x => x.MaterialLotId,
                        principalTable: "MaterialLots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WoMaterialConsumptions_WoLegs_LegId",
                        column: x => x.LegId,
                        principalTable: "WoLegs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WoMaterialConsumptions_WoMaterials_WoMaterialId",
                        column: x => x.WoMaterialId,
                        principalTable: "WoMaterials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WoMaterialConsumptions_WorkOrders_WoId",
                        column: x => x.WoId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaterialLots_IqcInspectionId",
                table: "MaterialLots",
                column: "IqcInspectionId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialLots_LotNo_PartNo_Unresolved",
                table: "MaterialLots",
                columns: new[] { "LotNo", "PartNo" },
                unique: true,
                filter: "\"RawMaterialId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialLots_LotNo_RawMaterialId",
                table: "MaterialLots",
                columns: new[] { "LotNo", "RawMaterialId" },
                unique: true,
                filter: "\"RawMaterialId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialLots_RawMaterialId_Status",
                table: "MaterialLots",
                columns: new[] { "RawMaterialId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MaterialLots_Status_ExpiryAt",
                table: "MaterialLots",
                columns: new[] { "Status", "ExpiryAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WoMaterialConsumptions_LegId",
                table: "WoMaterialConsumptions",
                column: "LegId");

            migrationBuilder.CreateIndex(
                name: "IX_WoMaterialConsumptions_MaterialLotId",
                table: "WoMaterialConsumptions",
                column: "MaterialLotId");

            migrationBuilder.CreateIndex(
                name: "IX_WoMaterialConsumptions_WoId_LegId",
                table: "WoMaterialConsumptions",
                columns: new[] { "WoId", "LegId" });

            migrationBuilder.CreateIndex(
                name: "IX_WoMaterialConsumptions_WoMaterialId",
                table: "WoMaterialConsumptions",
                column: "WoMaterialId");

            // A1 — RowVersion của MaterialLots (L38 Option-B), mirror
            // 20260723081701_AddSemiStock.cs. EF gửi X'' lúc INSERT rồi trigger
            // randomblob(8) bump; IsConcurrencyToken ở model biến nó thành khoá
            // lạc quan, nhờ đó hai operator quét cùng một lô không tiêu thụ vượt
            // tồn — 1 người thắng, người còn lại nhận 409 lot.conflict.
            // KHÔNG có IsRowVersion(): EF chỉ tự bump trên SQL Server.
            migrationBuilder.Sql(@"
                CREATE TRIGGER IF NOT EXISTS MaterialLots_RowVersion_OnInsert
                AFTER INSERT ON MaterialLots
                FOR EACH ROW
                WHEN length(NEW.RowVersion) = 0
                BEGIN
                    UPDATE MaterialLots SET RowVersion = randomblob(8) WHERE rowid = NEW.rowid;
                END;
            ");
            migrationBuilder.Sql(@"
                CREATE TRIGGER IF NOT EXISTS MaterialLots_RowVersion_OnUpdate
                AFTER UPDATE ON MaterialLots
                FOR EACH ROW
                WHEN NEW.RowVersion = OLD.RowVersion
                BEGIN
                    UPDATE MaterialLots SET RowVersion = randomblob(8) WHERE rowid = NEW.rowid;
                END;
            ");

            // ── Chỉ từ đây mới chạm WoMaterials (rebuild bảng, xem chú thích
            //    ở đầu Up()). Additive thuần: cột mới nullable + index + FK.
            //    KHÔNG AlterColumn LotNo — Phase 3 mới siết read-only.
            migrationBuilder.AddColumn<long>(
                name: "MaterialLotId",
                table: "WoMaterials",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WoMaterials_MaterialLotId",
                table: "WoMaterials",
                column: "MaterialLotId");

            migrationBuilder.AddForeignKey(
                name: "FK_WoMaterials_MaterialLots_MaterialLotId",
                table: "WoMaterials",
                column: "MaterialLotId",
                principalTable: "MaterialLots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <summary>
        /// ⚠ <b>KHÔNG dùng Down() để rollback trên live.</b> <c>DropColumn
        /// MaterialLotId</c> rebuild lại <c>WoMaterials</c> — đúng thao tác §3.3
        /// cảnh báo. Đường lùi trên live là <b>restore file backup Phase A
        /// byte-identical</b>. Down() ở đây để round-trip trên /tmp và để EF
        /// hợp lệ, không phải quy trình vận hành.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS MaterialLots_RowVersion_OnInsert;");
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS MaterialLots_RowVersion_OnUpdate;");

            migrationBuilder.DropForeignKey(
                name: "FK_WoMaterials_MaterialLots_MaterialLotId",
                table: "WoMaterials");

            migrationBuilder.DropTable(
                name: "WoMaterialConsumptions");

            migrationBuilder.DropTable(
                name: "MaterialLots");

            migrationBuilder.DropIndex(
                name: "IX_WoMaterials_MaterialLotId",
                table: "WoMaterials");

            migrationBuilder.DropColumn(
                name: "MaterialLotId",
                table: "WoMaterials");
        }
    }
}
