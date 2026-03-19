import type { TableInfo } from "@/api/document";
import type { BatchTableConfig } from "@/api/matching";
import {
  ColumnMappingMatchMode,
  ColumnMappingTargetField,
  type ColumnMappingRule
} from "@/api/column-mapping-rules";

export const DEFAULT_EXCEL_PROBE_PREVIEW_ROWS = 20;

export type AutoDetectionConfidence = "high" | "medium" | "low";

export interface SmartFillExcelAutoDetection
  extends Pick<
    BatchTableConfig,
    | "projectColumnIndex"
    | "specificationColumnIndex"
    | "acceptanceColumnIndex"
    | "remarkColumnIndex"
    | "headerRowStart"
    | "headerRowCount"
    | "dataStartRow"
    | "filterEmptySourceRows"
  > {
  headers: string[];
  sampleRows: string[][];
  confidence: AutoDetectionConfidence;
  mode: "detected" | "fallback";
  summary: string;
  summaryLines: string[];
  warnings: string[];
}

type RowAnalysis = {
  rowIndex: number;
  row: string[];
  nonEmptyCount: number;
  shortLabelCount: number;
  longTextCount: number;
  codeLikeCount: number;
  psdFieldCount: number;
  projectHit: boolean;
  specificationStrongHit: boolean;
  specificationFallbackHit: boolean;
  acceptanceHit: boolean;
  remarkHit: boolean;
  ruleHitCount: number;
  keywordHitCount: number;
  score: number;
};

const smartHeaderKeywords = {
  project: ["psd_project", "评议项目", "project", "项目"],
  specificationStrong: [
    "psd_bjjslj",
    "标准统计计算逻辑",
    "统计计算逻辑",
    "bjjslj"
  ],
  specificationFallback: [
    "psd_itemdesc",
    "评议标准要求",
    "itemdesc",
    "规格内容",
    "技术要求",
    "标准要求",
    "规格",
    "要求"
  ],
  acceptance: ["psd_ysfs", "验收方式", "ysfs", "acceptance"],
  remark: ["psd_outline", "评议大纲", "outline", "备注", "remark"]
} as const;

const normalizeHeaderText = (value?: string) =>
  (value || "")
    .toLowerCase()
    .replace(/[()（）【】\[\]：:]/g, " ")
    .replace(/\s+/g, "");

const matchHeader = (header: string, rule: ColumnMappingRule): boolean => {
  if (!header) return false;
  const normalizedHeader = header.toLowerCase();
  const normalizedPattern = rule.pattern.toLowerCase();

  switch (rule.matchMode) {
    case ColumnMappingMatchMode.Contains:
      return normalizedHeader.includes(normalizedPattern);
    case ColumnMappingMatchMode.Equals:
      return normalizedHeader === normalizedPattern;
    case ColumnMappingMatchMode.Regex:
      try {
        return new RegExp(rule.pattern, "i").test(header);
      } catch {
        return false;
      }
    default:
      return false;
  }
};

const buildRulesByField = (rules: ColumnMappingRule[]) => {
  const grouped = new Map<number, ColumnMappingRule[]>();
  for (const rule of rules) {
    if (!grouped.has(rule.targetField)) {
      grouped.set(rule.targetField, []);
    }
    grouped.get(rule.targetField)!.push(rule);
  }

  for (const arr of grouped.values()) {
    arr.sort((a, b) => a.priority - b.priority);
  }

  return grouped;
};

const findHeaderIndexByKeywords = (
  headers: string[],
  keywords: readonly string[]
) => {
  return headers.findIndex(header => {
    const normalized = normalizeHeaderText(header);
    return keywords.some(keyword => normalized.includes(keyword));
  });
};

const findHeaderIndexByRules = (
  headers: string[],
  rulesByField: Map<number, ColumnMappingRule[]>,
  targetField: ColumnMappingTargetField
) => {
  const fieldRules = rulesByField.get(targetField) ?? [];
  for (const rule of fieldRules) {
    for (let i = 0; i < headers.length; i++) {
      if (matchHeader(headers[i], rule)) return i;
    }
  }
  return -1;
};

const createDefaultColumns = () => ({
  projectColumnIndex: 0,
  specificationColumnIndex: 1,
  acceptanceColumnIndex: 2,
  remarkColumnIndex: 3 as number | undefined
});

const autoMatchColumns = (headers: string[], rules: ColumnMappingRule[]) => {
  const fieldMap: Record<number, keyof ReturnType<typeof createDefaultColumns>> = {
    [ColumnMappingTargetField.Project]: "projectColumnIndex",
    [ColumnMappingTargetField.Specification]: "specificationColumnIndex",
    [ColumnMappingTargetField.Acceptance]: "acceptanceColumnIndex",
    [ColumnMappingTargetField.Remark]: "remarkColumnIndex"
  };

  const result = createDefaultColumns();
  if (headers.length === 0 || rules.length === 0) return result;

  const rulesByField = buildRulesByField(rules);
  const matched = new Set<number>();

  for (const [targetField, fieldKey] of Object.entries(fieldMap)) {
    const fieldRules = rulesByField.get(Number(targetField));
    if (!fieldRules) continue;

    let found = false;
    for (const rule of fieldRules) {
      for (let i = 0; i < headers.length; i++) {
        if (matched.has(i)) continue;
        if (matchHeader(headers[i], rule)) {
          (result as any)[fieldKey] = i;
          matched.add(i);
          found = true;
          break;
        }
      }
      if (found) break;
    }
  }

  return result;
};

const pickBestContentColumn = (
  candidateIndexes: number[],
  sampleRows: string[][]
) => {
  if (candidateIndexes.length === 0) return -1;
  if (candidateIndexes.length === 1) return candidateIndexes[0];

  let bestIndex = candidateIndexes[0];
  let bestScore = -1;

  for (const index of candidateIndexes) {
    const nonEmpty = sampleRows
      .map(row => (row[index] || "").trim())
      .filter(Boolean);
    const averageLength =
      nonEmpty.length === 0
        ? 0
        : nonEmpty.reduce((sum, item) => sum + item.length, 0) / nonEmpty.length;
    const score = nonEmpty.length * 1000 + averageLength;
    if (score > bestScore) {
      bestScore = score;
      bestIndex = index;
    }
  }

  return bestIndex;
};

const isLikelyShortLabel = (value: string) => {
  const text = value.trim();
  if (!text) return false;
  if (text.length > 24) return false;
  return !/[，,。；;]/.test(text);
};

const isLikelyLongText = (value: string) => {
  const text = value.trim();
  if (!text) return false;
  if (text.length >= 28) return true;
  return text.length >= 16 && /[，,。；;]/.test(text);
};

const isCodeLikeHeader = (value: string) => {
  const normalized = normalizeHeaderText(value);
  if (!normalized) return false;
  return /^psd_[a-z0-9_]+$/.test(normalized) || /^[a-z]{2,}[a-z0-9_]{2,}$/.test(normalized);
};

const analyzeRow = (
  row: string[],
  rowIndex: number,
  rulesByField: Map<number, ColumnMappingRule[]>
): RowAnalysis => {
  const nonEmptyCount = row.filter(cell => !!cell?.trim()).length;
  const shortLabelCount = row.filter(cell => isLikelyShortLabel(cell || "")).length;
  const longTextCount = row.filter(cell => isLikelyLongText(cell || "")).length;
  const codeLikeCount = row.filter(cell => isCodeLikeHeader(cell || "")).length;
  const psdFieldCount = row.filter(cell =>
    normalizeHeaderText(cell).includes("psd_")
  ).length;

  const projectHit =
    findHeaderIndexByKeywords(row, smartHeaderKeywords.project) >= 0 ||
    findHeaderIndexByRules(row, rulesByField, ColumnMappingTargetField.Project) >= 0;
  const specificationStrongHit =
    findHeaderIndexByKeywords(row, smartHeaderKeywords.specificationStrong) >= 0;
  const specificationFallbackHit =
    findHeaderIndexByKeywords(row, smartHeaderKeywords.specificationFallback) >= 0 ||
    findHeaderIndexByRules(row, rulesByField, ColumnMappingTargetField.Specification) >= 0;
  const acceptanceHit =
    findHeaderIndexByKeywords(row, smartHeaderKeywords.acceptance) >= 0 ||
    findHeaderIndexByRules(row, rulesByField, ColumnMappingTargetField.Acceptance) >= 0;
  const remarkHit =
    findHeaderIndexByKeywords(row, smartHeaderKeywords.remark) >= 0 ||
    findHeaderIndexByRules(row, rulesByField, ColumnMappingTargetField.Remark) >= 0;

  const keywordHitCount = [
    projectHit,
    specificationStrongHit,
    specificationFallbackHit,
    acceptanceHit,
    remarkHit
  ].filter(Boolean).length;

  const ruleHitCount = [
    findHeaderIndexByRules(row, rulesByField, ColumnMappingTargetField.Project) >= 0,
    findHeaderIndexByRules(row, rulesByField, ColumnMappingTargetField.Specification) >= 0,
    findHeaderIndexByRules(row, rulesByField, ColumnMappingTargetField.Acceptance) >= 0,
    findHeaderIndexByRules(row, rulesByField, ColumnMappingTargetField.Remark) >= 0
  ].filter(Boolean).length;

  const labelDensity = nonEmptyCount === 0 ? 0 : shortLabelCount / nonEmptyCount;
  const score =
    (projectHit ? 14 : 0) +
    (specificationStrongHit ? 14 : 0) +
    (specificationFallbackHit ? 6 : 0) +
    (acceptanceHit ? 5 : 0) +
    (remarkHit ? 4 : 0) +
    Math.min(psdFieldCount, 4) * 3 +
    Math.min(codeLikeCount, 4) * 2 +
    Math.min(nonEmptyCount, 10) +
    labelDensity * 4 -
    longTextCount * 2;

  return {
    rowIndex,
    row,
    nonEmptyCount,
    shortLabelCount,
    longTextCount,
    codeLikeCount,
    psdFieldCount,
    projectHit,
    specificationStrongHit,
    specificationFallbackHit,
    acceptanceHit,
    remarkHit,
    ruleHitCount,
    keywordHitCount,
    score
  };
};

const shouldIncludeAdjacentHeaderRow = (
  primary: RowAnalysis,
  adjacent: RowAnalysis | undefined,
  relation: "before" | "after"
) => {
  if (!adjacent) return false;
  if (adjacent.nonEmptyCount < 2) return false;
  if (adjacent.longTextCount > 1) return false;

  const hasHeaderSignals =
    adjacent.keywordHitCount > 0 ||
    adjacent.ruleHitCount > 0 ||
    adjacent.psdFieldCount > 0 ||
    adjacent.codeLikeCount >= 2 ||
    adjacent.shortLabelCount >= Math.max(2, adjacent.nonEmptyCount - 1);
  if (!hasHeaderSignals) return false;

  if (relation === "before") {
    return (
      primary.psdFieldCount > 0 ||
      primary.codeLikeCount > 0 ||
      adjacent.keywordHitCount > 0
    );
  }

  return (
    primary.keywordHitCount > 0 &&
    (adjacent.psdFieldCount > 0 || adjacent.keywordHitCount > 0)
  );
};

const buildCombinedHeaders = (
  rows: string[][],
  columnCount: number
) => {
  return Array.from({ length: columnCount }, (_, columnIndex) => {
    const parts: string[] = [];
    for (const row of rows) {
      const value = (row[columnIndex] || "").trim();
      if (!value || parts.includes(value)) continue;
      parts.push(value);
    }
    return parts.join(" / ");
  });
};

const formatColumnLabel = (index: number | undefined, headers: string[]) => {
  if (index === undefined || index < 0) return "未识别";
  const header = headers[index] || `列${index + 1}`;
  return `[${index}] ${header}`;
};

const buildSummary = (
  detection: Omit<SmartFillExcelAutoDetection, "summary" | "summaryLines">
) => {
  const headerEndRow =
    (detection.headerRowStart ?? 1) + Math.max(1, detection.headerRowCount ?? 1) - 1;
  const headerText =
    detection.headerRowCount && detection.headerRowCount > 1
      ? `表头建议第 ${detection.headerRowStart}-${headerEndRow} 行`
      : `表头建议第 ${detection.headerRowStart} 行`;

  const summaryLines = [
    headerText,
    `数据建议从第 ${detection.dataStartRow} 行开始`,
    `项目列：${formatColumnLabel(detection.projectColumnIndex, detection.headers)}`,
    `规格列：${formatColumnLabel(detection.specificationColumnIndex, detection.headers)}`
  ];

  if (detection.acceptanceColumnIndex !== undefined) {
    summaryLines.push(
      `验收列：${formatColumnLabel(detection.acceptanceColumnIndex, detection.headers)}`
    );
  }

  if (detection.remarkColumnIndex !== undefined) {
    summaryLines.push(
      `备注列：${formatColumnLabel(detection.remarkColumnIndex, detection.headers)}`
    );
  }

  if (detection.warnings.length > 0) {
    summaryLines.push(`注意：${detection.warnings.join("；")}`);
  }

  return {
    summary: summaryLines.slice(0, 4).join("，"),
    summaryLines
  };
};

const resolveColumns = (
  headers: string[],
  sampleRows: string[][],
  rules: ColumnMappingRule[]
) => {
  const result = autoMatchColumns(headers, rules);
  const rulesByField = buildRulesByField(rules);

  const projectIndex = [
    findHeaderIndexByKeywords(headers, smartHeaderKeywords.project),
    findHeaderIndexByRules(headers, rulesByField, ColumnMappingTargetField.Project)
  ].find(index => index >= 0);

  if (projectIndex !== undefined && projectIndex >= 0) {
    result.projectColumnIndex = projectIndex;
  }

  const specStrongIndex = findHeaderIndexByKeywords(
    headers,
    smartHeaderKeywords.specificationStrong
  );
  if (specStrongIndex >= 0) {
    result.specificationColumnIndex = specStrongIndex;
  } else {
    const fallbackCandidates = headers
      .map((header, index) => ({ header, index }))
      .filter(({ header, index }) => {
        if (index === result.projectColumnIndex) return false;
        const normalized = normalizeHeaderText(header);
        if (
          smartHeaderKeywords.specificationFallback.some(keyword =>
            normalized.includes(keyword)
          )
        ) {
          return true;
        }

        return (
          findHeaderIndexByRules(
            [header],
            rulesByField,
            ColumnMappingTargetField.Specification
          ) >= 0
        );
      })
      .map(item => item.index);

    const bestSpecIndex = pickBestContentColumn(fallbackCandidates, sampleRows);
    if (bestSpecIndex >= 0) {
      result.specificationColumnIndex = bestSpecIndex;
    }
  }

  const acceptanceIndex = [
    findHeaderIndexByKeywords(headers, smartHeaderKeywords.acceptance),
    findHeaderIndexByRules(headers, rulesByField, ColumnMappingTargetField.Acceptance)
  ].find(index => index >= 0);
  if (acceptanceIndex !== undefined && acceptanceIndex >= 0) {
    result.acceptanceColumnIndex = acceptanceIndex;
  }

  const remarkIndex = [
    findHeaderIndexByKeywords(headers, smartHeaderKeywords.remark),
    findHeaderIndexByRules(headers, rulesByField, ColumnMappingTargetField.Remark)
  ].find(index => index >= 0);
  if (remarkIndex !== undefined && remarkIndex >= 0) {
    result.remarkColumnIndex = remarkIndex;
  }

  return result;
};

export const detectExcelSmartFillConfig = (
  table: TableInfo,
  previewRows: string[][],
  rules: ColumnMappingRule[]
): SmartFillExcelAutoDetection => {
  const usedStartRow = Math.max(1, table.usedRangeStartRow ?? 1);
  const fallbackHeaders = table.headers ?? [];
  const fallbackRows = previewRows;
  const fallbackColumns = resolveColumns(fallbackHeaders, fallbackRows, rules);
  const fallbackWarnings: string[] = [];

  if (fallbackColumns.projectColumnIndex === fallbackColumns.specificationColumnIndex) {
    fallbackWarnings.push("项目列与规格列落在同一列，请人工确认");
  }

  const fallbackBase: Omit<
    SmartFillExcelAutoDetection,
    "summary" | "summaryLines"
  > = {
    ...fallbackColumns,
    headerRowStart: usedStartRow,
    headerRowCount: 1,
    dataStartRow: usedStartRow + 1,
    filterEmptySourceRows: true,
    headers: fallbackHeaders,
    sampleRows: fallbackRows,
    confidence: "low",
    mode: "fallback",
    warnings: [
      "未可靠识别到表头，已回退到首行默认值",
      ...fallbackWarnings
    ]
  };
  const fallbackSummary = buildSummary(fallbackBase);
  const fallback = { ...fallbackBase, ...fallbackSummary };

  if (!previewRows.length) return fallback;

  const rulesByField = buildRulesByField(rules);
  const analyses = previewRows.map((row, index) => analyzeRow(row, index, rulesByField));
  const best = analyses.reduce<RowAnalysis | null>((current, item) => {
    if (item.nonEmptyCount === 0) return current;
    if (current == null || item.score > current.score) return item;
    return current;
  }, null);

  if (!best || best.score < 18) {
    return fallback;
  }

  let headerStartIndex = best.rowIndex;
  let headerRowCount = 1;

  const previous = analyses[best.rowIndex - 1];
  if (shouldIncludeAdjacentHeaderRow(best, previous, "before")) {
    headerStartIndex -= 1;
    headerRowCount += 1;
  }

  const next = analyses[best.rowIndex + 1];
  if (
    headerRowCount === 1 &&
    shouldIncludeAdjacentHeaderRow(best, next, "after")
  ) {
    headerRowCount += 1;
  }

  const headerRows = previewRows.slice(headerStartIndex, headerStartIndex + headerRowCount);
  const columnCount = Math.max(
    table.columnCount ?? 0,
    ...headerRows.map(row => row.length),
    ...previewRows.map(row => row.length)
  );
  const headers = buildCombinedHeaders(headerRows, columnCount);
  const sampleRows = previewRows.slice(headerStartIndex + headerRowCount);
  const columns = resolveColumns(headers, sampleRows, rules);

  const warnings: string[] = [];
  if (columns.projectColumnIndex === columns.specificationColumnIndex) {
    warnings.push("项目列与规格列落在同一列，请人工确认");
  }
  if (best.score < 24) {
    warnings.push("表头识别置信度一般，建议核对行号");
  }
  if (!sampleRows.some(row => row.some(cell => !!cell?.trim()))) {
    warnings.push("预览样本不足，建议刷新确认");
  }

  const confidence: AutoDetectionConfidence =
    best.score >= 30 &&
    columns.projectColumnIndex !== columns.specificationColumnIndex
      ? "high"
      : best.score >= 24
        ? "medium"
        : "low";

  const detectedBase: Omit<
    SmartFillExcelAutoDetection,
    "summary" | "summaryLines"
  > = {
    ...columns,
    headerRowStart: usedStartRow + headerStartIndex,
    headerRowCount,
    dataStartRow: usedStartRow + headerStartIndex + headerRowCount,
    filterEmptySourceRows: true,
    headers,
    sampleRows,
    confidence,
    mode: "detected",
    warnings
  };
  const detectedSummary = buildSummary(detectedBase);

  return {
    ...detectedBase,
    ...detectedSummary
  };
};
