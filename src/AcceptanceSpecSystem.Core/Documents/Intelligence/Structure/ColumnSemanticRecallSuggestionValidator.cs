using AcceptanceSpecSystem.Core.Documents.Intelligence;

namespace AcceptanceSpecSystem.Core.Documents.Intelligence.Structure;

public static class ColumnSemanticRecallSuggestionValidator
{
    public static IReadOnlyList<LlmColumnSemanticRecallSuggestion> Validate(
        IReadOnlyList<LlmColumnSemanticRecallSuggestion> suggestions,
        IReadOnlyList<ColumnSemanticRecallHeaderCandidate> unmappedHeaders,
        IReadOnlyDictionary<string, int?> mappedFields)
    {
        var unmappedByIndex = unmappedHeaders
            .GroupBy(item => item.ColumnIndex)
            .ToDictionary(group => group.Key, group => group.First());
        var occupiedFields = mappedFields
            .Where(pair => pair.Value.HasValue)
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var acceptedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var acceptedColumns = new HashSet<int>();
        var valid = new List<LlmColumnSemanticRecallSuggestion>();

        foreach (var suggestion in suggestions
                     .OrderByDescending(item => item.Confidence)
                     .ThenBy(item => item.ColumnIndex))
        {
            if (!unmappedByIndex.TryGetValue(suggestion.ColumnIndex, out var header))
            {
                continue;
            }

            var targetField = NormalizeTargetField(suggestion.TargetField);
            if (targetField == null || occupiedFields.Contains(targetField))
            {
                continue;
            }

            if (targetField == "Acceptance" &&
                AcceptanceResultHeaderPolicy.IsAcceptanceMethodHeader(header.Header))
            {
                continue;
            }

            if (acceptedColumns.Contains(suggestion.ColumnIndex) ||
                (!string.Equals(targetField, "Unknown", StringComparison.OrdinalIgnoreCase) &&
                 acceptedFields.Contains(targetField)))
            {
                continue;
            }

            acceptedColumns.Add(suggestion.ColumnIndex);
            if (!string.Equals(targetField, "Unknown", StringComparison.OrdinalIgnoreCase))
            {
                acceptedFields.Add(targetField);
            }

            valid.Add(new LlmColumnSemanticRecallSuggestion
            {
                ColumnIndex = suggestion.ColumnIndex,
                Header = string.IsNullOrWhiteSpace(suggestion.Header) ? header.Header : suggestion.Header.Trim(),
                TargetField = targetField,
                Confidence = Math.Clamp(suggestion.Confidence, 0, 1),
                Reason = suggestion.Reason,
                Source = "SemanticRecall"
            });
        }

        return valid
            .OrderBy(item => item.ColumnIndex)
            .ToList();
    }

    private static string? NormalizeTargetField(string? value)
    {
        return value?.Trim() switch
        {
            "Project" => "Project",
            "Specification" => "Specification",
            "Acceptance" => "Acceptance",
            "Remark" => "Remark",
            "Unknown" => "Unknown",
            _ => null
        };
    }
}
