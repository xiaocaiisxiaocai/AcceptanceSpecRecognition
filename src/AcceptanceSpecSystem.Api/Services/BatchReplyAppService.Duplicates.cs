using System.IO.Compression;
using System.Security.Claims;
using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Api.Services;

public sealed partial class BatchReplyAppService
{
    private static BatchReplyResolvedLookup<BatchReplySourceRow> BuildSourceRowLookup(BatchReplySourceTable sourceTable)
    {
        var result = new BatchReplyResolvedLookup<BatchReplySourceRow>();
        var resolutionLookup = BuildDuplicateResolutionLookup(sourceTable.DuplicateResolutions);
        foreach (var group in sourceTable.Rows
                     .GroupBy(row => BuildRowKey(row.Project, row.Specification))
                     .OrderBy(item => item.Min(row => row.RowIndex)))
        {
            var rows = group.OrderBy(row => row.RowIndex).ToList();
            if (rows.Count == 1)
            {
                result.Lookup[group.Key] = rows[0];
                continue;
            }

            var groupId = BuildDuplicateGroupId(DuplicateSourceKindSource, sourceTable.TableIndex, group.Key);
            if (!resolutionLookup.TryGetValue(groupId, out var strategy))
            {
                result.DuplicateGroups.Add(CreateSourceDuplicateGroup(groupId, sourceTable.TableIndex, rows));
                continue;
            }

            switch (strategy)
            {
                case DuplicateStrategyKeepFirst:
                    result.Lookup[group.Key] = rows[0];
                    break;
                case DuplicateStrategyKeepLast:
                    result.Lookup[group.Key] = rows[^1];
                    break;
                case DuplicateStrategySkip:
                    result.SkippedKeys.Add(group.Key);
                    break;
            }
        }

        return result;
    }

    private static BatchReplyResolvedLookup<MatchSourceItem> BuildTargetRowLookup(
        int tableIndex,
        IReadOnlyCollection<MatchSourceItem> targetRows,
        IReadOnlyCollection<BatchReplyDuplicateResolutionDto>? duplicateResolutions)
    {
        var result = new BatchReplyResolvedLookup<MatchSourceItem>();
        var resolutionLookup = BuildDuplicateResolutionLookup(duplicateResolutions);
        foreach (var group in targetRows
                     .GroupBy(row => BuildRowKey(row.Project, row.Specification))
                     .OrderBy(item => item.Min(row => row.RowIndex)))
        {
            var rows = group.OrderBy(row => row.RowIndex).ToList();
            if (rows.Count == 1)
            {
                result.Lookup[group.Key] = rows[0];
                continue;
            }

            var groupId = BuildDuplicateGroupId(DuplicateSourceKindTarget, tableIndex, group.Key);
            if (!resolutionLookup.TryGetValue(groupId, out var strategy))
            {
                result.DuplicateGroups.Add(CreateTargetDuplicateGroup(groupId, tableIndex, rows));
                continue;
            }

            switch (strategy)
            {
                case DuplicateStrategyKeepFirst:
                    result.Lookup[group.Key] = rows[0];
                    break;
                case DuplicateStrategyKeepLast:
                    result.Lookup[group.Key] = rows[^1];
                    break;
                case DuplicateStrategySkip:
                    result.SkippedKeys.Add(group.Key);
                    break;
            }
        }

        return result;
    }

    private static Dictionary<string, string> BuildDuplicateResolutionLookup(
        IReadOnlyCollection<BatchReplyDuplicateResolutionDto>? duplicateResolutions)
    {
        var lookup = new Dictionary<string, string>(StringComparer.Ordinal);
        if (duplicateResolutions == null)
        {
            return lookup;
        }

        foreach (var resolution in duplicateResolutions)
        {
            if (string.IsNullOrWhiteSpace(resolution.GroupId))
            {
                continue;
            }

            var normalizedStrategy = NormalizeDuplicateStrategy(resolution.Strategy);
            if (normalizedStrategy == null)
            {
                continue;
            }

            lookup[resolution.GroupId.Trim()] = normalizedStrategy;
        }

        return lookup;
    }

    private static string? NormalizeDuplicateStrategy(string? strategy)
    {
        if (string.IsNullOrWhiteSpace(strategy))
        {
            return null;
        }

        return strategy.Trim() switch
        {
            DuplicateStrategyKeepFirst => DuplicateStrategyKeepFirst,
            DuplicateStrategyKeepLast => DuplicateStrategyKeepLast,
            DuplicateStrategySkip => DuplicateStrategySkip,
            _ => null
        };
    }

    private static string BuildDuplicateGroupId(
        string duplicateSource,
        int tableIndex,
        (string Project, string Specification) key)
    {
        return $"{duplicateSource}:{tableIndex}:{key.Project}|{key.Specification}";
    }

    private static BatchReplyDuplicateGroupDto CreateSourceDuplicateGroup(
        string groupId,
        int tableIndex,
        IReadOnlyCollection<BatchReplySourceRow> rows)
    {
        var orderedRows = rows.OrderBy(row => row.RowIndex).ToList();
        var firstRow = orderedRows[0];
        return new BatchReplyDuplicateGroupDto
        {
            GroupId = groupId,
            DuplicateSource = DuplicateSourceKindSource,
            TableIndex = tableIndex,
            Project = firstRow.Project,
            Specification = firstRow.Specification,
            Rows = orderedRows.Select(row => new BatchReplyDuplicateRowDto
            {
                RowIndex = row.RowIndex,
                Project = row.Project,
                Specification = row.Specification,
                Acceptance = row.Acceptance,
                Remark = row.Remark
            }).ToList()
        };
    }

    private static BatchReplyDuplicateGroupDto CreateTargetDuplicateGroup(
        string groupId,
        int tableIndex,
        IReadOnlyCollection<MatchSourceItem> rows)
    {
        var orderedRows = rows.OrderBy(row => row.RowIndex).ToList();
        var firstRow = orderedRows[0];
        return new BatchReplyDuplicateGroupDto
        {
            GroupId = groupId,
            DuplicateSource = DuplicateSourceKindTarget,
            TableIndex = tableIndex,
            Project = firstRow.Project,
            Specification = firstRow.Specification,
            Rows = orderedRows.Select(row => new BatchReplyDuplicateRowDto
            {
                RowIndex = row.RowIndex,
                Project = row.Project,
                Specification = row.Specification
            }).ToList()
        };
    }

    private static (string Project, string Specification) BuildRowKey(string? project, string? specification)
    {
        return (NormalizeStrictText(project), NormalizeStrictText(specification));
    }

    private static int ResolveSourceTableIndex(BatchTableConfig targetConfig)
    {
        return targetConfig.SourceTableIndex ?? targetConfig.TableIndex;
    }

    private static string NormalizeStrictText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(" ", value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private sealed class BatchReplyResolvedLookup<TRow>
    {
        public Dictionary<(string Project, string Specification), TRow> Lookup { get; } = [];

        public List<BatchReplyDuplicateGroupDto> DuplicateGroups { get; } = [];

        public HashSet<(string Project, string Specification)> SkippedKeys { get; } = [];
    }
}
