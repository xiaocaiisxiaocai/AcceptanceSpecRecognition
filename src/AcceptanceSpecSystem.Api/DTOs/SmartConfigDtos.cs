using System.ComponentModel.DataAnnotations;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Api.DTOs;

public sealed class SmartConfigConfirmRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "FileId 必须为正整数")]
    public int? FileId { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "TableIndex 不能为负数")]
    public int TableIndex { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "CustomerId 必须为正整数")]
    public int CustomerId { get; set; }

    public string? TemplateName { get; set; }

    [Required]
    public List<string> Headers { get; set; } = [];

    [Range(0, int.MaxValue, ErrorMessage = "项目列索引超出表头范围")]
    public int? ProjectColumnIndex { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "规格列索引超出表头范围")]
    public int SpecificationColumnIndex { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "验收列索引超出表头范围")]
    public int? AcceptanceColumnIndex { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "备注列索引超出表头范围")]
    public int? RemarkColumnIndex { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "HeaderRowIndex 不能为负数")]
    public int HeaderRowIndex { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "表头行数必须大于0")]
    public int HeaderRowCount { get; set; } = 1;

    [Range(0, int.MaxValue, ErrorMessage = "DataStartRowIndex 不能为负数")]
    public int DataStartRowIndex { get; set; } = 1;

    [Range(0, int.MaxValue, ErrorMessage = "DataEndRowIndex 不能为负数")]
    public int? DataEndRowIndex { get; set; }

    public bool IsSpecificationOnly { get; set; }

    public string? TableKind { get; set; }

    public string? Recommendation { get; set; }

    public bool UserModifiedStructure { get; set; }

    [Required]
    public List<SmartConfigLearnedColumnRequest> LearnedColumns { get; set; } = [];

    [Required]
    public List<SmartConfigConfirmRegionRequest> Regions { get; set; } = [];
}

public sealed class SmartConfigConfirmRegionRequest
{
    public string? RegionId { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "RegionIndex 不能为负数")]
    public int RegionIndex { get; set; }

    [Required]
    public List<string> Headers { get; set; } = [];

    [Range(0, int.MaxValue, ErrorMessage = "项目列索引超出表头范围")]
    public int? ProjectColumnIndex { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "规格列索引超出表头范围")]
    public int SpecificationColumnIndex { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "验收列索引超出表头范围")]
    public int? AcceptanceColumnIndex { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "备注列索引超出表头范围")]
    public int? RemarkColumnIndex { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "HeaderRowIndex 不能为负数")]
    public int HeaderRowIndex { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "表头行数必须大于0")]
    public int HeaderRowCount { get; set; } = 1;

    [Range(0, int.MaxValue, ErrorMessage = "DataStartRowIndex 不能为负数")]
    public int DataStartRowIndex { get; set; } = 1;

    [Range(0, int.MaxValue, ErrorMessage = "DataEndRowIndex 不能为负数")]
    public int? DataEndRowIndex { get; set; }

    public bool IsSpecificationOnly { get; set; }
}
public sealed class SmartConfigRecognizeRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "FileId 必须为正整数")]
    public int FileId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "CustomerId 必须为正整数")]
    public int? CustomerId { get; set; }

    public bool EnableLlmAssistance { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "LlmServiceId 必须为正整数")]
    public int? LlmServiceId { get; set; }
}

public sealed class SmartConfigLearnedColumnRequest
{
    [Required]
    public string Header { get; set; } = string.Empty;

    public ColumnMappingTargetField TargetField { get; set; }
}
