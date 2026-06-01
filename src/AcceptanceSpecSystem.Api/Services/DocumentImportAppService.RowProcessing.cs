using System.Text;
using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Api.Services;

public sealed partial class DocumentImportAppService
{
    private async Task<ImportDuplicateDetectionSession> CreateDuplicateDetectionSessionAsync(
        IReadOnlyCollection<AcceptanceSpec> existingSpecs,
        IEnumerable<string>? confirmedDifferenceKeys,
        IEnumerable<string>? partiallyConfirmedDifferenceKeys,
        IEnumerable<string>? skippedDifferenceKeys,
        ImportDuplicateCheckOptions? options,
        CancellationToken cancellationToken)
    {
        if (HasReplayDifferenceDecisions(
                confirmedDifferenceKeys,
                partiallyConfirmedDifferenceKeys,
                skippedDifferenceKeys))
        {
            _logger.LogInformation("检测到已确认的导入差异决策，本次确认提交跳过 AI/Embedding 重复检测");
            return ImportDuplicateDetectionSession.Disabled(new ImportDuplicateCheckOptions());
        }

        return await _importDuplicateDetectionService.CreateSessionAsync(
            existingSpecs,
            options,
            cancellationToken);
    }

    private async Task ProcessImportRowAsync(
        ImportExecutionContext context,
        int tableIndex,
        ImportRowPayload row,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(row.Project) || string.IsNullOrWhiteSpace(row.Specification))
        {
            AddSkippedRow(
                context,
                row.RowIndex,
                string.IsNullOrWhiteSpace(row.Project) && string.IsNullOrWhiteSpace(row.Specification)
                    ? "项目列与规格列均为空"
                    : string.IsNullOrWhiteSpace(row.Project)
                        ? "项目列为空"
                        : "规格列为空",
                row.RowValues);
            return;
        }

        var normalizedProject = NormalizeText(row.Project);
        var normalizedSpecification = NormalizeText(row.Specification);
        var normalizedAcceptance = NormalizeText(row.Acceptance);
        var normalizedRemark = NormalizeText(row.Remark);

        if (await TryApplyExplicitPendingDecisionAsync(
                context,
                tableIndex,
                row,
                normalizedProject,
                normalizedSpecification,
                normalizedAcceptance,
                normalizedRemark,
                cancellationToken))
        {
            return;
        }

        var exactExisting = context.ExistingSpecs.FirstOrDefault(spec =>
            IsSameContent(spec, normalizedProject, normalizedSpecification, normalizedAcceptance, normalizedRemark));
        if (exactExisting != null)
        {
            AddSkippedRow(context, row.RowIndex, "数据库中已存在完全相同内容，已自动跳过", row.RowValues);
            return;
        }

        var inBatchExact = context.PendingInsertedSpecs.FirstOrDefault(spec =>
            IsSameContent(spec, normalizedProject, normalizedSpecification, normalizedAcceptance, normalizedRemark));
        if (inBatchExact != null)
        {
            AddSkippedRow(context, row.RowIndex, "本次待导入数据中已存在完全相同内容，已自动保留首条", row.RowValues);
            return;
        }

        var projectConflict = context.ExistingSpecs.FirstOrDefault(spec =>
            HasSameProjectAndSpecification(spec, normalizedProject, normalizedSpecification));
        if (projectConflict != null)
        {
            var diffKey = BuildDifferenceKey(
                tableIndex,
                row.RowIndex,
                MatchTypeConflict,
                projectConflict.Id,
                normalizedProject,
                normalizedSpecification,
                normalizedAcceptance,
                normalizedRemark);
            if (await TryApplyPendingDecisionAsync(
                    context,
                    row,
                    diffKey,
                    MatchTypeConflict,
                    projectConflict,
                    null,
                    cancellationToken))
            {
                return;
            }

            // 确认回放时，只重跑有待确认项的工作表；其它工作表可能已先落库。
            // 同一文件内后续发现的相同项目/规格，直接保留已落库首条，避免反复弹确认框。
            if (IsSameFileConfirmationReplayConflict(context, projectConflict))
            {
                AddSkippedRow(context, row.RowIndex, "同一文件已导入相同项目与规格，确认回放时已自动保留首条", row.RowValues);
                return;
            }

            AddPendingDifference(
                context,
                row,
                diffKey,
                MatchTypeConflict,
                projectConflict,
                null);
            return;
        }

        var inBatchConflict = context.PendingInsertedSpecs.FirstOrDefault(spec =>
            HasSameProjectAndSpecification(spec, normalizedProject, normalizedSpecification));
        if (inBatchConflict != null)
        {
            AddSkippedRow(context, row.RowIndex, "本次待导入数据中已存在相同项目与规格，已自动保留首条", row.RowValues);
            return;
        }

        if (context.DuplicateSession.IsEnabled && !context.SkipSemanticDetection)
        {
            var semanticMatch = await context.DuplicateSession.DetectAsync(
                normalizedProject,
                normalizedSpecification,
                cancellationToken);
            if (semanticMatch != null)
            {
                var diffKey = BuildDifferenceKey(
                    tableIndex,
                    row.RowIndex,
                    MatchTypeSemantic,
                    semanticMatch.ExistingSpec.Id,
                    normalizedProject,
                    normalizedSpecification,
                    normalizedAcceptance,
                    normalizedRemark);
                if (await TryApplyPendingDecisionAsync(
                        context,
                        row,
                        diffKey,
                        MatchTypeSemantic,
                        semanticMatch.ExistingSpec,
                        semanticMatch,
                        cancellationToken))
                {
                    return;
                }

                AddPendingDifference(
                    context,
                    row,
                    diffKey,
                    MatchTypeSemantic,
                    semanticMatch.ExistingSpec,
                    semanticMatch);
                return;
            }
        }

        var spec = CreateAcceptanceSpec(
            context.CustomerId,
            context.ProcessId,
            context.MachineModelId,
            context.FileId,
            row.Project,
            row.Specification,
            row.Acceptance,
            row.Remark,
            context.UserId,
            context.OwnerOrgUnitId);
        context.SpecsToInsert.Add(spec);
        context.PendingInsertedSpecs.Add(spec);
    }

    private async Task<bool> TryApplyPendingDecisionAsync(
        ImportExecutionContext context,
        ImportRowPayload row,
        string diffKey,
        string matchType,
        AcceptanceSpec existingSpec,
        ImportSemanticDuplicateMatch? semanticMatch,
        CancellationToken cancellationToken)
    {
        if (context.ConfirmedDifferenceKeys.Contains(diffKey))
        {
            var searchTextChanged =
                NormalizeText(existingSpec.Project) != NormalizeText(row.Project) ||
                NormalizeText(existingSpec.Specification) != NormalizeText(row.Specification);

            OverwriteAcceptanceSpec(
                existingSpec,
                context.CustomerId,
                context.ProcessId,
                context.MachineModelId,
                context.FileId,
                row.Project,
                row.Specification,
                row.Acceptance,
                row.Remark);
            await _specEmbeddingCacheService.RemoveSpecCachesAsync(existingSpec.Id);

            if (searchTextChanged && !context.SkipSemanticDetection)
            {
                await context.DuplicateSession.RefreshCandidateAsync(existingSpec, cancellationToken);
            }

            context.OverwriteCount++;
            return true;
        }

        if (context.PartiallyConfirmedDifferenceKeys.Contains(diffKey))
        {
            OverwriteAcceptanceAndRemark(
                existingSpec,
                context.CustomerId,
                context.ProcessId,
                context.MachineModelId,
                context.FileId,
                row.Acceptance,
                row.Remark);
            await _specEmbeddingCacheService.RemoveSpecCachesAsync(existingSpec.Id);
            context.OverwriteCount++;
            return true;
        }

        if (context.SkippedDifferenceKeys.Contains(diffKey))
        {
            AddSkippedRow(context, row.RowIndex, GetSkippedMessage(matchType), row.RowValues);
            return true;
        }

        return false;
    }


    private async Task<bool> TryApplyExplicitPendingDecisionAsync(
        ImportExecutionContext context,
        int tableIndex,
        ImportRowPayload row,
        string normalizedProject,
        string normalizedSpecification,
        string normalizedAcceptance,
        string normalizedRemark,
        CancellationToken cancellationToken)
    {
        if (context.PendingDecisionMap.Count == 0)
        {
            return false;
        }

        var decisionKey = BuildPendingDecisionLookupKey(
            tableIndex,
            row.RowIndex,
            normalizedProject,
            normalizedSpecification,
            normalizedAcceptance,
            normalizedRemark);

        if (!context.PendingDecisionMap.TryGetValue(decisionKey, out var decision))
        {
            return false;
        }

        var existingSpec = context.ExistingSpecs.FirstOrDefault(spec => spec.Id == decision.ExistingSpecId);
        if (existingSpec == null)
        {
            throw new InvalidOperationException("已确认的重复项对应记录不存在，请重新发起导入");
        }

        if (decision.Decision == DifferenceDecision.Import)
        {
            OverwriteAcceptanceSpec(
                existingSpec,
                context.CustomerId,
                context.ProcessId,
                context.MachineModelId,
                context.FileId,
                row.Project,
                row.Specification,
                row.Acceptance,
                row.Remark);
            await _specEmbeddingCacheService.RemoveSpecCachesAsync(existingSpec.Id);
            context.OverwriteCount++;
            return true;
        }

        if (decision.Decision == DifferenceDecision.PartialImport)
        {
            OverwriteAcceptanceAndRemark(
                existingSpec,
                context.CustomerId,
                context.ProcessId,
                context.MachineModelId,
                context.FileId,
                row.Acceptance,
                row.Remark);
            await _specEmbeddingCacheService.RemoveSpecCachesAsync(existingSpec.Id);
            context.OverwriteCount++;
            return true;
        }

        AddSkippedRow(context, row.RowIndex, GetSkippedMessage(decision.MatchType), row.RowValues);
        return true;
    }

    private static void AddPendingDifference(
        ImportExecutionContext context,
        ImportRowPayload row,
        string diffKey,
        string matchType,
        AcceptanceSpec existingSpec,
        ImportSemanticDuplicateMatch? semanticMatch)
    {
        context.Result.RequiresConfirmation = true;
        context.Result.PendingCount++;
        context.Result.PendingDifferences.Add(new ImportPendingDifference
        {
            Key = diffKey,
            MatchType = matchType,
            RowIndex = row.RowIndex,
            RowValues = row.RowValues,
            IncomingProject = NormalizeText(row.Project),
            IncomingSpecification = NormalizeText(row.Specification),
            IncomingAcceptance = NormalizeNullable(row.Acceptance),
            IncomingRemark = NormalizeNullable(row.Remark),
            ExistingSpecId = existingSpec.Id,
            ExistingProject = existingSpec.Project,
            ExistingSpecification = existingSpec.Specification,
            ExistingAcceptance = existingSpec.Acceptance,
            ExistingRemark = existingSpec.Remark,
            EmbeddingScore = semanticMatch?.EmbeddingScore,
            LlmScore = semanticMatch?.LlmScore,
            FinalScore = semanticMatch?.FinalScore,
            IsHighConfidence = semanticMatch?.IsHighConfidence ?? false,
            ReviewReason = semanticMatch?.ReviewReason,
            ReviewCommentary = semanticMatch?.ReviewCommentary
        });
    }

    private static void AddSkippedRow(
        ImportExecutionContext context,
        int rowIndex,
        string message,
        List<string> rowValues)
    {
        context.Result.SkippedCount++;
        if (!context.PreviewSkippedRows)
        {
            return;
        }

        context.Result.SkippedRows.Add(new ImportSkippedRow
        {
            RowIndex = rowIndex,
            Message = message,
            RowValues = rowValues
        });
    }

    private static string BuildAiImportUnavailableMessage(
        ImportDuplicateCheckOptions? options,
        AiServiceUnavailableException ex)
    {
        var details = ex.Details.Count > 0
            ? $" 详细信息：{string.Join("；", ex.Details)}"
            : string.Empty;

        if (options?.EnableSemanticDuplicateCheck != true)
        {
            return $"AI 服务不可用：{ex.Reason}{details}";
        }

        if (options.EnableLlmDuplicateReview && ex.Reason.Contains("LLM", StringComparison.OrdinalIgnoreCase))
        {
            return $"AI 重复复核不可用，请关闭 LLM 复核或检查 AI 服务配置后重试：{ex.Reason}{details}";
        }

        return $"AI 疑似重复识别不可用，请关闭 AI 模式后重试或检查 Embedding 服务配置：{ex.Reason}{details}";
    }

    private static string GetSkippedMessage(string matchType)
    {
        return matchType switch
        {
            MatchTypeExact => "完全重复数据已确认跳过",
            MatchTypeSemantic => "AI 疑似重复数据已确认跳过",
            _ => "差异数据已确认跳过"
        };
    }

    private static bool HasSameProjectAndSpecification(
        AcceptanceSpec spec,
        string normalizedProject,
        string normalizedSpecification)
    {
        return NormalizeText(spec.Project) == normalizedProject &&
               NormalizeText(spec.Specification) == normalizedSpecification;
    }

    private static bool IsSameFileConfirmationReplayConflict(
        ImportExecutionContext context,
        AcceptanceSpec existingSpec)
    {
        return context.IsConfirmationReplay &&
               existingSpec.WordFileId == context.FileId;
    }


}
