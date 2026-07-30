import type {
  SmartConfigRecognizedField,
  SmartConfigRecognizedRegion
} from "../../api/smart-config";
import {
  excelColumnLabelToNumber,
  parseExcelA1ColumnRange,
  toExcelColumnLabel
} from "./smart-structure-recognition";

export type SmartStructureExcelField =
  | "project"
  | "specification"
  | "acceptance"
  | "remark";

export type SmartStructureExcelEndpoint = "start" | "end";

export interface SmartStructureExcelFieldEndpoints {
  start: string;
  end: string;
}

export interface SmartStructureExcelRegionBounds {
  baseRow: number;
  baseColumn: number;
  rowCount: number;
  columnCount: number;
}

export interface SmartStructureExcelRegionDraft {
  regionId: string;
  source: SmartConfigRecognizedRegion;
  headerStartRow: number;
  headerRowCount: number;
  dataStartRow: number;
  dataEndRow: number;
  projectColumn?: number;
  specificationColumn?: number;
  acceptanceColumn?: number;
  remarkColumn?: number;
  isSpecificationOnly: boolean;
}

export type SmartStructureExcelRowPatch = Partial<
  Pick<SmartStructureExcelRegionDraft, "dataStartRow" | "dataEndRow">
>;

export type SmartStructureExcelRegionValidationField =
  | "headerStartRow"
  | "headerRowCount"
  | "dataStartRow"
  | "dataEndRow"
  | "projectColumn"
  | "specificationColumn"
  | "acceptanceColumn"
  | "remarkColumn"
  | "region";

export interface SmartStructureExcelRegionValidationIssue {
  regionId: string;
  regionIndex: number;
  field: SmartStructureExcelRegionValidationField;
  code:
    | "header-out-of-bounds"
    | "header-row-count"
    | "header-data-overlap"
    | "data-out-of-bounds"
    | "project-required"
    | "specification-required"
    | "acceptance-required"
    | "remark-required"
    | "column-out-of-bounds"
    | "duplicate-column"
    | "region-overlap";
  message: string;
}

export type SmartStructureExcelA1Errors = Record<
  string,
  Partial<
    Record<
      SmartStructureExcelField,
      Partial<Record<SmartStructureExcelEndpoint, string>>
    >
  >
>;

export type SmartStructureExcelA1PatchResult =
  | {
      ok: true;
      draft: SmartStructureExcelRegionDraft;
      normalizedRange: string;
      synchronizedRows: boolean;
    }
  | {
      ok: false;
      error: string;
    };

const columnKeys: Record<
  SmartStructureExcelField,
  "projectColumn" | "specificationColumn" | "acceptanceColumn" | "remarkColumn"
> = {
  project: "projectColumn",
  specification: "specificationColumn",
  acceptance: "acceptanceColumn",
  remark: "remarkColumn"
};

const fieldDefinitions: Array<{
  field: SmartStructureExcelField;
  recognizedField: SmartConfigRecognizedField["field"];
  label: string;
}> = [
  { field: "project", recognizedField: "Project", label: "项目列" },
  {
    field: "specification",
    recognizedField: "Specification",
    label: "规格列"
  },
  { field: "acceptance", recognizedField: "Acceptance", label: "验收列" },
  { field: "remark", recognizedField: "Remark", label: "备注列" }
];

const cloneRecognizedRegion = (
  region: SmartConfigRecognizedRegion
): SmartConfigRecognizedRegion => ({
  ...region,
  headers: [...region.headers],
  issues: region.issues?.map(issue => ({ ...issue })),
  fields: region.fields.map(field => ({ ...field })),
  fieldConflicts: region.fieldConflicts?.map(conflict => ({
    ...conflict,
    candidates: conflict.candidates.map(candidate => ({
      ...candidate,
      samples: [...candidate.samples]
    }))
  }))
});

export const normalizeSmartStructureInlineExcelRegion = (
  region: SmartConfigRecognizedRegion
): SmartConfigRecognizedRegion => {
  const source = cloneRecognizedRegion(region);
  const dataStartRowIndex = Number.isInteger(source.dataStartRowIndex)
    ? Math.max(1, source.dataStartRowIndex)
    : 1;
  const dataEndRowIndex =
    source.dataEndRowIndex == null
      ? undefined
      : Math.max(dataStartRowIndex, source.dataEndRowIndex);

  return {
    ...source,
    headerRowIndex: dataStartRowIndex - 1,
    headerRowCount: 1,
    dataStartRowIndex,
    dataEndRowIndex
  };
};

const toAbsoluteColumn = (
  relativeColumn: number | null | undefined,
  bounds: SmartStructureExcelRegionBounds
) => (relativeColumn == null ? undefined : bounds.baseColumn + relativeColumn);

const toRelativeColumn = (
  absoluteColumn: number | undefined,
  bounds: SmartStructureExcelRegionBounds
) => (absoluteColumn == null ? undefined : absoluteColumn - bounds.baseColumn);

const getMaximumRow = (bounds: SmartStructureExcelRegionBounds) =>
  bounds.baseRow + Math.max(1, bounds.rowCount) - 1;

const getMaximumColumn = (bounds: SmartStructureExcelRegionBounds) =>
  bounds.baseColumn + Math.max(1, bounds.columnCount) - 1;

export const getSmartStructureExcelRowInputLimits = (
  draft: SmartStructureExcelRegionDraft,
  bounds: SmartStructureExcelRegionBounds
) => {
  const maximumRow = getMaximumRow(bounds);
  const dataStartMinimum = bounds.baseRow + 1;
  const dataStartMaximum = Math.max(
    dataStartMinimum,
    maximumRow,
    draft.dataStartRow
  );
  const dataEndMinimum = Math.max(dataStartMinimum, draft.dataStartRow);
  const dataEndMaximum = Math.max(dataEndMinimum, maximumRow, draft.dataEndRow);

  return {
    dataStartMinimum,
    dataStartMaximum,
    dataEndMinimum,
    dataEndMaximum
  };
};

const getFieldColumn = (
  draft: SmartStructureExcelRegionDraft,
  field: SmartStructureExcelField
) => draft[columnKeys[field]];

export const createSmartStructureExcelRegionDraft = (
  region: SmartConfigRecognizedRegion,
  bounds: SmartStructureExcelRegionBounds
): SmartStructureExcelRegionDraft => {
  const source = normalizeSmartStructureInlineExcelRegion(region);
  return {
    regionId: source.regionId,
    source,
    headerStartRow: bounds.baseRow + source.headerRowIndex,
    headerRowCount: source.headerRowCount,
    dataStartRow: bounds.baseRow + source.dataStartRowIndex,
    dataEndRow:
      bounds.baseRow +
      (source.dataEndRowIndex ?? Math.max(0, bounds.rowCount - 1)),
    projectColumn: source.isSpecificationOnly
      ? undefined
      : toAbsoluteColumn(source.projectColumnIndex, bounds),
    specificationColumn: toAbsoluteColumn(
      source.specificationColumnIndex,
      bounds
    ),
    acceptanceColumn: toAbsoluteColumn(source.acceptanceColumnIndex, bounds),
    remarkColumn: toAbsoluteColumn(source.remarkColumnIndex, bounds),
    isSpecificationOnly: source.isSpecificationOnly
  };
};

export const formatSmartStructureExcelFieldRange = (
  draft: SmartStructureExcelRegionDraft,
  field: SmartStructureExcelField
) => {
  const column = getFieldColumn(draft, field);
  if (column == null || (field === "project" && draft.isSpecificationOnly)) {
    return "";
  }
  const label = toExcelColumnLabel(column);
  return `${label}${draft.dataStartRow}:${label}${draft.dataEndRow}`;
};

export const formatSmartStructureExcelFieldEndpoints = (
  draft: SmartStructureExcelRegionDraft,
  field: SmartStructureExcelField
): SmartStructureExcelFieldEndpoints => {
  const column = getFieldColumn(draft, field);
  if (column == null || (field === "project" && draft.isSpecificationOnly)) {
    return { start: "", end: "" };
  }
  const label = toExcelColumnLabel(column);
  return {
    start: `${label}${draft.dataStartRow}`,
    end: `${label}${draft.dataEndRow}`
  };
};

export const applySmartStructureExcelRowPatch = (
  draft: SmartStructureExcelRegionDraft,
  patch: SmartStructureExcelRowPatch,
  bounds: SmartStructureExcelRegionBounds
): SmartStructureExcelRegionDraft => {
  const dataStartRow = Math.max(
    bounds.baseRow + 1,
    patch.dataStartRow ?? draft.dataStartRow
  );
  const dataEndRow = Math.max(
    patch.dataEndRow ?? draft.dataEndRow,
    dataStartRow
  );

  return {
    ...draft,
    headerStartRow: dataStartRow - 1,
    headerRowCount: 1,
    dataStartRow,
    dataEndRow
  };
};

export const applySmartStructureExcelColumnPatch = (
  draft: SmartStructureExcelRegionDraft,
  field: SmartStructureExcelField,
  column: number | null | undefined
): SmartStructureExcelRegionDraft => ({
  ...draft,
  [columnKeys[field]]: column ?? undefined
});

export const applySmartStructureExcelA1Patch = (
  draft: SmartStructureExcelRegionDraft,
  field: SmartStructureExcelField,
  value: string,
  bounds: SmartStructureExcelRegionBounds
): SmartStructureExcelA1PatchResult => {
  const parsed = parseExcelA1ColumnRange(value);
  if (!parsed) {
    return {
      ok: false,
      error: "A1 范围必须是同一列、包含起止行且起止行正序"
    };
  }

  if (parsed.startRow <= bounds.baseRow) {
    return {
      ok: false,
      error: "A1 数据起始行前必须在当前工作表已用范围内保留一行表头"
    };
  }

  if (
    parsed.startRow < bounds.baseRow ||
    parsed.endRow > getMaximumRow(bounds)
  ) {
    return { ok: false, error: "A1 行范围超出当前工作表已用范围" };
  }

  if (
    parsed.columnNumber < bounds.baseColumn ||
    parsed.columnNumber > getMaximumColumn(bounds)
  ) {
    return { ok: false, error: "A1 列范围超出当前工作表已用范围" };
  }

  const synchronizedRows =
    parsed.startRow !== draft.dataStartRow ||
    parsed.endRow !== draft.dataEndRow;
  const updated = applySmartStructureExcelColumnPatch(
    applySmartStructureExcelRowPatch(
      draft,
      {
        dataStartRow: parsed.startRow,
        dataEndRow: parsed.endRow
      },
      bounds
    ),
    field,
    parsed.columnNumber
  );

  return {
    ok: true,
    draft: updated,
    normalizedRange: parsed.normalized,
    synchronizedRows
  };
};

const parseExcelA1Cell = (value: string) => {
  const normalizedInput = value
    .trim()
    .toUpperCase()
    .replace(/\$/g, "")
    .replace(/\s+/g, "");
  const match = /^([A-Z]+)([1-9]\d*)$/.exec(normalizedInput);
  if (!match) return undefined;
  const columnNumber = excelColumnLabelToNumber(match[1]);
  const row = Number(match[2]);
  if (!columnNumber || !Number.isSafeInteger(row)) return undefined;
  return {
    columnNumber,
    row,
    normalized: `${toExcelColumnLabel(columnNumber)}${row}`
  };
};

export const applySmartStructureExcelEndpointPatch = (
  draft: SmartStructureExcelRegionDraft,
  field: SmartStructureExcelField,
  endpoint: SmartStructureExcelEndpoint,
  value: string,
  bounds: SmartStructureExcelRegionBounds
): SmartStructureExcelA1PatchResult => {
  const parsed = parseExcelA1Cell(value);
  if (!parsed) {
    return {
      ok: false,
      error: `${endpoint === "start" ? "起始" : "结束"}单元格必须使用 A1 格式，例如 C9`
    };
  }
  if (endpoint === "end" && parsed.row < draft.dataStartRow) {
    return { ok: false, error: "结束单元格不能早于起始单元格" };
  }
  if (endpoint === "start" && parsed.row > draft.dataEndRow) {
    return { ok: false, error: "起始单元格不能晚于结束单元格" };
  }

  const column = toExcelColumnLabel(parsed.columnNumber);
  const startRow = endpoint === "start" ? parsed.row : draft.dataStartRow;
  const endRow = endpoint === "start" ? draft.dataEndRow : parsed.row;
  return applySmartStructureExcelA1Patch(
    draft,
    field,
    `${column}${startRow}:${column}${endRow}`,
    bounds
  );
};

export const setSmartStructureSpecificationOnly = (
  draft: SmartStructureExcelRegionDraft,
  enabled: boolean
): SmartStructureExcelRegionDraft => ({
  ...draft,
  isSpecificationOnly: enabled,
  projectColumn: enabled ? undefined : draft.projectColumn
});

const rebuildRecognizedFields = (
  draft: SmartStructureExcelRegionDraft,
  bounds: SmartStructureExcelRegionBounds
): SmartConfigRecognizedField[] =>
  fieldDefinitions.map(definition => {
    const absoluteColumn =
      definition.field === "project" && draft.isSpecificationOnly
        ? undefined
        : getFieldColumn(draft, definition.field);
    const columnIndex = toRelativeColumn(absoluteColumn, bounds);
    const previous = draft.source.fields.find(
      field => field.field === definition.recognizedField
    );
    return {
      field: definition.recognizedField,
      columnIndex,
      header:
        columnIndex == null
          ? undefined
          : draft.source.headers[columnIndex] || undefined,
      confidence: previous?.confidence ?? 1,
      source: previous?.source ?? "Manual"
    };
  });

export const toSmartConfigRecognizedRegion = (
  draft: SmartStructureExcelRegionDraft,
  bounds: SmartStructureExcelRegionBounds,
  regionIndex = draft.source.regionIndex
): SmartConfigRecognizedRegion => ({
  ...cloneRecognizedRegion(draft.source),
  regionId: draft.regionId,
  regionIndex,
  headerRowIndex: draft.dataStartRow - 1 - bounds.baseRow,
  headerRowCount: 1,
  dataStartRowIndex: draft.dataStartRow - bounds.baseRow,
  dataEndRowIndex: draft.dataEndRow - bounds.baseRow,
  projectColumnIndex: draft.isSpecificationOnly
    ? undefined
    : toRelativeColumn(draft.projectColumn, bounds),
  specificationColumnIndex: toRelativeColumn(draft.specificationColumn, bounds),
  acceptanceColumnIndex: toRelativeColumn(draft.acceptanceColumn, bounds),
  remarkColumnIndex: toRelativeColumn(draft.remarkColumn, bounds),
  isSpecificationOnly: draft.isSpecificationOnly,
  fields: rebuildRecognizedFields(draft, bounds)
});

const addIssue = (
  issues: SmartStructureExcelRegionValidationIssue[],
  draft: SmartStructureExcelRegionDraft,
  regionIndex: number,
  field: SmartStructureExcelRegionValidationField,
  code: SmartStructureExcelRegionValidationIssue["code"],
  message: string
) => {
  issues.push({
    regionId: draft.regionId,
    regionIndex,
    field,
    code,
    message
  });
};

export const validateSmartStructureExcelRegionDrafts = (
  drafts: SmartStructureExcelRegionDraft[],
  bounds: SmartStructureExcelRegionBounds
): SmartStructureExcelRegionValidationIssue[] => {
  const issues: SmartStructureExcelRegionValidationIssue[] = [];
  const maximumRow = getMaximumRow(bounds);
  const maximumColumn = getMaximumColumn(bounds);

  drafts.forEach((draft, regionIndex) => {
    const headerStartValid =
      Number.isInteger(draft.headerStartRow) &&
      draft.headerStartRow >= bounds.baseRow &&
      draft.headerStartRow <= maximumRow;
    const headerCountValid =
      Number.isInteger(draft.headerRowCount) && draft.headerRowCount === 1;
    const headerEndRow =
      draft.headerStartRow + Math.max(1, draft.headerRowCount) - 1;

    if (!headerStartValid || (headerCountValid && headerEndRow > maximumRow)) {
      addIssue(
        issues,
        draft,
        regionIndex,
        "headerStartRow",
        "header-out-of-bounds",
        "表头行超出当前工作表已用范围"
      );
    }
    if (!headerCountValid) {
      addIssue(
        issues,
        draft,
        regionIndex,
        "headerRowCount",
        "header-row-count",
        "表头固定只读取数据起始行的上一行"
      );
    }

    const dataValid =
      Number.isInteger(draft.dataStartRow) &&
      Number.isInteger(draft.dataEndRow) &&
      draft.dataStartRow >= bounds.baseRow + 1 &&
      draft.dataEndRow >= draft.dataStartRow &&
      draft.dataEndRow <= maximumRow;
    if (!dataValid) {
      addIssue(
        issues,
        draft,
        regionIndex,
        "dataEndRow",
        "data-out-of-bounds",
        "数据起止行必须正序且位于当前工作表已用范围内"
      );
    }
    if (
      headerStartValid &&
      headerCountValid &&
      Number.isInteger(draft.dataStartRow) &&
      (headerEndRow !== draft.dataStartRow - 1 ||
        draft.headerStartRow !== draft.dataStartRow - 1)
    ) {
      addIssue(
        issues,
        draft,
        regionIndex,
        "dataStartRow",
        "header-data-overlap",
        "表头必须固定为数据起始行的上一行"
      );
    }

    if (!draft.isSpecificationOnly && draft.projectColumn == null) {
      addIssue(
        issues,
        draft,
        regionIndex,
        "projectColumn",
        "project-required",
        "普通模式必须选择项目列"
      );
    }
    if (draft.specificationColumn == null) {
      addIssue(
        issues,
        draft,
        regionIndex,
        "specificationColumn",
        "specification-required",
        "必须选择规格列"
      );
    }
    if (draft.acceptanceColumn == null) {
      addIssue(
        issues,
        draft,
        regionIndex,
        "acceptanceColumn",
        "acceptance-required",
        "必须选择验收列"
      );
    }
    if (draft.remarkColumn == null) {
      addIssue(
        issues,
        draft,
        regionIndex,
        "remarkColumn",
        "remark-required",
        "必须选择备注列"
      );
    }

    const activeFields = fieldDefinitions.filter(
      definition => definition.field !== "project" || !draft.isSpecificationOnly
    );
    const columns = new Map<number, typeof activeFields>();
    for (const definition of activeFields) {
      const column = getFieldColumn(draft, definition.field);
      if (column == null) continue;
      if (
        !Number.isInteger(column) ||
        column < bounds.baseColumn ||
        column > maximumColumn
      ) {
        addIssue(
          issues,
          draft,
          regionIndex,
          columnKeys[definition.field],
          "column-out-of-bounds",
          `${definition.label}超出当前工作表已用范围`
        );
      }
      const matches = columns.get(column) ?? [];
      matches.push(definition);
      columns.set(column, matches);
    }

    for (const matches of columns.values()) {
      if (matches.length < 2) continue;
      for (const definition of matches) {
        addIssue(
          issues,
          draft,
          regionIndex,
          columnKeys[definition.field],
          "duplicate-column",
          `${matches.map(match => match.label).join("、")}不能使用同一列`
        );
      }
    }
  });

  const overlappingRegions = new Set<number>();
  for (let leftIndex = 0; leftIndex < drafts.length; leftIndex += 1) {
    const left = drafts[leftIndex];
    const leftEnd = left.dataEndRow;
    for (
      let rightIndex = leftIndex + 1;
      rightIndex < drafts.length;
      rightIndex += 1
    ) {
      const right = drafts[rightIndex];
      const overlaps =
        left.headerStartRow <= right.dataEndRow &&
        right.headerStartRow <= leftEnd;
      if (!overlaps) continue;
      overlappingRegions.add(leftIndex);
      overlappingRegions.add(rightIndex);
    }
  }

  for (const regionIndex of overlappingRegions) {
    addIssue(
      issues,
      drafts[regionIndex],
      regionIndex,
      "region",
      "region-overlap",
      "同一工作表的数据区域不能重叠"
    );
  }

  return issues;
};

export const resolveSmartStructureExcelBlockingValidationError = (
  drafts: SmartStructureExcelRegionDraft[],
  a1Errors: SmartStructureExcelA1Errors,
  bounds: SmartStructureExcelRegionBounds
) => {
  for (const [regionIndex, draft] of drafts.entries()) {
    const localError = Object.values(a1Errors[draft.regionId] ?? {})
      .flatMap(errors => Object.values(errors ?? {}))
      .find(Boolean);
    if (localError) return `区域 ${regionIndex + 1}：${localError}`;
  }

  const issue = validateSmartStructureExcelRegionDrafts(drafts, bounds)[0];
  return issue ? `区域 ${issue.regionIndex + 1}：${issue.message}` : "";
};
