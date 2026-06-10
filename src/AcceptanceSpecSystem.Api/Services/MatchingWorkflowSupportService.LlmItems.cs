using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Core.TextProcessing.Interfaces;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.AspNetCore.Http;

namespace AcceptanceSpecSystem.Api.Services;

public sealed partial class MatchingWorkflowSupportService
{
    private async Task<List<LlmStreamItemContext>> BuildAuthoritativeLlmStreamItemsAsync(
        IReadOnlyList<MatchLlmStreamItem> requestItems,
        IReadOnlyList<MatchCandidate> candidates,
        MatchingConfig config,
        CancellationToken cancellationToken)
    {
        if (requestItems.Count == 0)
        {
            return [];
        }

        if (candidates.Count == 0)
        {
            return requestItems
                .Select(item => new LlmStreamItemContext
                {
                    Item = CreateNoMatchLlmStreamItem(item)
                })
                .ToList();
        }

        var candidateList = candidates.ToList();
        var tpSession = await _textPipeline.CreateSessionAsync();
        var processedCandidates = BuildProcessedCandidates(candidateList, tpSession);

        var sourceItems = requestItems.Select(item => new MatchSource
        {
            Project = tpSession.Process(item.SourceProject),
            Specification = tpSession.Process(item.SourceSpecification)
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
                    candidateList,
                    config.EmbeddingServiceId,
                    cancellationToken);
                processedCandidates = BuildProcessedCandidates(candidateList, tpSession);
                batchResult = await _matchingService.BatchMatchAsync(sourceItems, processedCandidates, config, cancellationToken: cancellationToken);
            }
        }
        catch (AiServiceUnavailableException ex)
        {
            throw Failure(400, $"Embedding 服务不可用: {ex.Reason}");
        }

        var normalizedItems = new List<LlmStreamItemContext>(requestItems.Count);
        for (var index = 0; index < requestItems.Count; index++)
        {
            var requestItem = requestItems[index];
            var result = index < batchResult.Results.Count
                ? batchResult.Results[index]
                : null;

            normalizedItems.Add(CreateAuthoritativeLlmStreamItemContext(requestItem, result));
        }

        return normalizedItems;
    }


    private static LlmStreamItemKey GetLlmStreamItemKey(MatchLlmStreamItem item)
    {
        return new LlmStreamItemKey(item.TableIndex, item.RowIndex);
    }

    private static string FormatStreamRowKey(int? tableIndex, int rowIndex)
    {
        return $"{tableIndex.GetValueOrDefault()}:{rowIndex}";
    }

    private static bool RequiresReviewForStreamItem(MatchLlmStreamItem item)
    {
        return item.BestMatchSpecId.HasValue &&
               (item.IsAmbiguous || RequiresManualReviewByEquivalenceVerdict(item.LlmEquivalenceVerdict));
    }

    private static string FormatStreamItemLocation(MatchLlmStreamItem item)
    {
        return item.TableIndex.HasValue
            ? $"表{item.TableIndex.Value + 1}/行{item.RowIndex + 1}"
            : $"行{item.RowIndex + 1}";
    }

    private async Task<DataScopeResult?> ResolveSpecScopeAsync(ClaimsPrincipal user)
    {
        return await SpecDataScopeHelper.ResolveScopeAsync(user, _authDataScopeService);
    }

    private async Task<Dictionary<int, AcceptanceSpec>> GetScopedSpecDictionaryAsync(
        IEnumerable<int> specIds,
        DataScopeResult scope)
    {
        var distinctIds = specIds
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        if (distinctIds.Count == 0)
        {
            return new Dictionary<int, AcceptanceSpec>();
        }

        var specs = await _unitOfWork.AcceptanceSpecs.FindAsync(s => distinctIds.Contains(s.Id));
        return SpecDataScopeHelper.ApplyScope(specs, scope)
            .ToDictionary(spec => spec.Id);
    }

    private static LlmStreamItemContext CreateAuthoritativeLlmStreamItemContext(
        MatchLlmStreamItem requestItem,
        MatchResult? result)
    {
        if (result == null || !result.MatchedSpecId.HasValue)
        {
            return new LlmStreamItemContext
            {
                Item = CreateNoMatchLlmStreamItem(requestItem)
            };
        }

        var item = new MatchLlmStreamItem
        {
            TableIndex = requestItem.TableIndex,
            RowIndex = requestItem.RowIndex,
            SourceProject = requestItem.SourceProject,
            SourceSpecification = requestItem.SourceSpecification,
            BestMatchSpecId = result.MatchedSpecId,
            BestMatchScore = result.Score,
            ScoreDetails = result.ScoreDetails,
            Decision = MatchingResultDtoMapper.ToDecisionKey(result.Decision),
            LlmEquivalenceVerdict = result.LlmEquivalence == null
                ? GetAuthoritativeRequestManualReviewVerdict(requestItem)
                : MatchingResultDtoMapper.ToEquivalenceVerdictKey(result.LlmEquivalence.Verdict),
            IsAmbiguous = result.IsAmbiguous,
            EvidenceSummary = [.. result.Evidence.Summary],
            ConflictSummary = [.. result.Evidence.Conflicts]
        };

        return new LlmStreamItemContext
        {
            Item = item,
            AuthoritativeBestMatch = MatchingResultDtoMapper.ToMatchResultDto(result)
        };
    }

    private static MatchLlmStreamItem CreateNoMatchLlmStreamItem(MatchLlmStreamItem item)
    {
        return new MatchLlmStreamItem
        {
            TableIndex = item.TableIndex,
            RowIndex = item.RowIndex,
            SourceProject = item.SourceProject,
            SourceSpecification = item.SourceSpecification,
            BestMatchSpecId = null,
            BestMatchScore = null,
            ScoreDetails = null,
            Decision = "manualReview",
            LlmEquivalenceVerdict = null,
            IsAmbiguous = false,
            EvidenceSummary = [],
            ConflictSummary = []
        };
    }

    private static string? GetAuthoritativeRequestManualReviewVerdict(MatchLlmStreamItem item)
    {
        return RequiresManualReviewByEquivalenceVerdict(item.LlmEquivalenceVerdict)
            ? item.LlmEquivalenceVerdict
            : null;
    }

    private static string BuildReviewTrigger(MatchLlmStreamItem item)
    {
        if (RequiresManualReviewByEquivalenceVerdict(item.LlmEquivalenceVerdict))
        {
            return "AI 等价裁决已要求人工确认，禁止旧复核反向放行";
        }

        if (item.ConflictSummary?.Count > 0)
        {
            return "存在结构化冲突证据，需要结合 AI 复核确认";
        }

        if (!string.IsNullOrWhiteSpace(item.Decision) &&
            string.Equals(item.Decision, "manualReview", StringComparison.OrdinalIgnoreCase))
        {
            return "证据不足或候选接近，需要人工/LLM进一步复核";
        }

        return "需要补充复核结论";
    }
}
