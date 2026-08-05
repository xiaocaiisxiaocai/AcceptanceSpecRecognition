import type { ColumnMapping } from "@/api/document";
import type { BatchTableRegionConfig } from "@/api/matching";
import type { BatchTableConfigItem } from "./components/batchTableConfig.types";

export interface SmartFillStructurePreviewRegion {
  key: string;
  label: string;
  regionIndex: number;
  headerRowIndex: number;
  headerRowCount: number;
  dataStartRowIndex: number;
  dataEndRowIndex?: number;
  previewRows: number;
  sourceRowNumberStart: number;
  mapping: ColumnMapping;
}

const MAX_PREVIEW_ROWS = 500;

const toRegionConfig = (
  config: BatchTableConfigItem
): BatchTableRegionConfig => ({
  regionIndex: 0,
  projectColumnIndex: config.projectColumnIndex,
  specificationColumnIndex: config.specificationColumnIndex,
  acceptanceColumnIndex: config.acceptanceColumnIndex,
  remarkColumnIndex: config.remarkColumnIndex,
  headerRowStart: config.headerRowStart,
  headerRowCount: config.headerRowCount,
  dataStartRow: config.dataStartRow,
  dataEndRow: config.dataEndRow
});

export const resolveSmartFillStructurePreviewConfig = (
  configs: BatchTableConfigItem[],
  activeTableIndex?: number
) =>
  configs.find(config => config.tableIndex === activeTableIndex) ??
  configs.find(config => config.selected) ??
  configs[0];

export const buildSmartFillStructurePreviewRegions = (
  config: BatchTableConfigItem,
  isExcelFile: boolean
): SmartFillStructurePreviewRegion[] => {
  const rowBase = isExcelFile
    ? Math.max(1, config.tableInfo.usedRangeStartRow ?? 1)
    : 1;
  const sourceRegions = config.regions?.length
    ? [...config.regions].sort(
        (left, right) => left.regionIndex - right.regionIndex
      )
    : [toRegionConfig(config)];

  return sourceRegions.map((region, position) => {
    const headerRowStart = region.headerRowStart ?? rowBase;
    const headerRowCount = Math.max(1, region.headerRowCount ?? 1);
    const dataStartRow = region.dataStartRow ?? headerRowStart + headerRowCount;
    const headerRowIndex = Math.max(0, headerRowStart - rowBase);
    const dataStartRowIndex = Math.max(0, dataStartRow - rowBase);
    const dataEndRowIndex =
      region.dataEndRow === undefined
        ? undefined
        : Math.max(dataStartRowIndex, region.dataEndRow - rowBase);
    const regionRowCount =
      dataEndRowIndex === undefined
        ? Math.max(1, config.tableInfo.rowCount - dataStartRowIndex)
        : dataEndRowIndex - dataStartRowIndex + 1;

    return {
      key: `${config.tableIndex}:${position}:${region.regionId?.trim() || "region"}`,
      label: `区域 ${position + 1}`,
      regionIndex: region.regionIndex,
      headerRowIndex,
      headerRowCount,
      dataStartRowIndex,
      dataEndRowIndex,
      previewRows: Math.min(MAX_PREVIEW_ROWS, regionRowCount),
      sourceRowNumberStart: dataStartRow,
      mapping: {
        projectColumn: region.projectColumnIndex,
        specificationColumn: region.specificationColumnIndex,
        acceptanceColumn: region.acceptanceColumnIndex,
        remarkColumn: region.remarkColumnIndex,
        headerRowIndex,
        dataStartRowIndex
      }
    };
  });
};
