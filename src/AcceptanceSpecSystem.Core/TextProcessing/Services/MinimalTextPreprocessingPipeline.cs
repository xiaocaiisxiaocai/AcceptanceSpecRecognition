using AcceptanceSpecSystem.Core.TextProcessing.Interfaces;
using AcceptanceSpecSystem.Core.TextProcessing.Models;

namespace AcceptanceSpecSystem.Core.TextProcessing.Services;

/// <summary>
/// 仅执行最小安全归一化的文本预处理管道。
/// </summary>
public sealed class MinimalTextPreprocessingPipeline : ITextPreprocessingPipeline
{
    private static readonly TextProcessingConfigModel MinimalConfig = new()
    {
        EnableChineseConversion = false,
        ConversionMode = ChineseConversionMode.None,
        EnableSynonym = false,
        EnableOkNgConversion = false,
        OkStandardFormat = "OK",
        NgStandardFormat = "NG",
        EnableKeywordHighlight = false,
        HighlightColorHex = "#FFFF00"
    };

    private static readonly IChineseConversionService PassthroughChineseConversion = new PassthroughChineseConversionService();
    private static readonly IOkNgConversionService OkNgConversion = new OkNgConversionService();
    private static readonly IReadOnlyDictionary<string, string> EmptySynonymMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 创建最小归一化会话。
    /// </summary>
    public Task<TextProcessingSession> CreateSessionAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new TextProcessingSession(
            MinimalConfig,
            PassthroughChineseConversion,
            OkNgConversion,
            EmptySynonymMap));
    }

    private sealed class PassthroughChineseConversionService : IChineseConversionService
    {
        public string Convert(string text, ChineseConversionMode mode)
        {
            return text;
        }
    }
}
