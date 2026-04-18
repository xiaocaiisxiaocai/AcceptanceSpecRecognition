import type {
  LlmEquivalenceReasonType,
  LlmEquivalenceResult,
  LlmEquivalenceVerdict
} from "../../../api/matching.ts";

export type LlmEquivalenceDifferenceTone = "neutral" | "hint" | "decision";

type EquivalenceLike =
  | Pick<LlmEquivalenceResult, "verdict" | "reasonType" | "reason" | "confidence">
  | null
  | undefined;

const hintReasonTypes: LlmEquivalenceReasonType[] = [
  "format_only",
  "punctuation_only",
  "equivalent_expression",
  "symbol_equivalent"
];

const decisionReasonTypes: LlmEquivalenceReasonType[] = [
  "semantic_difference",
  "symbol_conflict",
  "uncertain"
];

export const getLlmEquivalenceVerdictText = (
  verdict?: LlmEquivalenceVerdict
) => {
  switch (verdict) {
    case "equivalent":
      return "等价";
    case "different":
      return "不同";
    case "uncertain":
    default:
      return "不确定";
  }
};

export const getLlmEquivalenceVerdictTagType = (
  verdict?: LlmEquivalenceVerdict
): "success" | "warning" | "danger" | "info" => {
  switch (verdict) {
    case "equivalent":
      return "success";
    case "different":
      return "danger";
    case "uncertain":
      return "warning";
    default:
      return "info";
  }
};

export const getLlmEquivalenceReasonTypeText = (
  reasonType?: LlmEquivalenceReasonType
) => {
  switch (reasonType) {
    case "format_only":
      return "格式差异";
    case "punctuation_only":
      return "标点差异";
    case "equivalent_expression":
      return "等价表达";
    case "symbol_equivalent":
      return "符号等价";
    case "semantic_difference":
      return "语义差异";
    case "symbol_conflict":
      return "符号冲突";
    case "uncertain":
    default:
      return "无法确认";
  }
};

export const getLlmEquivalenceReasonTagType = (
  reasonType?: LlmEquivalenceReasonType
): "success" | "warning" | "danger" | "info" => {
  switch (reasonType) {
    case "equivalent_expression":
    case "symbol_equivalent":
      return "success";
    case "format_only":
    case "punctuation_only":
      return "info";
    case "semantic_difference":
    case "symbol_conflict":
      return "danger";
    case "uncertain":
    default:
      return "warning";
  }
};

export const isLlmEquivalenceHintOnly = (result?: EquivalenceLike) =>
  !!result &&
  result.verdict === "equivalent" &&
  hintReasonTypes.includes(result.reasonType);

export const isLlmEquivalenceDecisionRisk = (result?: EquivalenceLike) =>
  !!result &&
  (result.verdict === "different" ||
    result.verdict === "uncertain" ||
    decisionReasonTypes.includes(result.reasonType));

export const getLlmEquivalenceDifferenceTone = (
  result?: EquivalenceLike
): LlmEquivalenceDifferenceTone => {
  if (isLlmEquivalenceHintOnly(result)) {
    return "hint";
  }

  if (isLlmEquivalenceDecisionRisk(result)) {
    return "decision";
  }

  return "neutral";
};

export const getLlmEquivalenceDifferenceToneText = (
  tone: LlmEquivalenceDifferenceTone
) => {
  switch (tone) {
    case "hint":
      return "提示型差异";
    case "decision":
      return "决策型风险";
    case "neutral":
    default:
      return "一般差异";
  }
};

export const getLlmEquivalenceDifferenceToneTagType = (
  tone: LlmEquivalenceDifferenceTone
): "success" | "warning" | "danger" | "info" => {
  switch (tone) {
    case "hint":
      return "info";
    case "decision":
      return "warning";
    case "neutral":
    default:
      return "info";
  }
};

export const getLlmEquivalenceDifferenceToneDescription = (
  tone: LlmEquivalenceDifferenceTone
) => {
  switch (tone) {
    case "hint":
      return "提示型差异：保留原文 diff 展示，但不单独提升风险。";
    case "decision":
      return "决策型风险：保留原文 diff 展示，需要人工确认后再采用。";
    case "neutral":
    default:
      return "当前仅展示原文差异，是否影响决策需结合上下文判断。";
  }
};

export const getLlmEquivalenceHeadline = (result?: EquivalenceLike) => {
  if (!result) {
    return "当前未触发 AI 等价裁决";
  }

  if (result.verdict === "equivalent") {
    return `AI 判断为${getLlmEquivalenceReasonTypeText(result.reasonType)}`;
  }

  if (result.verdict === "different") {
    return `AI 判断存在${getLlmEquivalenceReasonTypeText(result.reasonType)}`;
  }

  return "AI 暂无法确认是否等价";
};

export const getLlmEquivalenceSummaryText = (result?: EquivalenceLike) => {
  if (!result) {
    return "当前未触发 AI 等价裁决。";
  }

  if (result.reason?.trim()) {
    return `${getLlmEquivalenceHeadline(result)}：${result.reason.trim()}`;
  }

  return getLlmEquivalenceHeadline(result);
};

export const shouldHideInlineLlmEquivalenceSummary = (
  result?: EquivalenceLike,
  score?: number
) => {
  if (!result || score !== 1 || result.verdict !== "equivalent") {
    return false;
  }

  const reason = result.reason?.trim();
  if (!reason) {
    return false;
  }

  return reason.includes("已直接视为等价");
};
