import type { BatchTableConfig } from "@/api/matching";

export type ExcelBatchTableRowField =
  | "headerRowStart"
  | "headerRowCount"
  | "dataStartRow";

export type ExcelBatchTableRowConfig = Pick<
  BatchTableConfig,
  "headerRowStart" | "headerRowCount" | "dataStartRow"
> & {
  tableInfo: {
    usedRangeStartRow?: number;
  };
};

export const normalizeExcelBatchTableRows = <
  T extends ExcelBatchTableRowConfig
>(
  item: T
) => {
  const usedStartRow = Math.max(1, item.tableInfo.usedRangeStartRow ?? 1);
  const headerRowStart = Math.max(
    usedStartRow,
    item.headerRowStart ?? usedStartRow
  );
  const headerRowCount = Math.max(0, item.headerRowCount ?? 1);
  const minDataStartRow = headerRowStart + headerRowCount;
  const dataStartRow = Math.max(
    minDataStartRow,
    item.dataStartRow ?? minDataStartRow
  );

  return {
    ...item,
    headerRowStart,
    headerRowCount,
    dataStartRow
  };
};

export const applyExcelBatchTableRowFieldChange = <
  T extends ExcelBatchTableRowConfig
>(
  item: T,
  field: ExcelBatchTableRowField,
  value: number
) => {
  const normalizedCurrent = normalizeExcelBatchTableRows(item);
  const normalizedDraft = normalizeExcelBatchTableRows({
    ...normalizedCurrent,
    [field]: value
  });

  if (field === "headerRowStart" || field === "headerRowCount") {
    return normalizeExcelBatchTableRows({
      ...normalizedDraft,
      dataStartRow:
        normalizedDraft.headerRowStart + normalizedDraft.headerRowCount
    });
  }

  return normalizedDraft;
};
