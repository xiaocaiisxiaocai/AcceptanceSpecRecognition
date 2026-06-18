using AcceptanceSpecSystem.Api.DTOs;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 智能填充执行记录精简器。
///
/// 大批量任务的回放归档（<see cref="ExecutionHistorySmartFillPlaybackDto"/>）体量随行数增长，
/// 超出持久化上限时，旧逻辑会整段丢弃回放，导致大任务在行级分析上成为盲区。
///
/// 本精简器剥离“重负载”字段（原文文本、Top 候选明细、证据/冲突摘要、问题描述、裁决理由等），
/// 但保留“逐行分析信号”（行号、标签、命中来源、决策、置信度、选定方式、AI 裁决结论与问题码），
/// 使大任务在大幅缩减体量后仍可被 <c>tools/SmartFillInsightReport</c> 与前端回放（降级）使用。
/// </summary>
internal static class ExecutionHistorySmartFillSlimmer
{
    private const int MaxSlimmedSourceTextLength = 80;

    /// <summary>
    /// 就地精简执行记录详情：丢弃通用明细 Files，并逐行剥离 SmartFillPlayback 的重负载字段。
    /// 调用方负责在精简后重新评估体量。
    /// </summary>
    public static void SlimInPlace(ExecutionHistoryDetailDto detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        // 通用明细对 smart-fill 是冗余的（回放走 SmartFillPlayback），直接丢弃。
        detail.Files = [];

        var playback = detail.SmartFillPlayback;
        if (playback == null)
        {
            return;
        }

        playback.IsSlimmed = true;

        foreach (var file in playback.Files ?? [])
        {
            foreach (var sheet in file.Sheets ?? [])
            {
                foreach (var row in sheet.Rows ?? [])
                {
                    SlimRow(row);
                }
            }
        }
    }

    /// <summary>
    /// 二级精简：常规精简后仍超限时，保留文件/表格/行骨架和执行状态，剥离匹配详情。
    /// 这样前端仍可进入行级回放视图，而不是退化成仅汇总提示。
    /// </summary>
    public static void SlimToPlaybackOutlineInPlace(ExecutionHistoryDetailDto detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        detail.Files = [];

        var playback = detail.SmartFillPlayback;
        if (playback == null)
        {
            return;
        }

        playback.IsSlimmed = true;

        foreach (var file in playback.Files ?? [])
        {
            foreach (var sheet in file.Sheets ?? [])
            {
                foreach (var row in sheet.Rows ?? [])
                {
                    SlimRow(row);
                    row.SourceProject = string.Empty;
                    row.SourceSpecification = string.Empty;
                    row.PreviewSnapshot = new ExecutionHistorySmartFillPreviewSnapshotDto
                    {
                        ConfidenceLevel = row.PreviewSnapshot?.ConfidenceLevel ?? "none",
                        NoMatchReason = row.PreviewSnapshot?.NoMatchReason
                    };
                }
            }
        }
    }

    private static void SlimRow(ExecutionHistorySmartFillRowDto row)
    {
        // 行级来源文本在大表中会重复上百/上千次，是精简后仍超限的主要来源。
        // 保留短摘要，既能定位行内容，又避免退化成仅汇总提示。
        row.SourceProject = TruncateText(row.SourceProject);
        row.SourceSpecification = TruncateText(row.SourceSpecification);

        // 执行快照：保留选中规格ID/状态/人工标记，剥离原文文本。
        var execution = row.ExecutionSnapshot;
        execution.SelectedProject = null;
        execution.SelectedSpecification = null;
        execution.FinalAcceptance = null;
        execution.FinalRemark = null;
        execution.OverrideAcceptance = null;
        execution.OverrideRemark = null;

        var best = row.PreviewSnapshot?.BestMatch;
        if (best == null)
        {
            return;
        }

        // 文本、候选明细和逐算法得分会按行重复，精简归档只保留最终得分与决策信号。
        best.Project = string.Empty;
        best.Specification = string.Empty;
        best.Acceptance = null;
        best.Remark = null;
        best.ScoreDetails = [];
        best.EvidenceSummary = [];
        best.ConflictSummary = [];
        best.Entities = [];
        best.TopCandidates = [];
        best.RerankSummary = null;
        best.SelectionSummary = null;
        best.ReviewReason = null;
        best.ReviewCommentary = null;

        // AI 裁决：保留 verdict/reasonType/confidence（分析信号），剥离自由文本理由。
        if (best.LlmEquivalence is { } equivalence)
        {
            equivalence.Reason = null;
        }

        // 问题：仅保留首个问题的 Code/Severity/FieldName 作为分析信号，剥离冗长值与用户说明。
        if (best.Issues.Count > 1)
        {
            best.Issues = best.Issues.Take(1).ToList();
        }

        foreach (var issue in best.Issues)
        {
            issue.SourceValue = null;
            issue.CandidateValue = null;
            issue.Message = string.Empty;
            issue.SuggestedAction = null;
        }
    }

    private static string TruncateText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        return normalized.Length <= MaxSlimmedSourceTextLength
            ? normalized
            : normalized[..MaxSlimmedSourceTextLength];
    }
}
