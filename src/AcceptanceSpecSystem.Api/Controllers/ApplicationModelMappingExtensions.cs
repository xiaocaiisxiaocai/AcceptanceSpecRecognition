using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Application.Models;

namespace AcceptanceSpecSystem.Api.Controllers;

internal static class ApplicationModelMappingExtensions
{
    public static PagedData<CustomerDto> ToDto(this PagedResult<CustomerSummary> data)
    {
        return new PagedData<CustomerDto>
        {
            Items = data.Items.Select(item => item.ToDto()).ToList(),
            Total = data.Total,
            Page = data.Page,
            PageSize = data.PageSize
        };
    }

    public static PagedData<ProcessDto> ToDto(this PagedResult<ProcessSummary> data)
    {
        return new PagedData<ProcessDto>
        {
            Items = data.Items.Select(item => item.ToDto()).ToList(),
            Total = data.Total,
            Page = data.Page,
            PageSize = data.PageSize
        };
    }

    public static PagedData<AcceptanceSpecDto> ToDto(this PagedResult<AcceptanceSpecSummary> data)
    {
        return new PagedData<AcceptanceSpecDto>
        {
            Items = data.Items.Select(item => item.ToDto()).ToList(),
            Total = data.Total,
            Page = data.Page,
            PageSize = data.PageSize
        };
    }

    public static CustomerDto ToDto(this CustomerSummary item)
    {
        return new CustomerDto
        {
            Id = item.Id,
            Name = item.Name,
            CreatedAt = item.CreatedAt,
            ProcessCount = item.ProcessCount,
            SpecCount = item.SpecCount
        };
    }

    public static ProcessDto ToDto(this ProcessSummary item)
    {
        return new ProcessDto
        {
            Id = item.Id,
            Name = item.Name,
            CreatedAt = item.CreatedAt,
            SpecCount = item.SpecCount
        };
    }

    public static MachineModelDto ToDto(this MachineModelSummary item)
    {
        return new MachineModelDto
        {
            Id = item.Id,
            Name = item.Name,
            CreatedAt = item.CreatedAt,
            SpecCount = item.SpecCount
        };
    }

    public static AcceptanceSpecDto ToDto(this AcceptanceSpecSummary item)
    {
        return new AcceptanceSpecDto
        {
            Id = item.Id,
            CustomerId = item.CustomerId,
            ProcessId = item.ProcessId,
            MachineModelId = item.MachineModelId,
            ProcessName = item.ProcessName,
            MachineModelName = item.MachineModelName,
            CustomerName = item.CustomerName,
            Project = item.Project,
            Specification = item.Specification,
            Acceptance = item.Acceptance,
            Remark = item.Remark,
            ImportedAt = item.ImportedAt,
            UpdatedAt = item.UpdatedAt,
            OwnerOrgUnitId = item.OwnerOrgUnitId,
            CreatedByUserId = item.CreatedByUserId
        };
    }

    public static SpecGroupDto ToDto(this SpecGroupSummary item)
    {
        return new SpecGroupDto
        {
            CustomerId = item.CustomerId,
            CustomerName = item.CustomerName,
            MachineModelId = item.MachineModelId,
            MachineModelName = item.MachineModelName,
            ProcessId = item.ProcessId,
            ProcessName = item.ProcessName,
            SpecCount = item.SpecCount
        };
    }

    public static SpecDuplicateDetectionResultDto ToDto(this SpecDuplicateDetectionResultModel item)
    {
        return new SpecDuplicateDetectionResultDto
        {
            ScannedCount = item.ScannedCount,
            ExactGroupCount = item.ExactGroupCount,
            SimilarGroupCount = item.SimilarGroupCount,
            ExactGroups = item.ExactGroups.Select(group => group.ToDto()).ToList(),
            SimilarGroups = item.SimilarGroups.Select(group => group.ToDto()).ToList()
        };
    }

    public static BatchImportResult ToDto(this BatchImportResultModel item)
    {
        return new BatchImportResult
        {
            SuccessCount = item.SuccessCount,
            FailedCount = item.FailedCount,
            TotalCount = item.TotalCount
        };
    }

    public static SpecRemarkReplacePreviewResponse ToDto(this SpecRemarkReplacePreviewModel item)
    {
        return new SpecRemarkReplacePreviewResponse
        {
            AffectedSpecCount = item.AffectedSpecCount,
            MatchCount = item.MatchCount,
            ConfirmationToken = item.ConfirmationToken,
            Samples = item.Samples.Select(sample => new SpecRemarkReplaceSampleDto
            {
                SpecId = sample.SpecId,
                Project = sample.Project,
                BeforePreview = sample.BeforePreview,
                AfterPreview = sample.AfterPreview
            }).ToList()
        };
    }

    public static SpecRemarkReplaceResult ToDto(this SpecRemarkReplaceResultModel item)
    {
        return new SpecRemarkReplaceResult
        {
            UpdatedSpecCount = item.UpdatedSpecCount,
            ReplacedMatchCount = item.ReplacedMatchCount
        };
    }

    public static BatchImportSpecItemInput ToInput(this SpecImportItem item)
    {
        return new BatchImportSpecItemInput
        {
            Project = item.Project,
            Specification = item.Specification,
            Acceptance = item.Acceptance,
            Remark = item.Remark
        };
    }

    private static SpecDuplicateGroupDto ToDto(this SpecDuplicateGroupModel item)
    {
        return new SpecDuplicateGroupDto
        {
            GroupType = item.GroupType,
            Project = item.Project,
            SpecificationPreview = item.SpecificationPreview,
            Reason = item.Reason,
            SimilarityScore = item.SimilarityScore,
            ItemCount = item.ItemCount,
            Items = item.Items.Select(detail => detail.ToDto()).ToList()
        };
    }

    private static SpecDuplicateItemDto ToDto(this SpecDuplicateItemModel item)
    {
        return new SpecDuplicateItemDto
        {
            Id = item.Id,
            Project = item.Project,
            Specification = item.Specification,
            Acceptance = item.Acceptance,
            Remark = item.Remark,
            ImportedAt = item.ImportedAt
        };
    }
}
