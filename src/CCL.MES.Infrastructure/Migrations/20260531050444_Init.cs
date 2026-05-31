using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCL.MES.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(nullable: false),
                    Name = table.Column<string>(nullable: false),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DowntimeReasons",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(nullable: false),
                    Name = table.Column<string>(nullable: false),
                    Category = table.Column<string>(nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DowntimeReasons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Machines",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(nullable: false),
                    Name = table.Column<string>(nullable: false),
                    Type = table.Column<string>(nullable: true),
                    CurrentState = table.Column<string>(nullable: false),
                    IdealCycleTimeSec = table.Column<double>(nullable: false),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Machines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ManufacturingStructures",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ParentPart = table.Column<string>(nullable: false),
                    ParentDescription = table.Column<string>(nullable: true),
                    ComponentPart = table.Column<string>(nullable: false),
                    ComponentDescription = table.Column<string>(nullable: true),
                    QtyAssembly = table.Column<double>(nullable: false),
                    Uom = table.Column<string>(nullable: true),
                    ScrapFactor = table.Column<double>(nullable: false),
                    ScrapPct = table.Column<string>(nullable: true),
                    Pitch = table.Column<string>(nullable: true),
                    Cavity = table.Column<string>(nullable: true),
                    Color = table.Column<string>(nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManufacturingStructures", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RawMaterials",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PartNo = table.Column<string>(nullable: false),
                    PartDescription = table.Column<string>(nullable: true),
                    SupplierId = table.Column<string>(nullable: true),
                    SupplierName = table.Column<string>(nullable: true),
                    Price = table.Column<double>(nullable: false),
                    Currency = table.Column<string>(nullable: true),
                    PriceUom = table.Column<string>(nullable: true),
                    CatalogGroup = table.Column<string>(nullable: true),
                    CatalogDesc = table.Column<string>(nullable: true),
                    Grp = table.Column<string>(nullable: true),
                    Type = table.Column<string>(nullable: true),
                    TypeDesc = table.Column<string>(nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawMaterials", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RoutingOperations",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PartNo = table.Column<string>(nullable: false),
                    PartDescription = table.Column<string>(nullable: true),
                    OpNo = table.Column<string>(nullable: true),
                    Operation = table.Column<string>(nullable: true),
                    WorkCenterNo = table.Column<string>(nullable: true),
                    WorkCenterDescription = table.Column<string>(nullable: true),
                    MachineSetupTime = table.Column<double>(nullable: false),
                    LaborSetupTime = table.Column<double>(nullable: false),
                    MachineRunTime = table.Column<double>(nullable: false),
                    LaborRunTime = table.Column<double>(nullable: false),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoutingOperations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Username = table.Column<string>(nullable: false),
                    PasswordHash = table.Column<string>(nullable: false),
                    Role = table.Column<string>(nullable: false),
                    DisplayName = table.Column<string>(nullable: true),
                    LastLoginAt = table.Column<DateTime>(nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkCenters",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(nullable: false),
                    Description = table.Column<string>(nullable: false),
                    Area = table.Column<string>(nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkCenters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductCode = table.Column<string>(nullable: false),
                    Name = table.Column<string>(nullable: false),
                    CustomerId = table.Column<long>(nullable: false),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Products_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Specs",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SpecCode = table.Column<string>(nullable: false),
                    Title = table.Column<string>(nullable: false),
                    ProductId = table.Column<long>(nullable: false),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
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
                name: "WorkInstructions",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(nullable: false),
                    ProductId = table.Column<long>(nullable: false),
                    ProcessStep = table.Column<string>(nullable: false),
                    MachineCode = table.Column<string>(nullable: true),
                    VersionNo = table.Column<int>(nullable: false),
                    Status = table.Column<string>(nullable: false),
                    EffectiveDate = table.Column<DateTime>(nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkInstructions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkInstructions_Products_ProductId",
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
                    VersionNo = table.Column<int>(nullable: false),
                    Status = table.Column<string>(nullable: false),
                    EffectiveDate = table.Column<DateTime>(nullable: true),
                    ApprovedBy = table.Column<string>(nullable: true),
                    ApprovedAt = table.Column<DateTime>(nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
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
                name: "WiStepDetails",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorkInstructionId = table.Column<long>(nullable: false),
                    Sequence = table.Column<int>(nullable: false),
                    Description = table.Column<string>(nullable: false),
                    ImageUrl = table.Column<string>(nullable: true),
                    WarningNote = table.Column<string>(nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WiStepDetails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WiStepDetails_WorkInstructions_WorkInstructionId",
                        column: x => x.WorkInstructionId,
                        principalTable: "WorkInstructions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SpecParameters",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SpecVersionId = table.Column<long>(nullable: false),
                    ParamName = table.Column<string>(nullable: false),
                    Nominal = table.Column<string>(nullable: true),
                    TolMin = table.Column<string>(nullable: true),
                    TolMax = table.Column<string>(nullable: true),
                    Uom = table.Column<string>(nullable: true),
                    IsCritical = table.Column<bool>(nullable: false),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
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

            migrationBuilder.CreateTable(
                name: "WorkOrders",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WoNo = table.Column<string>(nullable: false),
                    CustomerId = table.Column<long>(nullable: false),
                    ProductId = table.Column<long>(nullable: false),
                    ProductName = table.Column<string>(nullable: false),
                    SpecVersionId = table.Column<long>(nullable: true),
                    MachineCode = table.Column<string>(nullable: true),
                    MachineName = table.Column<string>(nullable: true),
                    TargetQty = table.Column<int>(nullable: false),
                    Uom = table.Column<string>(nullable: false),
                    ProducedQty = table.Column<int>(nullable: false),
                    CurrentStep = table.Column<string>(nullable: false),
                    Status = table.Column<string>(nullable: false),
                    Priority = table.Column<int>(nullable: false),
                    MaterialsReady = table.Column<bool>(nullable: false),
                    SetupConfirmed = table.Column<bool>(nullable: false),
                    RohsOk = table.Column<bool>(nullable: false),
                    PlannedStart = table.Column<DateTime>(nullable: true),
                    PlannedEnd = table.Column<DateTime>(nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkOrders_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkOrders_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkOrders_SpecVersions_SpecVersionId",
                        column: x => x.SpecVersionId,
                        principalTable: "SpecVersions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ProductionLogs",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorkOrderId = table.Column<long>(nullable: false),
                    MachineId = table.Column<long>(nullable: false),
                    EventType = table.Column<string>(nullable: false),
                    StartAt = table.Column<DateTime>(nullable: false),
                    EndAt = table.Column<DateTime>(nullable: true),
                    GoodQty = table.Column<int>(nullable: false),
                    RejectQty = table.Column<int>(nullable: false),
                    OperatorId = table.Column<string>(nullable: true),
                    DowntimeReasonId = table.Column<long>(nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionLogs_DowntimeReasons_DowntimeReasonId",
                        column: x => x.DowntimeReasonId,
                        principalTable: "DowntimeReasons",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ProductionLogs_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductionLogs_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QcInspections",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorkOrderId = table.Column<long>(nullable: false),
                    Type = table.Column<string>(nullable: false),
                    Result = table.Column<string>(nullable: false),
                    InspectorId = table.Column<string>(nullable: true),
                    SampleSize = table.Column<int>(nullable: false),
                    ApprovedBy = table.Column<string>(nullable: true),
                    ApprovedAt = table.Column<DateTime>(nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QcInspections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QcInspections_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WoStatusHistories",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorkOrderId = table.Column<long>(nullable: false),
                    FromStep = table.Column<string>(nullable: false),
                    ToStep = table.Column<string>(nullable: false),
                    Action = table.Column<string>(nullable: false),
                    ByUser = table.Column<string>(nullable: true),
                    Reason = table.Column<string>(nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WoStatusHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WoStatusHistories_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QcResultDetails",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    QcInspectionId = table.Column<long>(nullable: false),
                    ItemName = table.Column<string>(nullable: false),
                    MeasuredValue = table.Column<string>(nullable: true),
                    Pass = table.Column<bool>(nullable: false),
                    DefectCode = table.Column<string>(nullable: true),
                    Qty = table.Column<int>(nullable: false),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QcResultDetails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QcResultDetails_QcInspections_QcInspectionId",
                        column: x => x.QcInspectionId,
                        principalTable: "QcInspections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Machines_Code",
                table: "Machines",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingStructures_ParentPart",
                table: "ManufacturingStructures",
                column: "ParentPart");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionLogs_DowntimeReasonId",
                table: "ProductionLogs",
                column: "DowntimeReasonId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionLogs_MachineId",
                table: "ProductionLogs",
                column: "MachineId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionLogs_WorkOrderId",
                table: "ProductionLogs",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_CustomerId",
                table: "Products",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_QcInspections_WorkOrderId",
                table: "QcInspections",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_QcResultDetails_QcInspectionId",
                table: "QcResultDetails",
                column: "QcInspectionId");

            migrationBuilder.CreateIndex(
                name: "IX_RawMaterials_PartNo",
                table: "RawMaterials",
                column: "PartNo");

            migrationBuilder.CreateIndex(
                name: "IX_RoutingOperations_PartNo",
                table: "RoutingOperations",
                column: "PartNo");

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

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WiStepDetails_WorkInstructionId",
                table: "WiStepDetails",
                column: "WorkInstructionId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkCenters_Code",
                table: "WorkCenters",
                column: "Code");

            migrationBuilder.CreateIndex(
                name: "IX_WorkInstructions_ProductId",
                table: "WorkInstructions",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_CustomerId",
                table: "WorkOrders",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_ProductId",
                table: "WorkOrders",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_SpecVersionId",
                table: "WorkOrders",
                column: "SpecVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_WoNo",
                table: "WorkOrders",
                column: "WoNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WoStatusHistories_WorkOrderId",
                table: "WoStatusHistories",
                column: "WorkOrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ManufacturingStructures");

            migrationBuilder.DropTable(
                name: "ProductionLogs");

            migrationBuilder.DropTable(
                name: "QcResultDetails");

            migrationBuilder.DropTable(
                name: "RawMaterials");

            migrationBuilder.DropTable(
                name: "RoutingOperations");

            migrationBuilder.DropTable(
                name: "SpecParameters");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "WiStepDetails");

            migrationBuilder.DropTable(
                name: "WorkCenters");

            migrationBuilder.DropTable(
                name: "WoStatusHistories");

            migrationBuilder.DropTable(
                name: "DowntimeReasons");

            migrationBuilder.DropTable(
                name: "Machines");

            migrationBuilder.DropTable(
                name: "QcInspections");

            migrationBuilder.DropTable(
                name: "WorkInstructions");

            migrationBuilder.DropTable(
                name: "WorkOrders");

            migrationBuilder.DropTable(
                name: "SpecVersions");

            migrationBuilder.DropTable(
                name: "Specs");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "Customers");
        }
    }
}
