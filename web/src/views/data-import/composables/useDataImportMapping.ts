import type { ComputedRef, Ref } from "vue";
import {
  getExcelPreviewColumnIndexes,
  getMissingExcelMappingFields,
  getMissingMappingFields,
  getPreviewCellValue,
  getWordPreviewColumnIndexes,
  normalizeExcelMappingByTable
} from "../dataImport.helpers";
import type { TableImportConfig } from "../dataImport.types";

type UseDataImportMappingOptions = {
  isExcelFile: ComputedRef<boolean>;
  tableConfigs: Ref<TableImportConfig[]>;
  excludedRowIndexMap: Ref<Record<number, number[]>>;
};

export function useDataImportMapping(options: UseDataImportMappingOptions) {
  const getExcludedRowIndexes = (tableIndex: number) =>
    options.excludedRowIndexMap.value[tableIndex] || [];

  const getExcludedRowIndexSet = (tableIndex: number) =>
    new Set(getExcludedRowIndexes(tableIndex));

  const setExcludedRowIndexes = (tableIndex: number, rowIndexes: number[]) => {
    const deduped = Array.from(
      new Set(
        rowIndexes
          .filter(index => Number.isInteger(index) && index >= 0)
          .sort((a, b) => a - b)
      )
    );

    if (deduped.length === 0) {
      const { [tableIndex]: _, ...rest } = options.excludedRowIndexMap.value;
      options.excludedRowIndexMap.value = rest;
      return;
    }

    options.excludedRowIndexMap.value = {
      ...options.excludedRowIndexMap.value,
      [tableIndex]: deduped
    };
  };

  const getExcelPreviewOptions = (cfg: TableImportConfig) => {
    const usedStartRow = cfg.tableInfo?.usedRangeStartRow ?? 1;
    const mapping = normalizeExcelMappingByTable(cfg.tableInfo, cfg.excelMapping);

    return {
      headerRowIndex: Math.max(0, (mapping.headerRowStart || usedStartRow) - usedStartRow),
      headerRowCount: Math.max(1, mapping.headerRowCount ?? 1),
      dataStartRowIndex: Math.max(0, (mapping.dataStartRow || usedStartRow) - usedStartRow)
    };
  };

  const validateAllTableMappings = () => {
    const missingByTable = options.tableConfigs.value
      .map(cfg => ({
        tableIndex: cfg.tableIndex,
        missing: options.isExcelFile.value
          ? getMissingExcelMappingFields(cfg.excelMapping)
          : getMissingMappingFields(cfg.wordMapping!)
      }))
      .filter(item => item.missing.length > 0);

    return {
      ok: missingByTable.length === 0,
      missingByTable
    };
  };

  return {
    getExcludedRowIndexes,
    getExcludedRowIndexSet,
    setExcludedRowIndexes,
    getExcelPreviewOptions,
    getMissingMappingFields,
    getMissingExcelMappingFields,
    getWordPreviewColumnIndexes,
    getExcelPreviewColumnIndexes,
    getPreviewCellValue,
    validateAllTableMappings
  };
}
