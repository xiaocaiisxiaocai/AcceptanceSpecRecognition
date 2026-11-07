using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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

/// <summary>
/// LLM 匹配辅助服务：各类 LLM 输出（等价裁决、候选重排、结构裁决、列语义召回）的结构化解析。
/// </summary>
public partial class LlmMatchingAssistService
{
    public bool TryParseAdjudicationResult(string raw, out LlmEquivalenceAdjudicationResult result)
    {
        result = null!;
        if (!TryParseJson(raw, out var doc))
            return false;
        using var parsedDocument = doc;

        var verdictText = TryGetString(doc.RootElement, "verdict");
        var reasonTypeText = TryGetString(doc.RootElement, "reasonType");
        if (!TryParseEquivalenceVerdict(verdictText, out var verdict) ||
            !TryParseEquivalenceReasonType(reasonTypeText, out var reasonType))
        {
            return false;
        }

        if (!IsCompatibleEquivalenceReasonType(verdict, reasonType))
        {
            return false;
        }

        if (!TryGetDouble(doc.RootElement, "confidence", out var confidence))
            return false;

        result = new LlmEquivalenceAdjudicationResult
        {
            Verdict = verdict,
            ReasonType = reasonType,
            Confidence = Math.Clamp(confidence, 0, 1),
            Reason = TryGetString(doc.RootElement, "reason")
        };
        return true;
    }

    public bool TryParseRerankResult(string raw, out LlmCandidateRerankResult result)
    {
        result = null!;
        if (!TryParseJson(raw, out var doc))
            return false;
        using var parsedDocument = doc;

        if (!TryGetInt32(doc.RootElement, "selectedSpecId", out var selectedSpecId) ||
            selectedSpecId <= 0)
        {
            return false;
        }

        if (!TryGetDouble(doc.RootElement, "confidence", out var confidence))
            return false;

        result = new LlmCandidateRerankResult
        {
            SelectedSpecId = selectedSpecId,
            Confidence = Math.Clamp(confidence, 0, 1),
            Reason = TryGetString(doc.RootElement, "reason")
        };
        return true;
    }

    public bool TryParseDocumentStructureResult(
        string raw,
        out LlmDocumentStructureAdjudicationResult result)
    {
        result = null!;
        if (!TryParseJson(raw, out var doc))
            return false;
        using var parsedDocument = doc;

        if (!doc.RootElement.TryGetProperty("tables", out var tablesElement) ||
            tablesElement.ValueKind != JsonValueKind.Array ||
            !TryGetDouble(doc.RootElement, "confidence", out var confidence))
        {
            return false;
        }

        var tables = new List<DocumentStructureCandidate>();
        foreach (var item in tablesElement.EnumerateArray())
        {
            if (!TryGetInt32(item, "tableIndex", out var tableIndex))
            {
                return false;
            }

            tables.Add(new DocumentStructureCandidate
            {
                TableIndex = tableIndex,
                TableName = TryGetString(item, "tableName"),
                HeaderRowIndex = TryGetInt32(item, "headerRowIndex", out var headerRowIndex) ? headerRowIndex : 0,
                HeaderRowCount = TryGetInt32(item, "headerRowCount", out var headerRowCount) ? Math.Max(1, headerRowCount) : 1,
                DataStartRowIndex = TryGetInt32(item, "dataStartRowIndex", out var dataStartRowIndex) ? dataStartRowIndex : 1,
                DataEndRowIndex = TryGetNullableInt32(item, "dataEndRowIndex"),
                ProjectColumnIndex = TryGetNullableInt32(item, "projectColumnIndex"),
                SpecificationColumnIndex = TryGetNullableInt32(item, "specificationColumnIndex"),
                AcceptanceColumnIndex = TryGetNullableInt32(item, "acceptanceColumnIndex"),
                RemarkColumnIndex = TryGetNullableInt32(item, "remarkColumnIndex"),
                IsSpecificationOnly = TryGetBool(item, "isSpecificationOnly", out var isSpecificationOnly) && isSpecificationOnly,
                Confidence = TryGetDouble(item, "confidence", out var tableConfidence)
                    ? Math.Clamp(tableConfidence, 0, 1)
                    : Math.Clamp(confidence, 0, 1),
                Source = DocumentStructureCandidateSource.Llm
            });
        }

        result = new LlmDocumentStructureAdjudicationResult
        {
            Tables = tables,
            Confidence = Math.Clamp(confidence, 0, 1),
            Decision = TryGetString(doc.RootElement, "decision") ?? "needConfirm",
            Reason = TryGetString(doc.RootElement, "reason") ?? string.Empty
        };
        return true;
    }

    public bool TryParseColumnSemanticRecallResult(
        string raw,
        out LlmColumnSemanticRecallResult result)
    {
        result = null!;
        if (!TryParseJson(raw, out var doc))
            return false;
        using var parsedDocument = doc;

        if (!doc.RootElement.TryGetProperty("suggestions", out var suggestionsElement) ||
            suggestionsElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var suggestions = new List<LlmColumnSemanticRecallSuggestion>();
        foreach (var item in suggestionsElement.EnumerateArray())
        {
            if (!TryGetInt32(item, "columnIndex", out var columnIndex))
            {
                return false;
            }

            var targetField = TryGetString(item, "targetField") ?? "Unknown";
            suggestions.Add(new LlmColumnSemanticRecallSuggestion
            {
                ColumnIndex = columnIndex,
                Header = TryGetString(item, "header") ?? string.Empty,
                TargetField = targetField,
                Confidence = TryGetDouble(item, "confidence", out var confidence)
                    ? Math.Clamp(confidence, 0, 1)
                    : 0,
                Reason = TryGetString(item, "reason") ?? string.Empty,
                Source = "SemanticRecall"
            });
        }

        result = new LlmColumnSemanticRecallResult
        {
            Suggestions = suggestions
        };
        return true;
    }
}
