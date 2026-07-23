using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Api.DTOs;

public sealed class SmartConfigConfirmRequest
{
    public int? FileId { get; set; }

    public int TableIndex { get; set; }

    public int CustomerId { get; set; }

    public string? TemplateName { get; set; }

    public List<string> Headers { get; set; } = [];

    public int? ProjectColumnIndex { get; set; }

    public int SpecificationColumnIndex { get; set; }

    public int? AcceptanceColumnIndex { get; set; }

    public int? RemarkColumnIndex { get; set; }

    public int HeaderRowIndex { get; set; }

    public int HeaderRowCount { get; set; } = 1;

    public int DataStartRowIndex { get; set; } = 1;

    public int? DataEndRowIndex { get; set; }

    public bool IsSpecificationOnly { get; set; }

    public string? TableKind { get; set; }

    public string? Recommendation { get; set; }

    public bool UserModifiedStructure { get; set; }

    public List<SmartConfigLearnedColumnRequest> LearnedColumns { get; set; } = [];

    public List<SmartConfigConfirmRegionRequest> Regions { get; set; } = [];
}

public sealed class SmartConfigConfirmRegionRequest
{
    public string? RegionId { get; set; }
    public int RegionIndex { get; set; }
    public List<string> Headers { get; set; } = [];
    public int? ProjectColumnIndex { get; set; }
    public int SpecificationColumnIndex { get; set; }
    public int? AcceptanceColumnIndex { get; set; }
    public int? RemarkColumnIndex { get; set; }
    public int HeaderRowIndex { get; set; }
    public int HeaderRowCount { get; set; } = 1;
    public int DataStartRowIndex { get; set; } = 1;
    public int? DataEndRowIndex { get; set; }
    public bool IsSpecificationOnly { get; set; }
}
public sealed class SmartConfigRecognizeRequest
{
    public int FileId { get; set; }

    public int? CustomerId { get; set; }

    public bool EnableLlmAssistance { get; set; }

    public int? LlmServiceId { get; set; }
}

public sealed class SmartConfigLearnedColumnRequest
{
    public string Header { get; set; } = string.Empty;

    public ColumnMappingTargetField TargetField { get; set; }
}
