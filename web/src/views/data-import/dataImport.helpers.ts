import type { ColumnMapping as ColumnMappingType, TableInfo } from "@/api/document";
import type { ExcelSheetMapping, TableImportConfig } from "./dataImport.types";

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
  const headerRowStart = Math.max(usedStartRow, current.headerRowStart || usedStartRow);
  const minDataStart = headerRowStart + headerRowCount;
  const dataStartRow = Math.max(minDataStart, current.dataStartRow || minDataStart);
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
      dataStartRow: normalizedDraft.headerRowStart + normalizedDraft.headerRowCount
    });
  }

  return normalizedDraft;
};

export const createDefaultExcelMapping = (tableInfo?: TableInfo): ExcelSheetMapping =>
  normalizeExcelMappingByTable(tableInfo, {
    ...defaultExcelMapping(),
    headerRowStart: Math.max(1, tableInfo?.usedRangeStartRow ?? 1),
    dataEndRow: Math.max(
      1,
      (tableInfo?.usedRangeStartRow ?? 1) + Math.max(0, (tableInfo?.rowCount ?? 0) - 1)
    )
  });

export const getMissingMappingFields = (mapping: ColumnMappingType) => {
  const missing: string[] = [];
  if (mapping.projectColumn === undefined) missing.push("项目名称列");
  if (mapping.specificationColumn === undefined) missing.push("规格内容列");
  if (mapping.acceptanceColumn === undefined) missing.push("验收标准列");
  if (mapping.remarkColumn === undefined) missing.push("备注列");
  return missing;
};

export const getMissingExcelMappingFields = (mapping?: ExcelSheetMapping) => {
  const missing: string[] = [];
  if (!mapping) return ["Excel 映射未配置"];
  if (!mapping.projectColumn) missing.push("项目列");
  if (!mapping.specificationColumn) missing.push("规格列");
  if (mapping.headerRowStart < 1) missing.push("表头起始行");
  if (mapping.headerRowCount < 0) missing.push("表头行数");
  if (mapping.dataStartRow < 1) missing.push("数据起始行");
  if (mapping.dataEndRow < mapping.dataStartRow) missing.push("数据结束行");
  return missing;
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
