using System.Text;
using AcceptanceSpecSystem.Core.TextProcessing.Models;
using AcceptanceSpecSystem.Core.TextProcessing.Services;

namespace AcceptanceSpecSystem.Core.Documents.Intelligence.Strategies;

/// <summary>
/// 为列头规则匹配提供跨简繁体和空白差异的稳定文本身份。
/// 原始文本仍用于界面展示；这里只用于规则比较。
/// </summary>
public static class ColumnHeaderTextCanonicalizer
{
    private const string CompositeHeaderSeparator = " / ";
    private static readonly Lazy<OpenCcChineseConversionService> ChineseConverter =
        new(() => new OpenCcChineseConversionService(), LazyThreadSafetyMode.ExecutionAndPublication);

    public static string Canonicalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = NormalizeWhitespace(value);
        var simplified = ChineseConverter.Value.Convert(
            normalized,
            ChineseConversionMode.TWToHans);

        // 合并单元格表头可能在多次识别后形成“父 / 父 / 子”。
        // 比较身份时保留层级顺序，但移除已经出现过的重复层级。
        var segments = simplified.Split(
            CompositeHeaderSeparator,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length > 1)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            simplified = string.Join(
                CompositeHeaderSeparator,
                segments.Where(segment => seen.Add(segment)));
        }

        return simplified.ToUpperInvariant();
    }

    private static string NormalizeWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var character in value)
        {
            if (character is '\u200B' or '\uFEFF')
            {
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
