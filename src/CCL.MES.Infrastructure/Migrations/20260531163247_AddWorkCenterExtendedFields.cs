using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkCenterExtendedFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Phase 7 hạng mục 5 — 3 cột mới khớp CMES UI tham chiếu.
            // Provider-agnostic: KHÔNG có `type: "TEXT"/"REAL"/"INTEGER"`
            // để cùng migration chạy được trên SQL Server (provider tự map
            // bool→bit, double→float, string→nvarchar).
            migrationBuilder.AddColumn<bool>(
                name: "Active",
                table: "WorkCenters",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "IdealSpeedPcsH",
                table: "WorkCenters",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShiftPattern",
                table: "WorkCenters",
                nullable: true);

            // Q2 chốt — set Active = TRUE cho 43 row hiện tại. Lý do: tất cả
            // WC hiện tại derive từ Routing CSV → đang được dùng → coi như
            // active. NULL sẽ confuse operator (filter "active vs inactive"
            // không phân biệt được). Apply chỉ với rows đang NULL để idempotent.
            // SQLite/SQL Server đều hiểu literal 1 cho bool TRUE.
            migrationBuilder.Sql("UPDATE \"WorkCenters\" SET \"Active\" = 1 WHERE \"Active\" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Active",
                table: "WorkCenters");

            migrationBuilder.DropColumn(
                name: "IdealSpeedPcsH",
                table: "WorkCenters");

            migrationBuilder.DropColumn(
                name: "ShiftPattern",
                table: "WorkCenters");
        }
    }
}
