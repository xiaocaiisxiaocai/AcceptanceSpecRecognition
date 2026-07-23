using System.Text.Json;
using System.Text.RegularExpressions;
using AcceptanceSpecSystem.Core.Matching.Models;

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
        var sampleValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["workflowScene"] = "智能填充复核",
            ["sourceProject"] = "平台吸附精度",
            ["sourceSpecification"] = "平台平面度需控制在0.05mm以内",
            ["bestMatchProject"] = "平台吸附精度",
            ["bestMatchSpecification"] = "平台平面度需控制在0.05mm以内",
            ["bestMatchAcceptance"] = "平面度实测不超过0.05mm",
            ["bestMatchRemark"] = "历史备注示例",
            ["baseScore"] = "95.6",
            ["scoreDetailsJson"] = "{\"Embedding\":95.6}",
            ["currentDecision"] = "manualReview",
            ["reviewTrigger"] = "Top1 与 Top2 证据接近，需要进一步复核",
            ["evidenceSummaryJson"] = "[\"数值约束相容：平面度\",\"项目一致\"]",
            ["conflictSummaryJson"] = "[]",
            ["referenceInfo"] = "项目：平台吸附精度\\n规格：平台平面度需控制在0.05mm以内",
            ["candidateProject"] = "平台吸附精度候选",
            ["candidateSpecification"] = "平台平面度不超过0.05mm",
            ["currentTopCandidateSpecId"] = "101",
            ["candidatesJson"] =
                "[{\"rank\":1,\"specId\":101,\"project\":\"平台吸附精度\",\"specification\":\"平台平面度控制在0.08mm以内\",\"embeddingScore\":0.98,\"finalScore\":0.87,\"scoreDetails\":{\"Embedding\":0.98,\"Final\":0.87},\"evidenceSummary\":[\"项目一致\"],\"conflictSummary\":[\"边界条件疑似偏宽\"]},{\"rank\":2,\"specId\":102,\"project\":\"平台吸附精度\",\"specification\":\"平台平面度控制在0.05mm以内\",\"embeddingScore\":0.95,\"finalScore\":0.85,\"scoreDetails\":{\"Embedding\":0.95,\"Final\":0.85},\"evidenceSummary\":[\"项目一致\",\"规格更接近\"],\"conflictSummary\":[]}]",
            ["documentTablesJson"] =
                "[{\"tableIndex\":0,\"tableName\":\"表1\",\"rows\":[[\"项目\",\"规格\",\"验收标准\",\"备注\"],[\"平台吸附精度\",\"平面度需控制在0.05mm以内\",\"实测不超过0.05mm\",\"\"]]}]",
            ["ruleCandidatesJson"] =
                "[{\"tableIndex\":0,\"projectColumnIndex\":0,\"specificationColumnIndex\":1,\"acceptanceColumnIndex\":2,\"remarkColumnIndex\":3,\"confidence\":0.92}]",
            ["inputJson"] =
                "{\"TableIndex\":0,\"TableName\":\"验收表\",\"Headers\":[\"项目\",\"管控要求\"],\"MappedFields\":{\"Project\":0,\"Specification\":null},\"UnmappedHeaders\":[{\"ColumnIndex\":1,\"Header\":\"管控要求\"}],\"SampleRows\":[[\"外观\",\"无划伤\"]]}"
        };

        return PromptTemplatePlaceholderRenderer.ReplacePlaceholders(template, sampleValues);
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

        // 校验“所有”符合输出格式（含全部必需键）的 JSON 示例：任一不合法即判模板无效。
        // 含 few-shot 示例的模板会有多个输出格式 JSON，单个坏示例不能被其他合法示例遮蔽。
        // 缺少必需键的 JSON（如上下文里的 scoreDetailsJson）视为非输出示例，跳过。
        var sawValidExample = false;

        foreach (var candidate in candidates)
        {
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(candidate);
            }
            catch (JsonException)
            {
                // 文本里偶发的非 JSON 花括号片段，跳过
                continue;
            }

            using (document)
            {
                var missingKey = definition.RequiredJsonKeys
                    .FirstOrDefault(key => !document.RootElement.TryGetProperty(key, out _));
                if (missingKey != null)
                {
                    continue;
                }

                if (!TryValidateStructuredOutputPayload(
                        definition.Scene,
                        document.RootElement,
                        out var payloadError))
                {
                    exampleJson = candidate;
                    error = payloadError;
                    return false;
                }

                exampleJson ??= candidate;
                sawValidExample = true;
            }
        }

        if (sawValidExample)
        {
            return true;
        }

        error = "未找到满足场景要求的 JSON 示例";
        return false;
    }

    private static bool TryValidateStructuredOutputPayload(
        PromptTemplateScene scene,
        JsonElement payload,
        out string? error)
    {
        return scene switch
        {
            PromptTemplateScene.MatchingReview or PromptTemplateScene.ImportDuplicateReview
                => TryValidateMatchingReviewPayload(payload, out error),
            PromptTemplateScene.MatchingEquivalenceAdjudication
                => TryValidateEquivalencePayload(payload, out error),
            PromptTemplateScene.MatchingCandidateRerank
                => TryValidateCandidateRerankPayload(payload, out error),
            _ => Succeed(out error)
        };
    }

    private static bool TryValidateMatchingReviewPayload(JsonElement payload, out string? error)
    {
        if (!TryReadRequiredNumber(payload, "score", out var score, out error))
        {
            return false;
        }

        if (score is < 0 or > 100)
        {
            error = "JSON 示例字段 score 必须位于 0~100";
            return false;
        }

        if (!TryReadRequiredString(payload, "reason", out _, out error) ||
            !TryReadRequiredString(payload, "commentary", out _, out error))
        {
            return false;
        }

        return Succeed(out error);
    }

    private static bool TryValidateEquivalencePayload(JsonElement payload, out string? error)
    {
        if (!TryReadRequiredString(payload, "verdict", out var verdictText, out error))
        {
            return false;
        }

        if (!TryParseEquivalenceVerdict(verdictText, out var verdict))
        {
            error = $"JSON 示例字段 verdict 值无效: {verdictText}";
            return false;
        }

        if (!TryReadRequiredString(payload, "reasonType", out var reasonTypeText, out error))
        {
            return false;
        }

        if (!TryParseEquivalenceReasonType(reasonTypeText, out var reasonType))
        {
            error = $"JSON 示例字段 reasonType 值无效: {reasonTypeText}";
            return false;
        }

        if (!TryReadRequiredNumber(payload, "confidence", out var confidence, out error))
        {
            return false;
        }

        if (confidence is < 0 or > 1)
        {
            error = "JSON 示例字段 confidence 必须位于 0~1";
            return false;
        }

        if (!TryReadRequiredString(payload, "reason", out _, out error))
        {
            return false;
        }

        if (!IsReasonTypeCompatible(verdict, reasonType))
        {
            error = "JSON 示例字段 verdict 与 reasonType 组合不兼容";
            return false;
        }

        return Succeed(out error);
    }

    private static bool TryValidateCandidateRerankPayload(JsonElement payload, out string? error)
    {
        if (!payload.TryGetProperty("selectedSpecId", out var selectedSpecIdElement) ||
            selectedSpecIdElement.ValueKind != JsonValueKind.Number ||
            !selectedSpecIdElement.TryGetInt32(out var selectedSpecId) ||
            selectedSpecId <= 0)
        {
            error = "JSON 示例字段 selectedSpecId 必须是正整数";
            return false;
        }

        if (!TryReadRequiredString(payload, "reason", out _, out error))
        {
            return false;
        }

        if (!TryReadRequiredNumber(payload, "confidence", out var confidence, out error))
        {
            return false;
        }

        if (confidence is < 0 or > 1)
        {
            error = "JSON 示例字段 confidence 必须位于 0~1";
            return false;
        }

        return Succeed(out error);
    }

    private static bool TryReadRequiredNumber(
        JsonElement payload,
        string propertyName,
        out double value,
        out string? error)
    {
        value = 0;
        error = null;

        if (!payload.TryGetProperty(propertyName, out var element))
        {
            error = $"JSON 示例缺少字段: {propertyName}";
            return false;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out value))
        {
            error = $"JSON 示例字段 {propertyName} 必须是数字";
            return false;
        }

        return true;
    }

    private static bool TryReadRequiredString(
        JsonElement payload,
        string propertyName,
        out string value,
        out string? error)
    {
        value = string.Empty;
        error = null;

        if (!payload.TryGetProperty(propertyName, out var element))
        {
            error = $"JSON 示例缺少字段: {propertyName}";
            return false;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            error = $"JSON 示例字段 {propertyName} 必须是字符串";
            return false;
        }

        value = element.GetString() ?? string.Empty;
        return true;
    }

    private static bool TryParseEquivalenceVerdict(string value, out LlmEquivalenceVerdict verdict)
    {
        verdict = value.Trim().ToLowerInvariant() switch
        {
            "equivalent" => LlmEquivalenceVerdict.Equivalent,
            "different" => LlmEquivalenceVerdict.Different,
            "uncertain" => LlmEquivalenceVerdict.Uncertain,
            _ => default
        };

        return verdict != default;
    }

    private static bool TryParseEquivalenceReasonType(string value, out LlmEquivalenceReasonType reasonType)
    {
        reasonType = value.Trim().ToLowerInvariant() switch
        {
            "format_only" => LlmEquivalenceReasonType.FormatOnly,
            "punctuation_only" => LlmEquivalenceReasonType.PunctuationOnly,
            "equivalent_expression" => LlmEquivalenceReasonType.EquivalentExpression,
            "symbol_equivalent" => LlmEquivalenceReasonType.SymbolEquivalent,
            "semantic_difference" => LlmEquivalenceReasonType.SemanticDifference,
            "symbol_conflict" => LlmEquivalenceReasonType.SymbolConflict,
            "uncertain" => LlmEquivalenceReasonType.Uncertain,
            _ => default
        };

        return reasonType != default;
    }

    private static bool IsReasonTypeCompatible(
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
            LlmEquivalenceVerdict.Uncertain => reasonType == LlmEquivalenceReasonType.Uncertain,
            _ => false
        };
    }

    private static bool Succeed(out string? error)
    {
        error = null;
        return true;
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

internal static class PromptTemplatePlaceholderRenderer
{
    private static readonly Regex PlaceholderRegex = new(
        @"\{\{\s*([a-zA-Z0-9_]+)\s*\}\}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string ReplacePlaceholders(string? template, IReadOnlyDictionary<string, string> values)
    {
        var input = template ?? string.Empty;
        if (string.IsNullOrEmpty(input) || values.Count == 0)
        {
            return input;
        }

        return PlaceholderRegex.Replace(input, match =>
        {
            var key = match.Groups[1].Value;
            if (values.TryGetValue(key, out var value))
            {
                return value ?? string.Empty;
            }

            foreach (var pair in values)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Value ?? string.Empty;
                }
            }

            return match.Value;
        });
    }
}
