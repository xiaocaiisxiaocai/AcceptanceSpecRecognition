import type { ColumnMappingTargetField } from "@/api/column-mapping-rules";
import type {
  SmartConfigConfirmRequest,
  SmartConfigDecision,
  SmartConfigRecommendation,
  SmartConfigRecognizedField,
  SmartConfigRecognitionIssue,
  SmartConfigRecognizedRegion,
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

export const filterSmartStructureIssuesForRegions = (
  issues: SmartConfigRecognitionIssue[],
  regions: SmartConfigRecognizedRegion[]
) => {
  if (regions.length === 0) return issues;

  const resolvedCodes = new Set<string>();
  if (
    regions.every(
      region => region.isSpecificationOnly || region.projectColumnIndex != null
    )
  ) {
    resolvedCodes.add("MissingProjectColumn");
  }
  if (regions.every(region => region.specificationColumnIndex != null)) {
    resolvedCodes.add("MissingSpecificationColumn");
  }
  if (regions.every(region => region.acceptanceColumnIndex != null)) {
    resolvedCodes.add("MissingAcceptanceColumn");
  }
  if (regions.every(region => region.remarkColumnIndex != null)) {
    resolvedCodes.add("MissingRemarkColumn");
  }

  return issues.filter(issue => !resolvedCodes.has(issue.code));
};

export type SmartStructureDisplayGroup = {
  key: "recommended" | "needConfirm" | "skip";
  title: string;
  tagType: ElementPlusTagType;
  tables: SmartConfigRecognizedTable[];
};

export const canConfirmSmartStructureTable = ({
  readonly,
  confirmationLocked,
  customerId,
  allRegionsConfirmable,
  structureValidationError,
  decision,
  hasStructureChanges
}: {
  readonly: boolean;
  confirmationLocked: boolean;
  customerId?: number;
  allRegionsConfirmable: boolean;
  structureValidationError: string;
  decision: SmartConfigDecision;
  hasStructureChanges: boolean;
}) =>
  !readonly &&
  !confirmationLocked &&
  !!customerId &&
  allRegionsConfirmable &&
  !structureValidationError &&
  (decision !== "Reject" || hasStructureChanges);

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

export type SmartStructureHeaderProbe = {
  columnIndex?: number | null;
  expectedHeader?: string | null;
};

const normalizeSmartStructureHeaderText = (value: string | null | undefined) =>
  (value ?? "")
    .normalize("NFKC")
    .toLocaleLowerCase()
    .replace(/[\s\p{P}\p{S}]+/gu, "");

const headerTextMatches = (
  candidate: string | null | undefined,
  expected: string | null | undefined
) => {
  const normalizedCandidate = normalizeSmartStructureHeaderText(candidate);
  const normalizedExpected = normalizeSmartStructureHeaderText(expected);
  return (
    normalizedCandidate.length > 0 &&
    normalizedExpected.length > 0 &&
    (normalizedCandidate === normalizedExpected ||
      normalizedCandidate.includes(normalizedExpected) ||
      normalizedExpected.includes(normalizedCandidate))
  );
};

/**
 * 从预览行末尾向上寻找最近的有效单行表头。
 * Excel 解析器会先将合并单元格展开，因此合并区域的末行仍能匹配左上角标题。
 */
export const findNearestSmartStructureHeaderRowIndex = (
  rows: string[][],
  probes: SmartStructureHeaderProbe[]
) => {
  const configured = probes.filter(
    probe => probe.columnIndex != null && probe.columnIndex >= 0
  );
  const comparable = configured.filter(
    probe => normalizeSmartStructureHeaderText(probe.expectedHeader).length > 0
  );
  if (configured.length === 0 || comparable.length === 0) return undefined;

  for (let rowIndex = rows.length - 1; rowIndex >= 0; rowIndex -= 1) {
    const row = rows[rowIndex] ?? [];
    const allConfiguredCellsHaveValues = configured.every(probe =>
      Boolean(row[probe.columnIndex!]?.trim())
    );
    if (!allConfiguredCellsHaveValues) continue;

    const matches = comparable.filter(probe =>
      headerTextMatches(row[probe.columnIndex!], probe.expectedHeader)
    ).length;
    if (matches === comparable.length) return rowIndex;
  }

  return undefined;
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

export type ExcelA1ColumnRange = {
  columnNumber: number;
  startRow: number;
  endRow: number;
  normalized: string;
};

export const toExcelColumnLabel = (columnNumber: number) => {
  let value = Math.max(1, Math.trunc(columnNumber));
  let label = "";
  while (value > 0) {
    value -= 1;
    label = String.fromCharCode(65 + (value % 26)) + label;
    value = Math.floor(value / 26);
  }
  return label;
};

export const excelColumnLabelToNumber = (columnLabel: string) => {
  const normalized = columnLabel.trim().toUpperCase();
  if (!/^[A-Z]+$/.test(normalized)) return undefined;

  return [...normalized].reduce(
    (columnNumber, character) =>
      columnNumber * 26 + character.charCodeAt(0) - 64,
    0
  );
};

export const parseExcelA1ColumnRange = (
  value: string
): ExcelA1ColumnRange | undefined => {
  const normalizedInput = value
    .trim()
    .toUpperCase()
    .replace(/[：]/g, ":")
    .replace(/\$/g, "")
    .replace(/\s+/g, "");
  const match = /^([A-Z]+)([1-9]\d*):([A-Z]+)([1-9]\d*)$/.exec(normalizedInput);
  if (!match || match[1] !== match[3]) return undefined;

  const columnNumber = excelColumnLabelToNumber(match[1]);
  const startRow = Number(match[2]);
  const endRow = Number(match[4]);
  if (!columnNumber || endRow < startRow) return undefined;

  const column = toExcelColumnLabel(columnNumber);
  return {
    columnNumber,
    startRow,
    endRow,
    normalized: `${column}${startRow}:${column}${endRow}`
  };
};

export type SmartStructureExcelRangeField =
  | "projectRange"
  | "specificationRange"
  | "acceptanceRange"
  | "remarkRange";

export type SmartStructureExcelRangeValidation = {
  fieldErrors: Partial<Record<SmartStructureExcelRangeField, string>>;
  parsedRanges: Partial<
    Record<SmartStructureExcelRangeField, ExcelA1ColumnRange>
  >;
};

export const validateSmartStructureExcelRanges = (
  ranges: Record<SmartStructureExcelRangeField, string>,
  bounds: {
    baseColumn: number;
    columnCount: number;
    baseRow: number;
    maximumRow: number;
  }
): SmartStructureExcelRangeValidation => {
  const definitions: Array<{
    field: SmartStructureExcelRangeField;
    label: string;
    required: boolean;
  }> = [
    {
      field: "projectRange",
      label: "项目范围",
      required: false
    },
    {
      field: "specificationRange",
      label: "规格范围",
      required: true
    },
    {
      field: "acceptanceRange",
      label: "验收范围",
      required: true
    },
    {
      field: "remarkRange",
      label: "备注范围",
      required: false
    }
  ];
  const fieldErrors: SmartStructureExcelRangeValidation["fieldErrors"] = {};
  const parsedRanges: SmartStructureExcelRangeValidation["parsedRanges"] = {};

  for (const definition of definitions) {
    const value = ranges[definition.field].trim();
    if (!value) {
      if (definition.required) {
        fieldErrors[definition.field] = `${definition.label}不能为空`;
      }
      continue;
    }

    const parsed = parseExcelA1ColumnRange(value);
    if (!parsed) {
      fieldErrors[definition.field] =
        `${definition.label}格式无效，请输入同一列且起止行正序的范围，例如 C9:C112`;
      continue;
    }
    if (
      parsed.columnNumber < bounds.baseColumn ||
      parsed.columnNumber >= bounds.baseColumn + bounds.columnCount
    ) {
      fieldErrors[definition.field] = `${definition.label}超出当前工作表列范围`;
      continue;
    }
    if (parsed.startRow < bounds.baseRow || parsed.endRow > bounds.maximumRow) {
      fieldErrors[definition.field] = `${definition.label}超出当前工作表行范围`;
      continue;
    }
    parsedRanges[definition.field] = parsed;
  }

  const validEntries = definitions.flatMap(definition => {
    const parsed = parsedRanges[definition.field];
    return parsed ? [{ ...definition, parsed }] : [];
  });
  const reference = validEntries[0];
  if (reference) {
    for (const entry of validEntries.slice(1)) {
      if (
        entry.parsed.startRow !== reference.parsed.startRow ||
        entry.parsed.endRow !== reference.parsed.endRow
      ) {
        fieldErrors[entry.field] =
          `${entry.label}的起止行需与${reference.label}一致（${reference.parsed.startRow}–${reference.parsed.endRow} 行）`;
      }
    }
  }

  const firstFieldByColumn = new Map<number, (typeof validEntries)[number]>();
  for (const entry of validEntries) {
    const previous = firstFieldByColumn.get(entry.parsed.columnNumber);
    if (!previous) {
      firstFieldByColumn.set(entry.parsed.columnNumber, entry);
      continue;
    }
    fieldErrors[entry.field] =
      `${entry.label}不能与${previous.label}使用同一列`;
  }

  return { fieldErrors, parsedRanges };
};

export const formatDisplayRowRange = ({
  headerRowIndex,
  dataStartRowIndex
}: {
  headerRowIndex: number;
  dataStartRowIndex: number;
}) =>
  `表头 ${formatDisplayIndexFromZeroBased(headerRowIndex)} / 数据 ${formatDisplayIndexFromZeroBased(dataStartRowIndex)}`;

const smartStructureSourceLabels: Record<string, string> = {
  Template: "历史模板",
  RuleBased: "规则识别",
  Fused: "综合识别",
  SemanticRecall: "语义召回",
  RepeatedHeader: "重复表头",
  Failed: "识别失败"
};

export const getSmartStructureSourceLabel = (source?: string | null) => {
  const normalized = source?.trim();
  return normalized
    ? (smartStructureSourceLabels[normalized] ?? normalized)
    : "-";
};

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

export const resolveSmartStructureRegionEndRowIndex = (
  region: Pick<
    SmartConfigRecognizedRegion,
    "dataStartRowIndex" | "dataEndRowIndex"
  >,
  tableInfo?: TableInfo
) =>
  region.dataEndRowIndex ??
  (tableInfo?.rowCount
    ? Math.max(region.dataStartRowIndex, tableInfo.rowCount - 1)
    : region.dataStartRowIndex);

export const countSmartStructureRegionRows = (
  regions: Pick<
    SmartConfigRecognizedRegion,
    "dataStartRowIndex" | "dataEndRowIndex"
  >[],
  tableInfo?: TableInfo
) =>
  regions.reduce(
    (sum, region) =>
      sum +
      Math.max(
        0,
        resolveSmartStructureRegionEndRowIndex(region, tableInfo) -
          region.dataStartRowIndex +
          1
      ),
    0
  );

export const validateSmartStructureRegions = (
  regions: SmartConfigRecognizedRegion[],
  tableInfo?: TableInfo
) => {
  if (regions.length === 0) return "请至少保留一个数据区域";
  const maximumRowIndex = tableInfo?.rowCount
    ? Math.max(0, tableInfo.rowCount - 1)
    : undefined;
  const columnCount = tableInfo?.columnCount;

  for (const [index, region] of regions.entries()) {
    const label = `区域 ${index + 1}`;
    const headerEndRowIndex =
      region.headerRowIndex + Math.max(1, region.headerRowCount) - 1;
    const dataEndRowIndex = resolveSmartStructureRegionEndRowIndex(
      region,
      tableInfo
    );
    if (
      region.headerRowIndex < 0 ||
      region.headerRowCount < 1 ||
      region.dataStartRowIndex <= headerEndRowIndex ||
      dataEndRowIndex < region.dataStartRowIndex ||
      (maximumRowIndex != null &&
        (headerEndRowIndex > maximumRowIndex ||
          dataEndRowIndex > maximumRowIndex))
    ) {
      return `${label}的表头或数据行范围无效`;
    }
    if (!region.isSpecificationOnly && region.projectColumnIndex == null) {
      return `${label}请选择项目列`;
    }
    if (region.specificationColumnIndex == null) {
      return `${label}请选择规格列`;
    }
    if (region.acceptanceColumnIndex == null) {
      return `${label}请选择验收列`;
    }
    const columns = [
      region.isSpecificationOnly ? null : region.projectColumnIndex,
      region.specificationColumnIndex,
      region.acceptanceColumnIndex,
      region.remarkColumnIndex
    ].filter((value): value is number => value != null);
    if (
      columns.some(
        column => column < 0 || (columnCount != null && column >= columnCount)
      )
    ) {
      return `${label}包含超出表格范围的字段列`;
    }
    if (new Set(columns).size !== columns.length) {
      return `${label}的字段列不能重复`;
    }
  }

  const ordered = [...regions].sort(
    (left, right) => left.headerRowIndex - right.headerRowIndex
  );
  for (let index = 1; index < ordered.length; index += 1) {
    if (
      ordered[index].headerRowIndex <=
      resolveSmartStructureRegionEndRowIndex(ordered[index - 1], tableInfo)
    ) {
      return "数据区域之间不能重叠";
    }
  }
  return "";
};

export const getSmartStructureImportSelectionDisabledReason = (
  table: SmartConfigRecognizedTable
) => {
  if (table.decision === "Reject") {
    return table.skipReason?.trim() || "后端判定该表不可导入";
  }
  if (table.recommendation === "Skip") {
    return table.skipReason?.trim() || "后端建议跳过该表";
  }

  return "";
};

export const getSmartStructureImportReadinessReason = (
  table: SmartConfigRecognizedTable
) => {
  const selectionDisabledReason =
    getSmartStructureImportSelectionDisabledReason(table);
  if (selectionDisabledReason) {
    return selectionDisabledReason;
  }

  const regions = table.regions?.length ? table.regions : [table];
  const missingFields = Array.from(
    new Set(
      regions
        .flatMap(region => [
          !region.isSpecificationOnly && region.projectColumnIndex == null
            ? "项目列"
            : "",
          region.specificationColumnIndex == null ? "规格列" : "",
          region.acceptanceColumnIndex == null ? "验收列" : ""
        ])
        .filter(Boolean)
    )
  );

  return missingFields.length > 0
    ? `缺少${missingFields.join("、")}；请补齐后点击“确认并学习”`
    : "";
};

export const canSelectSmartStructureTable = (
  table: SmartConfigRecognizedTable
) => getSmartStructureImportSelectionDisabledReason(table) === "";

export const needsManualStructureFallback = (
  table: SmartConfigRecognizedTable
) => table.decision === "Reject" || table.recommendation === "Skip";

export const shouldShowSmartStructureManualFallback = ({
  recognitionAttempted,
  recognizing,
  error,
  tables
}: {
  recognitionAttempted: boolean;
  recognizing: boolean;
  error: string;
  tables: SmartConfigRecognizedTable[];
}) =>
  recognitionAttempted &&
  !recognizing &&
  (error.trim().length > 0 ||
    tables.length === 0 ||
    tables.some(needsManualStructureFallback));

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

export const sortSmartStructureTablesByIndex = (
  tables: SmartConfigRecognizedTable[]
) => [...tables].sort((a, b) => a.tableIndex - b.tableIndex);

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
      "fileId" | "templateName" | "learnedColumns" | "userModifiedStructure"
    >
  > = {}
): SmartConfigConfirmRequest => {
  const sourceRegions =
    table.regions && table.regions.length > 0
      ? table.regions
      : [
          {
            regionId: `table-${table.tableIndex}-region-0`,
            regionIndex: 0,
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
            fields: table.fields
          }
        ];

  if (sourceRegions.some(region => region.specificationColumnIndex == null)) {
    throw new Error("规格列不能为空");
  }
  if (sourceRegions.some(region => region.acceptanceColumnIndex == null)) {
    throw new Error("验收列不能为空");
  }

  const regions = sourceRegions.map(region => ({
    regionId: region.regionId,
    regionIndex: region.regionIndex,
    headers: region.headers,
    projectColumnIndex: region.projectColumnIndex ?? undefined,
    specificationColumnIndex: region.specificationColumnIndex!,
    acceptanceColumnIndex: region.acceptanceColumnIndex ?? undefined,
    remarkColumnIndex: region.remarkColumnIndex ?? undefined,
    headerRowIndex: region.headerRowIndex,
    headerRowCount: region.headerRowCount,
    dataStartRowIndex: Math.max(
      region.dataStartRowIndex,
      region.headerRowIndex + Math.max(region.headerRowCount, 1)
    ),
    dataEndRowIndex: region.dataEndRowIndex ?? undefined,
    isSpecificationOnly: region.isSpecificationOnly
  }));
  const primary = regions[0];
  const learnedFields = sourceRegions.flatMap(region => region.fields ?? []);

  return {
    customerId,
    fileId: overrides.fileId,
    tableIndex: table.tableIndex,
    templateName:
      overrides.templateName ??
      table.tableName?.trim() ??
      `表格 ${table.tableIndex + 1}`,
    headers: primary.headers,
    projectColumnIndex: primary.projectColumnIndex,
    specificationColumnIndex: primary.specificationColumnIndex,
    acceptanceColumnIndex: primary.acceptanceColumnIndex,
    remarkColumnIndex: primary.remarkColumnIndex,
    headerRowIndex: primary.headerRowIndex,
    headerRowCount: primary.headerRowCount,
    dataStartRowIndex: primary.dataStartRowIndex,
    dataEndRowIndex: primary.dataEndRowIndex,
    isSpecificationOnly: primary.isSpecificationOnly,
    tableKind: table.tableKind,
    // 用户确认意味着该结构已经可用；Skip/NeedConfirm 只能作为识别阶段建议，
    // 不能继续固化到模板，否则重传同结构时仍会被永久跳过。
    recommendation:
      table.recommendation === "Recommended" ? "Recommended" : "Optional",
    userModifiedStructure: overrides.userModifiedStructure ?? false,
    learnedColumns:
      overrides.learnedColumns ?? buildLearnedColumns(learnedFields),
    regions
  };
};

export const applySmartConfigConfirmRequestToTable = (
  table: SmartConfigRecognizedTable,
  request: SmartConfigConfirmRequest
): SmartConfigRecognizedTable => {
  const regions = request.regions?.map(
    (region, index): SmartConfigRecognizedRegion => {
      const previous =
        table.regions?.find(item => item.regionId === region.regionId) ??
        table.regions?.[index];
      return {
        ...previous,
        ...region,
        regionId:
          region.regionId ??
          previous?.regionId ??
          `table-${table.tableIndex}-region-${index}`,
        regionIndex: index,
        headers: [...region.headers],
        confidence: previous?.confidence ?? table.confidence,
        source: previous?.source ?? table.source,
        decision: "AutoApply",
        issues: [],
        fields: previous?.fields ?? table.fields
      };
    }
  );

  return {
    ...table,
    tableName: request.templateName || table.tableName,
    headers: request.headers,
    projectColumnIndex: request.projectColumnIndex,
    specificationColumnIndex: request.specificationColumnIndex,
    acceptanceColumnIndex: request.acceptanceColumnIndex,
    remarkColumnIndex: request.remarkColumnIndex,
    headerRowIndex: request.headerRowIndex,
    headerRowCount: request.headerRowCount,
    dataStartRowIndex: request.dataStartRowIndex,
    dataEndRowIndex: request.dataEndRowIndex,
    isSpecificationOnly: request.isSpecificationOnly,
    regions: regions?.length ? regions : table.regions,
    decision: "AutoApply",
    recommendation:
      table.recommendation === "Recommended" ? "Recommended" : "Optional",
    skipReason: undefined,
    issues: []
  };
};
