using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Core.TextProcessing.Interfaces;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.AspNetCore.Http;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace AcceptanceSpecSystem.Api.Services;

public sealed partial class MatchingWorkflowSupportService
{
    private async Task<MatchingConfig> ResolveExecutionMatchingConfigAsync(MatchConfigDto? dto)
    {
        return await _matchingConfigResolver.ResolveAsync(dto);
    }

    private static Dictionary<int, ExecutionMatchSnapshot> BuildExecutionPreviewSnapshots(
        IReadOnlyCollection<ExecutionHistoryPreviewTableSnapshot>? previewTables)
    {
        if (previewTables == null || previewTables.Count == 0)
        {
            return [];
        }

        return previewTables.ToDictionary(
            table => table.TableIndex,
            table =>
            {
                var sourceRowLookup = table.Items
                    .GroupBy(item => item.RowIndex)
                    .ToDictionary(
                        group => group.Key,
                        group =>
                        {
                            var item = group.First();
                            return new MatchSourceItem
                            {
                                RowIndex = item.RowIndex,
                                Project = item.SourceProject,
                                Specification = item.SourceSpecification
                            };
                        });

                var matchLookup = table.Items
                    .Where(item => item.BestMatch != null)
                    .GroupBy(item => item.RowIndex)
                    .ToDictionary(
                        group => group.Key,
                        group => ConvertPreviewBestMatchToMatchResult(group.First().BestMatch!));

                return new ExecutionMatchSnapshot
                {
                    MatchLookup = matchLookup,
                    SourceRowLookup = sourceRowLookup
                };
            });
    }

    private static bool ExecutionPreviewSnapshotCoversMappings(
        BatchTableFillMapping table,
        ExecutionMatchSnapshot? snapshot,
        MatchingApprovalTokenService.ApprovalTokenBundle? reviewApprovalBundle)
    {
        if (snapshot == null)
        {
            return false;
        }

        foreach (var mapping in table.Mappings)
        {
            if (MappingHasApprovalToken(table.TableIndex, mapping, reviewApprovalBundle) ||
                mapping.SpecId.GetValueOrDefault() <= 0)
            {
                if (!snapshot.SourceRowLookup.ContainsKey(mapping.RowIndex))
                {
                    return false;
                }
                continue;
            }

            // previewTables 来自客户端回传，只能作为性能快照，不能单独决定规格是否可执行。
            // 无服务端签名 token 的规格映射必须重建当前匹配门禁，避免客户端伪造 autoApply。
            return false;
        }

        return true;
    }

    private static HashSet<int> GetRowsRequiringCurrentMatch(
        BatchTableFillMapping table,
        MatchingApprovalTokenService.ApprovalTokenBundle? reviewApprovalBundle)
    {
        return table.Mappings
            .Where(mapping =>
                mapping.SpecId.GetValueOrDefault() > 0 &&
                !MappingHasApprovalToken(table.TableIndex, mapping, reviewApprovalBundle))
            .Select(mapping => mapping.RowIndex)
            .ToHashSet();
    }

    private static bool MissingSourceRowsForTokenValidation(
        BatchTableFillMapping table,
        ExecutionMatchSnapshot? snapshot,
        MatchingApprovalTokenService.ApprovalTokenBundle? reviewApprovalBundle)
    {
        if (reviewApprovalBundle == null)
        {
            return false;
        }

        return table.Mappings.Any(mapping =>
            MappingHasApprovalToken(table.TableIndex, mapping, reviewApprovalBundle) &&
            snapshot?.SourceRowLookup.ContainsKey(mapping.RowIndex) != true);
    }

    private static bool MappingHasApprovalToken(
        int tableIndex,
        FillMapping mapping,
        MatchingApprovalTokenService.ApprovalTokenBundle? reviewApprovalBundle)
    {
        return reviewApprovalBundle?.Tokens.ContainsKey(
            new MatchingApprovalTokenService.ApprovalLookupKey(tableIndex, mapping.RowIndex)) == true;
    }

    private static MatchResult ConvertPreviewBestMatchToMatchResult(MatchResultDto dto)
    {
        return new MatchResult
        {
            SourceText = string.Empty,
            MatchedText = $"{dto.Project} {dto.Specification}".Trim(),
            MatchedSpecId = dto.SpecId,
            MatchedProject = dto.Project,
            MatchedSpecification = dto.Specification,
            MatchedAcceptance = dto.Acceptance,
            MatchedRemark = dto.Remark,
            Score = dto.Score,
            EmbeddingScore = dto.EmbeddingScore,
            ScoreDetails = new Dictionary<string, double>(dto.ScoreDetails),
            Decision = ParseMatchDecision(dto.Decision),
            IsAmbiguous = dto.IsAmbiguous,
            ScoreGap = dto.ScoreGap,
            RerankSummary = dto.RerankSummary,
            LlmEquivalence = ConvertPreviewLlmEquivalence(dto.LlmEquivalence)
        };
    }

    private static MatchDecision ParseMatchDecision(string? value)
    {
        return value switch
        {
            "autoApply" => MatchDecision.AutoApply,
            "reject" => MatchDecision.Reject,
            _ => MatchDecision.ManualReview
        };
    }

    private static LlmEquivalenceAdjudicationResult? ConvertPreviewLlmEquivalence(LlmEquivalenceDto? dto)
    {
        if (dto == null)
        {
            return null;
        }

        return new LlmEquivalenceAdjudicationResult
        {
            Verdict = ParseLlmEquivalenceVerdict(dto.Verdict),
            ReasonType = ParseLlmEquivalenceReasonType(dto.ReasonType),
            Confidence = dto.Confidence,
            Reason = dto.Reason
        };
    }

    private static LlmEquivalenceVerdict ParseLlmEquivalenceVerdict(string? value)
    {
        return value switch
        {
            "equivalent" => LlmEquivalenceVerdict.Equivalent,
            "different" => LlmEquivalenceVerdict.Different,
            _ => LlmEquivalenceVerdict.Uncertain
        };
    }

    private static LlmEquivalenceReasonType ParseLlmEquivalenceReasonType(string? value)
    {
        return value switch
        {
            "format_only" => LlmEquivalenceReasonType.FormatOnly,
            "punctuation_only" => LlmEquivalenceReasonType.PunctuationOnly,
            "equivalent_expression" => LlmEquivalenceReasonType.EquivalentExpression,
            "symbol_equivalent" => LlmEquivalenceReasonType.SymbolEquivalent,
            "semantic_difference" => LlmEquivalenceReasonType.SemanticDifference,
            "symbol_conflict" => LlmEquivalenceReasonType.SymbolConflict,
            _ => LlmEquivalenceReasonType.Uncertain
        };
    }

    private async Task<ExecutionMatchSnapshot> BuildCurrentMatchLookupAsync(
        WordFile wordFile,
        int tableIndex,
        int? projectColumnIndex,
        int? specificationColumnIndex,
        int? headerRowStart,
        int? headerRowCount,
        int? dataStartRow,
        bool filterEmptySourceRows,
        int? customerId,
        int? processId,
        int? machineModelId,
        MatchingConfig config,
        DataScopeResult scope,
        ExecutionMatchSnapshot? existingSnapshot = null,
        IReadOnlySet<int>? rowIndexesRequiringCurrentMatch = null)
    {
        if (!projectColumnIndex.HasValue || !specificationColumnIndex.HasValue)
        {
            return new ExecutionMatchSnapshot();
        }

        var sourceRows = await _documentTableAccessService.ExtractMatchSourceItemsAsync(
            wordFile,
            tableIndex,
            projectColumnIndex.Value,
            specificationColumnIndex.Value,
            headerRowStart,
            headerRowCount,
            dataStartRow,
            filterEmptySourceRows);

        if (sourceRows.Count == 0)
        {
            throw Failure(400, "无法重建执行前的源项目/规格数据，请重新预览后再执行");
        }

        var sourceRowLookup = sourceRows.ToDictionary(item => item.RowIndex);
        var rowsToMatch = rowIndexesRequiringCurrentMatch == null
            ? sourceRows
            : sourceRows.Where(item => rowIndexesRequiringCurrentMatch.Contains(item.RowIndex)).ToList();

        var lookup = existingSnapshot?.MatchLookup != null
            ? new Dictionary<int, MatchResult>(existingSnapshot.MatchLookup)
            : [];

        if (rowIndexesRequiringCurrentMatch != null)
        {
            foreach (var rowIndex in rowIndexesRequiringCurrentMatch)
            {
                // 这些行必须用服务端当前匹配结果覆盖，不能沿用客户端回传的预览决策。
                lookup.Remove(rowIndex);
            }
        }

        if (rowsToMatch.Count == 0)
        {
            return new ExecutionMatchSnapshot
            {
                MatchLookup = lookup,
                SourceRowLookup = sourceRowLookup
            };
        }

        var candidates = await _matchingCandidateProvider.GetCandidatesAsync(
            customerId,
            processId,
            machineModelId,
            scope,
            config.EmbeddingServiceId,
            hydrateEmbeddings: false);

        if (candidates.Count == 0)
        {
            return new ExecutionMatchSnapshot
            {
                MatchLookup = lookup,
                SourceRowLookup = sourceRowLookup
            };
        }

        var tpSession = await _textPipeline.CreateSessionAsync();
        var processedCandidates = BuildProcessedCandidates(candidates, tpSession);

        var sourceItems = rowsToMatch.Select(item => new MatchSource
        {
            Project = tpSession.Process(item.Project),
            Specification = tpSession.Process(item.Specification)
        }).ToList();

        BatchMatchResult batchResult;
        try
        {
            if (config.ExactMatchOnly || !RequiresSemanticMatching(sourceItems, processedCandidates, config))
            {
                batchResult = BuildExactMatchBatchResult(sourceItems, processedCandidates, config);
            }
            else
            {
                await _matchingCandidateProvider.HydrateCandidateEmbeddingsAsync(
                    candidates,
                    config.EmbeddingServiceId,
                    CancellationToken.None);
                processedCandidates = BuildProcessedCandidates(candidates, tpSession);
                batchResult = await _matchingService.BatchMatchAsync(sourceItems, processedCandidates, config);
            }
        }
        catch (AiServiceUnavailableException ex)
        {
            throw Failure(400, $"Embedding 服务不可用: {ex.Reason}");
        }

        for (var index = 0; index < rowsToMatch.Count && index < batchResult.Results.Count; index++)
        {
            var result = batchResult.Results[index];
            if (!result.MatchedSpecId.HasValue)
            {
                continue;
            }

            lookup[rowsToMatch[index].RowIndex] = result;
        }

        return new ExecutionMatchSnapshot
        {
            MatchLookup = lookup,
            SourceRowLookup = sourceRowLookup
        };
    }


    private static BatchMatchResult BuildExactMatchBatchResult(
        IReadOnlyList<MatchSource> sources,
        IReadOnlyList<MatchCandidate> candidates,
        MatchingConfig config)
    {
        var lookup = BuildExactMatchLookup(candidates, config);

        return new BatchMatchResult
        {
            Results = sources
                .Select(source =>
                {
                    var key = BuildExactMatchLookupKey(source.Project, source.Specification, config);
                    return lookup.TryGetValue(key, out var candidatesForKey)
                        ? CreateExactMatchResult(source, candidatesForKey, config)
                        : CreateNoMatchResult(source, config);
                })
                .ToList()
        };
    }

    private static Dictionary<string, List<MatchCandidate>> BuildExactMatchLookup(
        IReadOnlyList<MatchCandidate> candidates,
        MatchingConfig config)
    {
        return candidates
            .GroupBy(candidate => BuildExactMatchLookupKey(candidate.Project, candidate.Specification, config))
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(candidate => HasText(candidate.Acceptance))
                    .ThenByDescending(candidate => HasText(candidate.Remark))
                    .ThenByDescending(candidate => candidate.SpecId)
                    .ToList());
    }

    private static string BuildExactMatchLookupKey(
        string? project,
        string? specification,
        MatchingConfig config)
    {
        return config.MatchingMode == MatchingMode.SpecificationOnly
            ? MatchingCandidateProvider.BuildCandidateDedupKey(null, specification)
            : MatchingCandidateProvider.BuildCandidateDedupKey(project, specification);
    }

    private static MatchResult CreateNoMatchResult(MatchSource source, MatchingConfig config)
    {
        return new MatchResult
        {
            SourceText = source.CombinedText,
            MinScoreThreshold = config.MinScoreThreshold,
            HighConfidenceThreshold = config.HighConfidenceThreshold,
            Decision = MatchDecision.ManualReview
        };
    }

    private static bool RequiresSemanticMatching(
        IReadOnlyList<MatchSource> sources,
        IReadOnlyList<MatchCandidate> candidates,
        MatchingConfig config)
    {
        if (sources.Count == 0)
        {
            return false;
        }

        var lookup = BuildExactMatchLookup(candidates, config);

        return sources.Any(source =>
            !lookup.ContainsKey(BuildExactMatchLookupKey(source.Project, source.Specification, config)));
    }

    private static List<MatchCandidate> BuildProcessedCandidates(
        IEnumerable<MatchCandidate> candidates,
        TextProcessingSession tpSession)
    {
        return candidates.Select(candidate => new MatchCandidate
        {
            SpecId = candidate.SpecId,
            Project = tpSession.Process(candidate.Project),
            Specification = tpSession.Process(candidate.Specification),
            Acceptance = candidate.Acceptance,
            Remark = candidate.Remark,
            Embedding = candidate.Embedding
        }).ToList();
    }

    private static MatchResult CreateExactMatchResult(
        MatchSource source,
        IReadOnlyList<MatchCandidate> candidates,
        MatchingConfig config)
    {
        var candidate = candidates[0];
        var isSpecificationOnly = config.MatchingMode == MatchingMode.SpecificationOnly;
        var hasMultipleCandidates = isSpecificationOnly && candidates.Count > 1;
        var scoreDetails = new Dictionary<string, double>
        {
            ["Final"] = 1,
            ["Embedding"] = 1,
            ["Exact"] = 1
        };

        var equivalence = new LlmEquivalenceAdjudicationResult
        {
            Verdict = LlmEquivalenceVerdict.Equivalent,
            ReasonType = LlmEquivalenceReasonType.EquivalentExpression,
            Reason = isSpecificationOnly
                ? "规格文本完全一致，已按用户选择的仅规格模式命中"
                : "项目与规格文本完全一致，已直接视为等价",
            Confidence = 1
        };

        return new MatchResult
        {
            SourceText = source.CombinedText,
            MatchedText = candidate.CombinedText,
            MatchedSpecId = candidate.SpecId,
            MatchedProject = candidate.Project,
            MatchedSpecification = candidate.Specification,
            MatchedAcceptance = candidate.Acceptance,
            MatchedRemark = candidate.Remark,
            Score = 1,
            EmbeddingScore = 1,
            ScoreDetails = scoreDetails,
            Decision = hasMultipleCandidates ? MatchDecision.ManualReview : MatchDecision.AutoApply,
            SelectionMode = MatchSelectionMode.ExactShortcut,
            SelectionSummary = isSpecificationOnly
                ? hasMultipleCandidates
                    ? "规格精确一致，但同规格存在多条候选，需人工确认"
                    : "规格精确一致，按仅规格模式直接命中"
                : "项目与规格精确一致，直接命中",
            MatchBasis = isSpecificationOnly ? MatchBasis.Specification : MatchBasis.ProjectSpecification,
            RecalledCandidateCount = isSpecificationOnly ? candidates.Count : 1,
            IsAmbiguous = hasMultipleCandidates,
            MinScoreThreshold = config.MinScoreThreshold,
            HighConfidenceThreshold = config.HighConfidenceThreshold,
            LlmEquivalence = equivalence,
            TopCandidates = candidates
                .Take(isSpecificationOnly ? 3 : 1)
                .Select((item, index) => new MatchCandidateSnapshot
                {
                    Rank = index + 1,
                    SpecId = item.SpecId,
                    Project = item.Project,
                    Specification = item.Specification,
                    Acceptance = item.Acceptance,
                    Remark = item.Remark,
                    Score = 1,
                    EmbeddingScore = 1,
                    ScoreDetails = scoreDetails,
                    SelectionMode = MatchSelectionMode.ExactShortcut,
                    SelectionSummary = isSpecificationOnly ? "规格精确一致" : "项目与规格精确一致，直接命中",
                    MatchBasis = isSpecificationOnly ? MatchBasis.Specification : MatchBasis.ProjectSpecification,
                    LlmEquivalence = index == 0 ? equivalence : null
                })
                .ToList()
        };
    }

}
