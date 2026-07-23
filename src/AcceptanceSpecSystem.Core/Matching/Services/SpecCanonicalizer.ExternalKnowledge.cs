using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AcceptanceSpecSystem.Core.Matching.Interfaces;


namespace AcceptanceSpecSystem.Core.Matching.Services;

public sealed partial class SpecCanonicalizer
{
    private static IReadOnlyDictionary<string, (string Dimension, double Factor)> BuildUnitRoots(
        ExternalMatchingKnowledge? externalKnowledge)
    {
        var result = new Dictionary<string, (string Dimension, double Factor)>(
            UnitRoots,
            StringComparer.Ordinal);

        foreach (var unit in externalKnowledge?.Units ?? [])
        {
            if (string.IsNullOrWhiteSpace(unit.Dimension) || unit.Tokens.Count == 0)
                continue;

            foreach (var token in unit.Tokens)
            {
                if (string.IsNullOrWhiteSpace(token))
                    continue;

                result[token.Trim()] = (unit.Dimension.Trim(), unit.Factor);
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<string, string> BuildBrandNormMap(
        ExternalMatchingKnowledge? externalKnowledge)
    {
        var result = new Dictionary<string, string>(BrandNormMap, StringComparer.OrdinalIgnoreCase);

        foreach (var brand in externalKnowledge?.Brands ?? [])
        {
            if (string.IsNullOrWhiteSpace(brand.Canonical))
                continue;

            var canonical = brand.Canonical.Trim();
            result[canonical] = canonical;
            foreach (var alias in brand.Aliases)
            {
                if (!string.IsNullOrWhiteSpace(alias))
                    result[alias.Trim()] = canonical;
            }
        }

        return result;
    }

    private static IReadOnlyList<string> BuildBrandAdjacentDeviceWords(
        ExternalMatchingKnowledge? externalKnowledge)
    {
        return BrandAdjacentDeviceWords
            .Concat(externalKnowledge?.BrandDeviceWords ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ExternalMatchingKnowledge? LoadDefaultExternalKnowledge()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var path = Path.Combine(
            baseDirectory,
            DefaultKnowledgeRelativePath.Replace('/', Path.DirectorySeparatorChar));

        return LoadExternalKnowledge(path, throwIfMissing: false);
    }

    private static ExternalMatchingKnowledge? LoadExternalKnowledge(string? path, bool throwIfMissing = true)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var resolvedPath = Path.GetFullPath(path);
        if (!File.Exists(resolvedPath))
        {
            if (throwIfMissing)
                throw new FileNotFoundException("智能填充外置知识库文件不存在", resolvedPath);

            return null;
        }

        var json = File.ReadAllText(resolvedPath, Encoding.UTF8);
        return JsonSerializer.Deserialize<ExternalMatchingKnowledge>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
    }

    private sealed class ExternalMatchingKnowledge
    {
        [JsonPropertyName("brands")]
        public List<ExternalBrandRule> Brands { get; init; } = [];

        [JsonPropertyName("brandDeviceWords")]
        public List<string> BrandDeviceWords { get; init; } = [];

        [JsonPropertyName("units")]
        public List<ExternalUnitRule> Units { get; init; } = [];
    }

    private sealed class ExternalBrandRule
    {
        [JsonPropertyName("canonical")]
        public string Canonical { get; init; } = string.Empty;

        [JsonPropertyName("aliases")]
        public List<string> Aliases { get; init; } = [];
    }

    private sealed class ExternalUnitRule
    {
        [JsonPropertyName("dimension")]
        public string Dimension { get; init; } = string.Empty;

        [JsonPropertyName("factor")]
        public double Factor { get; init; } = 1;

        [JsonPropertyName("tokens")]
        public List<string> Tokens { get; init; } = [];
    }
}
