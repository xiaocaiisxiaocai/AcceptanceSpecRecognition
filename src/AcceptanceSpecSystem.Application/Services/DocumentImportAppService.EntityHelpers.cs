using System.Text;
using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Application.Services;

public sealed partial class DocumentImportAppService
{
    private static void OverwriteAcceptanceSpec(
        AcceptanceSpec existingSpec,
        int customerId,
        int? processId,
        int? machineModelId,
        int wordFileId,
        string? project,
        string? specification,
        string? acceptance,
        string? remark)
    {
        existingSpec.CustomerId = customerId;
        existingSpec.ProcessId = processId;
        existingSpec.MachineModelId = machineModelId;
        existingSpec.Project = project?.Trim() ?? string.Empty;
        existingSpec.Specification = specification?.Trim() ?? string.Empty;
        existingSpec.Acceptance = NormalizeNullable(acceptance);
        existingSpec.Remark = NormalizeNullable(remark);
        existingSpec.WordFileId = wordFileId;
        existingSpec.ImportedAt = DateTime.UtcNow;
    }

    private static void OverwriteAcceptanceAndRemark(
        AcceptanceSpec existingSpec,
        int customerId,
        int? processId,
        int? machineModelId,
        int wordFileId,
        string? acceptance,
        string? remark)
    {
        existingSpec.CustomerId = customerId;
        existingSpec.ProcessId = processId;
        existingSpec.MachineModelId = machineModelId;
        existingSpec.Acceptance = NormalizeNullable(acceptance);
        existingSpec.Remark = NormalizeNullable(remark);
        existingSpec.WordFileId = wordFileId;
        existingSpec.ImportedAt = DateTime.UtcNow;
    }

    private static AcceptanceSpec CreateAcceptanceSpec(
        int customerId,
        int? processId,
        int? machineModelId,
        int wordFileId,
        string? project,
        string? specification,
        string? acceptance,
        string? remark,
        int createdByUserId,
        int? ownerOrgUnitId)
    {
        return new AcceptanceSpec
        {
            CustomerId = customerId,
            ProcessId = processId,
            MachineModelId = machineModelId,
            Project = project?.Trim() ?? string.Empty,
            Specification = specification?.Trim() ?? string.Empty,
            Acceptance = NormalizeNullable(acceptance),
            Remark = NormalizeNullable(remark),
            CreatedByUserId = createdByUserId,
            OwnerOrgUnitId = ownerOrgUnitId,
            WordFileId = wordFileId,
            ImportedAt = DateTime.UtcNow
        };
    }

    private static string NormalizeText(string? value)
    {
        return (value ?? string.Empty).Trim();
    }

    private static string? NormalizeNullable(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool IsSameContent(
        AcceptanceSpec spec,
        string project,
        string specification,
        string acceptance,
        string remark)
    {
        return NormalizeText(spec.Project) == project &&
               NormalizeText(spec.Specification) == specification &&
               NormalizeText(spec.Acceptance) == acceptance &&
               NormalizeText(spec.Remark) == remark;
    }

    private static string? GetCellValue(RowData row, int columnIndex)
    {
        return row.GetValue(columnIndex);
    }

    private static List<string> GetRowValues(RowData row)
    {
        if (row.Cells == null || row.Cells.Count == 0)
        {
            return [];
        }

        var maxColumnIndex = row.Cells.Max(cell => cell.ColumnIndex);
        var valuesByColumn = row.Cells
            .GroupBy(cell => cell.ColumnIndex)
            .ToDictionary(group => group.Key, group => group.FirstOrDefault()?.Value ?? string.Empty);

        var values = new List<string>(maxColumnIndex + 1);
        for (var col = 0; col <= maxColumnIndex; col++)
        {
            values.Add(valuesByColumn.TryGetValue(col, out var value) ? value : string.Empty);
        }

        return values;
    }
}
