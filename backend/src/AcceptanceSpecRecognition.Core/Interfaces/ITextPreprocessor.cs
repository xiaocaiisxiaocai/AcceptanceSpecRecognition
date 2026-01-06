using AcceptanceSpecRecognition.Core.Models;

namespace AcceptanceSpecRecognition.Core.Interfaces;

/// <summary>
/// 文本预处理器接口
/// </summary>
public interface ITextPreprocessor
{
    /// <summary>
    /// 预处理入口
    /// </summary>
    PreprocessedText Preprocess(string text);

    /// <summary>
    /// 符号标准化：中文标点→英文标点，全角→半角
    /// </summary>
    string NormalizeSymbols(string text);

    /// <summary>
    /// 错别字修正
    /// </summary>
    string CorrectTypos(string text);

    /// <summary>
    /// 单位标准化
    /// </summary>
    NormalizedUnitText NormalizeUnits(string text);

    /// <summary>
    /// 空白字符标准化
    /// </summary>
    string NormalizeWhitespace(string text);

    /// <summary>
    /// 繁体转简体（台湾繁体→大陆简体）
    /// </summary>
    string ConvertToSimplified(string text);
}
