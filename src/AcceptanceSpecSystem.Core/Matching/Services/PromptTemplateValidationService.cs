using System.Text.Json;
using System.Text.RegularExpressions;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Core.Matching.Services;

public sealed class PromptTemplateValidationService
{
    private static readonly Regex VariableRegex = new(@"\{\{\s*([a-zA-Z0-9_]+)\s*\}\}", RegexOptions.Compiled);

    public PromptTemplateValidationResult Validate(PromptTemplateScene scene, string content)
    {
        return Validate(PromptTemplateCatalog.GetByScene(scene), content);
    }

    public PromptTemplateValidationResult Validate(SystemPromptTemplateDefinition definition, string content)
    {
        var normalizedContent = content ?? string.Empty;
        var variables = VariableRegex.Matches(normalizedContent)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var errors = new List<string>();
        foreach (var variable in definition.RequiredVariables)
        {
            if (!variables.Contains(variable, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"缺少必需占位符: {variable}");
            }
        }

        foreach (var variable in variables)
        {
            if (!definition.AvailableVariables.Contains(variable, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"存在未知占位符: {variable}");
            }
        }

        var renderedPrompt = Render(normalizedContent);
        var structuredOutputIsValid = TryValidateStructuredOutputExample(
            definition,
            renderedPrompt,
            out var exampleJson,
            out var structuredOutputError);

        if (!structuredOutputIsValid && !string.IsNullOrWhiteSpace(structuredOutputError))
        {
            errors.Add(structuredOutputError);
        }

        return new PromptTemplateValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            RenderedPrompt = renderedPrompt,
            ExampleJson = exampleJson,
            StructuredOutputIsValid = structuredOutputIsValid,
            StructuredOutputError = structuredOutputError
        };
    }

    private static string Render(string template)
    {
        var result = template ?? string.Empty;
        var sampleValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sourceProject"] = "平台吸附精度",
            ["sourceSpecification"] = "平台平面度需控制在0.05mm以内",
            ["bestMatchProject"] = "平台吸附精度",
            ["bestMatchSpecification"] = "平台平面度需控制在0.05mm以内",
            ["bestMatchAcceptance"] = "平面度实测不超过0.05mm",
            ["bestMatchRemark"] = "历史备注示例",
            ["baseScore"] = "95.6",
            ["scoreDetailsJson"] = "{\"Embedding\":95.6}",
            ["referenceInfo"] = "项目：平台吸附精度\\n规格：平台平面度需控制在0.05mm以内"
        };

        foreach (var pair in sampleValues)
        {
            result = result.Replace($"{{{{{pair.Key}}}}}", pair.Value, StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }

    private static bool TryValidateStructuredOutputExample(
        SystemPromptTemplateDefinition definition,
        string renderedPrompt,
        out string? exampleJson,
        out string? error)
    {
        exampleJson = null;
        error = null;

        var candidates = ExtractJsonObjects(renderedPrompt).ToList();
        if (candidates.Count == 0)
        {
            error = "未找到可解析的 JSON 示例";
            return false;
        }

        string? lastParsedJson = null;
        string? lastError = null;

        foreach (var candidate in candidates)
        {
            try
            {
                using var document = JsonDocument.Parse(candidate);
                lastParsedJson = candidate;

                var missingKey = definition.RequiredJsonKeys
                    .FirstOrDefault(key => !document.RootElement.TryGetProperty(key, out _));
                if (missingKey == null)
                {
                    exampleJson = candidate;
                    return true;
                }

                lastError = $"JSON 示例缺少字段: {missingKey}";
            }
            catch (JsonException ex)
            {
                lastError = $"JSON 示例无效: {ex.Message}";
            }
        }

        exampleJson = lastParsedJson ?? candidates[^1];
        error = lastError ?? "未找到满足场景要求的 JSON 示例";
        return false;
    }

    private static IEnumerable<string> ExtractJsonObjects(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        for (var start = 0; start < text.Length; start++)
        {
            if (text[start] != '{')
            {
                continue;
            }

            var depth = 0;
            var inString = false;
            var escaped = false;
            for (var index = start; index < text.Length; index++)
            {
                var current = text[index];
                if (current == '"' && !escaped)
                {
                    inString = !inString;
                }

                if (!inString)
                {
                    if (current == '{')
                    {
                        depth++;
                    }
                    else if (current == '}')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            yield return text[start..(index + 1)];
                            start = index;
                            break;
                        }
                    }
                }

                if (current == '\\' && !escaped)
                {
                    escaped = true;
                }
                else
                {
                    escaped = false;
                }
            }
        }
    }
}

public sealed class PromptTemplateValidationResult
{
    public bool IsValid { get; set; }

    public List<string> Errors { get; set; } = [];

    public string RenderedPrompt { get; set; } = string.Empty;

    public string? ExampleJson { get; set; }

    public bool StructuredOutputIsValid { get; set; }

    public string? StructuredOutputError { get; set; }
}
