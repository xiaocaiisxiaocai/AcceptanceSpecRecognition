import type { ColumnMappingTargetField } from "@/api/column-mapping-rules";
import type {
  SmartConfigConfirmRequest,
  SmartConfigDecision,
  SmartConfigRecognizedField,
  SmartConfigRecognizedTable
} from "@/api/smart-config";

export type SmartStructureSummary = {
  total: number;
  autoApply: number;
  needConfirm: number;
  reject: number;
  averageConfidence: number;
  canAutoApplyAll: boolean;
  hasNeedConfirm: boolean;
  hasReject: boolean;
};

type ElementPlusTagType = "success" | "warning" | "danger" | "info";

export const getSmartStructureFieldLabel = (field: string) => {
  switch (field) {
    case "Project":
      return "项目";
    case "Specification":
      return "规格";
    case "Acceptance":
      return "验收";
    case "Remark":
      return "备注";
    default:
      return field || "-";
  }
};

export const getSmartStructureFieldTarget = (
  field: string
): ColumnMappingTargetField | undefined => {
  switch (field) {
    case "Project":
      return 1 as ColumnMappingTargetField;
    case "Specification":
      return 2 as ColumnMappingTargetField;
    case "Acceptance":
      return 3 as ColumnMappingTargetField;
    case "Remark":
      return 4 as ColumnMappingTargetField;
    default:
      return undefined;
  }
};

export const getSmartStructureDecisionTag = (
  decision: SmartConfigDecision
): {
  text: string;
  type: ElementPlusTagType;
} => {
  switch (decision) {
    case "AutoApply":
      return { text: "可直达", type: "success" };
    case "NeedConfirm":
      return { text: "待确认", type: "warning" };
    case "Reject":
      return { text: "不可用", type: "danger" };
    default:
      return { text: decision || "-", type: "info" };
  }
};

export const formatSmartStructurePercent = (value: number | undefined) => {
  if (value === undefined || value === null || Number.isNaN(value)) {
    return "-";
  }

  return `${(Math.max(0, Math.min(1, value)) * 100).toFixed(0)}%`;
};

export const createSmartStructureSummary = (
  tables: SmartConfigRecognizedTable[]
): SmartStructureSummary => {
  const summary = tables.reduce(
    (acc, table) => {
      if (table.decision === "AutoApply") acc.autoApply += 1;
      if (table.decision === "NeedConfirm") acc.needConfirm += 1;
      if (table.decision === "Reject") acc.reject += 1;
      acc.confidenceSum += table.confidence || 0;
      return acc;
    },
    {
      autoApply: 0,
      needConfirm: 0,
      reject: 0,
      confidenceSum: 0
    }
  );

  const total = tables.length;
  const averageConfidence =
    total === 0 ? 0 : Math.round((summary.confidenceSum / total) * 100) / 100;

  return {
    total,
    autoApply: summary.autoApply,
    needConfirm: summary.needConfirm,
    reject: summary.reject,
    averageConfidence,
    canAutoApplyAll: total > 0 && summary.autoApply === total,
    hasNeedConfirm: summary.needConfirm > 0,
    hasReject: summary.reject > 0
  };
};

const buildLearnedColumns = (fields: SmartConfigRecognizedField[]) => {
  const seen = new Set<string>();

  return fields.flatMap(field => {
    const header = field.header?.trim();
    const targetField = getSmartStructureFieldTarget(field.field);
    if (!header || targetField === undefined) {
      return [];
    }

    const key = `${targetField}:${header}`;
    if (seen.has(key)) {
      return [];
    }

    seen.add(key);
    return [{ header, targetField }];
  });
};

export const buildSmartConfigConfirmRequest = (
  customerId: number,
  table: SmartConfigRecognizedTable,
  overrides: Partial<
    Pick<SmartConfigConfirmRequest, "templateName" | "learnedColumns">
  > = {}
): SmartConfigConfirmRequest => {
  if (table.specificationColumnIndex === undefined) {
    throw new Error("规格列不能为空");
  }

  return {
    customerId,
    templateName:
      overrides.templateName ??
      table.tableName?.trim() ??
      `表格 ${table.tableIndex + 1}`,
    headers: table.headers,
    projectColumnIndex: table.projectColumnIndex,
    specificationColumnIndex: table.specificationColumnIndex,
    acceptanceColumnIndex: table.acceptanceColumnIndex,
    remarkColumnIndex: table.remarkColumnIndex,
    headerRowIndex: table.headerRowIndex,
    headerRowCount: table.headerRowCount,
    dataStartRowIndex: table.dataStartRowIndex,
    dataEndRowIndex: table.dataEndRowIndex,
    isSpecificationOnly: table.isSpecificationOnly,
    learnedColumns:
      overrides.learnedColumns ?? buildLearnedColumns(table.fields ?? [])
  };
};
