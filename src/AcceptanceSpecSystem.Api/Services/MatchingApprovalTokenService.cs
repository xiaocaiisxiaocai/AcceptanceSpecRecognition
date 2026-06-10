using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.AspNetCore.DataProtection;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 智能填充预览/执行放行令牌服务。
/// </summary>
public sealed class MatchingApprovalTokenService
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(2);
    private readonly IDataProtector _protector;

    public MatchingApprovalTokenService(IDataProtectionProvider dataProtectionProvider)
    {
        _protector = dataProtectionProvider.CreateProtector("MatchingWorkflowSupportService.ReviewApprovalToken.v1");
    }

    internal sealed class ApprovalTokenPayload
    {
        public int UserId { get; init; }

        public int? TableIndex { get; init; }

        public int RowIndex { get; init; }

        public int SpecId { get; init; }

        public string SourceProject { get; init; } = string.Empty;

        public string SourceSpecification { get; init; } = string.Empty;

        public string SpecFingerprint { get; init; } = string.Empty;

        public int? CustomerId { get; init; }

        public int? ProcessId { get; init; }

        public int? MachineModelId { get; init; }

        public MatchingConfig Config { get; init; } = new();

        public DateTimeOffset IssuedAtUtc { get; init; }

        public DateTimeOffset ExpiresAtUtc { get; init; }
    }

    internal sealed class ApprovalTokenBundle
    {
        public int UserId { get; init; }

        public int? CustomerId { get; init; }

        public int? ProcessId { get; init; }

        public int? MachineModelId { get; init; }

        public MatchingConfig Config { get; init; } = new();

        public Dictionary<ApprovalLookupKey, ApprovalTokenPayload> Tokens { get; init; } = [];
    }

    internal readonly record struct ApprovalLookupKey(int? TableIndex, int RowIndex);

    public string IssueToken(
        int userId,
        int? tableIndex,
        int rowIndex,
        int specId,
        string sourceProject,
        string sourceSpecification,
        string? specProject,
        string? specSpecification,
        string? specAcceptance,
        string? specRemark,
        int? customerId,
        int? processId,
        int? machineModelId,
        MatchingConfig config)
    {
        var now = DateTimeOffset.UtcNow;
        var payload = new ApprovalTokenPayload
        {
            UserId = userId,
            TableIndex = tableIndex,
            RowIndex = rowIndex,
            SpecId = specId,
            SourceProject = NormalizeForDedup(sourceProject),
            SourceSpecification = NormalizeForDedup(sourceSpecification),
            SpecFingerprint = ComputeSpecFingerprint(specProject, specSpecification, specAcceptance, specRemark),
            CustomerId = customerId,
            ProcessId = processId,
            MachineModelId = machineModelId,
            Config = CloneMatchingConfig(config),
            IssuedAtUtc = now,
            ExpiresAtUtc = now.Add(TokenLifetime)
        };

        var json = JsonSerializer.Serialize(payload);
        return _protector.Protect(json);
    }

    internal ApprovalTokenBundle? ResolveBundle(
        IEnumerable<(int? TableIndex, FillMapping Mapping)> mappings,
        int executingUserId)
    {
        ApprovalTokenPayload? baseline = null;
        var tokens = new Dictionary<ApprovalLookupKey, ApprovalTokenPayload>();
        var now = DateTimeOffset.UtcNow;

        foreach (var (tableIndex, mapping) in mappings)
        {
            if (string.IsNullOrWhiteSpace(mapping.ReviewApprovalToken))
            {
                continue;
            }

            ApprovalTokenPayload payload;
            try
            {
                var json = _protector.Unprotect(mapping.ReviewApprovalToken);
                payload = JsonSerializer.Deserialize<ApprovalTokenPayload>(json)
                    ?? throw new InvalidOperationException("放行令牌为空");
            }
            catch (Exception ex) when (ex is CryptographicException or JsonException or InvalidOperationException)
            {
                throw new MatchingApiException(400, "放行令牌无效，请重新预览后再执行");
            }

            if (payload.ExpiresAtUtc <= now)
            {
                throw new MatchingApiException(400, "放行令牌已过期，请重新预览后再执行");
            }

            if (payload.UserId != executingUserId)
            {
                throw new MatchingApiException(400, "放行令牌不属于当前用户，请重新预览后再执行");
            }

            if (payload.TableIndex != tableIndex ||
                payload.RowIndex != mapping.RowIndex ||
                payload.SpecId != (mapping.SpecId ?? 0))
            {
                throw new MatchingApiException(400, "放行令牌与当前行或规格不一致，请重新预览后再执行");
            }

            var key = new ApprovalLookupKey(payload.TableIndex, payload.RowIndex);
            if (!tokens.TryAdd(key, payload))
            {
                throw new MatchingApiException(400, "同一行存在重复放行令牌，请重新预览后再执行");
            }

            if (baseline == null)
            {
                baseline = payload;
                continue;
            }

            if (!HasSameContext(baseline, payload))
            {
                throw new MatchingApiException(400, "放行令牌来自不同的预览上下文，请分批执行");
            }
        }

        if (baseline == null)
        {
            return null;
        }

        return new ApprovalTokenBundle
        {
            UserId = baseline.UserId,
            CustomerId = baseline.CustomerId,
            ProcessId = baseline.ProcessId,
            MachineModelId = baseline.MachineModelId,
            Config = CloneMatchingConfig(baseline.Config),
            Tokens = tokens
        };
    }

    internal void EnsureRequestContextMatchesBundle(
        ApprovalTokenBundle? bundle,
        int? customerId,
        int? processId,
        int? machineModelId,
        MatchingConfig config)
    {
        if (bundle == null)
        {
            return;
        }

        if (bundle.CustomerId != customerId ||
            bundle.ProcessId != processId ||
            bundle.MachineModelId != machineModelId ||
            !HasSameMatchingConfig(bundle.Config, config))
        {
            throw new MatchingApiException(400, "放行令牌与当前执行范围或配置不一致，请重新预览后再执行");
        }
    }

    internal bool MatchesToken(
        ApprovalTokenPayload token,
        int selectedSpecId,
        string? sourceProject,
        string? sourceSpecification,
        AcceptanceSpec? selectedSpec)
    {
        if (selectedSpec == null || selectedSpecId <= 0)
        {
            return false;
        }

        return token.SpecId == selectedSpecId &&
               string.Equals(token.SourceProject, NormalizeForDedup(sourceProject), StringComparison.Ordinal) &&
               string.Equals(token.SourceSpecification, NormalizeForDedup(sourceSpecification), StringComparison.Ordinal) &&
               string.Equals(
                   token.SpecFingerprint,
                   ComputeSpecFingerprint(
                       selectedSpec.Project,
                       selectedSpec.Specification,
                       selectedSpec.Acceptance,
                       selectedSpec.Remark),
                   StringComparison.Ordinal);
    }

    private static bool HasSameContext(ApprovalTokenPayload left, ApprovalTokenPayload right)
    {
        return left.UserId == right.UserId &&
               left.CustomerId == right.CustomerId &&
               left.ProcessId == right.ProcessId &&
               left.MachineModelId == right.MachineModelId &&
               HasSameMatchingConfig(left.Config, right.Config);
    }

    private static bool HasSameMatchingConfig(MatchingConfig left, MatchingConfig right)
    {
        return left.EmbeddingServiceId == right.EmbeddingServiceId &&
               left.LlmServiceId == right.LlmServiceId &&
               left.MinScoreThreshold == right.MinScoreThreshold &&
               left.RecallTopK == right.RecallTopK &&
               left.AmbiguityMargin == right.AmbiguityMargin &&
               left.HighConfidenceThreshold == right.HighConfidenceThreshold &&
               left.LlmParallelism == right.LlmParallelism &&
               left.LlmRowTimeoutSeconds == right.LlmRowTimeoutSeconds &&
               left.LlmRetryCount == right.LlmRetryCount &&
               left.LlmCircuitBreakFailures == right.LlmCircuitBreakFailures &&
               left.MatchingMode == right.MatchingMode &&
               left.EnableLlmEquivalenceAdjudication == right.EnableLlmEquivalenceAdjudication &&
               left.EnableDeterministicAutoApply == right.EnableDeterministicAutoApply &&
               left.LlmEquivalenceMinConfidence == right.LlmEquivalenceMinConfidence &&
               left.LlmMaxCallsPerBatch == right.LlmMaxCallsPerBatch &&
               left.ExactMatchOnly == right.ExactMatchOnly &&
               left.FilterEmptySourceRows == right.FilterEmptySourceRows &&
               left.EnableLlmSemanticPriority == right.EnableLlmSemanticPriority &&
               left.LlmSemanticRecallThreshold == right.LlmSemanticRecallThreshold;
    }

    private static MatchingConfig CloneMatchingConfig(MatchingConfig config)
    {
        return new MatchingConfig
        {
            EmbeddingServiceId = config.EmbeddingServiceId,
            LlmServiceId = config.LlmServiceId,
            MinScoreThreshold = config.MinScoreThreshold,
            RecallTopK = config.RecallTopK,
            AmbiguityMargin = config.AmbiguityMargin,
            HighConfidenceThreshold = config.HighConfidenceThreshold,
            LlmParallelism = config.LlmParallelism,
            LlmRowTimeoutSeconds = config.LlmRowTimeoutSeconds,
            LlmRetryCount = config.LlmRetryCount,
            LlmCircuitBreakFailures = config.LlmCircuitBreakFailures,
            MatchingMode = config.MatchingMode,
            EnableLlmEquivalenceAdjudication = config.EnableLlmEquivalenceAdjudication,
            EnableDeterministicAutoApply = config.EnableDeterministicAutoApply,
            LlmEquivalenceMinConfidence = config.LlmEquivalenceMinConfidence,
            LlmMaxCallsPerBatch = config.LlmMaxCallsPerBatch,
            ExactMatchOnly = config.ExactMatchOnly,
            FilterEmptySourceRows = config.FilterEmptySourceRows,
            EnableLlmSemanticPriority = config.EnableLlmSemanticPriority,
            LlmSemanticRecallThreshold = config.LlmSemanticRecallThreshold
        };
    }

    private static string NormalizeForDedup(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(" ", value
            .Trim()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string ComputeSpecFingerprint(
        string? project,
        string? specification,
        string? acceptance,
        string? remark)
    {
        var normalized = string.Join('\n', [
            NormalizeForDedup(project),
            NormalizeForDedup(specification),
            NormalizeForDedup(acceptance),
            NormalizeForDedup(remark)
        ]);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash);
    }
}
