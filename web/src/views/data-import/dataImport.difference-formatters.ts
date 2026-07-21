import type {
  DifferenceColumnDef,
  ImportPendingDifferenceWithTable
} from "./dataImport.types";

export const formatDifferenceValue = (value?: string | null) => {
  const normalized = value?.trim();
  return normalized ? normalized : "-";
};

export const formatScorePercent = (value?: number | null) => {
  if (value === undefined || value === null || Number.isNaN(value)) return "-";
  return `${(value * 100).toFixed(1)}%`;
};

export const getDifferenceMatchTypeLabel = (matchType?: string) => {
  switch (matchType) {
    case "exact":
      return "完全重复";
    case "semantic":
      return "AI 疑似重复";
    case "conflict":
    default:
      return "同项目同规格";
  }
};

export const getDifferenceMatchTypeTagType = (matchType?: string) => {
  switch (matchType) {
    case "exact":
      return "danger";
    case "semantic":
      return "success";
    case "conflict":
    default:
      return "warning";
  }
};

export const hasAiDifferenceMeta = (item: ImportPendingDifferenceWithTable) => {
  return (
    item.matchType === "semantic" &&
    (item.embeddingScore !== undefined ||
      item.llmScore !== undefined ||
      item.finalScore !== undefined ||
      !!item.reviewReason ||
      !!item.reviewCommentary)
  );
};

const isDifferenceFieldChanged = (
  existing?: string | null,
  incoming?: string | null
) => {
  return (existing?.trim() || "") !== (incoming?.trim() || "");
};

export const differenceColumnDefs: DifferenceColumnDef[] = [
  {
    key: "project",
    label: "项目",
    getExisting: item => item.existingProject,
    getIncoming: item => item.incomingProject
  },
  {
    key: "specification",
    label: "规格",
    getExisting: item => item.existingSpecification,
    getIncoming: item => item.incomingSpecification
  },
  {
    key: "acceptance",
    label: "验收",
    getExisting: item => item.existingAcceptance,
    getIncoming: item => item.incomingAcceptance
  },
  {
    key: "remark",
    label: "备注",
    getExisting: item => item.existingRemark,
    getIncoming: item => item.incomingRemark
  }
];

export const isDifferenceColumnChanged = (
  item: ImportPendingDifferenceWithTable,
  column: DifferenceColumnDef
) => {
  return isDifferenceFieldChanged(
    column.getExisting(item),
    column.getIncoming(item)
  );
};
