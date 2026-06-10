import type { BatchTableConfig as ApiBatchTableConfig } from "@/api/matching";
import type { TableInfo } from "@/api/document";

export interface BatchReplyTableConfigItem extends ApiBatchTableConfig {
  selected: boolean;
  tableInfo: TableInfo;
}

export type SourceTableOption = {
  value: number;
  label: string;
};

export type TargetPreviewState = {
  configs: BatchReplyTableConfigItem[];
  previewResults: Record<number, { canApply?: boolean } | null>;
};

const clampColumnIndex = (table: TableInfo, preferredIndex: number) => {
  const totalColumns = Math.max(table.columnCount, table.headers.length, 1);
  return Math.min(preferredIndex, totalColumns - 1);
};

export const buildTableConfig = (
  table: TableInfo,
  isExcel: boolean,
  selected: boolean,
  sourceTableIndex?: number
): BatchReplyTableConfigItem => {
  const usedStartRow = Math.max(1, table.usedRangeStartRow ?? 1);
  const totalColumns = Math.max(table.columnCount, table.headers.length, 1);

  return {
    tableIndex: table.index,
    sourceTableIndex,
    projectColumnIndex: clampColumnIndex(table, 0),
    specificationColumnIndex: clampColumnIndex(table, 1),
    acceptanceColumnIndex: clampColumnIndex(table, 2),
    remarkColumnIndex: totalColumns > 3 ? 3 : undefined,
    headerRowStart: isExcel ? usedStartRow : 1,
    headerRowCount: 1,
    dataStartRow: isExcel ? usedStartRow + 1 : 2,
    filterEmptySourceRows: true,
    duplicateResolutions: [],
    selected,
    tableInfo: table
  };
};

export const toBatchTableConfig = (
  item: BatchReplyTableConfigItem
): ApiBatchTableConfig => ({
  tableIndex: item.tableIndex,
  sourceTableIndex: item.sourceTableIndex,
  projectColumnIndex: item.projectColumnIndex,
  specificationColumnIndex: item.specificationColumnIndex,
  acceptanceColumnIndex: item.acceptanceColumnIndex,
  remarkColumnIndex: item.remarkColumnIndex,
  headerRowStart: item.headerRowStart,
  headerRowCount: item.headerRowCount,
  dataStartRow: item.dataStartRow,
  filterEmptySourceRows: item.filterEmptySourceRows,
  duplicateResolutions: item.duplicateResolutions
});

export const resolveDefaultSourceTableIndex = (
  tableIndex: number,
  options: SourceTableOption[]
) => {
  if (options.length === 0) {
    return undefined;
  }

  return options.some(option => option.value === tableIndex)
    ? tableIndex
    : options[0].value;
};

export function isTargetExecutable(targetFile: TargetPreviewState) {
  const selectedTables = targetFile.configs.filter(item => item.selected);
  if (selectedTables.length === 0) {
    return false;
  }

  return selectedTables.every(
    item => targetFile.previewResults[item.tableIndex]?.canApply === true
  );
}
