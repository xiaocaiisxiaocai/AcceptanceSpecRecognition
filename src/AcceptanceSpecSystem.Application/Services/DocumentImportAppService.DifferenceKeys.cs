using System.Text;
using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Application.Services;

public sealed partial class DocumentImportAppService
{
    private static bool HasReplayDifferenceDecisions(
        IEnumerable<string>? confirmedDifferenceKeys,
        IEnumerable<string>? partiallyConfirmedDifferenceKeys,
        IEnumerable<string>? skippedDifferenceKeys)
    {
        return HasAnyDifferenceDecision(confirmedDifferenceKeys) ||
               HasAnyDifferenceDecision(partiallyConfirmedDifferenceKeys) ||
               HasAnyDifferenceDecision(skippedDifferenceKeys);
    }

    private static bool HasAnyDifferenceDecision(IEnumerable<string>? keys)
    {
        return keys?.Any(key => !string.IsNullOrWhiteSpace(key)) == true;
    }


    private static Dictionary<string, PendingDecisionEntry> BuildPendingDecisionMap(
        IEnumerable<string>? confirmedDifferenceKeys,
        IEnumerable<string>? partiallyConfirmedDifferenceKeys,
        IEnumerable<string>? skippedDifferenceKeys)
    {
        var result = new Dictionary<string, PendingDecisionEntry>(StringComparer.Ordinal);

        foreach (var key in confirmedDifferenceKeys ?? [])
        {
            if (TryParsePendingDecisionEntry(key, DifferenceDecision.Import, out var entry))
            {
                result[entry.LookupKey] = entry;
            }
        }

        foreach (var key in partiallyConfirmedDifferenceKeys ?? [])
        {
            if (TryParsePendingDecisionEntry(key, DifferenceDecision.PartialImport, out var entry))
            {
                result[entry.LookupKey] = entry;
            }
        }

        foreach (var key in skippedDifferenceKeys ?? [])
        {
            if (TryParsePendingDecisionEntry(key, DifferenceDecision.Skip, out var entry))
            {
                result[entry.LookupKey] = entry;
            }
        }

        return result;
    }

    private static bool TryParsePendingDecisionEntry(
        string encodedKey,
        DifferenceDecision decision,
        out PendingDecisionEntry entry)
    {
        entry = null!;
        if (string.IsNullOrWhiteSpace(encodedKey))
        {
            return false;
        }

        string raw;
        try
        {
            raw = Encoding.UTF8.GetString(Convert.FromBase64String(encodedKey));
        }
        catch
        {
            return false;
        }

        if (!TryReadNextSegment(raw, 0, out var tableIndexText, out var cursor) ||
            !int.TryParse(tableIndexText, out var tableIndex) ||
            !TryReadNextSegment(raw, cursor, out var rowIndexText, out cursor) ||
            !int.TryParse(rowIndexText, out var rowIndex) ||
            !TryReadNextSegment(raw, cursor, out var matchType, out cursor) ||
            !TryReadNextSegment(raw, cursor, out var specIdText, out cursor) ||
            !int.TryParse(specIdText, out var existingSpecId))
        {
            return false;
        }

        var contentPayload = cursor <= raw.Length ? raw[cursor..] : string.Empty;
        entry = new PendingDecisionEntry
        {
            LookupKey = BuildPendingDecisionLookupKey(tableIndex, rowIndex, contentPayload),
            MatchType = matchType,
            ExistingSpecId = existingSpecId,
            Decision = decision
        };
        return true;
    }

    private static bool TryReadNextSegment(
        string value,
        int startIndex,
        out string segment,
        out int nextIndex)
    {
        segment = string.Empty;
        nextIndex = startIndex;
        if (startIndex > value.Length)
        {
            return false;
        }

        var separatorIndex = value.IndexOf('|', startIndex);
        if (separatorIndex < 0)
        {
            return false;
        }

        segment = value[startIndex..separatorIndex];
        nextIndex = separatorIndex + 1;
        return true;
    }

    private static string BuildPendingDecisionLookupKey(
        int tableIndex,
        int rowIndex,
        string normalizedProject,
        string normalizedSpecification,
        string normalizedAcceptance,
        string normalizedRemark)
    {
        return $"{tableIndex}|{rowIndex}|{normalizedProject}|{normalizedSpecification}|{normalizedAcceptance}|{normalizedRemark}";
    }

    private static string BuildPendingDecisionLookupKey(int tableIndex, int rowIndex, string contentPayload)
    {
        return $"{tableIndex}|{rowIndex}|{contentPayload}";
    }

    private static string BuildDifferenceKey(
        int tableIndex,
        int rowIndex,
        string matchType,
        int existingSpecId,
        string project,
        string specification,
        string acceptance,
        string remark)
    {
        var raw = $"{tableIndex}|{rowIndex}|{matchType}|{existingSpecId}|{project}|{specification}|{acceptance}|{remark}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
    }
}
