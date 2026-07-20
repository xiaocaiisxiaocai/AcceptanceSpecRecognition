using AcceptanceSpecSystem.Core.Documents.Models;

namespace AcceptanceSpecSystem.Application.Services;

public sealed partial class DocumentImportAppService
{
    private static void ValidateSpecificationOnlyProjectBackfill(
        bool isSpecificationOnly,
        int? projectColumn,
        int? specificationColumn,
        int projectColumnBase)
    {
        if (!specificationColumn.HasValue)
        {
            throw new ApplicationServiceException(400, "规格内容列为必填");
        }

        if (projectColumn.HasValue && projectColumn.Value < projectColumnBase)
        {
            throw new ApplicationServiceException(400, "项目列配置不合法");
        }

        if (!projectColumn.HasValue && !isSpecificationOnly)
        {
            throw new ApplicationServiceException(400, "缺少项目列；如确认该表为仅规格导入，请先启用仅规格确认后再导入");
        }
    }

    private static void ValidateSpecificationOnlyColumnHealth(
        TableData tableData,
        int? projectColumn,
        int specificationColumn,
        bool isSpecificationOnly)
    {
        if (!ShouldBackfillProjectFromSpecification(isSpecificationOnly, projectColumn))
        {
            return;
        }

        var hasSpecificationValue = tableData.Rows.Any(row =>
            !string.IsNullOrWhiteSpace(GetCellValue(row, specificationColumn)));
        if (!hasSpecificationValue)
        {
            throw new ApplicationServiceException(400, "仅规格导入要求规格列存在有效数据");
        }
    }

    private static string? ResolveImportProjectValue(
        RowData row,
        int? projectColumn,
        int specificationColumn,
        bool isSpecificationOnly)
    {
        if (projectColumn.HasValue)
        {
            return GetCellValue(row, projectColumn.Value);
        }

        return ShouldBackfillProjectFromSpecification(isSpecificationOnly, projectColumn)
            ? GetCellValue(row, specificationColumn)
            : null;
    }

    private static void MarkSpecificationOnlyBackfill(
        DocumentImportAppResult result,
        bool isSpecificationOnly,
        int? projectColumn)
    {
        if (ShouldBackfillProjectFromSpecification(isSpecificationOnly, projectColumn))
        {
            result.Result.ProjectBackfilledFromSpecification = true;
        }
    }

    private static bool ShouldBackfillProjectFromSpecification(
        bool isSpecificationOnly,
        int? projectColumn)
    {
        return isSpecificationOnly && !projectColumn.HasValue;
    }
}
