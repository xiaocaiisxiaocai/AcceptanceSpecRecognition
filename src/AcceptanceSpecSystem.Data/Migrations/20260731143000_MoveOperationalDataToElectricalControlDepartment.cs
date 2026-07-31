using AcceptanceSpecSystem.Data.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceSpecSystem.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260731143000_MoveOperationalDataToElectricalControlDepartment")]
public sealed class MoveOperationalDataToElectricalControlDepartment : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (!migrationBuilder.ActiveProvider.Contains("MySql", StringComparison.OrdinalIgnoreCase))
            return;

        migrationBuilder.Sql(
            """
            SET @company_id = (
                SELECT `Id`
                FROM `OrgCompanies`
                WHERE `Code` = 'default-company'
                ORDER BY `Id`
                LIMIT 1
            );

            SET @root_org_id = (
                SELECT `Id`
                FROM `OrgUnits`
                WHERE `CompanyId` = @company_id
                  AND `ParentId` IS NULL
                  AND `UnitType` = 0
                ORDER BY `Id`
                LIMIT 1
            );

            INSERT INTO `OrgUnits`
                (`CompanyId`, `ParentId`, `UnitType`, `Code`, `Name`, `Path`,
                 `Depth`, `Sort`, `IsActive`, `CreatedAt`, `UpdatedAt`)
            SELECT
                @company_id,
                @root_org_id,
                2,
                'ELECTRICAL_CONTROL',
                '电控工程部',
                CONCAT(rootOrg.`Path`, 'electrical-control/'),
                rootOrg.`Depth` + 1,
                10,
                1,
                UTC_TIMESTAMP(6),
                UTC_TIMESTAMP(6)
            FROM `OrgUnits` AS rootOrg
            WHERE rootOrg.`Id` = @root_org_id
              AND NOT EXISTS (
                  SELECT 1
                  FROM `OrgUnits` AS existingOrg
                  WHERE existingOrg.`CompanyId` = @company_id
                    AND existingOrg.`ParentId` = @root_org_id
                    AND (
                        existingOrg.`Code` = 'ELECTRICAL_CONTROL'
                        OR (
                            existingOrg.`UnitType` = 2
                            AND existingOrg.`Name` = '电控工程部'
                        )
                    )
              );

            SET @department_id = (
                SELECT `Id`
                FROM `OrgUnits`
                WHERE `CompanyId` = @company_id
                  AND `ParentId` = @root_org_id
                  AND (
                      `Code` = 'ELECTRICAL_CONTROL'
                      OR (`UnitType` = 2 AND `Name` = '电控工程部')
                  )
                ORDER BY CASE WHEN `Code` = 'ELECTRICAL_CONTROL' THEN 0 ELSE 1 END, `Id`
                LIMIT 1
            );

            UPDATE `OrgUnits` AS department
            INNER JOIN `OrgUnits` AS rootOrg ON rootOrg.`Id` = @root_org_id
            SET department.`UnitType` = 2,
                department.`Code` = 'ELECTRICAL_CONTROL',
                department.`Name` = '电控工程部',
                department.`Path` = CONCAT(rootOrg.`Path`, department.`Id`, '/'),
                department.`Depth` = rootOrg.`Depth` + 1,
                department.`IsActive` = 1,
                department.`UpdatedAt` = UTC_TIMESTAMP(6)
            WHERE department.`Id` = @department_id;

            UPDATE `AuthUserOrgUnits` AS userOrg
            INNER JOIN `SystemUsers` AS systemUser ON systemUser.`Id` = userOrg.`UserId`
            SET userOrg.`OrgUnitId` = @department_id,
                userOrg.`IsPrimary` = 1
            WHERE userOrg.`OrgUnitId` = @root_org_id
              AND systemUser.`CompanyId` = @company_id
              AND NOT EXISTS (
                  SELECT 1
                  FROM `AuthUserRoles` AS userRole
                  INNER JOIN `AuthRoles` AS role ON role.`Id` = userRole.`RoleId`
                  WHERE userRole.`UserId` = systemUser.`Id`
                    AND role.`Code` = 'admin'
                    AND (userRole.`StartAt` IS NULL OR userRole.`StartAt` <= UTC_TIMESTAMP(6))
                    AND (userRole.`EndAt` IS NULL OR userRole.`EndAt` > UTC_TIMESTAMP(6))
              );

            UPDATE `SystemUsers` AS systemUser
            SET systemUser.`PermissionVersion` = systemUser.`PermissionVersion` + 1,
                systemUser.`UpdatedAt` = UTC_TIMESTAMP(6)
            WHERE systemUser.`CompanyId` = @company_id
              AND EXISTS (
                  SELECT 1
                  FROM `AuthUserOrgUnits` AS userOrg
                  WHERE userOrg.`UserId` = systemUser.`Id`
                    AND userOrg.`OrgUnitId` = @department_id
              );

            UPDATE `AcceptanceSpecs`
            SET `OwnerOrgUnitId` = @department_id
            WHERE `OwnerOrgUnitId` = @root_org_id;

            UPDATE `WordFiles`
            SET `OwnerOrgUnitId` = @department_id
            WHERE `OwnerOrgUnitId` = @root_org_id;

            UPDATE `ExecutionHistoryRecords`
            SET `OwnerOrgUnitId` = @department_id
            WHERE `CompanyId` = @company_id
              AND (`OwnerOrgUnitId` IS NULL OR `OwnerOrgUnitId` = @root_org_id);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // 业务归属迁移不可可靠逆推原始组织，回滚应从执行前的已验证备份恢复。
    }
}
