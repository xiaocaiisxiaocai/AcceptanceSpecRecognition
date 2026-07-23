using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AcceptanceSpecSystem.Core.AI.Models;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Diagnostics;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Structure;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;


namespace AcceptanceSpecSystem.Core.Matching.Services;

public partial class LlmMatchingAssistService
{
    private bool TryParseJson(string raw, out JsonDocument doc)
    {
        doc = null!;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var text = ExtractJson(SanitizeLlmOutput(raw));
        if (string.IsNullOrWhiteSpace(text))
            return false;

        try
        {
            doc = JsonDocument.Parse(text);
            return true;
        }
        catch (Exception)
        {
            _logger.LogWarning(
                "LLM 输出 JSON 解析失败，输出摘要: {Summary}, traceId={TraceId}",
                SensitiveLogFormatter.DescribePayload(raw),
                Activity.Current?.TraceId.ToString());
            return false;
        }
    }

    private static string ExtractJson(string text)
    {
        text = text.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineEnd = text.IndexOf('\n');
            if (firstLineEnd >= 0)
                text = text[(firstLineEnd + 1)..];
            var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence >= 0)
                text = text[..lastFence];
        }

        var candidates = ExtractJsonObjects(text).ToList();
        if (candidates.Count > 0)
            return candidates[^1].Trim();

        return text.Trim();
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

    private static string SanitizeLlmOutput(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var filter = new ThinkContentFilter();
        var sanitized = filter.Push(raw) + filter.Flush();
        return sanitized.Trim();
    }

    private static bool TryGetDouble(JsonElement element, string name, out double value)
    {
        value = 0;
        if (!element.TryGetProperty(name, out var prop))
            return false;

        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDouble(out value))
            return true;

        if (prop.ValueKind == JsonValueKind.String && double.TryParse(prop.GetString(), out value))
            return true;

        return false;
    }

    private static bool TryGetInt32(JsonElement element, string name, out int value)
    {
        value = 0;
        if (!element.TryGetProperty(name, out var prop))
            return false;

        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out value))
            return true;

        if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out value))
            return true;

        return false;
    }

    private static int? TryGetNullableInt32(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var prop) || prop.ValueKind == JsonValueKind.Null)
            return null;

        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var number))
            return number;

        if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out number))
            return number;

        return null;
    }

    private static bool TryGetBool(JsonElement element, string name, out bool value)
    {
        value = false;
        if (!element.TryGetProperty(name, out var prop))
            return false;

        if (prop.ValueKind == JsonValueKind.True || prop.ValueKind == JsonValueKind.False)
        {
            value = prop.GetBoolean();
            return true;
        }

        return prop.ValueKind == JsonValueKind.String && bool.TryParse(prop.GetString(), out value);
    }

    private static string? TryGetString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var prop))
            return null;

        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : prop.ToString();
    }

    private static bool TryParseEquivalenceVerdict(string? value, out LlmEquivalenceVerdict verdict)
    {
        verdict = LlmEquivalenceVerdict.Uncertain;
        return value?.Trim().ToLowerInvariant() switch
        {
            "equivalent" => SetVerdict(LlmEquivalenceVerdict.Equivalent, out verdict),
            "different" => SetVerdict(LlmEquivalenceVerdict.Different, out verdict),
            "uncertain" => SetVerdict(LlmEquivalenceVerdict.Uncertain, out verdict),
            _ => false
        };
    }

    private static bool TryParseEquivalenceReasonType(string? value, out LlmEquivalenceReasonType reasonType)
    {
        reasonType = LlmEquivalenceReasonType.Uncertain;
        return value?.Trim().ToLowerInvariant() switch
        {
            "format_only" => SetReasonType(LlmEquivalenceReasonType.FormatOnly, out reasonType),
            "punctuation_only" => SetReasonType(LlmEquivalenceReasonType.PunctuationOnly, out reasonType),
            "equivalent_expression" => SetReasonType(LlmEquivalenceReasonType.EquivalentExpression, out reasonType),
            "symbol_equivalent" => SetReasonType(LlmEquivalenceReasonType.SymbolEquivalent, out reasonType),
            "semantic_difference" => SetReasonType(LlmEquivalenceReasonType.SemanticDifference, out reasonType),
            "symbol_conflict" => SetReasonType(LlmEquivalenceReasonType.SymbolConflict, out reasonType),
            "uncertain" => SetReasonType(LlmEquivalenceReasonType.Uncertain, out reasonType),
            _ => false
        };
    }

    private static bool SetVerdict(LlmEquivalenceVerdict value, out LlmEquivalenceVerdict verdict)
    {
        verdict = value;
        return true;
    }

    private static bool SetReasonType(LlmEquivalenceReasonType value, out LlmEquivalenceReasonType reasonType)
    {
        reasonType = value;
        return true;
    }

    private static bool IsCompatibleEquivalenceReasonType(
        LlmEquivalenceVerdict verdict,
        LlmEquivalenceReasonType reasonType)
    {
        return verdict switch
        {
            LlmEquivalenceVerdict.Equivalent => reasonType is
                LlmEquivalenceReasonType.FormatOnly or
                LlmEquivalenceReasonType.PunctuationOnly or
                LlmEquivalenceReasonType.EquivalentExpression or
                LlmEquivalenceReasonType.SymbolEquivalent,
            LlmEquivalenceVerdict.Different => reasonType is
                LlmEquivalenceReasonType.SemanticDifference or
                LlmEquivalenceReasonType.SymbolConflict,
            _ => reasonType == LlmEquivalenceReasonType.Uncertain
        };
    }
}
