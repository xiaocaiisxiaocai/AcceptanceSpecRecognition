using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Core.TextProcessing.Interfaces;

public interface ITextPreprocessingPipeline
{
    Task<TextProcessingSession> CreateSessionAsync(CancellationToken cancellationToken = default);
}

public sealed class TextProcessingSession
{
    private readonly TextProcessingConfig _config;
    private readonly IChineseConversionService _chinese;
    private readonly IOkNgConversionService _okNg;
    private readonly IReadOnlyDictionary<string, string> _synonymMap;

    internal TextProcessingSession(
        TextProcessingConfig config,
        IChineseConversionService chinese,
        IOkNgConversionService okNg,
        IReadOnlyDictionary<string, string> synonymMap)
    {
        _config = config;
        _chinese = chinese;
        _okNg = okNg;
        _synonymMap = synonymMap;
    }

    public TextProcessingConfig Config => _config;

    public string Process(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var output = NormalizeWhitespace(text);

        if (_config.EnableChineseConversion && _config.ConversionMode != ChineseConversionMode.None)
        {
            output = _chinese.Convert(output, _config.ConversionMode);
        }

        if (_config.EnableSynonym && _synonymMap.Count > 0)
        {
            output = ReplaceTokens(output, _synonymMap);
        }

        if (_config.EnableOkNgConversion)
        {
            output = _okNg.NormalizeOkNg(output, _config.OkStandardFormat, _config.NgStandardFormat);
        }

        return output;
    }

    private static string NormalizeWhitespace(string input)
    {
        var s = input.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
        return System.Text.RegularExpressions.Regex.Replace(s, "\\s+", " ").Trim();
    }

    private static string ReplaceTokens(string input, IReadOnlyDictionary<string, string> map)
    {
        // 用分隔符切词，然后逐词替换（避免对连续中文进行不安全的子串替换）
        var parts = System.Text.RegularExpressions.Regex.Split(input, "([\\s\\p{P}]+)");
        for (var i = 0; i < parts.Length; i++)
        {
            var p = parts[i];
            if (string.IsNullOrWhiteSpace(p))
                continue;

            // 分隔符直接跳过
            if (System.Text.RegularExpressions.Regex.IsMatch(p, "^[\\s\\p{P}]+$"))
                continue;

            if (map.TryGetValue(p, out var standard))
            {
                parts[i] = standard;
            }
        }

        return string.Concat(parts);
    }
}

