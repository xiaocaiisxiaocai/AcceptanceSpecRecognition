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

    public static PagedData<AcceptanceSpecListItemDto> ToDto(this PagedResult<AcceptanceSpecSummary> data)
    {
        return new PagedData<AcceptanceSpecListItemDto>
        {
            Items = data.Items.Select(item => item.ToListItemDto()).ToList(),
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
        var dto = new AcceptanceSpecDto();
        MapCommonFields(item, dto);
        return dto;
    }

    private static AcceptanceSpecListItemDto ToListItemDto(this AcceptanceSpecSummary item)
    {
        var dto = new AcceptanceSpecListItemDto
        {
            LastReferencedAtUtc = item.LastReferencedAtUtc
        };
        MapCommonFields(item, dto);
        return dto;
    }

    private static void MapCommonFields(AcceptanceSpecSummary item, AcceptanceSpecDto dto)
    {
        dto.Id = item.Id;
        dto.CustomerId = item.CustomerId;
        dto.ProcessId = item.ProcessId;
        dto.MachineModelId = item.MachineModelId;
        dto.ProcessName = item.ProcessName;
        dto.MachineModelName = item.MachineModelName;
        dto.CustomerName = item.CustomerName;
        dto.Project = item.Project;
        dto.Specification = item.Specification;
        dto.Acceptance = item.Acceptance;
        dto.Remark = item.Remark;
        dto.ReferenceCount = item.ReferenceCount;
        dto.ReferenceVersion = item.ReferenceVersion;
        dto.ImportedAt = item.ImportedAt;
        dto.UpdatedAt = item.UpdatedAt;
        dto.OwnerOrgUnitId = item.OwnerOrgUnitId;
        dto.CreatedByUserId = item.CreatedByUserId;
    }

    public static AcceptanceSpecReferenceHistoryDto ToDto(
        this AcceptanceSpecReferenceHistoryModel model)
    {
        return new AcceptanceSpecReferenceHistoryDto
        {
            SpecId = model.SpecId,
            CurrentReferenceVersion = model.CurrentReferenceVersion,
            CurrentReferenceCount = model.CurrentReferenceCount,
            RecordedReferenceCount = model.RecordedReferenceCount,
            UntrackedReferenceCount = model.UntrackedReferenceCount,
            IncludePreviousVersions = model.IncludePreviousVersions,
            Sort = model.Sort,
            Items = model.Items.Select(item => new AcceptanceSpecReferenceHistoryItemDto
            {
                Id = item.Id,
                ReferenceOrdinal = item.ReferenceOrdinal,
                ReferenceVersion = item.ReferenceVersion,
                IsCurrentVersion = item.IsCurrentVersion,
                ReferencedAtUtc = item.ReferencedAtUtc
            }).ToList(),
            Total = model.Total,
            Page = model.Page,
            PageSize = model.PageSize
        };
    }

    public static AcceptanceSpecContentVersionHistoryDto ToDto(
        this AcceptanceSpecContentVersionHistoryModel model)
    {
        return new AcceptanceSpecContentVersionHistoryDto
        {
            SpecId = model.SpecId,
            CurrentVersion = model.CurrentVersion,
            EarliestAvailableVersion = model.EarliestAvailableVersion,
            HasUnavailableEarlierVersions = model.HasUnavailableEarlierVersions,
            Sort = model.Sort,
            Items = model.Items.Select(ToDto).ToList(),
            Total = model.Total,
            Page = model.Page,
            PageSize = model.PageSize
        };
    }

    public static AcceptanceSpecContentVersionDetailDto ToDto(
        this AcceptanceSpecContentVersionDetailModel model)
    {
        var dto = new AcceptanceSpecContentVersionDetailDto
        {
            SpecId = model.SpecId,
            Project = model.Project,
            Specification = model.Specification,
            Acceptance = model.Acceptance,
            Remark = model.Remark
        };
        MapContentVersionFields(model, dto);
        return dto;
    }

    public static AcceptanceSpecContentVersionDiffDto ToDto(
        this AcceptanceSpecContentVersionDiffModel model)
    {
        return new AcceptanceSpecContentVersionDiffDto
        {
            SpecId = model.SpecId,
            FromVersion = model.FromVersion,
            ToVersion = model.ToVersion,
            Fields = model.Fields.ToDictionary(
                pair => pair.Key,
                pair => new AcceptanceSpecContentFieldDiffDto
                {
                    Before = pair.Value.Before,
                    After = pair.Value.After,
                    Changed = pair.Value.Changed
                })
        };
    }

    private static AcceptanceSpecContentVersionItemDto ToDto(
        AcceptanceSpecContentVersionItemModel model)
    {
        var dto = new AcceptanceSpecContentVersionItemDto();
        MapContentVersionFields(model, dto);
        return dto;
    }

    private static void MapContentVersionFields(
        AcceptanceSpecContentVersionItemModel model,
        AcceptanceSpecContentVersionItemDto dto)
    {
        dto.Version = model.Version;
        dto.ChangedAtUtc = model.ChangedAtUtc;
        dto.ChangedByUserId = model.ChangedByUserId;
        dto.ChangedByNameSnapshot = model.ChangedByNameSnapshot;
        dto.ChangeSource = model.ChangeSource;
        dto.ChangeReason = model.ChangeReason;
        dto.RestoredFromVersion = model.RestoredFromVersion;
        dto.IsMigrationBaseline = model.IsMigrationBaseline;
        dto.ChangedFields = model.ChangedFields;
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
            SamplePage = item.SamplePage,
            SamplePageSize = item.SamplePageSize,
            SampleTotal = item.SampleTotal,
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
