using AcceptanceSpecSystem.Data.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcceptanceSpecSystem.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260719170000_BackfillDocumentTemplateRegions")]
public sealed class BackfillDocumentTemplateRegions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            INSERT INTO `DocumentTemplateRegions`
                (`DocumentTemplateId`, `RegionIndex`, `HeadersJson`, `HeaderRowIndex`,
                 `HeaderRowCount`, `DataStartRowIndex`, `DataEndRowIndex`,
                 `ProjectColumnIndex`, `SpecificationColumnIndex`, `AcceptanceColumnIndex`,
                 `RemarkColumnIndex`, `IsSpecificationOnly`)
            SELECT
                template.`Id`, 0, template.`HeadersJson`, template.`HeaderRowIndex`,
                template.`HeaderRowCount`, template.`DataStartRowIndex`, template.`DataEndRowIndex`,
                template.`ProjectColumnIndex`, template.`SpecificationColumnIndex`,
                template.`AcceptanceColumnIndex`, template.`RemarkColumnIndex`,
                template.`IsSpecificationOnly`
            FROM `DocumentTemplates` AS template
            WHERE NOT EXISTS (
                SELECT 1
                FROM `DocumentTemplateRegions` AS region
                WHERE region.`DocumentTemplateId` = template.`Id`
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // 数据回填无法在不误删用户后续修改的区域配置时安全反向执行。
    }
}
