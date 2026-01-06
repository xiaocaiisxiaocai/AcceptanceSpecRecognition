using System.Text;
using System.Text.RegularExpressions;
using AcceptanceSpecRecognition.Core.Interfaces;
using AcceptanceSpecRecognition.Core.Models;
using OpenCCNET;

namespace AcceptanceSpecRecognition.Core.Services;

/// <summary>
/// 文本预处理器实现
/// </summary>
public class TextPreprocessor : ITextPreprocessor
{
    private readonly IJsonStorageService _storage;
    private readonly IConfigManager _configManager;
    private TypoCorrections _typoCorrections;
    private UnitMappings _unitMappings;

    // 中文标点到英文标点的映射
    private static readonly Dictionary<char, char> ChinesePunctuationMap = new()
    {
        { '\uFF0C', ',' }, // ，
        { '\u3002', '.' }, // 。
        { '\uFF01', '!' }, // ！
        { '\uFF1F', '?' }, // ？
        { '\uFF1A', ':' }, // ：
        { '\uFF1B', ';' }, // ；
        { '\u201C', '"' }, // "
        { '\u201D', '"' }, // "
        { '\u2018', '\'' }, // '
        { '\u2019', '\'' }, // '
        { '\uFF08', '(' }, // （
        { '\uFF09', ')' }, // ）
        { '\u3010', '[' }, // 【
        { '\u3011', ']' }, // 】
        { '\u300A', '<' }, // 《
        { '\u300B', '>' }, // 》
        { '\u3001', ',' }, // 、
        { '\uFF5E', '~' }, // ～
        { '\u2014', '-' }, // —
        { '\u2026', '.' }  // …
    };

    public TextPreprocessor(IJsonStorageService storage, IConfigManager configManager)
    {
        _storage = storage;
        _configManager = configManager;
        _typoCorrections = LoadTypoCorrections().GetAwaiter().GetResult();
        _unitMappings = LoadUnitMappings().GetAwaiter().GetResult();
    }

    private async Task<TypoCorrections> LoadTypoCorrections()
    {
        var corrections = await _storage.ReadAsync<TypoCorrections>("./data/typo_corrections.json");
        return corrections ?? new TypoCorrections();
    }

    private async Task<UnitMappings> LoadUnitMappings()
    {
        var mappings = await _storage.ReadAsync<UnitMappings>("./data/unit_mappings.json");
        return mappings ?? GetDefaultUnitMappings();
    }

    private UnitMappings GetDefaultUnitMappings()
    {
        return new UnitMappings
        {
            Version = "1.0",
            Units = new Dictionary<string, UnitDefinition>
            {
                ["voltage"] = new() { Standard = "V", Aliases = new() { "伏", "伏特", "v", "Volt" } },
                ["current"] = new() { Standard = "A", Aliases = new() { "安", "安培", "a", "Amp" } },
                ["power"] = new() { Standard = "W", Aliases = new() { "瓦", "瓦特", "w", "Watt" } },
                ["frequency"] = new() { Standard = "Hz", Aliases = new() { "赫兹", "hz", "HZ", "Hertz" } }
            },
            ElectricalPrefixes = new Dictionary<string, List<string>>
            {
                ["DC"] = new() { "直流", "dc", "D.C." },
                ["AC"] = new() { "交流", "ac", "A.C." },
                ["1P"] = new() { "单相", "1相" },
                ["3P"] = new() { "三相", "3相" }
            }
        };
    }

    public PreprocessedText Preprocess(string text)
    {
        var config = _configManager.GetAll();
        var corrections = new List<Correction>();
        var normalized = text;

        // 1. 繁简体转换（如果启用）- 最先执行，统一字符集
        if (config.Preprocessing.EnableChineseSimplification)
        {
            var simplified = ConvertToSimplified(normalized);
            if (simplified != normalized)
            {
                corrections.Add(new Correction
                {
                    Type = "ChineseSimplification",
                    Original = normalized,
                    Corrected = simplified
                });
                normalized = simplified;
            }
        }

        // 2. 符号标准化
        if (config.Preprocessing.EnableSymbolNormalization)
        {
            var symbolNormalized = NormalizeSymbols(normalized);
            if (symbolNormalized != normalized)
            {
                corrections.Add(new Correction
                {
                    Type = "SymbolNormalization",
                    Original = normalized,
                    Corrected = symbolNormalized
                });
                normalized = symbolNormalized;
            }
        }

        // 3. 错别字修正
        if (config.Preprocessing.EnableTypoCorrection)
        {
            var typoFixed = CorrectTypos(normalized);
            if (typoFixed != normalized)
            {
                corrections.Add(new Correction
                {
                    Type = "TypoCorrection",
                    Original = normalized,
                    Corrected = typoFixed
                });
                normalized = typoFixed;
            }
        }

        // 4. 空白字符标准化（始终执行）
        var whitespaceNormalized = NormalizeWhitespace(normalized);
        if (whitespaceNormalized != normalized)
        {
            corrections.Add(new Correction
            {
                Type = "WhitespaceNormalization",
                Original = normalized,
                Corrected = whitespaceNormalized
            });
            normalized = whitespaceNormalized;
        }

        return new PreprocessedText
        {
            Original = text,
            Normalized = normalized,
            Corrections = corrections
        };
    }

    public string NormalizeSymbols(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            // 中文标点转英文标点
            if (ChinesePunctuationMap.TryGetValue(c, out var replacement))
            {
                sb.Append(replacement);
            }
            // 全角转半角
            else if (c >= 0xFF01 && c <= 0xFF5E)
            {
                sb.Append((char)(c - 0xFEE0));
            }
            // 全角空格转半角空格
            else if (c == 0x3000)
            {
                sb.Append(' ');
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    public string CorrectTypos(string text)
    {
        if (string.IsNullOrEmpty(text) || _typoCorrections.Corrections.Count == 0)
            return text;

        var result = text;
        foreach (var (typo, correction) in _typoCorrections.Corrections)
        {
            result = result.Replace(typo, correction);
        }
        return result;
    }

    public NormalizedUnitText NormalizeUnits(string text)
    {
        if (string.IsNullOrEmpty(text))
            return new NormalizedUnitText { Text = text };

        var result = text;
        var extractedUnits = new List<UnitInfo>();

        // 标准化单位别名
        foreach (var (_, unitDef) in _unitMappings.Units)
        {
            foreach (var alias in unitDef.Aliases)
            {
                if (result.Contains(alias))
                {
                    result = result.Replace(alias, unitDef.Standard);
                }
            }
        }

        // 标准化电气前缀
        foreach (var (standard, aliases) in _unitMappings.ElectricalPrefixes)
        {
            foreach (var alias in aliases)
            {
                if (result.Contains(alias))
                {
                    result = result.Replace(alias, standard);
                }
            }
        }

        // 提取数值和单位
        var unitPattern = @"(\d+(?:\.\d+)?)\s*(K|k|M|m)?(V|A|W|Hz|Ω)";
        var matches = Regex.Matches(result, unitPattern);
        foreach (Match match in matches)
        {
            var value = double.Parse(match.Groups[1].Value);
            var prefix = match.Groups[2].Value;
            var unit = match.Groups[3].Value;

            // 应用前缀倍数
            if (!string.IsNullOrEmpty(prefix))
            {
                value *= prefix.ToUpper() switch
                {
                    "K" => 1000,
                    "M" => 1000000,
                    _ => 1
                };
            }

            extractedUnits.Add(new UnitInfo
            {
                Value = value,
                Unit = match.Value,
                StandardUnit = unit
            });
        }

        return new NormalizedUnitText
        {
            Text = result,
            ExtractedUnits = extractedUnits
        };
    }

    public string NormalizeWhitespace(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // 将多个连续空白字符替换为单个空格
        var result = Regex.Replace(text, @"\s+", " ");
        return result.Trim();
    }

    /// <summary>
    /// 繁体转简体（使用 OpenCCNET）
    /// </summary>
    public string ConvertToSimplified(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        try
        {
            return ZhConverter.HantToHans(text);
        }
        catch
        {
            // 转换失败时返回原文
            return text;
        }
    }
}
