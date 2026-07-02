namespace AcceptanceSpecSystem.Application.Services;

public sealed class SmartConfigurationRecognizeCommand
{
    public int FileId { get; init; }

    public int? CustomerId { get; init; }
}

public sealed class SmartConfigurationRecognizeResult
{
    public int FileId { get; init; }

    public List<SmartConfigurationRecognizedTable> Tables { get; init; } = [];
}

public sealed class SmartConfigurationRecognizedTable
{
    public int TableIndex { get; init; }

    public string? TableName { get; init; }

    public List<string> Headers { get; init; } = [];

    public int HeaderRowIndex { get; init; }

    public int HeaderRowCount { get; init; } = 1;

    public int DataStartRowIndex { get; init; } = 1;

    public int? DataEndRowIndex { get; init; }

    public int? ProjectColumnIndex { get; init; }

    public int? SpecificationColumnIndex { get; init; }

    public int? AcceptanceColumnIndex { get; init; }

    public int? RemarkColumnIndex { get; init; }

    public bool IsSpecificationOnly { get; init; }

    public double Confidence { get; init; }

    public string Source { get; init; } = string.Empty;

    public string Decision { get; init; } = string.Empty;

    public List<SmartConfigurationRecognizedField> Fields { get; init; } = [];
}

public sealed class SmartConfigurationRecognizedField
{
    public string Field { get; init; } = string.Empty;

    public int? ColumnIndex { get; init; }

    public string? Header { get; init; }

    public double Confidence { get; init; }

    public string Source { get; init; } = string.Empty;
}
