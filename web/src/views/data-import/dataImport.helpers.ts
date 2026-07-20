import type {
  ColumnMapping as ColumnMappingType,
  TableData,
  TableInfo
} from "@/api/document";
import type {
  ExcelSheetMapping,
  ImportSkippedRowWithTable,
  SkippedPreviewColumn,
  SkippedRowsGroup,
  TableImportConfig
} from "./dataImport.types";

export const defaultWordMapping = (): ColumnMappingType => ({
  projectColumn: undefined,
  specificationColumn: undefined,
  acceptanceColumn: undefined,
  remarkColumn: undefined,
  headerRowIndex: 0,
  dataStartRowIndex: 1
});

export const defaultExcelMapping = (): ExcelSheetMapping => ({
  projectColumn: undefined,
  specificationColumn: undefined,
  acceptanceColumn: undefined,
  remarkColumn: undefined,
  headerRowStart: 1,
  headerRowCount: 1,
  dataStartRow: 2,
  dataEndRow: 2
});

export const normalizeExcelMappingByTable = (
  tableInfo: TableInfo | undefined,
  mapping?: ExcelSheetMapping
): ExcelSheetMapping => {
  const usedStartRow = Math.max(1, tableInfo?.usedRangeStartRow ?? 1);
  const usedEndRow = Math.max(
    usedStartRow,
    usedStartRow + Math.max(0, (tableInfo?.rowCount ?? 0) - 1)
  );
  const current = mapping ?? defaultExcelMapping();
  const headerRowCount = Math.max(0, current.headerRowCount ?? 1);
  const headerRowStart = Math.max(
    usedStartRow,
    current.headerRowStart || usedStartRow
  );
  const minDataStart = headerRowStart + headerRowCount;
  const dataStartRow = Math.max(
    minDataStart,
    current.dataStartRow || minDataStart
  );
  const dataEndRow = Math.max(
    dataStartRow,
    Math.min(usedEndRow, current.dataEndRow || usedEndRow)
  );

  return {
    ...current,
    headerRowStart,
    headerRowCount,
    dataStartRow,
    dataEndRow
  };
};

export type ExcelColumnOption = {
  value: number;
  label: string;
  letter: string;
  localIndex: number;
  header: string;
};

export const toExcelColumnLetter = (columnNumber?: number) => {
  if (!columnNumber || columnNumber <= 0) {
    return "";
  }

  let current = columnNumber;
  let result = "";
  while (current > 0) {
    current -= 1;
    result = String.fromCharCode(65 + (current % 26)) + result;
    current = Math.floor(current / 26);
  }
  return result;
};

export const buildExcelColumnOptions = (
  tableInfo: TableInfo | undefined,
  previewData?: TableData | null
): ExcelColumnOption[] => {
  const usedStartColumn = tableInfo?.usedRangeStartColumn ?? 1;
  const displayHeaders = previewData?.headers || tableInfo?.headers || [];
  const rowMaxColumnCount = Math.max(
    0,
    ...(previewData?.rows || []).map(row => row.length)
  );
  const columnCount = Math.max(
    tableInfo?.columnCount ?? 0,
    previewData?.columnCount ?? 0,
    displayHeaders.length,
    rowMaxColumnCount
  );

  return Array.from({ length: columnCount }, (_, index) => {
    const value = usedStartColumn + index;
    const header = (displayHeaders[index] || "").trim();
    const letter = toExcelColumnLetter(value);
    return {
      value,
      label: `第 ${index + 1} 列（${letter}）${header}`,
      letter,
      localIndex: index,
      header
    };
  });
};

export type ExcelMappingRowField =
  | "headerRowStart"
  | "headerRowCount"
  | "dataStartRow"
  | "dataEndRow";

export const applyExcelMappingRowFieldChange = (
  tableInfo: TableInfo | undefined,
  mapping: ExcelSheetMapping | undefined,
  field: ExcelMappingRowField,
  value: number
): ExcelSheetMapping => {
  const normalizedCurrent = normalizeExcelMappingByTable(tableInfo, mapping);
  const normalizedDraft = normalizeExcelMappingByTable(tableInfo, {
    ...normalizedCurrent,
    [field]: value
  });

  if (field === "headerRowStart" || field === "headerRowCount") {
    return normalizeExcelMappingByTable(tableInfo, {
      ...normalizedDraft,
      dataStartRow:
        normalizedDraft.headerRowStart + normalizedDraft.headerRowCount
    });
  }

  return normalizedDraft;
};

export const createDefaultExcelMapping = (
  tableInfo?: TableInfo
): ExcelSheetMapping =>
  normalizeExcelMappingByTable(tableInfo, {
    ...defaultExcelMapping(),
    headerRowStart: Math.max(1, tableInfo?.usedRangeStartRow ?? 1),
    dataEndRow: Math.max(
      1,
      (tableInfo?.usedRangeStartRow ?? 1) +
        Math.max(0, (tableInfo?.rowCount ?? 0) - 1)
    )
  });

export const getMissingMappingFields = (
  mapping: ColumnMappingType,
  isSpecificationOnly = false
) => {
  const missing: string[] = [];
  if (!isSpecificationOnly && mapping.projectColumn == null) {
    missing.push("项目名称列");
  }
  if (mapping.specificationColumn == null) missing.push("规格内容列");
  if (mapping.acceptanceColumn == null) missing.push("验收标准列");
  if (mapping.remarkColumn == null) missing.push("备注列");
  return missing;
};

export const getMissingExcelMappingFields = (
  mapping?: ExcelSheetMapping,
  isSpecificationOnly = false
) => {
  const missing: string[] = [];
  if (!mapping) return ["Excel 映射未配置"];
  if (!isSpecificationOnly && !mapping.projectColumn) missing.push("项目列");
  if (!mapping.specificationColumn) missing.push("规格列");
  if (mapping.headerRowStart < 1) missing.push("表头起始行");
  if (mapping.headerRowCount < 0) missing.push("表头行数");
  if (mapping.dataStartRow < 1) missing.push("数据起始行");
  if (mapping.dataEndRow < mapping.dataStartRow) missing.push("数据结束行");
  return missing;
};

export const shouldBackfillProjectFromSpecification = (
  cfg: TableImportConfig
) => {
  if (!cfg.isSpecificationOnly) {
    return false;
  }

  const projectColumn = cfg.excelMapping
    ? normalizeExcelMappingByTable(cfg.tableInfo, cfg.excelMapping)
        .projectColumn
    : cfg.wordMapping?.projectColumn;

  return projectColumn == null;
};

export const getWordPreviewColumnIndexes = (cfg: TableImportConfig) => {
  const mapping = cfg.wordMapping;
  return {
    projectColumn: mapping?.projectColumn,
    specificationColumn: mapping?.specificationColumn,
    acceptanceColumn: mapping?.acceptanceColumn,
    remarkColumn: mapping?.remarkColumn
  };
};

export const getExcelPreviewColumnIndexes = (cfg: TableImportConfig) => {
  const mapping = normalizeExcelMappingByTable(cfg.tableInfo, cfg.excelMapping);
  const usedStartColumn = cfg.tableInfo?.usedRangeStartColumn ?? 1;
  const toLocalColumn = (column?: number) =>
    column && column >= usedStartColumn ? column - usedStartColumn : undefined;

  return {
    projectColumn: toLocalColumn(mapping.projectColumn),
    specificationColumn: toLocalColumn(mapping.specificationColumn),
    acceptanceColumn: toLocalColumn(mapping.acceptanceColumn),
    remarkColumn: toLocalColumn(mapping.remarkColumn)
  };
};

export const getPreviewCellValue = (row: string[], columnIndex?: number) => {
  if (columnIndex === undefined || columnIndex < 0) return "";
  const value = row?.[columnIndex];
  return typeof value === "string" ? value.trim() : "";
};

export const buildSkippedRowsGroups = (
  rows: ImportSkippedRowWithTable[],
  tableConfigs: TableImportConfig[]
): SkippedRowsGroup[] => {
  if (rows.length === 0) return [];

  const grouped = new Map<string, ImportSkippedRowWithTable[]>();
  for (const row of rows) {
    const groupKey = `${row.tableIndex}:${row.regionId ?? "default"}`;
    const list = grouped.get(groupKey) || [];
    list.push(row);
    grouped.set(groupKey, list);
  }

  return Array.from(grouped.entries())
    .sort((a, b) => {
      const first = a[1][0];
      const second = b[1][0];
      return (
        first.tableIndex - second.tableIndex ||
        (first.regionId ?? "").localeCompare(second.regionId ?? "")
      );
    })
    .map(([, groupRows]) => {
      const tableIndex = groupRows[0].tableIndex;
      const regionId = groupRows[0].regionId;
      const tableCfg = tableConfigs.find(cfg => cfg.tableIndex === tableIndex);
      const regionLocation = tableCfg?.excelPreviewRowLocations?.find(
        item => item.regionId === regionId
      );
      const headers = regionLocation?.headers?.length
        ? regionLocation.headers
        : tableCfg?.previewData?.headers || tableCfg?.tableInfo?.headers || [];
      const maxColumnCount = groupRows.reduce(
        (max, row) => Math.max(max, row.rowValues?.length || 0),
        0
      );

      const columns: SkippedPreviewColumn[] = Array.from(
        { length: maxColumnCount },
        (_, i) => {
          const header = (headers[i] || "").trim();
          return {
            index: i,
            label: header || `列${i + 1}`
          };
        }
      );

      return {
        tableIndex,
        regionId,
        regionIndex: regionLocation?.regionIndex,
        rows: groupRows,
        columns
      };
    });
};
