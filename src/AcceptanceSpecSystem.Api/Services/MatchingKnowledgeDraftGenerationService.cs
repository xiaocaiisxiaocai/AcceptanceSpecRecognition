using System.Text;
using System.Security.Claims;
using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Options;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Api.Services;

public sealed class MatchingKnowledgeDraftGenerationService
{
    public const string CategoryEntityAliases = "entityAliases";
    public const string CategoryUnitAliases = "unitAliases";
    public const string CategoryFieldAliases = "fieldAliases";
    public const string CategoryConflictPairs = "conflictPairs";

    private const int MaxSpecCount = 200;
    private const int MaxSourceTextLength = 40000;

    private readonly IUnitOfWork _unitOfWork;
    private readonly MatchingKnowledgeBootstrapper _bootstrapper;
    private readonly IMatchingKnowledgeDraftAiService _draftAiService;
    private readonly IAuthDataScopeService _authDataScopeService;

    public MatchingKnowledgeDraftGenerationService(
        IUnitOfWork unitOfWork,
        MatchingKnowledgeBootstrapper bootstrapper,
        IMatchingKnowledgeDraftAiService draftAiService,
        IAuthDataScopeService authDataScopeService)
    {
        _unitOfWork = unitOfWork;
        _bootstrapper = bootstrapper;
        _draftAiService = draftAiService;
        _authDataScopeService = authDataScopeService;
    }

    public async Task<MatchingKnowledgeDraftResponseDto> GenerateAsync(
        ClaimsPrincipal user,
        GenerateMatchingKnowledgeDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        var category = NormalizeCategory(request.Category);
        if (category == null)
        {
            throw new ArgumentException("不支持的匹配知识分类");
        }

        var scope = await SpecDataScopeHelper.ResolveScopeAsync(user, _authDataScopeService);
        if (scope == null)
        {
            throw new UnauthorizedAccessException("会话缺少用户上下文");
        }

        var sourceText = await BuildSourceTextAsync(request, scope, cancellationToken);

        await _bootstrapper.EnsureInitializedAsync();
        var entity = await _unitOfWork.MatchingKnowledgeConfigs.GetConfigAsync();
        var effective = MatchingKnowledgeComposition.ToDomainModel(MatchingKnowledgeComposition.ToDto(entity));

        var aiItems = await _draftAiService.GenerateAsync(new MatchingKnowledgeDraftAiRequest
        {
            Category = category,
            SourceText = sourceText,
            LlmServiceId = request.LlmServiceId
        }, cancellationToken);

        return new MatchingKnowledgeDraftResponseDto
        {
            Category = category,
            Items = category == CategoryConflictPairs
                ? MarkConflictPairDrafts(aiItems, effective)
                : MarkMappingDrafts(category, aiItems, effective)
        };
    }

    private async Task<string> BuildSourceTextAsync(
        GenerateMatchingKnowledgeDraftRequest request,
        DataScopeResult scope,
        CancellationToken cancellationToken)
    {
        var filter = request.SpecFilter;
        if (filter?.ImportedFrom.HasValue == true &&
            filter.ImportedTo.HasValue &&
            filter.ImportedFrom.Value > filter.ImportedTo.Value)
        {
            throw new ArgumentException("导入开始时间不能晚于结束时间");
        }

        var specs = await _unitOfWork.AcceptanceSpecs.GetFilteredWithIncludesAsync(new AcceptanceSpecQueryOptions
        {
            UserId = scope.UserId,
            IsAll = scope.IsAll,
            IncludeSelf = scope.IncludeSelf,
            OrgUnitIds = scope.OrgUnitIds.ToArray(),
            CustomerId = filter?.CustomerId,
            ProcessId = filter?.ProcessId,
            MachineModelId = filter?.MachineModelId,
            Keyword = filter?.Keyword?.Trim(),
            ImportedFrom = filter?.ImportedFrom,
            ImportedTo = filter?.ImportedTo,
            Page = 1,
            PageSize = MaxSpecCount
        });

        if (specs.Count == 0)
        {
            throw new ArgumentException("当前筛选条件下没有可用于生成的历史验规");
        }

        if (specs.Count > MaxSpecCount)
        {
            throw new ArgumentException("命中的历史验规过多，请收窄筛选条件后重试");
        }
        var builder = new StringBuilder();
        foreach (var spec in specs)
        {
            AppendSpecSource(builder, spec);
        }

        var sourceText = builder.ToString().Trim();
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            throw new ArgumentException("当前筛选条件下没有可用于生成的历史验规");
        }

        if (sourceText.Length > MaxSourceTextLength)
        {
            throw new ArgumentException("筛选结果文本过长，请收窄筛选条件后重试");
        }

        return sourceText;
    }

    private static void AppendSpecSource(StringBuilder builder, AcceptanceSpec spec)
    {
        if (builder.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine("---");
        }

        builder.AppendLine($"客户：{spec.Customer?.Name ?? "-"}");
        builder.AppendLine($"制程：{spec.Process?.Name ?? "-"}");
        builder.AppendLine($"机型：{spec.MachineModel?.Name ?? "-"}");
        builder.AppendLine($"项目：{spec.Project}");
        builder.AppendLine($"规格内容：{spec.Specification}");

        if (!string.IsNullOrWhiteSpace(spec.Acceptance))
        {
            builder.AppendLine($"验收标准：{spec.Acceptance.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(spec.Remark))
        {
            builder.AppendLine($"备注：{spec.Remark.Trim()}");
        }

        builder.AppendLine($"导入时间：{spec.ImportedAt:O}");
    }

    private static string? NormalizeCategory(string? category)
    {
        return category?.Trim() switch
        {
            CategoryEntityAliases => CategoryEntityAliases,
            CategoryUnitAliases => CategoryUnitAliases,
            CategoryFieldAliases => CategoryFieldAliases,
            CategoryConflictPairs => CategoryConflictPairs,
            _ => null
        };
    }

    private static List<MatchingKnowledgeDraftItemDto> MarkMappingDrafts(
        string category,
        IReadOnlyList<MatchingKnowledgeDraftCandidate> aiItems,
        MatchingKnowledge effective)
    {
        var existing = category switch
        {
            CategoryEntityAliases => effective.EntityAliases,
            CategoryUnitAliases => effective.UnitAliases,
            CategoryFieldAliases => effective.FieldAliases,
            _ => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };

        var result = new List<MatchingKnowledgeDraftItemDto>();
        var seenReady = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in aiItems)
        {
            var key = candidate.Key.Trim();
            var value = candidate.Value.Trim();
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            string status;
            string? statusMessage = null;

            if (existing.TryGetValue(key, out var existingValue))
            {
                if (string.Equals(existingValue, value, StringComparison.OrdinalIgnoreCase))
                {
                    status = "duplicate";
                    statusMessage = "与当前生效规则重复，导入时会自动忽略";
                }
                else
                {
                    status = "conflict";
                    statusMessage = $"当前生效值为“{existingValue}”，需人工确认";
                }
            }
            else if (seenReady.TryGetValue(key, out var seenValue))
            {
                if (string.Equals(seenValue, value, StringComparison.OrdinalIgnoreCase))
                {
                    status = "duplicate";
                    statusMessage = "与本次草稿中的其他候选重复";
                }
                else
                {
                    status = "conflict";
                    statusMessage = $"本次草稿中已存在“{key} -> {seenValue}”";
                }
            }
            else
            {
                status = "ready";
                seenReady[key] = value;
            }

            result.Add(new MatchingKnowledgeDraftItemDto
            {
                Key = key,
                Value = value,
                EvidenceSnippet = candidate.EvidenceSnippet,
                Reason = candidate.Reason,
                Status = status,
                StatusMessage = statusMessage
            });
        }

        return result;
    }

    private static List<MatchingKnowledgeDraftItemDto> MarkConflictPairDrafts(
        IReadOnlyList<MatchingKnowledgeDraftCandidate> aiItems,
        MatchingKnowledge effective)
    {
        var existingKeys = effective.ConflictPairs
            .Select(pair => BuildConflictKey(pair.Left, pair.Right))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var result = new List<MatchingKnowledgeDraftItemDto>();
        foreach (var candidate in aiItems)
        {
            var left = candidate.Key.Trim();
            var right = candidate.Value.Trim();
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                continue;
            }

            var pairKey = BuildConflictKey(left, right);
            var status = "ready";
            string? statusMessage = null;

            if (existingKeys.Contains(pairKey))
            {
                status = "duplicate";
                statusMessage = "与当前生效冲突词对重复，导入时会自动忽略";
            }
            else if (!seen.Add(pairKey))
            {
                status = "duplicate";
                statusMessage = "与本次草稿中的其他候选重复";
            }

            result.Add(new MatchingKnowledgeDraftItemDto
            {
                Key = left,
                Value = right,
                EvidenceSnippet = candidate.EvidenceSnippet,
                Reason = candidate.Reason,
                Status = status,
                StatusMessage = statusMessage
            });
        }

        return result;
    }

    private static string BuildConflictKey(string left, string right)
    {
        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase) <= 0
            ? $"{left.Trim()}__{right.Trim()}"
            : $"{right.Trim()}__{left.Trim()}";
    }
}
