namespace AcceptanceSpecRecognition.Core.Models;

/// <summary>
/// 预处理后的文本
/// </summary>
public class PreprocessedText
{
    public string Original { get; set; } = string.Empty;
    public string Normalized { get; set; } = string.Empty;
    public List<Correction> Corrections { get; set; } = new();
}

/// <summary>
/// 修正记录
/// </summary>
public class Correction
{
    public string Type { get; set; } = string.Empty;
    public string Original { get; set; } = string.Empty;
    public string Corrected { get; set; } = string.Empty;
    public int Position { get; set; }
}

/// <summary>
/// 标准化后的单位文本
/// </summary>
public class NormalizedUnitText
{
    public string Text { get; set; } = string.Empty;
    public List<UnitInfo> ExtractedUnits { get; set; } = new();
}

/// <summary>
/// 单位信息
/// </summary>
public class UnitInfo
{
    public double Value { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string StandardUnit { get; set; } = string.Empty;
    public string? Prefix { get; set; }
}

/// <summary>
/// 错别字映射表
/// </summary>
public class TypoCorrections
{
    public string Version { get; set; } = "1.0";
    public Dictionary<string, string> Corrections { get; set; } = new();
}

/// <summary>
/// 单位映射表
/// </summary>
public class UnitMappings
{
    public string Version { get; set; } = "1.0";
    public string Description { get; set; } = "系统内置单位映射表，无需用户维护";
    public Dictionary<string, UnitDefinition> Units { get; set; } = new();
    public Dictionary<string, List<string>> ElectricalPrefixes { get; set; } = new();
}

/// <summary>
/// 单位定义
/// </summary>
public class UnitDefinition
{
    public string Standard { get; set; } = string.Empty;
    public List<string> Aliases { get; set; } = new();
    public Dictionary<string, double>? Prefixes { get; set; }
    public Dictionary<string, double>? Conversions { get; set; }
}


/// <summary>
/// 错别字修正库
/// </summary>
public class TypoCorrectionLibrary
{
    public List<TypoCorrection> Corrections { get; set; } = new();
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// 错别字修正项
/// </summary>
public class TypoCorrection
{
    public string Wrong { get; set; } = string.Empty;
    public string Correct { get; set; } = string.Empty;
}

/// <summary>
/// 单位映射库
/// </summary>
public class UnitMappingLibrary
{
    public List<UnitMapping> Mappings { get; set; } = new();
    public List<ElectricalPrefix> ElectricalPrefixes { get; set; } = new();
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// 单位映射
/// </summary>
public class UnitMapping
{
    public string Category { get; set; } = string.Empty;
    public string StandardUnit { get; set; } = string.Empty;
    public List<string> Variants { get; set; } = new();
}

/// <summary>
/// 电气前缀
/// </summary>
public class ElectricalPrefix
{
    public string Prefix { get; set; } = string.Empty;
    public List<string> Variants { get; set; } = new();
}
