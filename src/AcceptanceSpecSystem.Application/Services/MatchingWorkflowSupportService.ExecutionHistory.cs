using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Core.TextProcessing.Interfaces;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;

namespace AcceptanceSpecSystem.Application.Services;

public sealed partial class MatchingWorkflowSupportService
{
    private async Task SaveExecutionHistoryAsync(
        MatchingUserContext user,
        WordFile wordFile,
        string taskId,
        DateTime createdAt,
        IReadOnlyCollection<BatchTableFillMapping> tables,
        IReadOnlyCollection<ExecutionHistoryPreviewTableSnapshot> previewTables,
        MatchingConfig executionConfig,
        IReadOnlyDictionary<int, AcceptanceSpec> specDict,
        IReadOnlyDictionary<int, HashSet<int>> adoptedRowLookup,
        IReadOnlyDictionary<int, ExecutionMatchSnapshot> currentMatchLookups,
        SmartFillResultArchiveDraft resultArchive,
        bool saveImmediately = true,
        CancellationToken cancellationToken = default)
    {
        var tableMetas = await _documentTableAccessService.GetTablesAsync(wordFile, cancellationToken);
        var tableMetaLookup = tableMetas.ToDictionary(table => table.Index);
        var previewLookup = BuildAuthoritativeExecutionHistoryPreviewLookup(
            currentMatchLookups,
            executionConfig);
        var fileDetail = new ExecutionHistoryFileDto
        {
            FileName = wordFile.FileName,
            FileType = wordFile.FileType
        };
        var playbackFile = new ExecutionHistorySmartFillFileDto
        {
            FileName = wordFile.FileName,
            FileType = wordFile.FileType
        };

        foreach (var table in tables.OrderBy(item => item.TableIndex))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rows = await BuildExecutionHistoryRowsAsync(
                wordFile,
                table,
                specDict,
                adoptedRowLookup.GetValueOrDefault(table.TableIndex),
                currentMatchLookups.GetValueOrDefault(table.TableIndex)?.MatchLookup,
                executionConfig,
                cancellationToken);
            var sheetName = tableMetaLookup.TryGetValue(table.TableIndex, out var meta) && !string.IsNullOrWhiteSpace(meta.Name)
                ? meta.Name!
                : $"表格 {table.TableIndex + 1}";

            fileDetail.Sheets.Add(new ExecutionHistorySheetDto
            {
                SheetIndex = table.TableIndex,
                SheetName = sheetName,
                Rows = rows
            });

            if (previewLookup.Count > 0)
            {
                playbackFile.Sheets.Add(new ExecutionHistorySmartFillSheetDto
                {
                    SheetIndex = table.TableIndex,
                    SheetName = sheetName,
                    Rows = BuildSmartFillPlaybackRows(
                        rows,
                        table.Mappings,
                        previewLookup.GetValueOrDefault(table.TableIndex))
                });
            }
        }

        var playback = previewLookup.Count > 0
            ? new ExecutionHistorySmartFillPlaybackDto
            {
                PayloadVersion = ExecutionHistoryDraft.CurrentSmartFillPlaybackVersion,
                Files = [playbackFile]
            }
            : null;

        await _executionHistoryAppService.SaveAsync(user, new ExecutionHistoryDraft
        {
            TaskId = taskId,
            TaskType = ExecutionHistoryTaskTypes.SmartFill,
            SourceFileId = wordFile.Id,
            SourceFileName = wordFile.FileName,
            SourceFileType = wordFile.FileType,
            OwnerOrgUnitId = wordFile.OwnerOrgUnitId,
            CreatedAt = createdAt,
            Files = [fileDetail],
            SmartFillSummary = playback == null ? null : BuildSmartFillSummary(playback),
            SmartFillPlayback = playback,
            ResultArchive = resultArchive
        }, saveImmediately: saveImmediately);
    }

    private async Task<List<ExecutionHistoryRowDto>> BuildExecutionHistoryRowsAsync(
        WordFile wordFile,
        BatchTableFillMapping table,
        IReadOnlyDictionary<int, AcceptanceSpec> specDict,
        HashSet<int>? adoptedRows,
        IReadOnlyDictionary<int, MatchResult>? currentMatchLookup,
        MatchingConfig executionConfig,
        CancellationToken cancellationToken)
    {
        var mappingLookup = table.Mappings.ToDictionary(item => item.RowIndex);
        var sourceRows = new List<MatchSourceItem>();

        if (table.ProjectColumnIndex.HasValue && table.SpecificationColumnIndex.HasValue)
        {
            sourceRows = await ExtractMatchSourceItemsForRegionsAsync(
                wordFile,
                table.TableIndex,
                table.ProjectColumnIndex.Value,
                table.SpecificationColumnIndex.Value,
                table.HeaderRowStart,
                table.HeaderRowCount,
                table.DataStartRow,
                table.DataEndRow,
                table.Regions,
                table.FilterEmptySourceRows ?? executionConfig.FilterEmptySourceRows,
                cancellationToken);
        }

        if (sourceRows.Count == 0)
        {
            return table.Mappings
                .OrderBy(item => item.RowIndex)
                .Select(item => BuildExecutionHistoryRow(
                    item.RowIndex,
                    string.Empty,
                    string.Empty,
                    mappingLookup.GetValueOrDefault(item.RowIndex),
                    specDict,
                    adoptedRows,
                    currentMatchLookup?.GetValueOrDefault(item.RowIndex),
                    table.AcceptanceColumnIndex,
                    table.RemarkColumnIndex,
                    null))
                .ToList();
        }

        var sourceRowLookup = sourceRows.ToDictionary(item => item.RowIndex);
        var rowIndexes = sourceRowLookup.Keys
            .Concat(mappingLookup.Keys)
            .Distinct()
            .OrderBy(rowIndex => rowIndex);

        return rowIndexes
            .Select(rowIndex =>
            {
                sourceRowLookup.TryGetValue(rowIndex, out var sourceRow);
                return BuildExecutionHistoryRow(
                    rowIndex,
                    sourceRow?.Project ?? string.Empty,
                    sourceRow?.Specification ?? string.Empty,
                    mappingLookup.GetValueOrDefault(rowIndex),
                    specDict,
                    adoptedRows,
                    currentMatchLookup?.GetValueOrDefault(rowIndex),
                    sourceRow?.AcceptanceColumnIndex ?? table.AcceptanceColumnIndex,
                    sourceRow?.RemarkColumnIndex ?? table.RemarkColumnIndex,
                    sourceRow);
            })
            .ToList();
    }

    private ExecutionHistoryRowDto BuildExecutionHistoryRow(
        int rowIndex,
        string project,
        string specification,
        FillMapping? mapping,
        IReadOnlyDictionary<int, AcceptanceSpec> specDict,
        HashSet<int>? adoptedRows,
        MatchResult? currentMatch,
        int acceptanceColumnIndex,
        int? remarkColumnIndex,
        MatchSourceItem? sourceItem)
    {
        var selectedSpecId = mapping?.SpecId ?? 0;
        AcceptanceSpec? matchedSpec = null;
        var hasSpec = selectedSpecId > 0 && specDict.TryGetValue(selectedSpecId, out matchedSpec);
        var confidencePercent = currentMatch != null &&
                                currentMatch.MatchedSpecId == selectedSpecId &&
                                currentMatch.Score > 0
            ? Math.Round(currentMatch.Score * 100, 1)
            : 0;

        if (mapping == null || !hasSpec)
        {
            if (mapping?.ManualFill == true &&
                adoptedRows?.Contains(rowIndex) == true &&
                (mapping.OverrideAcceptance != null || mapping.OverrideRemark != null))
            {
                return new ExecutionHistoryRowDto
                {
                    RegionId = sourceItem?.RegionId,
                    RegionIndex = sourceItem?.RegionIndex,
                    RowIndex = rowIndex,
                    Project = project,
                    Specification = specification,
                    Acceptance = mapping.OverrideAcceptance,
                    Remark = mapping.OverrideRemark,
                    ConfidencePercent = 0,
                    Status = ExecutionHistoryStatuses.Adopted,
                    IsManualSelected = false,
                    AcceptanceColumnIndex = acceptanceColumnIndex,
                    RemarkColumnIndex = remarkColumnIndex
                };
            }

            return new ExecutionHistoryRowDto
            {
                RegionId = sourceItem?.RegionId,
                RegionIndex = sourceItem?.RegionIndex,
                RowIndex = rowIndex,
                Project = project,
                Specification = specification,
                ConfidencePercent = 0,
                Status = ExecutionHistoryStatuses.Unmatched,
                IsManualSelected = false,
                AcceptanceColumnIndex = acceptanceColumnIndex,
                RemarkColumnIndex = remarkColumnIndex
            };
        }

        var status = adoptedRows?.Contains(rowIndex) == true
            ? ExecutionHistoryStatuses.Adopted
            : ExecutionHistoryStatuses.NotAdopted;

        return new ExecutionHistoryRowDto
        {
            RegionId = sourceItem?.RegionId,
            RegionIndex = sourceItem?.RegionIndex,
            RowIndex = rowIndex,
            Project = project,
            Specification = specification,
            MatchedSpecId = matchedSpec!.Id,
            MatchedProject = matchedSpec.Project,
            MatchedSpecification = matchedSpec.Specification,
            Acceptance = mapping.OverrideAcceptance ?? matchedSpec.Acceptance,
            Remark = mapping.OverrideRemark ?? matchedSpec.Remark,
            ConfidencePercent = confidencePercent,
            Status = status,
            IsManualSelected = mapping.ManualConfirmed,
            AcceptanceColumnIndex = acceptanceColumnIndex,
            RemarkColumnIndex = remarkColumnIndex
        };
    }

    private static List<ExecutionHistorySmartFillRowDto> BuildSmartFillPlaybackRows(
        IReadOnlyCollection<ExecutionHistoryRowDto> rows,
        IReadOnlyCollection<FillMapping> mappings,
        IReadOnlyDictionary<int, MatchPreviewItem>? previewLookup)
    {
        var mappingLookup = mappings.ToDictionary(item => item.RowIndex);

        return rows
            .OrderBy(row => row.RowIndex)
            .Select(row =>
            {
                mappingLookup.TryGetValue(row.RowIndex, out var mapping);
                var previewItem = previewLookup?.GetValueOrDefault(row.RowIndex);
                var matchOrigin = ResolveMatchOrigin(previewItem);
                var manualEdited = mapping != null &&
                                   (mapping.OverrideAcceptance != null || mapping.OverrideRemark != null);

                return new ExecutionHistorySmartFillRowDto
                {
                    RegionId = row.RegionId,
                    RegionIndex = row.RegionIndex,
                    AcceptanceColumnIndex = row.AcceptanceColumnIndex,
                    RemarkColumnIndex = row.RemarkColumnIndex,
                    RowIndex = row.RowIndex,
                    SourceProject = row.Project,
                    SourceSpecification = row.Specification,
                    Status = row.Status,
                    MatchOrigin = matchOrigin,
                    IsManualConfirmed = mapping?.ManualConfirmed == true,
                    IsManualEdited = manualEdited,
                    DisplayTags = BuildDisplayTags(
                        matchOrigin,
                        mapping?.ManualConfirmed == true,
                        manualEdited,
                        row.Status),
                    PreviewSnapshot = BuildPersistedPreviewSnapshot(previewItem, matchOrigin),
                    ExecutionSnapshot = new ExecutionHistorySmartFillExecutionSnapshotDto
                    {
                        SelectedSpecId = row.MatchedSpecId,
                        SelectedProject = row.MatchedProject,
                        SelectedSpecification = row.MatchedSpecification,
                        FinalAcceptance = row.Acceptance,
                        FinalRemark = row.Remark,
                        OverrideAcceptance = mapping?.OverrideAcceptance,
                        OverrideRemark = mapping?.OverrideRemark,
                        ManualConfirmed = mapping?.ManualConfirmed == true,
                        ManualEdited = manualEdited,
                        Status = row.Status
                    }
                };
            })
            .ToList();
    }

    private static ExecutionHistorySmartFillPreviewSnapshotDto BuildPersistedPreviewSnapshot(
        MatchPreviewItem? previewItem,
        string matchOrigin)
    {
        return new ExecutionHistorySmartFillPreviewSnapshotDto
        {
            ConfidenceLevel = previewItem?.ConfidenceLevel ?? "none",
            NoMatchReason = previewItem?.NoMatchReason,
            BestMatch = previewItem?.BestMatch == null
                ? null
                : BuildPersistedBestMatchSnapshot(previewItem.BestMatch)
        };
    }

    private static MatchResultDto BuildPersistedBestMatchSnapshot(MatchResultDto bestMatch)
    {
        return new MatchResultDto
        {
            SpecId = bestMatch.SpecId,
            Project = bestMatch.Project,
            Specification = bestMatch.Specification,
            Acceptance = bestMatch.Acceptance,
            Remark = bestMatch.Remark,
            Score = bestMatch.Score,
            EmbeddingScore = bestMatch.EmbeddingScore,
            ScoreDetails = new Dictionary<string, double>(bestMatch.ScoreDetails),
            Decision = bestMatch.Decision,
            EvidenceSummary = [.. bestMatch.EvidenceSummary],
            ConflictSummary = [.. bestMatch.ConflictSummary],
            Issues = [.. bestMatch.Issues.Select(CloneIssueDto)],
            Entities = [.. bestMatch.Entities.Select(CloneEntityDto)],
            TopCandidates = [.. bestMatch.TopCandidates.Select(CloneCandidateDto)],
            RecalledCandidateCount = bestMatch.RecalledCandidateCount,
            IsAmbiguous = bestMatch.IsAmbiguous,
            ScoreGap = bestMatch.ScoreGap,
            RerankSummary = bestMatch.RerankSummary,
            SelectionMode = bestMatch.SelectionMode,
            SelectionSummary = bestMatch.SelectionSummary,
            MatchBasis = bestMatch.MatchBasis,
            LlmEquivalence = bestMatch.LlmEquivalence,
            ReviewApprovalToken = null,
            ReviewScore = bestMatch.ReviewScore,
            ReviewReason = bestMatch.ReviewReason,
            ReviewCommentary = bestMatch.ReviewCommentary
        };
    }

    private static MatchCandidateDto CloneCandidateDto(MatchCandidateDto candidate)
    {
        return new MatchCandidateDto
        {
            Rank = candidate.Rank,
            SpecId = candidate.SpecId,
            Project = candidate.Project,
            Specification = candidate.Specification,
            Acceptance = candidate.Acceptance,
            Remark = candidate.Remark,
            Score = candidate.Score,
            EmbeddingScore = candidate.EmbeddingScore,
            ScoreDetails = new Dictionary<string, double>(candidate.ScoreDetails),
            Decision = candidate.Decision,
            EvidenceSummary = [.. candidate.EvidenceSummary],
            ConflictSummary = [.. candidate.ConflictSummary],
            Issues = [.. candidate.Issues.Select(CloneIssueDto)],
            Entities = [.. candidate.Entities.Select(CloneEntityDto)],
            RerankSummary = candidate.RerankSummary,
            SelectionMode = candidate.SelectionMode,
            SelectionSummary = candidate.SelectionSummary,
            MatchBasis = candidate.MatchBasis,
            LlmEquivalence = candidate.LlmEquivalence
        };
    }

    private static MatchIssueDto CloneIssueDto(MatchIssueDto issue)
    {
        return new MatchIssueDto
        {
            Code = issue.Code,
            Severity = issue.Severity,
            FieldName = issue.FieldName,
            SourceValue = issue.SourceValue,
            CandidateValue = issue.CandidateValue,
            Message = issue.Message,
            SuggestedAction = issue.SuggestedAction
        };
    }

    private static MatchEntityEvidenceDto CloneEntityDto(MatchEntityEvidenceDto entity)
    {
        return new MatchEntityEvidenceDto
        {
            EntityType = entity.EntityType,
            SourceValue = entity.SourceValue,
            CandidateValue = entity.CandidateValue,
            NormalizedSourceValue = entity.NormalizedSourceValue,
            NormalizedCandidateValue = entity.NormalizedCandidateValue,
            Relation = entity.Relation
        };
    }

    private static ExecutionHistorySmartFillSummaryDto BuildSmartFillSummary(
        ExecutionHistorySmartFillPlaybackDto playback)
    {
        var rows = playback.Files
            .SelectMany(file => file.Sheets)
            .SelectMany(sheet => sheet.Rows)
            .ToList();

        return new ExecutionHistorySmartFillSummaryDto
        {
            ExactMatchedRowCount = rows.Count(row => row.MatchOrigin == ExecutionHistoryMatchOrigins.Exact),
            AiMatchedRowCount = rows.Count(row => row.MatchOrigin == ExecutionHistoryMatchOrigins.Ai),
            ManualConfirmedRowCount = rows.Count(row => row.IsManualConfirmed),
            ManualEditedRowCount = rows.Count(row => row.IsManualEdited),
            NotUsedRowCount = rows.Count(row => row.Status != ExecutionHistoryStatuses.Adopted),
            HasPlaybackArchive = true
        };
    }

    private static string ResolveMatchOrigin(MatchPreviewItem? previewItem)
    {
        if (string.Equals(previewItem?.BestMatch?.SelectionMode, "exactShortcut", StringComparison.Ordinal))
        {
            return ExecutionHistoryMatchOrigins.Exact;
        }

        if (previewItem?.HasMatch == true)
        {
            return ExecutionHistoryMatchOrigins.Ai;
        }

        return ExecutionHistoryMatchOrigins.None;
    }

    private static List<string> BuildDisplayTags(
        string matchOrigin,
        bool isManualConfirmed,
        bool isManualEdited,
        string status)
    {
        var tags = new List<string>();

        if (string.Equals(matchOrigin, ExecutionHistoryMatchOrigins.Exact, StringComparison.Ordinal))
        {
            tags.Add(ExecutionHistoryDisplayTags.ExactMatch);
        }
        else if (string.Equals(matchOrigin, ExecutionHistoryMatchOrigins.Ai, StringComparison.Ordinal))
        {
            tags.Add(ExecutionHistoryDisplayTags.AiMatch);
        }

        if (isManualConfirmed)
        {
            tags.Add(ExecutionHistoryDisplayTags.ManualConfirm);
        }

        if (isManualEdited)
        {
            tags.Add(ExecutionHistoryDisplayTags.ManualWrite);
        }

        if (!string.Equals(status, ExecutionHistoryStatuses.Adopted, StringComparison.Ordinal))
        {
            tags.Add(ExecutionHistoryDisplayTags.NotUsed);
        }

        return tags;
    }

}
