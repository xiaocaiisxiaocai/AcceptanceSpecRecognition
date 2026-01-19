namespace AcceptanceSpecSystem.Data.Entities;

/// <summary>
/// 文本处理配置实体（单例模式，仅一条记录）
/// </summary>
public class TextProcessingConfig
{
    /// <summary>
    /// 配置ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 是否启用简繁转换
    /// </summary>
    public bool EnableChineseConversion { get; set; }

    /// <summary>
    /// 简繁转换模式
    /// </summary>
    public ChineseConversionMode ConversionMode { get; set; }

    /// <summary>
    /// 是否启用同义词替换
    /// </summary>
    public bool EnableSynonym { get; set; }

    /// <summary>
    /// 是否启用OK/NG格式转换
    /// </summary>
    public bool EnableOkNgConversion { get; set; }

    /// <summary>
    /// OK标准格式
    /// </summary>
    public string OkStandardFormat { get; set; } = "OK";

    /// <summary>
    /// NG标准格式
    /// </summary>
    public string NgStandardFormat { get; set; } = "NG";

    /// <summary>
    /// 是否启用关键字高亮
    /// </summary>
    public bool EnableKeywordHighlight { get; set; }

    /// <summary>
    /// 高亮颜色（十六进制）
    /// </summary>
    public string HighlightColorHex { get; set; } = "#FFFF00";

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
