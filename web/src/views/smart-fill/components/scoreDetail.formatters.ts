import type {
  MatchCandidateOption,
  MatchIssue,
  MatchLlmStreamDeltaEventData,
  MatchLlmStreamDoneEventData,
  MatchLlmStreamErrorEventData,
  MatchLlmStreamEvent,
  MatchLlmStreamEventData,
  MatchSelectionMode,
  MatchPreviewItem,
  MatchResult
} from "@/api/matching";
import { isLlmEquivalenceDecisionRisk } from "./scoreDetail.llmEquivalence.ts";

export const formatScore = (score: number) => `${(score * 100).toFixed(1)}%`;

export const formatOptionalScore = (score?: number) => {
  if (score === undefined || score === null) return "-";
  return formatScore(score);
};

export const getConfidenceClass = (
  level: string
): "success" | "warning" | "danger" | "info" => {
  const map: Record<string, "success" | "warning" | "danger" | "info"> = {
    high: "success",
    medium: "warning",
    low: "danger"
  };
  return map[level] || "info";
};

export const getConfidenceText = (level: string) => {
  const map: Record<string, string> = {
    high: "高",
    medium: "中",
    low: "低"
  };
  return map[level] || "无";
};

export const getDecisionText = (decision?: string) => {
  switch (decision) {
    case "autoApply":
      return "自动采用";
    case "reject":
      return "暂不采用";
    case "manualReview":
    default:
      return "人工确认";
  }
};

export const getDecisionTagType = (
  decision?: string
): "success" | "warning" | "danger" | "info" => {
  switch (decision) {
    case "autoApply":
      return "success";
    case "reject":
      return "danger";
    case "manualReview":
      return "warning";
    default:
      return "info";
  }
};

export const getIssueTagType = (
  severity?: string
): "danger" | "warning" | "info" | "success" => {
  switch (severity) {
    case "high":
      return "danger";
    case "medium":
    case "warning":
      return "warning";
    case "low":
    case "info":
      return "info";
    default:
      return "info";
  }
};

export const getIssueSeverityText = (severity?: string) => {
  switch (severity) {
    case "high":
      return "高风险";
    case "medium":
    case "warning":
      return "需确认";
    case "low":
      return "低风险";
    case "info":
      return "提示";
    default:
      return "问题";
  }
};

export const getIssueFieldText = (issue: MatchIssue) =>
  issue.fieldName || "未指定字段";

export const getCandidateDelta = (
  candidate: MatchCandidateOption,
  topCandidates: MatchCandidateOption[]
) => {
  const first = topCandidates[0];
  if (!first || candidate.rank === 1) return "最佳候选";
  const delta = (first.score - candidate.score) * 100;
  return `较 Top1 低 ${delta >= 0 ? delta.toFixed(1) : "0.0"} 分`;
};

export const getSelectionModeText = (selectionMode?: MatchSelectionMode) => {
  switch (selectionMode) {
    case "exactShortcut":
      return "100%精确直达";
    case "embeddingTop1":
      return "本地 Top1";
    case "aiRerank":
      return "AI 改选";
    default:
      return "未标注";
  }
};

export const getSelectionModeTagType = (
  selectionMode?: MatchSelectionMode
): "success" | "info" | "warning" => {
  switch (selectionMode) {
    case "exactShortcut":
      return "success";
    case "embeddingTop1":
      return "info";
    case "aiRerank":
      return "warning";
    default:
      return "info";
  }
};

export const getSelectionModeDescription = (
  selectionMode?: MatchSelectionMode
) => {
  switch (selectionMode) {
    case "exactShortcut":
      return "项目与规格命中精确直达，未走 AI 改选。";
    case "embeddingTop1":
      return "当前结果为本地召回 Top1，未触发 AI 改选。";
    case "aiRerank":
      return "当前结果由 AI 在候选集中重排后改选。";
    default:
      return "";
  }
};

export type SmartFillReviewStatus =
  | "none"
  | "direct"
  | "completed"
  | "manual"
  | "pending"
  | "waiting"
  | "streaming"
  | "blocked"
  | "error";

export type SmartFillFillRecommendation =
  | "fillable"
  | "review"
  | "blocked"
  | "unmatched";

const getDecision = (bestMatch?: MatchResult | null) =>
  bestMatch?.decision ?? "manualReview";

const hasCustomerVisibleRisk = (
  item: MatchPreviewItem,
  sourceBestRowCount = 0
) => {
  if (!item.bestMatch) return false;

  const hasMediumOrHighIssues = (item.bestMatch.issues ?? []).some(issue =>
    ["high", "medium", "warning"].includes(issue.severity || "")
  );

  return (
    getDecision(item.bestMatch) !== "autoApply" ||
    isLlmEquivalenceDecisionRisk(item.bestMatch.llmEquivalence) ||
    sourceBestRowCount > 0 ||
    item.confidenceLevel !== "high" ||
    !!item.bestMatch.isAmbiguous ||
    !!item.bestMatch.conflictSummary?.length ||
    !!item.llmReviewError ||
    hasMediumOrHighIssues
  );
};

export const shouldStreamMatchReview = (bestMatch?: MatchResult | null) => {
  if (!bestMatch?.specId) return false;

  return (
    getDecision(bestMatch) === "manualReview" &&
    (bestMatch.isAmbiguous === true ||
      isLlmEquivalenceDecisionRisk(bestMatch.llmEquivalence))
  );
};

export const getSmartFillTableState = (
  item: MatchPreviewItem,
  options: { llmStreaming?: boolean } = {}
): {
  reviewStatus: SmartFillReviewStatus;
  fillRecommendation: SmartFillFillRecommendation;
  hasCustomerVisibleRisk: boolean;
} => {
  const bestMatch = item.bestMatch;
  if (!bestMatch) {
    return {
      reviewStatus: "none" as SmartFillReviewStatus,
      fillRecommendation: "unmatched" as SmartFillFillRecommendation,
      hasCustomerVisibleRisk: false
    };
  }

  if (getDecision(bestMatch) === "reject") {
    return {
      reviewStatus: "blocked" as SmartFillReviewStatus,
      fillRecommendation: "blocked" as SmartFillFillRecommendation,
      hasCustomerVisibleRisk: true
    };
  }

  if (item.llmReviewError) {
    return {
      reviewStatus: "error" as SmartFillReviewStatus,
      fillRecommendation: "review" as SmartFillFillRecommendation,
      hasCustomerVisibleRisk: true
    };
  }

  if (item.llmReviewStage === "streaming") {
    return {
      reviewStatus: "streaming" as SmartFillReviewStatus,
      fillRecommendation: "review" as SmartFillFillRecommendation,
      hasCustomerVisibleRisk: true
    };
  }

  if (item.llmReviewStage === "done") {
    return {
      reviewStatus:
        getDecision(bestMatch) === "autoApply" ? "completed" : "manual",
      fillRecommendation:
        getDecision(bestMatch) === "autoApply" ? "fillable" : "review",
      hasCustomerVisibleRisk: hasCustomerVisibleRisk(item)
    };
  }

  if (getDecision(bestMatch) === "autoApply") {
    return {
      reviewStatus: item.confidenceLevel === "high" ? "direct" : "completed",
      fillRecommendation: "fillable" as SmartFillFillRecommendation,
      hasCustomerVisibleRisk: hasCustomerVisibleRisk(item)
    };
  }

  if (shouldStreamMatchReview(bestMatch)) {
    return {
      reviewStatus: options.llmStreaming ? "waiting" : "pending",
      fillRecommendation: "review" as SmartFillFillRecommendation,
      hasCustomerVisibleRisk: true
    };
  }

  return {
    reviewStatus: "manual" as SmartFillReviewStatus,
    fillRecommendation: "review" as SmartFillFillRecommendation,
    hasCustomerVisibleRisk: true
  };
};

export const getSmartFillDecisionSummaryState = (
  item: MatchPreviewItem,
  options: { sourceBestRowCount?: number } = {}
) => {
  const bestMatch = item.bestMatch;
  const sourceBestRowCount = options.sourceBestRowCount ?? 0;

  if (!bestMatch) {
    return {
      recommendation: {
        title: "暂不填充",
        description: "暂无匹配结果。",
        type: "info" as const
      },
      actionSuggestion: "人工补充",
      riskLevel: {
        label: "中",
        type: "warning" as const,
        description: "需人工判断"
      }
    };
  }

  if (getDecision(bestMatch) === "reject") {
    return {
      recommendation: {
        title: "暂不填充",
        description: "当前推荐结果不适合直接采用，请先核对后再处理。",
        type: "warning" as const
      },
      actionSuggestion: "先核对再处理",
      riskLevel: {
        label: "高",
        type: "danger" as const,
        description: "需人工处理"
      }
    };
  }

  if (getDecision(bestMatch) !== "autoApply") {
    return {
      recommendation: {
        title: "需要确认",
        description: isLlmEquivalenceDecisionRisk(bestMatch.llmEquivalence)
          ? "AI 等价裁决已提示存在决策风险，请先确认。"
          : "系统最终决策为人工确认，请先核对后再填充。",
        type: "warning" as const
      },
      actionSuggestion: "确认后再填充",
      riskLevel: {
        label: "中",
        type: "warning" as const,
        description: "需人工确认"
      }
    };
  }

  return {
    recommendation: {
      title: "可直接填充",
      description: "系统最终决策允许自动采用。",
      type: "success" as const
    },
    actionSuggestion: "可直接填充",
    riskLevel: {
      label: "低",
      type: "success" as const,
      description: hasCustomerVisibleRisk(item, sourceBestRowCount)
        ? "已通过系统门禁，请结合详情说明自行复核"
        : "可直接处理"
    }
  };
};

export const applyMatchLlmStreamEventToPreviewItem = (
  item: MatchPreviewItem,
  event: MatchLlmStreamEvent,
  data: MatchLlmStreamEventData
) => {
  switch (event) {
    case "review.start":
      item.llmReviewStage = "streaming";
      item.llmReviewDraft = "";
      item.llmReviewError = undefined;
      return;
    case "review.delta":
      const delta = data as MatchLlmStreamDeltaEventData;
      item.llmReviewStage = "streaming";
      item.llmReviewDraft = (item.llmReviewDraft || "") + (delta.chunk || "");
      item.llmReviewError = undefined;
      return;
    case "review.done": {
      const done = data as MatchLlmStreamDoneEventData;
      if (done.bestMatch) {
        item.bestMatch = {
          ...done.bestMatch,
          scoreDetails: { ...(done.bestMatch.scoreDetails ?? {}) },
          evidenceSummary: [...(done.bestMatch.evidenceSummary ?? [])],
          conflictSummary: [...(done.bestMatch.conflictSummary ?? [])],
          issues: [...(done.bestMatch.issues ?? [])],
          entities: [...(done.bestMatch.entities ?? [])],
          topCandidates: (done.bestMatch.topCandidates ?? []).map(
            candidate => ({
              ...candidate,
              scoreDetails: { ...(candidate.scoreDetails ?? {}) },
              evidenceSummary: [...(candidate.evidenceSummary ?? [])],
              conflictSummary: [...(candidate.conflictSummary ?? [])],
              issues: [...(candidate.issues ?? [])],
              entities: [...(candidate.entities ?? [])]
            })
          )
        };
      }
      if (item.bestMatch) {
        item.bestMatch.decision = done.decision || item.bestMatch.decision;
        item.bestMatch.reviewScore = done.score;
        item.bestMatch.reviewReason = done.reason;
        item.bestMatch.reviewCommentary = done.commentary;
        item.bestMatch.reviewApprovalToken = done.reviewApprovalToken;
      }
      item.llmReviewStage = "done";
      item.llmReviewDraft = "";
      item.llmReviewError = undefined;
      return;
    }
    case "review.error": {
      const error = data as MatchLlmStreamErrorEventData;
      if (item.bestMatch) {
        item.bestMatch.decision = error.decision || "manualReview";
      }
      item.llmReviewStage = "error";
      item.llmReviewError = error.message || "LLM复核失败";
      item.llmReviewDraft = "";
      return;
    }
    case "stream.complete":
      if (item.llmReviewStage === "streaming") {
        applyMatchLlmStreamDisconnectToPreviewItem(
          item,
          "LLM复核未返回终态，已转为人工确认"
        );
        return;
      }

      if (
        item.llmReviewStage === undefined &&
        shouldStreamMatchReview(item.bestMatch)
      ) {
        item.llmReviewStage = "done";
        item.llmReviewDraft = "";
        item.llmReviewError = undefined;
      }
      return;
    default:
      return;
  }
};

export const applyMatchLlmStreamDisconnectToPreviewItem = (
  item: MatchPreviewItem,
  message = "LLM流式输出中断，已转为人工确认"
) => {
  const isInterruptedStreamingRow =
    item.llmReviewStage === "streaming" ||
    (item.llmReviewStage === undefined &&
      shouldStreamMatchReview(item.bestMatch));

  if (!isInterruptedStreamingRow) {
    return;
  }

  if (item.bestMatch && item.bestMatch.decision !== "reject") {
    item.bestMatch.decision = "manualReview";
  }

  item.llmReviewStage = "error";
  item.llmReviewDraft = "";
  item.llmReviewError = message;
};

export const getSortedScoreDetails = (candidate: MatchCandidateOption) => {
  return Object.entries(candidate.scoreDetails ?? {})
    .map(([key, value]) => [key, value] as const)
    .sort(([leftKey], [rightKey]) => {
      const order = [
        "Final",
        "Embedding",
        "ProjectMatch",
        "SpecificationText",
        "NumberUnit"
      ];
      const leftIndex = order.indexOf(leftKey);
      const rightIndex = order.indexOf(rightKey);
      if (leftIndex === -1 && rightIndex === -1) {
        return leftKey.localeCompare(rightKey);
      }
      if (leftIndex === -1) return 1;
      if (rightIndex === -1) return -1;
      return leftIndex - rightIndex;
    });
};

export const getScoreLabel = (key: string) => {
  const map: Record<string, string> = {
    Final: "最终",
    Embedding: "Embedding",
    ProjectMatch: "项目",
    SpecificationText: "规格文本",
    NumberUnit: "数值单位"
  };
  return map[key] || key;
};
