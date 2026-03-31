import type {
  MatchCandidateOption,
  MatchEntityEvidence,
  MatchIssue
} from "@/api/matching";

export const formatScore = (score: number) => `${(score * 100).toFixed(1)}%`;

export const formatLlmScore = (score?: number) => {
  if (score === undefined || score === null) return "-";
  return `${score.toFixed(1)}分`;
};

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
      return "拒绝";
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

export const getEntityRelationText = (
  relation?: MatchEntityEvidence["relation"]
) => {
  switch (relation) {
    case "exact":
      return "同一实体";
    case "aliasSame":
      return "别名同一";
    case "conflict":
      return "实体冲突";
    case "possiblyRelated":
      return "关系待确认";
    case "parentChild":
      return "上下级关系";
    case "overlap":
      return "部分重叠";
    case "compatible":
      return "相容";
    default:
      return "未知";
  }
};

export const getEntityRelationTagType = (
  relation?: MatchEntityEvidence["relation"]
): "success" | "warning" | "danger" | "info" => {
  switch (relation) {
    case "exact":
    case "aliasSame":
    case "compatible":
      return "success";
    case "conflict":
      return "danger";
    case "possiblyRelated":
    case "parentChild":
    case "overlap":
      return "warning";
    default:
      return "info";
  }
};

export const getCandidateDelta = (
  candidate: MatchCandidateOption,
  topCandidates: MatchCandidateOption[]
) => {
  const first = topCandidates[0];
  if (!first || candidate.rank === 1) return "最佳候选";
  const delta = (first.score - candidate.score) * 100;
  return `较 Top1 低 ${delta >= 0 ? delta.toFixed(1) : "0.0"} 分`;
};

export const getSortedScoreDetails = (candidate: MatchCandidateOption) => {
  return Object.entries(candidate.scoreDetails ?? {}).sort(
    ([leftKey], [rightKey]) => {
      const order = [
        "Final",
        "Embedding",
        "ProjectMatch",
        "SpecificationText",
        "NumberUnit",
        "KeywordOverlap",
        "ConflictPenalty"
      ];
      const leftIndex = order.indexOf(leftKey);
      const rightIndex = order.indexOf(rightKey);
      if (leftIndex === -1 && rightIndex === -1) {
        return leftKey.localeCompare(rightKey);
      }
      if (leftIndex === -1) return 1;
      if (rightIndex === -1) return -1;
      return leftIndex - rightIndex;
    }
  );
};

export const getScoreLabel = (key: string) => {
  const map: Record<string, string> = {
    Final: "最终",
    Embedding: "Embedding",
    ProjectMatch: "项目",
    SpecificationText: "规格文本",
    NumberUnit: "数值单位",
    KeywordOverlap: "关键词",
    ConflictPenalty: "冲突惩罚"
  };
  return map[key] || key;
};
