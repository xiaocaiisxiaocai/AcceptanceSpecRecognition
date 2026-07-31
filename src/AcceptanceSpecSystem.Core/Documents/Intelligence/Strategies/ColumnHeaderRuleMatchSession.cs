using AcceptanceSpecSystem.Core.Documents.Intelligence.Models;

namespace AcceptanceSpecSystem.Core.Documents.Intelligence.Strategies;

/// <summary>
/// 单次识别操作内复用列头规范化结果，避免按“单元格 × 规则”重复执行简繁转换。
/// </summary>
public sealed class ColumnHeaderRuleMatchSession
{
    private const int MaximumCachedValues = 4096;
    private readonly Dictionary<string, string> _canonicalizedValues =
        new(StringComparer.Ordinal);

    public ColumnHeaderRuleMatch MatchNormalizedHeader(
        string normalizedHeader,
        ColumnHeaderMappingRule rule)
    {
        return ColumnHeaderRuleMatcher.MatchNormalizedHeader(
            normalizedHeader,
            rule,
            Canonicalize);
    }

    internal int CanonicalizedValueCount => _canonicalizedValues.Count;

    private string Canonicalize(string value)
    {
        if (_canonicalizedValues.TryGetValue(value, out var canonicalized))
        {
            return canonicalized;
        }

        canonicalized = ColumnHeaderTextCanonicalizer.Canonicalize(value);
        if (_canonicalizedValues.Count < MaximumCachedValues)
        {
            _canonicalizedValues.TryAdd(value, canonicalized);
        }

        return canonicalized;
    }
}
