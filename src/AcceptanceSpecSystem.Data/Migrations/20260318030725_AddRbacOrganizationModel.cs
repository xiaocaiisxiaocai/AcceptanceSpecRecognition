using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceSpecSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRbacOrganizationModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SystemUsers_IsActive",
                table: "SystemUsers");

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "SystemUsers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PermissionVersion",
                table: "SystemUsers",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "AuthPermissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Code = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PermissionType = table.Column<int>(type: "int", nullable: false),
                    Resource = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Action = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RoutePath = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HttpMethod = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ApiPath = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsBuiltIn = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthPermissions", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "OrgCompanies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Code = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgCompanies", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AuthRoles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsBuiltIn = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthRoles_OrgCompanies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "OrgCompanies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "OrgUnits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    ParentId = table.Column<int>(type: "int", nullable: true),
                    UnitType = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Path = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Depth = table.Column<int>(type: "int", nullable: false),
                    Sort = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrgUnits_OrgCompanies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "OrgCompanies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrgUnits_OrgUnits_ParentId",
                        column: x => x.ParentId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AuthRoleDataScopes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    Resource = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ScopeType = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthRoleDataScopes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthRoleDataScopes_AuthRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AuthRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AuthRolePermissions",
                columns: table => new
                {
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    PermissionId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthRolePermissions", x => new { x.RoleId, x.PermissionId });
                    table.ForeignKey(
                        name: "FK_AuthRolePermissions_AuthPermissions_PermissionId",
                        column: x => x.PermissionId,
                        principalTable: "AuthPermissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AuthRolePermissions_AuthRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AuthRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AuthUserRoles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    StartAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    EndAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthUserRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthUserRoles_AuthRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AuthRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AuthUserRoles_SystemUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "SystemUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AuthUserOrgUnits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    OrgUnitId = table.Column<int>(type: "int", nullable: false),
                    IsPrimary = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    StartAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    EndAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthUserOrgUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthUserOrgUnits_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AuthUserOrgUnits_SystemUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "SystemUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AuthRoleDataScopeNodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    RoleDataScopeId = table.Column<int>(type: "int", nullable: false),
                    OrgUnitId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthRoleDataScopeNodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthRoleDataScopeNodes_AuthRoleDataScopes_RoleDataScopeId",
                        column: x => x.RoleDataScopeId,
                        principalTable: "AuthRoleDataScopes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AuthRoleDataScopeNodes_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_SystemUsers_CompanyId_IsActive",
                table: "SystemUsers",
                columns: new[] { "CompanyId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_AuthPermissions_Code",
                table: "AuthPermissions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthPermissions_PermissionType_Resource_Action",
                table: "AuthPermissions",
                columns: new[] { "PermissionType", "Resource", "Action" });

            migrationBuilder.CreateIndex(
                name: "IX_AuthRoleDataScopeNodes_OrgUnitId",
                table: "AuthRoleDataScopeNodes",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_AuthRoleDataScopeNodes_RoleDataScopeId_OrgUnitId",
                table: "AuthRoleDataScopeNodes",
                columns: new[] { "RoleDataScopeId", "OrgUnitId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthRoleDataScopes_RoleId_Resource_ScopeType",
                table: "AuthRoleDataScopes",
                columns: new[] { "RoleId", "Resource", "ScopeType" });

            migrationBuilder.CreateIndex(
                name: "IX_AuthRolePermissions_PermissionId",
                table: "AuthRolePermissions",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_AuthRoles_CompanyId_Code",
                table: "AuthRoles",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthUserOrgUnits_OrgUnitId",
                table: "AuthUserOrgUnits",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_AuthUserOrgUnits_UserId_OrgUnitId_StartAt_EndAt",
                table: "AuthUserOrgUnits",
                columns: new[] { "UserId", "OrgUnitId", "StartAt", "EndAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuthUserRoles_RoleId",
                table: "AuthUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_AuthUserRoles_UserId_RoleId_StartAt_EndAt",
                table: "AuthUserRoles",
                columns: new[] { "UserId", "RoleId", "StartAt", "EndAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrgCompanies_Code",
                table: "OrgCompanies",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrgUnits_CompanyId_Code",
                table: "OrgUnits",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrgUnits_CompanyId_ParentId",
                table: "OrgUnits",
                columns: new[] { "CompanyId", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrgUnits_CompanyId_Path",
                table: "OrgUnits",
                columns: new[] { "CompanyId", "Path" });

            migrationBuilder.CreateIndex(
                name: "IX_OrgUnits_ParentId",
                table: "OrgUnits",
                column: "ParentId");

            migrationBuilder.Sql("""
                INSERT INTO `OrgCompanies` (`Code`, `Name`, `IsActive`, `CreatedAt`)
                SELECT 'default-company', '默认公司', 1, NOW()
                WHERE NOT EXISTS (
                    SELECT 1 FROM `OrgCompanies` WHERE `Code` = 'default-company'
                );
                """);

            migrationBuilder.Sql("""
                INSERT INTO `OrgUnits` (`CompanyId`, `ParentId`, `UnitType`, `Code`, `Name`, `Path`, `Depth`, `Sort`, `IsActive`, `CreatedAt`)
                SELECT c.`Id`, NULL, 0, 'ROOT', '公司', '/', 0, 0, 1, NOW()
                FROM `OrgCompanies` c
                WHERE c.`Code` = 'default-company'
                  AND NOT EXISTS (
                    SELECT 1
                    FROM `OrgUnits` o
                    WHERE o.`CompanyId` = c.`Id`
                      AND o.`UnitType` = 0
                      AND o.`ParentId` IS NULL
                  );
                """);

            migrationBuilder.Sql("""
                UPDATE `OrgUnits` o
                INNER JOIN `OrgCompanies` c ON c.`Id` = o.`CompanyId` AND c.`Code` = 'default-company'
                SET o.`Path` = CONCAT('/', o.`Id`, '/'),
                    o.`UpdatedAt` = NOW()
                WHERE o.`UnitType` = 0
                  AND o.`ParentId` IS NULL
                  AND (o.`Path` = '/' OR o.`Path` IS NULL OR o.`Path` = '');
                """);

            migrationBuilder.Sql("""
                UPDATE `SystemUsers` su
                INNER JOIN `OrgCompanies` c ON c.`Code` = 'default-company'
                SET su.`CompanyId` = c.`Id`
                WHERE su.`CompanyId` = 0;
                """);

            migrationBuilder.Sql("""
                INSERT INTO `AuthRoles` (`CompanyId`, `Code`, `Name`, `Description`, `IsBuiltIn`, `IsActive`, `CreatedAt`)
                SELECT c.`Id`, 'admin', '系统管理员', '迁移生成管理员角色', 1, 1, NOW()
                FROM `OrgCompanies` c
                WHERE c.`Code` = 'default-company'
                  AND NOT EXISTS (
                    SELECT 1 FROM `AuthRoles` r WHERE r.`CompanyId` = c.`Id` AND r.`Code` = 'admin'
                  );
                """);

            migrationBuilder.Sql("""
                INSERT INTO `AuthRoles` (`CompanyId`, `Code`, `Name`, `Description`, `IsBuiltIn`, `IsActive`, `CreatedAt`)
                SELECT c.`Id`, 'common', '普通用户', '迁移生成普通角色', 1, 1, NOW()
                FROM `OrgCompanies` c
                WHERE c.`Code` = 'default-company'
                  AND NOT EXISTS (
                    SELECT 1 FROM `AuthRoles` r WHERE r.`CompanyId` = c.`Id` AND r.`Code` = 'common'
                  );
                """);

            migrationBuilder.Sql("""
                INSERT INTO `AuthUserRoles` (`UserId`, `RoleId`, `StartAt`, `EndAt`, `CreatedAt`)
                SELECT su.`Id`, r.`Id`, NULL, NULL, NOW()
                FROM `SystemUsers` su
                INNER JOIN `AuthRoles` r
                    ON r.`CompanyId` = su.`CompanyId`
                   AND r.`Code` = CASE
                        WHEN su.`RolesJson` LIKE '%"admin"%' THEN 'admin'
                        ELSE 'common'
                    END
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM `AuthUserRoles` ur
                    WHERE ur.`UserId` = su.`Id`
                      AND ur.`RoleId` = r.`Id`
                );
                """);

            migrationBuilder.Sql("""
                INSERT INTO `AuthUserOrgUnits` (`UserId`, `OrgUnitId`, `IsPrimary`, `StartAt`, `EndAt`, `CreatedAt`)
                SELECT su.`Id`, ou.`Id`, 1, NULL, NULL, NOW()
                FROM `SystemUsers` su
                INNER JOIN `OrgUnits` ou
                    ON ou.`CompanyId` = su.`CompanyId`
                   AND ou.`UnitType` = 0
                   AND ou.`ParentId` IS NULL
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM `AuthUserOrgUnits` uo
                    WHERE uo.`UserId` = su.`Id`
                );
                """);

            migrationBuilder.DropColumn(
                name: "PermissionsJson",
                table: "SystemUsers");

            migrationBuilder.DropColumn(
                name: "RolesJson",
                table: "SystemUsers");

            migrationBuilder.AddForeignKey(
                name: "FK_SystemUsers_OrgCompanies_CompanyId",
                table: "SystemUsers",
                column: "CompanyId",
                principalTable: "OrgCompanies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SystemUsers_OrgCompanies_CompanyId",
                table: "SystemUsers");

            migrationBuilder.DropTable(
                name: "AuthRoleDataScopeNodes");

            migrationBuilder.DropTable(
                name: "AuthRolePermissions");

            migrationBuilder.DropTable(
                name: "AuthUserOrgUnits");

            migrationBuilder.DropTable(
                name: "AuthUserRoles");

            migrationBuilder.DropTable(
                name: "AuthRoleDataScopes");

            migrationBuilder.DropTable(
                name: "AuthPermissions");

            migrationBuilder.DropTable(
                name: "OrgUnits");

            migrationBuilder.DropTable(
                name: "AuthRoles");

            migrationBuilder.DropTable(
                name: "OrgCompanies");

            migrationBuilder.DropIndex(
                name: "IX_SystemUsers_CompanyId_IsActive",
                table: "SystemUsers");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "SystemUsers");

            migrationBuilder.DropColumn(
                name: "PermissionVersion",
                table: "SystemUsers");

            migrationBuilder.AddColumn<string>(
                name: "PermissionsJson",
                table: "SystemUsers",
                type: "varchar(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "RolesJson",
                table: "SystemUsers",
                type: "varchar(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_SystemUsers_IsActive",
                table: "SystemUsers",
                column: "IsActive");
        }
    }
}
