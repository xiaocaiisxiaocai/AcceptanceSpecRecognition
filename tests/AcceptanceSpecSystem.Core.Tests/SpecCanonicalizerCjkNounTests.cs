using AcceptanceSpecSystem.Core.Matching.Services;
using FluentAssertions;

namespace AcceptanceSpecSystem.Core.Tests;

/// <summary>
/// 验证 SpecCanonicalizer.ExtractUnknownUnitTokens 对中文名词词组的处理，
/// 防止"4边吸取""米高"等中文名词被误报为"未识别单位"。
/// </summary>
public class SpecCanonicalizerCjkNounTests
{
    private readonly SpecCanonicalizer _canonicalizer = new();

    [Theory]
    // 中文名词词组：不应被识别为未识别单位
    [InlineData("吸板形式需为4边")]                 // "边"单字名词
    [InlineData("吸板形式需为4边吸取")]             // "边吸取"名词词组
    [InlineData("工作高度5米高")]                   // "米高"（米是单位但贪婪匹配带出"高"）
    [InlineData("等待3秒后启动")]                   // "秒后"
    [InlineData("传送线速0-8米/分钟可调")]          // "米/分钟可调"复合污染
    public void ExtractUnknownUnitTokens_CjkNounPhrase_NotReported(string text)
    {
        var tokens = _canonicalizer.ExtractUnknownUnitTokens(text);

        // 中文名词词组不应作为未识别单位被收集
        tokens.Should().BeEmpty(
            $"中文名词词组不应被误判为未识别单位，但得到：{string.Join(", ", tokens.Select(t => t.UnitToken))}");
    }

    [Theory]
    // 真正的未识别单位（含字母的非常规写法，不在词根表中）：仍应被识别
    [InlineData("频率5cps", "cps")]
    public void ExtractUnknownUnitTokens_RealUnknownUnit_StillReported(string text, string expectedToken)
    {
        var tokens = _canonicalizer.ExtractUnknownUnitTokens(text);

        tokens.Select(t => t.UnitToken)
            .Should().Contain(t => t.Equals(expectedToken, StringComparison.OrdinalIgnoreCase),
                $"真正的未识别单位 {expectedToken} 应被识别");
    }

    [Theory]
    // 合法标准单位：不应被报告为未识别
    [InlineData("功率7.5kW")]
    [InlineData("长度100mm")]
    [InlineData("重量3kg")]
    public void ExtractUnknownUnitTokens_KnownUnit_NotReported(string text)
    {
        var tokens = _canonicalizer.ExtractUnknownUnitTokens(text);
        tokens.Should().BeEmpty(
            $"合法标准单位不应被报未识别，但得到：{string.Join(", ", tokens.Select(t => t.UnitToken))}");
    }
}
