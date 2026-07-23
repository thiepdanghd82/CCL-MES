using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSemiStock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SemiAllocations",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorkOrderId = table.Column<long>(nullable: false),
                    AssemblyLegId = table.Column<long>(nullable: false),
                    SemiLotId = table.Column<long>(nullable: false),
                    QtyReserved = table.Column<int>(nullable: false),
                    QtyConsumed = table.Column<int>(nullable: false),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SemiAllocations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SemiLots",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LotNo = table.Column<string>(maxLength: 64, nullable: false),
                    SemiKind = table.Column<string>(maxLength: 16, nullable: false),
                    SpecRevisionId = table.Column<long>(nullable: true),
                    SourceWorkOrderId = table.Column<long>(nullable: false),
                    QtyProduced = table.Column<int>(nullable: false),
                    QtyAvailable = table.Column<int>(nullable: false),
                    QtyReserved = table.Column<int>(nullable: false),
                    Status = table.Column<string>(maxLength: 16, nullable: false),
                    ExpiryAt = table.Column<DateTime>(nullable: true),
                    RowVersion = table.Column<byte[]>(nullable: false),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SemiLots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SemiAllocations_SemiLotId",
                table: "SemiAllocations",
                column: "SemiLotId");

            migrationBuilder.CreateIndex(
                name: "IX_SemiAllocations_WorkOrderId_AssemblyLegId",
                table: "SemiAllocations",
                columns: new[] { "WorkOrderId", "AssemblyLegId" });

            migrationBuilder.CreateIndex(
                name: "IX_SemiLots_LotNo",
                table: "SemiLots",
                column: "LotNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SemiLots_SemiKind_Status_SpecRevisionId",
                table: "SemiLots",
                columns: new[] { "SemiKind", "Status", "SpecRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_SemiLots_SourceWorkOrderId",
                table: "SemiLots",
                column: "SourceWorkOrderId");

            // P11.5 — SemiLot RowVersion (L38 Option-B): EF gửi X'' lúc
            // INSERT, trigger randomblob(8) bump; IsConcurrencyToken cho
            // optimistic-lock reserve (2 assembly cùng lô không over-sell).
            migrationBuilder.Sql(@"
                CREATE TRIGGER IF NOT EXISTS SemiLots_RowVersion_OnInsert
                AFTER INSERT ON SemiLots
                FOR EACH ROW
                WHEN length(NEW.RowVersion) = 0
                BEGIN
                    UPDATE SemiLots SET RowVersion = randomblob(8) WHERE rowid = NEW.rowid;
                END;
            ");
            migrationBuilder.Sql(@"
                CREATE TRIGGER IF NOT EXISTS SemiLots_RowVersion_OnUpdate
                AFTER UPDATE ON SemiLots
                FOR EACH ROW
                WHEN NEW.RowVersion = OLD.RowVersion
                BEGIN
                    UPDATE SemiLots SET RowVersion = randomblob(8) WHERE rowid = NEW.rowid;
                END;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS SemiLots_RowVersion_OnInsert;");
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS SemiLots_RowVersion_OnUpdate;");

            migrationBuilder.DropTable(
                name: "SemiAllocations");

            migrationBuilder.DropTable(
                name: "SemiLots");
        }
    }
}
