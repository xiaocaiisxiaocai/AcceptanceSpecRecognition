namespace AcceptanceSpecSystem.Core.Matching.Interfaces;

/// <summary>
/// 规格文本规范化器接口。
/// 通过 SI 前缀引擎、品牌字典、同义表达字典消除等价差异，
/// 使"7.5kW vs 7500W"、"松下 vs Panasonic"等在规范化后变成精确命中。
/// </summary>
public interface ISpecCanonicalizer
{
    /// <summary>
    /// 对规格文本做全量规范化（单位归一 + 品牌统一 + 同义替换 + 格式归一）。
    /// 两边规范化结果相同 → 可视为语义等价。
    /// </summary>
    string Canonicalize(string? text);

    /// <summary>
    /// 尝试将数值+单位归一到基准量纲。
    /// 例：TryNormalizeToBaseUnit(7.5, "kW", out 7500, out "power") → true
    /// </summary>
    bool TryNormalizeToBaseUnit(
        double value,
        string unitToken,
        out double baseValue,
        out string baseDimension);

    /// <summary>
    /// 从文本中提取所有数值+单位表达式并归一到基准量纲。
    /// 返回列表：(基准值, 量纲, 原始表达式)
    /// </summary>
    IReadOnlyList<(double BaseValue, string Dimension, string OriginalExpression)>
        ExtractNormalizedValues(string text);

    /// <summary>
    /// 从文本中提取带单位的数值表达式，但只返回当前规则库无法识别的单位 token。
    /// 用于阻断未知单位场景的确定性自动通过，交由 LLM 或人工确认。
    /// </summary>
    IReadOnlyList<(string UnitToken, string OriginalExpression)> ExtractUnknownUnitTokens(string text);

    /// <summary>
    /// 判断品牌 token 是否能被当前品牌规则归一。
    /// 用于未知品牌门禁，避免外置品牌被误判为未知。
    /// </summary>
    bool TryNormalizeBrandToken(string brandToken, out string normalizedBrand);
}
