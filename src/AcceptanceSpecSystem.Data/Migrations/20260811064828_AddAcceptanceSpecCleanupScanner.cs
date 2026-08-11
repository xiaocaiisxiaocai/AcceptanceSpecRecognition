using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceSpecSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAcceptanceSpecCleanupScanner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CleanupScanIgnoreReason",
                table: "AcceptanceSpecs",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "CleanupScanIgnored",
                table: "AcceptanceSpecs",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "CleanupScanIgnoredAtUtc",
                table: "AcceptanceSpecs",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CleanupScanIgnoredByUserId",
                table: "AcceptanceSpecs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CleanupStatus",
                table: "AcceptanceSpecs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "QuarantineExpiresAtUtc",
                table: "AcceptanceSpecs",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuarantineReason",
                table: "AcceptanceSpecs",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "QuarantineSourceScanId",
                table: "AcceptanceSpecs",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "QuarantinedAtUtc",
                table: "AcceptanceSpecs",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QuarantinedByUserId",
                table: "AcceptanceSpecs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "QuarantinedReferenceVersion",
                table: "AcceptanceSpecs",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AcceptanceSpecCleanupDeletionRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    OriginalAcceptanceSpecId = table.Column<int>(type: "int", nullable: false),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    OwnerOrgUnitId = table.Column<int>(type: "int", nullable: true),
                    DeletedByUserId = table.Column<int>(type: "int", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    SourceScanId = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReferenceVersion = table.Column<long>(type: "bigint", nullable: false),
                    RecordedReferenceCount = table.Column<long>(type: "bigint", nullable: false),
                    ContentVersionCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceptanceSpecCleanupDeletionRecords", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AcceptanceSpecCleanupScans",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    RequestedByUserId = table.Column<int>(type: "int", nullable: false),
                    IsAllScope = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IncludeSelf = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ScopeOrgUnitIds = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NewItemGraceDays = table.Column<int>(type: "int", nullable: false),
                    UnusedDays = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    TotalCount = table.Column<int>(type: "int", nullable: false),
                    ProcessedCount = table.Column<int>(type: "int", nullable: false),
                    RecommendedCleanupCount = table.Column<int>(type: "int", nullable: false),
                    ManualReviewCount = table.Column<int>(type: "int", nullable: false),
                    HealthyCount = table.Column<int>(type: "int", nullable: false),
                    LastProcessedSpecId = table.Column<int>(type: "int", nullable: false),
                    CancellationRequested = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ErrorMessage = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceptanceSpecCleanupScans", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AcceptanceSpecCleanupScanItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ScanId = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AcceptanceSpecId = table.Column<int>(type: "int", nullable: false),
                    ReferenceVersion = table.Column<long>(type: "bigint", nullable: false),
                    CurrentReferenceCount = table.Column<long>(type: "bigint", nullable: false),
                    RecordedReferenceCount = table.Column<long>(type: "bigint", nullable: false),
                    UntrackedReferenceCount = table.Column<long>(type: "bigint", nullable: false),
                    LastReferencedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ContentActivityAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<int>(type: "int", nullable: false),
                    ReviewStatus = table.Column<int>(type: "int", nullable: false),
                    ScannedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceptanceSpecCleanupScanItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AcceptanceSpecCleanupScanItems_AcceptanceSpecCleanupScans_Sc~",
                        column: x => x.ScanId,
                        principalTable: "AcceptanceSpecCleanupScans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AcceptanceSpecCleanupScanItems_AcceptanceSpecs_AcceptanceSpe~",
                        column: x => x.AcceptanceSpecId,
                        principalTable: "AcceptanceSpecs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AcceptanceSpecs_CleanupStatus_CleanupScanIgnored_Id",
                table: "AcceptanceSpecs",
                columns: new[] { "CleanupStatus", "CleanupScanIgnored", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceptanceSpecs_CleanupStatus_QuarantineExpiresAtUtc_Id",
                table: "AcceptanceSpecs",
                columns: new[] { "CleanupStatus", "QuarantineExpiresAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceptanceSpecCleanupDeletionRecords_CompanyId_DeletedAtUtc_~",
                table: "AcceptanceSpecCleanupDeletionRecords",
                columns: new[] { "CompanyId", "DeletedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceptanceSpecCleanupDeletionRecords_OriginalAcceptanceSpecId",
                table: "AcceptanceSpecCleanupDeletionRecords",
                column: "OriginalAcceptanceSpecId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceptanceSpecCleanupScanItems_AcceptanceSpecId",
                table: "AcceptanceSpecCleanupScanItems",
                column: "AcceptanceSpecId");

            migrationBuilder.CreateIndex(
                name: "IX_AcceptanceSpecCleanupScanItems_ScanId_AcceptanceSpecId",
                table: "AcceptanceSpecCleanupScanItems",
                columns: new[] { "ScanId", "AcceptanceSpecId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceptanceSpecCleanupScanItems_ScanId_Category_Id",
                table: "AcceptanceSpecCleanupScanItems",
                columns: new[] { "ScanId", "Category", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceptanceSpecCleanupScans_CompanyId_RequestedByUserId_Creat~",
                table: "AcceptanceSpecCleanupScans",
                columns: new[] { "CompanyId", "RequestedByUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceptanceSpecCleanupScans_Status_CreatedAtUtc_Id",
                table: "AcceptanceSpecCleanupScans",
                columns: new[] { "Status", "CreatedAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AcceptanceSpecCleanupDeletionRecords");

            migrationBuilder.DropTable(
                name: "AcceptanceSpecCleanupScanItems");

            migrationBuilder.DropTable(
                name: "AcceptanceSpecCleanupScans");

            migrationBuilder.DropIndex(
                name: "IX_AcceptanceSpecs_CleanupStatus_CleanupScanIgnored_Id",
                table: "AcceptanceSpecs");

            migrationBuilder.DropIndex(
                name: "IX_AcceptanceSpecs_CleanupStatus_QuarantineExpiresAtUtc_Id",
                table: "AcceptanceSpecs");

            migrationBuilder.DropColumn(
                name: "CleanupScanIgnoreReason",
                table: "AcceptanceSpecs");

            migrationBuilder.DropColumn(
                name: "CleanupScanIgnored",
                table: "AcceptanceSpecs");

            migrationBuilder.DropColumn(
                name: "CleanupScanIgnoredAtUtc",
                table: "AcceptanceSpecs");

            migrationBuilder.DropColumn(
                name: "CleanupScanIgnoredByUserId",
                table: "AcceptanceSpecs");

            migrationBuilder.DropColumn(
                name: "CleanupStatus",
                table: "AcceptanceSpecs");

            migrationBuilder.DropColumn(
                name: "QuarantineExpiresAtUtc",
                table: "AcceptanceSpecs");

            migrationBuilder.DropColumn(
                name: "QuarantineReason",
                table: "AcceptanceSpecs");

            migrationBuilder.DropColumn(
                name: "QuarantineSourceScanId",
                table: "AcceptanceSpecs");

            migrationBuilder.DropColumn(
                name: "QuarantinedAtUtc",
                table: "AcceptanceSpecs");

            migrationBuilder.DropColumn(
                name: "QuarantinedByUserId",
                table: "AcceptanceSpecs");

            migrationBuilder.DropColumn(
                name: "QuarantinedReferenceVersion",
                table: "AcceptanceSpecs");
        }
    }
}
