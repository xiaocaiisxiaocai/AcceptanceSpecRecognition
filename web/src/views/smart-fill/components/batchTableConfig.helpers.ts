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

/** 将旧版表级编辑值投影到主区域，同时保留其余离散区域。 */
export const syncPrimaryBatchTableRegion = <T extends BatchTableConfig>(
  item: T
): T => {
  if (!item.regions?.length) return item;

  return {
    ...item,
    regions: item.regions.map((region, regionIndex) =>
      regionIndex === 0
        ? {
            ...region,
            projectColumnIndex: item.projectColumnIndex,
            specificationColumnIndex: item.specificationColumnIndex,
            acceptanceColumnIndex: item.acceptanceColumnIndex,
            remarkColumnIndex: item.remarkColumnIndex,
            headerRowStart: item.headerRowStart,
            headerRowCount: item.headerRowCount,
            dataStartRow: item.dataStartRow,
            dataEndRow: item.dataEndRow
          }
        : region
    )
  };
};
