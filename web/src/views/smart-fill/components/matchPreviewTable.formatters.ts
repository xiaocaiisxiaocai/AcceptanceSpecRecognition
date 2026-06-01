import type { MatchIssue, MatchPreviewItem } from "@/api/matching";
import type {
  SmartFillFillRecommendation,
  SmartFillReviewStatus
} from "./scoreDetail.formatters";

export const isNoAnswerPlaceholderRow = (item: MatchPreviewItem) => {
  const project = (item.sourceProject || "").trim();
  const specification = (item.sourceSpecification || "").trim();
  if (specification) return false;

  const placeholderProjects = new Set(["其他", "-", "/", "无", "n/a", "na"]);
  return placeholderProjects.has(project.toLowerCase());
};

export const getMatchPreviewDecision = (item: MatchPreviewItem) =>
  item.bestMatch?.decision ?? "manualReview";

export const isMatchPreviewAutoApply = (item: MatchPreviewItem) =>
  getMatchPreviewDecision(item) === "autoApply";

export const isMatchPreviewRejectDecision = (item: MatchPreviewItem) =>
  getMatchPreviewDecision(item) === "reject";

export const isHighConfidenceMatchPreview = (item: MatchPreviewItem) =>
  isMatchPreviewAutoApply(item) && item.confidenceLevel === "high";

export const getPreviewConfidenceClass = (level: string) => {
  switch (level) {
    case "high":
      return "confidence-high";
    case "medium":
      return "confidence-medium";
    case "low":
      return "confidence-low";
    default:
      return "confidence-none";
  }
};

export const getPreviewConfidenceText = (level: string) => {
  switch (level) {
    case "high":
      return "高";
    case "medium":
      return "中";
    case "low":
      return "低";
    default:
      return "无";
  }
};

export const formatPreviewScore = (score: number) => `${(score * 100).toFixed(1)}%`;

export const formatOptionalPercent = (value?: number) =>
  value === undefined || value === null ? "-" : `${(value * 100).toFixed(1)}%`;

export const getAmbiguityHint = (
  item: MatchPreviewItem,
  ambiguityMargin: number
) => {
  if (!item.bestMatch?.isAmbiguous) return "";

  return `Top1/Top2分差 ${formatOptionalPercent(item.bestMatch.scoreGap)}，歧义阈值 ${formatOptionalPercent(ambiguityMargin)}`;
};

export const getPrimaryIssue = (item: MatchPreviewItem): MatchIssue | undefined =>
  item.bestMatch?.issues?.[0];

export const formatIssueComparison = (issue?: MatchIssue) => {
  if (!issue?.sourceValue && !issue?.candidateValue) {
    return "";
  }

  if (issue.sourceValue && issue.candidateValue) {
    return `源值 ${issue.sourceValue}，候选 ${issue.candidateValue}`;
  }

  if (issue.sourceValue) {
    return `源值 ${issue.sourceValue}`;
  }

  return `候选 ${issue?.candidateValue}`;
};

export const getReviewStatusText = (
  item: MatchPreviewItem,
  status: SmartFillReviewStatus,
  highConfidence: boolean
) => {
  switch (status) {
    case "direct":
      return "无需复核";
    case "completed":
      return highConfidence ? "无需复核" : "AI判定可采用";
    case "manual":
      return item.llmReviewStage === "done" ? "复核后待确认" : "待确认";
    case "waiting":
      return "等待复核";
    case "pending":
      return "待复核";
    case "streaming":
      return "复核中...";
    case "blocked":
      return "暂不采用";
    case "error":
      return "已转人工确认";
    default:
      return "-";
  }
};

export const getReviewTagType = (
  status: SmartFillReviewStatus
): "success" | "warning" | "danger" | "info" => {
  switch (status) {
    case "direct":
    case "completed":
      return "success";
    case "manual":
    case "pending":
    case "streaming":
    case "error":
      return "warning";
    case "waiting":
      return "info";
    case "blocked":
      return "danger";
    default:
      return "info";
  }
};

export const isExactFillable = (
  item: MatchPreviewItem,
  recommendation: SmartFillFillRecommendation
) => recommendation === "fillable" && item.bestMatch?.selectionMode === "exactShortcut";

export const isPartialFillable = (
  item: MatchPreviewItem,
  recommendation: SmartFillFillRecommendation
) =>
  recommendation === "fillable" &&
  !!item.bestMatch &&
  item.bestMatch.selectionMode !== "exactShortcut";

export const getFillRecommendationText = (
  recommendation: SmartFillFillRecommendation
) => {
  switch (recommendation) {
    case "fillable":
      return "可直接填充";
    case "blocked":
      return "不建议填充";
    case "unmatched":
      return "无匹配";
    case "review":
    default:
      return "需要确认";
  }
};

export const getFillRecommendationTagType = (
  recommendation: SmartFillFillRecommendation
): "success" | "warning" | "danger" | "info" => {
  switch (recommendation) {
    case "fillable":
      return "success";
    case "blocked":
      return "danger";
    case "unmatched":
      return "info";
    case "review":
    default:
      return "warning";
  }
};
