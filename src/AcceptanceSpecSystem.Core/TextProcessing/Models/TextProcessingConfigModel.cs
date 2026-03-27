namespace AcceptanceSpecSystem.Core.TextProcessing.Models;

public class TextProcessingConfigModel
{
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
