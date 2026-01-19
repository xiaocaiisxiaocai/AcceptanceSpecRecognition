using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Api.DTOs;

public class TextProcessingConfigDto
{
    public int Id { get; set; }
    public bool EnableChineseConversion { get; set; }
    public ChineseConversionMode ConversionMode { get; set; }
    public bool EnableSynonym { get; set; }
    public bool EnableOkNgConversion { get; set; }
    public string OkStandardFormat { get; set; } = "OK";
    public string NgStandardFormat { get; set; } = "NG";
    public bool EnableKeywordHighlight { get; set; }
    public string HighlightColorHex { get; set; } = "#FFFF00";
    public DateTime UpdatedAt { get; set; }
}

public class UpdateTextProcessingConfigRequest
{
    public bool EnableChineseConversion { get; set; }
    public ChineseConversionMode ConversionMode { get; set; }
    public bool EnableSynonym { get; set; }
    public bool EnableOkNgConversion { get; set; }
    public string OkStandardFormat { get; set; } = "OK";
    public string NgStandardFormat { get; set; } = "NG";
    public bool EnableKeywordHighlight { get; set; }
    public string HighlightColorHex { get; set; } = "#FFFF00";
}

public class SynonymGroupDto
{
    public int Id { get; set; }
    public List<string> Words { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class UpsertSynonymGroupRequest
{
    /// <summary>
    /// 词语列表（第一个视为标准词）
    /// </summary>
    public List<string> Words { get; set; } = [];
}

public class KeywordDto
{
    public int Id { get; set; }
    public string Word { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class CreateKeywordRequest
{
    public string Word { get; set; } = string.Empty;
}

public class UpdateKeywordRequest
{
    public string Word { get; set; } = string.Empty;
}

public class BatchAddKeywordsRequest
{
    public List<string> Words { get; set; } = [];
}

