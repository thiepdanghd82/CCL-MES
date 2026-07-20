using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UsernameCaseInsensitive : Migration
    {
        // Username is matched case-insensitively (login lookup + unique index).
        // Setting a NOCASE collation on the column rebuilds the table (SQLite
        // has no ALTER COLUMN) and recreates IX_Users_Username as NOCASE, so
        // "OQC" and "oqc" resolve to the same row and can never coexist.
        // Type-affinity strings stripped per CLAUDE.md §4.5 (SQL Server gate).
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Username",
                table: "Users",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(string));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Username",
                table: "Users",
                nullable: false,
                oldClrType: typeof(string),
                oldCollation: "NOCASE");
        }
    }
}
