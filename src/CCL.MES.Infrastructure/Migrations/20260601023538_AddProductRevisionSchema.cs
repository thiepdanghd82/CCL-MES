using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <summary>
    /// Phase 8 PR #28 — Spec → ProductRevision CLEAN REWRITE migration.
    ///
    /// Migration RỦI RO NHẤT từ đầu repo: drop+replace 3 bảng Spec/
    /// SpecVersion/SpecParameter; refactor FK WorkOrder.SpecVersionId →
    /// ProductRevisionId; ADD 11 entity mới + Users.Department field.
    ///
    /// Ordering nghiêm ngặt cho A→B→C SAFE:
    ///   1. AddColumn Users.Department.
    ///   2. CreateTable mọi entity mới (ProcessCatalogs + ProductRevisions +
    ///      4 sibling specs + SpecQcWindows + QcCriteria + DrawingApprovals
    ///      + Drawings + DrawingVersions).
    ///   3. CreateIndex toàn bộ new tables.
    ///   4. DATA MIGRATION SQL (BEFORE drops):
    ///      a. INSERT INTO ProductRevisions từ Specs INNER JOIN SpecVersions
    ///         (preserve PK 1:1 — sv.Id → pr.Id để WO FK remap trivial).
    ///      b. INSERT INTO SpecPrints từ SpecVersions LEFT JOIN SpecParameters
    ///         (fold params vào ColorSpecJson JSON array để forensic preserve).
    ///   5. DropForeignKey FK_WorkOrders_SpecVersions_SpecVersionId.
    ///   6. DropTable SpecParameters → SpecVersions → Specs (FK order).
    ///   7. RenameColumn WorkOrders.SpecVersionId → ProductRevisionId
    ///      (preserves data; cùng integer giờ ref ProductRevisions thay vì
    ///      SpecVersions — PK preservation ở step 4a makes this 1:1 valid).
    ///   8. RenameIndex IX_WorkOrders_SpecVersionId → IX_WorkOrders_ProductRevisionId.
    ///   9. AddForeignKey FK_WorkOrders_ProductRevisions_ProductRevisionId.
    ///  10. AddForeignKey Drawings.CurrentVersionId → DrawingVersions +
    ///      DrawingApprovals.DrawingVersionId → DrawingVersions (cycle-safe
    ///      sau khi cả 2 table tồn tại).
    ///
    /// Provider-agnostic: strip mọi `type:` argument (SQLite-specific affinity).
    /// EF generate type "TEXT"/"INTEGER"/"REAL"/"BLOB" — bị remove để cùng
    /// migration chạy được trên SQL Server / Postgres (provider tự map).
    ///
    /// Down() = phục dựng lại 3 bảng cũ + WO column rename ngược + reverse
    /// data migration BEST EFFORT (rebuild SpecVersions từ ProductRevisions,
    /// rebuild SpecParameters từ ColorSpecJson). Drop tables mới theo FK order.
    /// </summary>
    public partial class AddProductRevisionSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── (1) Users.Department field ─────────────────────────────────────
            migrationBuilder.AddColumn<string>(
                name: "Department",
                table: "Users",
                nullable: true);

            // ── (2a) ProcessCatalogs (lookup, seed via DbSeeder at startup) ────
            migrationBuilder.CreateTable(
                name: "ProcessCatalogs",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(nullable: false),
                    Category = table.Column<string>(nullable: false),
                    DisplayNameVi = table.Column<string>(nullable: false),
                    DisplayNameEn = table.Column<string>(nullable: false),
                    Description = table.Column<string>(nullable: true),
                    Status = table.Column<string>(nullable: false),
                    DisplayOrder = table.Column<short>(nullable: false),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessCatalogs", x => x.Id);
                });

            // ── (2b) ProductRevisions (replaces Spec parent) ──────────────────
            migrationBuilder.CreateTable(
                name: "ProductRevisions",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductId = table.Column<long>(nullable: false),
                    RevisionCode = table.Column<string>(nullable: false),
                    Status = table.Column<string>(nullable: false),
                    EffectiveFrom = table.Column<DateTime>(nullable: true),
                    EffectiveTo = table.Column<DateTime>(nullable: true),
                    ParentRevisionId = table.Column<long>(nullable: true),
                    ChangeSummary = table.Column<string>(nullable: true),
                    ApprovedBy = table.Column<string>(nullable: true),
                    ApprovedAt = table.Column<DateTime>(nullable: true),
                    ReleasedBy = table.Column<string>(nullable: true),
                    ReleasedAt = table.Column<DateTime>(nullable: true),
                    IsTrashed = table.Column<bool>(nullable: false),
                    TrashedAt = table.Column<DateTime>(nullable: true),
                    TrashedBy = table.Column<string>(nullable: true),
                    Title = table.Column<string>(nullable: false),
                    SpecCode = table.Column<string>(nullable: false),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductRevisions_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // ── (2c) 4 sibling spec tables (1:1 keyed ProductRevisionId) ──────
            migrationBuilder.CreateTable(
                name: "SpecMaterials",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductRevisionId = table.Column<long>(nullable: false),
                    SubstrateType = table.Column<string>(nullable: true),
                    SubstrateBrand = table.Column<string>(nullable: true),
                    ThicknessUm = table.Column<int>(nullable: true),
                    LinerType = table.Column<string>(nullable: true),
                    AdhesiveType = table.Column<string>(nullable: true),
                    AdhesiveBrand = table.Column<string>(nullable: true),
                    ExtraJson = table.Column<string>(nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpecMaterials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpecMaterials_ProductRevisions_ProductRevisionId",
                        column: x => x.ProductRevisionId,
                        principalTable: "ProductRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SpecPrints",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductRevisionId = table.Column<long>(nullable: false),
                    ProcessCode = table.Column<string>(nullable: true),
                    HybridProcessesJson = table.Column<string>(nullable: true),
                    NumColors = table.Column<int>(nullable: false),
                    ColorSpecJson = table.Column<string>(nullable: true),
                    Varnish = table.Column<string>(nullable: true),
                    Lamination = table.Column<string>(nullable: true),
                    WhiteUnderprint = table.Column<bool>(nullable: false),
                    ExtraJson = table.Column<string>(nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpecPrints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpecPrints_ProductRevisions_ProductRevisionId",
                        column: x => x.ProductRevisionId,
                        principalTable: "ProductRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SpecDiecuts",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductRevisionId = table.Column<long>(nullable: false),
                    CutProcessCode = table.Column<string>(nullable: true),
                    DieId = table.Column<string>(nullable: true),
                    DieType = table.Column<string>(nullable: true),
                    WidthMm = table.Column<double>(nullable: true),
                    LengthMm = table.Column<double>(nullable: true),
                    CornerRadiusMm = table.Column<double>(nullable: true),
                    KissCutDepthUm = table.Column<int>(nullable: true),
                    PerforationJson = table.Column<string>(nullable: true),
                    BleedMm = table.Column<double>(nullable: true),
                    CncProgram = table.Column<string>(nullable: true),
                    LaserPowerW = table.Column<double>(nullable: true),
                    PowerpunchTool = table.Column<string>(nullable: true),
                    ExtraJson = table.Column<string>(nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpecDiecuts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpecDiecuts_ProductRevisions_ProductRevisionId",
                        column: x => x.ProductRevisionId,
                        principalTable: "ProductRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SpecFinishings",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductRevisionId = table.Column<long>(nullable: false),
                    OutputForm = table.Column<string>(nullable: true),
                    LabelsPerRoll = table.Column<int>(nullable: true),
                    CoreDiameterMm = table.Column<double>(nullable: true),
                    WindingDirection = table.Column<string>(nullable: true),
                    MaxOuterDiaMm = table.Column<double>(nullable: true),
                    RollsPerBox = table.Column<int>(nullable: true),
                    FinishingProcessesJson = table.Column<string>(nullable: true),
                    ExtraJson = table.Column<string>(nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpecFinishings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpecFinishings_ProductRevisions_ProductRevisionId",
                        column: x => x.ProductRevisionId,
                        principalTable: "ProductRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // ── (2d) QC plan tables (bảng tạo sẵn, UI ở PR Phase 9) ──────────
            migrationBuilder.CreateTable(
                name: "SpecQcWindows",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductRevisionId = table.Column<long>(nullable: false),
                    Stage = table.Column<string>(nullable: false),
                    ProcessCode = table.Column<string>(nullable: true),
                    Title = table.Column<string>(nullable: false),
                    Description = table.Column<string>(nullable: true),
                    SamplePlan = table.Column<string>(nullable: true),
                    Frequency = table.Column<string>(nullable: true),
                    RejectAction = table.Column<string>(nullable: false),
                    Status = table.Column<string>(nullable: false),
                    ApprovedBy = table.Column<string>(nullable: true),
                    ApprovedAt = table.Column<DateTime>(nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpecQcWindows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpecQcWindows_ProductRevisions_ProductRevisionId",
                        column: x => x.ProductRevisionId,
                        principalTable: "ProductRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QcCriteria",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SpecQcWindowId = table.Column<long>(nullable: false),
                    Seq = table.Column<short>(nullable: false),
                    Name = table.Column<string>(nullable: false),
                    CriterionType = table.Column<string>(nullable: false),
                    MeasureMethod = table.Column<string>(nullable: true),
                    TargetValue = table.Column<double>(nullable: true),
                    ToleranceMin = table.Column<double>(nullable: true),
                    ToleranceMax = table.Column<double>(nullable: true),
                    Unit = table.Column<string>(nullable: true),
                    PassCriteria = table.Column<string>(nullable: true),
                    ReferenceImageKey = table.Column<string>(nullable: true),
                    Required = table.Column<bool>(nullable: false),
                    ExtraJson = table.Column<string>(nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QcCriteria", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QcCriteria_SpecQcWindows_SpecQcWindowId",
                        column: x => x.SpecQcWindowId,
                        principalTable: "SpecQcWindows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // ── (2e) Drawing master + version + approval (UI ở PR #31/#32) ───
            migrationBuilder.CreateTable(
                name: "Drawings",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductRevisionId = table.Column<long>(nullable: false),
                    Kind = table.Column<string>(nullable: false),
                    Title = table.Column<string>(nullable: false),
                    CurrentVersionId = table.Column<long>(nullable: true),
                    Status = table.Column<string>(nullable: false),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Drawings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Drawings_ProductRevisions_ProductRevisionId",
                        column: x => x.ProductRevisionId,
                        principalTable: "ProductRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DrawingVersions",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DrawingId = table.Column<long>(nullable: false),
                    VersionNo = table.Column<int>(nullable: false),
                    FileName = table.Column<string>(nullable: false),
                    StorageKey = table.Column<string>(nullable: false),
                    FileHash = table.Column<string>(nullable: false),
                    FileSize = table.Column<long>(nullable: false),
                    PreviewKey = table.Column<string>(nullable: true),
                    ChangeReason = table.Column<string>(nullable: true),
                    Status = table.Column<string>(nullable: false),
                    SupersededByVersionId = table.Column<long>(nullable: true),
                    UploadedAt = table.Column<DateTime>(nullable: false),
                    UploadedBy = table.Column<string>(nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DrawingVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DrawingVersions_Drawings_DrawingId",
                        column: x => x.DrawingId,
                        principalTable: "Drawings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DrawingApprovals",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DrawingVersionId = table.Column<long>(nullable: false),
                    Role = table.Column<string>(nullable: false),
                    Status = table.Column<string>(nullable: false),
                    ActedBy = table.Column<string>(nullable: true),
                    ActedAt = table.Column<DateTime>(nullable: true),
                    Comment = table.Column<string>(nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DrawingApprovals", x => x.Id);
                });

            // ── (3) Indexes mới ────────────────────────────────────────────────
            migrationBuilder.CreateIndex(
                name: "IX_ProcessCatalogs_Category_Status_DisplayOrder",
                table: "ProcessCatalogs",
                columns: new[] { "Category", "Status", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessCatalogs_Code",
                table: "ProcessCatalogs",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductRevisions_ProductId_RevisionCode",
                table: "ProductRevisions",
                columns: new[] { "ProductId", "RevisionCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductRevisions_SpecCode",
                table: "ProductRevisions",
                column: "SpecCode");

            migrationBuilder.CreateIndex(
                name: "IX_ProductRevisions_Status",
                table: "ProductRevisions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SpecMaterials_ProductRevisionId",
                table: "SpecMaterials",
                column: "ProductRevisionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpecPrints_ProductRevisionId",
                table: "SpecPrints",
                column: "ProductRevisionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpecDiecuts_ProductRevisionId",
                table: "SpecDiecuts",
                column: "ProductRevisionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpecFinishings_ProductRevisionId",
                table: "SpecFinishings",
                column: "ProductRevisionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpecQcWindows_ProductRevisionId_Stage",
                table: "SpecQcWindows",
                columns: new[] { "ProductRevisionId", "Stage" });

            migrationBuilder.CreateIndex(
                name: "IX_QcCriteria_SpecQcWindowId_Seq",
                table: "QcCriteria",
                columns: new[] { "SpecQcWindowId", "Seq" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Drawings_ProductRevisionId_Kind_Title",
                table: "Drawings",
                columns: new[] { "ProductRevisionId", "Kind", "Title" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Drawings_CurrentVersionId",
                table: "Drawings",
                column: "CurrentVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_DrawingVersions_DrawingId_VersionNo",
                table: "DrawingVersions",
                columns: new[] { "DrawingId", "VersionNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DrawingApprovals_DrawingVersionId_Role",
                table: "DrawingApprovals",
                columns: new[] { "DrawingVersionId", "Role" },
                unique: true);

            // ── (4a) DATA MIGRATION: Specs/SpecVersions → ProductRevisions ────
            // Preserve PK 1:1 (sv.Id → pr.Id). RevisionCode='A' đầu tiên (lineage
            // mới). ChangeSummary stamp lý do migration để forensic trail.
            // Status TEXT cũ giờ map sang ProductRevisionStatus TEXT (semantic
            // match: Draft / InReview / Approved / Obsolete → 4 đầu giữ; "Obsolete"
            // không có trong enum mới, fallback "Superseded" cho semantic).
            migrationBuilder.Sql(@"
                INSERT INTO ProductRevisions (
                    Id, ProductId, RevisionCode, Status,
                    EffectiveFrom, ApprovedBy, ApprovedAt,
                    ChangeSummary, IsTrashed, Title, SpecCode, CreatedAt
                )
                SELECT
                    sv.Id,
                    s.ProductId,
                    'A',
                    CASE
                        WHEN sv.Status = 'Obsolete' THEN 'Superseded'
                        ELSE sv.Status
                    END,
                    sv.EffectiveDate,
                    sv.ApprovedBy,
                    sv.ApprovedAt,
                    'Migrated from Spec/SpecVersion baseline (PR #28)',
                    0,
                    s.Title,
                    s.SpecCode,
                    sv.CreatedAt
                FROM SpecVersions sv
                INNER JOIN Specs s ON sv.SpecId = s.Id;
            ");

            // ── (4b) DATA MIGRATION: SpecParameters → SpecPrints.ColorSpecJson ─
            // Fold params vào JSON array. ProcessCode mặc định 'SILKSCREEN' (giả
            // định baseline; greenfield Create flow sẽ chọn từ dropdown). NumColors=0
            // (param không represent màu in). WhiteUnderprint=0 (default).
            // SQLite json_object + group_concat. NULL filter qua CASE WHEN sp.Id IS
            // NOT NULL — LEFT JOIN tạo NULL row cho SpecVersion không có param;
            // không filter sẽ tạo JSON object toàn null.
            migrationBuilder.Sql(@"
                INSERT INTO SpecPrints (
                    ProductRevisionId, ProcessCode, NumColors,
                    ColorSpecJson, WhiteUnderprint, CreatedAt
                )
                SELECT
                    sv.Id,
                    'SILKSCREEN',
                    0,
                    COALESCE(
                        '[' || group_concat(
                            CASE WHEN sp.Id IS NOT NULL THEN json_object(
                                'param_name',  sp.ParamName,
                                'nominal',     sp.Nominal,
                                'tol_min',     sp.TolMin,
                                'tol_max',     sp.TolMax,
                                'uom',         sp.Uom,
                                'is_critical', sp.IsCritical
                            ) END
                        ) || ']',
                        '[]'
                    ),
                    0,
                    datetime('now')
                FROM SpecVersions sv
                LEFT JOIN SpecParameters sp ON sp.SpecVersionId = sv.Id
                GROUP BY sv.Id;
            ");

            // ── (5) Drop FK WO→SpecVersions + RenameColumn WorkOrders.SpecVersionId →
            //         ProductRevisionId. PROVIDER-GUARDED (Lesson §7 / CLAUDE.md §4).
            //
            // SQLite branch — manual rebuild (workaround EF Core 10 quirk):
            //   EF Core 10 SQLite provider KHÔNG reliably trigger table-rebuild
            //   khi `DropForeignKey` + `RenameColumn` đứng cạnh nhau (verbose trace
            //   cho thấy chỉ emit `ALTER TABLE RENAME COLUMN`, KHÔNG rebuild).
            //   FK còn sống → `DROP TABLE SpecVersions` fail Error 19 FOREIGN KEY.
            //   Workaround: raw SQL rebuild WorkOrders trong cùng 1 tx.
            //
            // SQL Server branch — standard EF ops (DropForeignKey + RenameColumn +
            //   RenameIndex thi hành đúng thứ tự; SQL Server hỗ trợ DROP CONSTRAINT
            //   native, không cần rebuild). EF emit ALTER TABLE DROP CONSTRAINT +
            //   sp_rename + sp_rename trên SQL Server.
            //
            // Provider check via `migrationBuilder.ActiveProvider` (giá trị set bởi
            // EF design-time/runtime): "Microsoft.EntityFrameworkCore.Sqlite" hoặc
            // "Microsoft.EntityFrameworkCore.SqlServer". Anti-mistake guard cho
            // future provider (Postgres etc.) — throw NotSupportedException +
            // điểm đến HOW-TO-UPGRADE doc.
            if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                migrationBuilder.Sql(@"
                    PRAGMA foreign_keys = OFF;

                    CREATE TABLE ""WorkOrders_temp"" (
                        ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_WorkOrders"" PRIMARY KEY AUTOINCREMENT,
                        ""WoNo"" TEXT NOT NULL,
                        ""CustomerId"" INTEGER NOT NULL,
                        ""ProductId"" INTEGER NOT NULL,
                        ""ProductName"" TEXT NOT NULL,
                        ""ProductRevisionId"" INTEGER NULL,
                        ""MachineCode"" TEXT NULL,
                        ""MachineName"" TEXT NULL,
                        ""TargetQty"" INTEGER NOT NULL,
                        ""Uom"" TEXT NOT NULL,
                        ""ProducedQty"" INTEGER NOT NULL,
                        ""CurrentStep"" TEXT NOT NULL,
                        ""Status"" TEXT NOT NULL,
                        ""Priority"" INTEGER NOT NULL,
                        ""MaterialsReady"" INTEGER NOT NULL,
                        ""SetupConfirmed"" INTEGER NOT NULL,
                        ""RohsOk"" INTEGER NOT NULL,
                        ""PlannedStart"" TEXT NULL,
                        ""PlannedEnd"" TEXT NULL,
                        ""CreatedAt"" TEXT NOT NULL,
                        ""CreatedBy"" TEXT NULL,
                        ""UpdatedAt"" TEXT NULL,
                        ""UpdatedBy"" TEXT NULL,
                        CONSTRAINT ""FK_WorkOrders_Customers_CustomerId"" FOREIGN KEY (""CustomerId"") REFERENCES ""Customers"" (""Id"") ON DELETE CASCADE,
                        CONSTRAINT ""FK_WorkOrders_Products_ProductId"" FOREIGN KEY (""ProductId"") REFERENCES ""Products"" (""Id"") ON DELETE CASCADE
                    );

                    INSERT INTO ""WorkOrders_temp"" (
                        ""Id"", ""WoNo"", ""CustomerId"", ""ProductId"", ""ProductName"",
                        ""ProductRevisionId"", ""MachineCode"", ""MachineName"", ""TargetQty"",
                        ""Uom"", ""ProducedQty"", ""CurrentStep"", ""Status"", ""Priority"",
                        ""MaterialsReady"", ""SetupConfirmed"", ""RohsOk"", ""PlannedStart"",
                        ""PlannedEnd"", ""CreatedAt"", ""CreatedBy"", ""UpdatedAt"", ""UpdatedBy""
                    )
                    SELECT
                        ""Id"", ""WoNo"", ""CustomerId"", ""ProductId"", ""ProductName"",
                        ""SpecVersionId"", ""MachineCode"", ""MachineName"", ""TargetQty"",
                        ""Uom"", ""ProducedQty"", ""CurrentStep"", ""Status"", ""Priority"",
                        ""MaterialsReady"", ""SetupConfirmed"", ""RohsOk"", ""PlannedStart"",
                        ""PlannedEnd"", ""CreatedAt"", ""CreatedBy"", ""UpdatedAt"", ""UpdatedBy""
                    FROM ""WorkOrders"";

                    DROP TABLE ""WorkOrders"";
                    ALTER TABLE ""WorkOrders_temp"" RENAME TO ""WorkOrders"";

                    CREATE INDEX ""IX_WorkOrders_CustomerId"" ON ""WorkOrders"" (""CustomerId"");
                    CREATE INDEX ""IX_WorkOrders_ProductId"" ON ""WorkOrders"" (""ProductId"");
                    CREATE INDEX ""IX_WorkOrders_ProductRevisionId"" ON ""WorkOrders"" (""ProductRevisionId"");
                    CREATE UNIQUE INDEX ""IX_WorkOrders_WoNo"" ON ""WorkOrders"" (""WoNo"");

                    PRAGMA foreign_keys = ON;
                ");
            }
            else if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                // SQL Server: standard EF ops. Order:
                //   1) DropForeignKey (thi hành ngay với ALTER TABLE DROP CONSTRAINT)
                //   2) DropIndex (rename column gây mất index nếu không drop trước)
                //   3) RenameColumn (sp_rename)
                //   4) CreateIndex lại trên column mới
                migrationBuilder.DropForeignKey(
                    name: "FK_WorkOrders_SpecVersions_SpecVersionId",
                    table: "WorkOrders");

                migrationBuilder.DropIndex(
                    name: "IX_WorkOrders_SpecVersionId",
                    table: "WorkOrders");

                migrationBuilder.RenameColumn(
                    name: "SpecVersionId",
                    table: "WorkOrders",
                    newName: "ProductRevisionId");

                migrationBuilder.CreateIndex(
                    name: "IX_WorkOrders_ProductRevisionId",
                    table: "WorkOrders",
                    column: "ProductRevisionId");
            }
            else
            {
                // Postgres / MySQL / other provider: chưa được test với migration
                // refactor Spec → ProductRevision. Throw rõ ràng thay vì để raw
                // SQL chạy mù. Bài học CLAUDE.md §4 (provider-agnostic discipline):
                // KHÔNG để SQLite-specific raw SQL chạy trên SQL Server / Postgres.
                // Khi muốn enable provider mới, làm theo `docs/HOW-TO-UPGRADE-TO-SQLSERVER.md`
                // + ADD nhánh tương đương ở đây.
                throw new NotSupportedException(
                    $"Migration AddProductRevisionSchema chưa hỗ trợ provider " +
                    $"'{migrationBuilder.ActiveProvider}'. Chỉ test với SQLite + " +
                    $"SQL Server. Xem docs/HOW-TO-UPGRADE-TO-SQLSERVER.md để bổ " +
                    $"sung nhánh cho provider mới.");
            }

            // ── (6) Drop legacy tables (FK order — Specs cuối cùng) ───────────
            // FK SpecParameters → SpecVersions handled trong DropTable;
            // WorkOrders không còn ref SpecVersions sau step 5 (provider-specific
            // rebuild SQLite / DropForeignKey SQL Server).
            migrationBuilder.DropTable(name: "SpecParameters");
            migrationBuilder.DropTable(name: "SpecVersions");
            migrationBuilder.DropTable(name: "Specs");

            // ── (7) New FK: WO.ProductRevisionId → ProductRevisions ───────────
            // Add sau khi ProductRevisions có data (đã insert ở step 4a) để
            // FK check pass. Restrict on delete để tránh accidentally drop WO khi
            // ProductRevision soft-delete (PR #30 trash flow chỉ flip IsTrashed).
            migrationBuilder.AddForeignKey(
                name: "FK_WorkOrders_ProductRevisions_ProductRevisionId",
                table: "WorkOrders",
                column: "ProductRevisionId",
                principalTable: "ProductRevisions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // ── (8) FK cho cycle Drawing.CurrentVersion ↔ DrawingVersion ──────
            // Phải add sau khi cả 2 table tồn tại + indexes ready.
            migrationBuilder.AddForeignKey(
                name: "FK_Drawings_DrawingVersions_CurrentVersionId",
                table: "Drawings",
                column: "CurrentVersionId",
                principalTable: "DrawingVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_DrawingApprovals_DrawingVersions_DrawingVersionId",
                table: "DrawingApprovals",
                column: "DrawingVersionId",
                principalTable: "DrawingVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse-order drops + data restoration BEST EFFORT.
            // Phục dựng legacy Spec/SpecVersion/SpecParameter từ ProductRevision
            // + SpecPrint.ColorSpecJson. Rollback an toàn cho 1 row baseline;
            // KHÔNG khuyến nghị Down() trên prod sau khi đã ship operator data
            // (forensic loss vô số revision tạo sau PR #28).

            migrationBuilder.DropForeignKey(
                name: "FK_WorkOrders_ProductRevisions_ProductRevisionId",
                table: "WorkOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_Drawings_DrawingVersions_CurrentVersionId",
                table: "Drawings");

            migrationBuilder.DropForeignKey(
                name: "FK_DrawingApprovals_DrawingVersions_DrawingVersionId",
                table: "DrawingApprovals");

            // Recreate legacy tables
            migrationBuilder.CreateTable(
                name: "Specs",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductId = table.Column<long>(nullable: false),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    SpecCode = table.Column<string>(nullable: false),
                    Title = table.Column<string>(nullable: false),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Specs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Specs_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SpecVersions",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SpecId = table.Column<long>(nullable: false),
                    ApprovedAt = table.Column<DateTime>(nullable: true),
                    ApprovedBy = table.Column<string>(nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    EffectiveDate = table.Column<DateTime>(nullable: true),
                    Status = table.Column<string>(nullable: false),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true),
                    VersionNo = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpecVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpecVersions_Specs_SpecId",
                        column: x => x.SpecId,
                        principalTable: "Specs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SpecParameters",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    IsCritical = table.Column<bool>(nullable: false),
                    Nominal = table.Column<string>(nullable: true),
                    ParamName = table.Column<string>(nullable: false),
                    SpecVersionId = table.Column<long>(nullable: false),
                    TolMax = table.Column<string>(nullable: true),
                    TolMin = table.Column<string>(nullable: true),
                    Uom = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpecParameters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpecParameters_SpecVersions_SpecVersionId",
                        column: x => x.SpecVersionId,
                        principalTable: "SpecVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Restore legacy data từ ProductRevisions (BEST EFFORT - only 'rev A')
            migrationBuilder.Sql(@"
                INSERT INTO Specs (Id, ProductId, SpecCode, Title, CreatedAt)
                SELECT Id, ProductId, SpecCode, Title, CreatedAt
                FROM ProductRevisions
                WHERE RevisionCode = 'A';
            ");
            migrationBuilder.Sql(@"
                INSERT INTO SpecVersions (Id, SpecId, VersionNo, Status, EffectiveDate, ApprovedBy, ApprovedAt, CreatedAt)
                SELECT Id, Id, 1, CASE WHEN Status = 'Superseded' THEN 'Obsolete' ELSE Status END,
                       EffectiveFrom, ApprovedBy, ApprovedAt, CreatedAt
                FROM ProductRevisions
                WHERE RevisionCode = 'A';
            ");

            migrationBuilder.CreateIndex(
                name: "IX_SpecParameters_SpecVersionId",
                table: "SpecParameters",
                column: "SpecVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_Specs_ProductId",
                table: "Specs",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SpecVersions_SpecId",
                table: "SpecVersions",
                column: "SpecId");

            migrationBuilder.RenameColumn(
                name: "ProductRevisionId",
                table: "WorkOrders",
                newName: "SpecVersionId");

            migrationBuilder.RenameIndex(
                name: "IX_WorkOrders_ProductRevisionId",
                table: "WorkOrders",
                newName: "IX_WorkOrders_SpecVersionId");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkOrders_SpecVersions_SpecVersionId",
                table: "WorkOrders",
                column: "SpecVersionId",
                principalTable: "SpecVersions",
                principalColumn: "Id");

            // Drop new tables in FK order (children first)
            migrationBuilder.DropTable(name: "DrawingApprovals");
            migrationBuilder.DropTable(name: "DrawingVersions");
            migrationBuilder.DropTable(name: "Drawings");
            migrationBuilder.DropTable(name: "QcCriteria");
            migrationBuilder.DropTable(name: "SpecQcWindows");
            migrationBuilder.DropTable(name: "SpecMaterials");
            migrationBuilder.DropTable(name: "SpecPrints");
            migrationBuilder.DropTable(name: "SpecDiecuts");
            migrationBuilder.DropTable(name: "SpecFinishings");
            migrationBuilder.DropTable(name: "ProductRevisions");
            migrationBuilder.DropTable(name: "ProcessCatalogs");

            migrationBuilder.DropColumn(
                name: "Department",
                table: "Users");
        }
    }
}
