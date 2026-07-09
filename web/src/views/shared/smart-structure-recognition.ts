import type { ColumnMappingTargetField } from "@/api/column-mapping-rules";
import type {
  SmartConfigConfirmRequest,
  SmartConfigDecision,
  SmartConfigRecommendation,
  SmartConfigRecognizedField,
  SmartConfigRecognizedTable
} from "@/api/smart-config";
import type { TableInfo } from "@/api/document";

export type SmartStructureSummary = {
  total: number;
  autoApply: number;
  needConfirm: number;
  reject: number;
  recommended: number;
  optional: number;
  skip: number;
  averageConfidence: number;
  canAutoApplyAll: boolean;
  hasNeedConfirm: boolean;
  hasReject: boolean;
};

type ElementPlusTagType = "success" | "warning" | "danger" | "info";

export type SmartStructureDisplayGroup = {
  key: "recommended" | "needConfirm" | "skip";
  title: string;
  tagType: ElementPlusTagType;
  tables: SmartConfigRecognizedTable[];
};

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

export const getSmartStructureRecommendationTag = (
  recommendation: SmartConfigRecommendation | undefined
): {
  text: string;
  type: ElementPlusTagType;
} => {
  switch (recommendation) {
    case "Recommended":
      return { text: "推荐导入", type: "success" };
    case "NeedConfirm":
      return { text: "需要确认", type: "warning" };
    case "Optional":
      return { text: "可选", type: "info" };
    case "Skip":
      return { text: "建议跳过", type: "info" };
    default:
      return { text: "待判断", type: "info" };
  }
};

export const getSmartStructureTableKindLabel = (
  tableKind: string | undefined
) => {
  switch (tableKind) {
    case "AcceptanceSpec":
      return "验收规格";
    case "SafetySpec":
      return "安全规格";
    case "EnvironmentalSpec":
      return "环保规格";
    case "SecsSpec":
      return "SECS/GEM";
    case "Utility":
      return "Utility";
    case "Quotation":
      return "报价单";
    case "Layout":
      return "Layout";
    case "BomOrSpareParts":
      return "备品清单";
    case "SignatureOrCover":
      return "签核/封面";
    case "Unknown":
      return "未知类型";
    default:
      return tableKind || "未知类型";
  }
};

export const getSmartStructureIssueTagType = (
  severity: string | undefined
): ElementPlusTagType => {
  switch (severity) {
    case "Error":
      return "danger";
    case "Warning":
      return "warning";
    default:
      return "info";
  }
};

export const formatSmartStructurePercent = (value: number | undefined) => {
  if (value === undefined || value === null || Number.isNaN(value)) {
    return "-";
  }

  return `${(Math.max(0, Math.min(1, value)) * 100).toFixed(0)}%`;
};

export const formatDisplayIndexFromZeroBased = (
  value: number | undefined
): number | "-" => (value === undefined ? "-" : value + 1);

export const toDisplayIndexFromZeroBased = (value: number | undefined) =>
  value === undefined ? undefined : value + 1;

export const toZeroBasedIndexFromDisplay = (
  value: number | undefined,
  min = 0
) => Math.max(min, (value ?? min + 1) - 1);

export const formatDisplayRowRange = ({
  headerRowIndex,
  dataStartRowIndex
}: {
  headerRowIndex: number;
  dataStartRowIndex: number;
}) =>
  `表头 ${formatDisplayIndexFromZeroBased(headerRowIndex)} / 数据 ${formatDisplayIndexFromZeroBased(dataStartRowIndex)}`;

export const toActualRowNumber = (
  tableInfo: TableInfo | undefined,
  rowIndex: number
) => Math.max(1, (tableInfo?.usedRangeStartRow ?? 1) + rowIndex);

export const toActualColumnNumber = (
  tableInfo: TableInfo | undefined,
  columnIndex?: number | null
) =>
  columnIndex == null
    ? undefined
    : Math.max(1, (tableInfo?.usedRangeStartColumn ?? 1) + columnIndex);

export const getRecognizedTableInfo = (
  tableInfos: TableInfo[],
  table: SmartConfigRecognizedTable
) => tableInfos.find(item => item.index === table.tableIndex);

export const createSmartStructureSummary = (
  tables: SmartConfigRecognizedTable[]
): SmartStructureSummary => {
  const summary = tables.reduce(
    (acc, table) => {
      if (table.decision === "AutoApply") acc.autoApply += 1;
      if (table.decision === "NeedConfirm") acc.needConfirm += 1;
      if (table.decision === "Reject") acc.reject += 1;
      if (table.recommendation === "Recommended") acc.recommended += 1;
      if (table.recommendation === "Optional") acc.optional += 1;
      if (table.recommendation === "Skip") acc.skip += 1;
      acc.confidenceSum += table.confidence || 0;
      return acc;
    },
    {
      autoApply: 0,
      needConfirm: 0,
      reject: 0,
      recommended: 0,
      optional: 0,
      skip: 0,
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
    recommended: summary.recommended,
    optional: summary.optional,
    skip: summary.skip,
    averageConfidence,
    canAutoApplyAll: total > 0 && summary.autoApply === total,
    hasNeedConfirm: summary.needConfirm > 0,
    hasReject: summary.reject > 0
  };
};

export const createSmartStructureDisplayGroups = (
  tables: SmartConfigRecognizedTable[]
): SmartStructureDisplayGroup[] => {
  const sortByRank = (
    a: SmartConfigRecognizedTable,
    b: SmartConfigRecognizedTable
  ) =>
    (b.rankingScore ?? 0) - (a.rankingScore ?? 0) ||
    a.tableIndex - b.tableIndex;

  const groups: SmartStructureDisplayGroup[] = [
    {
      key: "recommended",
      title: "推荐导入",
      tagType: "success",
      tables: tables
        .filter(table => table.recommendation === "Recommended")
        .sort(sortByRank)
    },
    {
      key: "needConfirm",
      title: "需要确认",
      tagType: "warning",
      tables: tables
        .filter(
          table =>
            table.recommendation !== "Recommended" &&
            table.recommendation !== "Skip"
        )
        .sort(sortByRank)
    },
    {
      key: "skip",
      title: "建议跳过",
      tagType: "info",
      tables: tables
        .filter(table => table.recommendation === "Skip")
        .sort(sortByRank)
    }
  ];

  return groups.filter(group => group.tables.length > 0);
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
    Pick<
      SmartConfigConfirmRequest,
      "templateName" | "learnedColumns" | "userModifiedStructure"
    >
  > = {}
): SmartConfigConfirmRequest => {
  if (table.specificationColumnIndex == null) {
    throw new Error("规格列不能为空");
  }
  if (table.acceptanceColumnIndex == null) {
    throw new Error("验收列不能为空");
  }

  return {
    customerId,
    templateName:
      overrides.templateName ??
      table.tableName?.trim() ??
      `表格 ${table.tableIndex + 1}`,
    headers: table.headers,
    projectColumnIndex: table.projectColumnIndex ?? undefined,
    specificationColumnIndex: table.specificationColumnIndex,
    acceptanceColumnIndex: table.acceptanceColumnIndex,
    remarkColumnIndex: table.remarkColumnIndex ?? undefined,
    headerRowIndex: table.headerRowIndex,
    headerRowCount: table.headerRowCount,
    dataStartRowIndex: table.dataStartRowIndex,
    dataEndRowIndex: table.dataEndRowIndex ?? undefined,
    isSpecificationOnly: table.isSpecificationOnly,
    tableKind: table.tableKind,
    recommendation: table.recommendation,
    userModifiedStructure: overrides.userModifiedStructure ?? false,
    learnedColumns:
      overrides.learnedColumns ?? buildLearnedColumns(table.fields ?? [])
  };
};
