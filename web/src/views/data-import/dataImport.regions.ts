import type { TableData } from "@/api/document";
import type {
  ExcelPreviewRowLocation,
  ExcelRegionMapping,
  ExcelSheetMapping,
  WordRegionMapping
} from "./dataImport.types";

const excelMappingFields: Array<keyof ExcelSheetMapping> = [
  "projectColumn",
  "specificationColumn",
  "acceptanceColumn",
  "remarkColumn",
  "headerRowStart",
  "headerRowCount",
  "dataStartRow",
  "dataEndRow"
];

const isSameExcelMapping = (
  left: ExcelSheetMapping,
  right: ExcelSheetMapping
) => excelMappingFields.every(field => left[field] === right[field]);

/**
 * 用高级表单的新值替换单个识别区域，同时保留同一工作表的其他离散区域。
 * 优先使用 regionId，其次匹配表单编辑前的范围；都无法匹配时才回退到主区域。
 */
export const replaceExcelRegionMapping = ({
  regions,
  mapping,
  previousMapping,
  targetRegionId
}: {
  regions: readonly ExcelRegionMapping[];
  mapping: ExcelSheetMapping;
  previousMapping?: ExcelSheetMapping;
  targetRegionId?: string;
}): ExcelRegionMapping[] => {
  if (regions.length === 0) return [];

  let targetIndex = targetRegionId
    ? regions.findIndex(region => region.regionId === targetRegionId)
    : -1;
  if (targetIndex < 0 && previousMapping) {
    targetIndex = regions.findIndex(region =>
      isSameExcelMapping(region, previousMapping)
    );
  }
  if (targetIndex < 0) targetIndex = 0;

  return regions.map((region, index) =>
    index === targetIndex
      ? {
          ...region,
          ...mapping,
          regionId: region.regionId,
          regionIndex: region.regionIndex,
          isSpecificationOnly: region.isSpecificationOnly
        }
      : region
  );
};

export const buildImportRegionKey = (tableIndex: number, regionId?: string) =>
  `${tableIndex}:${regionId ?? "default"}`;

export const buildImportDifferenceDecisionKey = (item: {
  tableIndex: number;
  regionId?: string;
  key: string;
}) => `${buildImportRegionKey(item.tableIndex, item.regionId)}:${item.key}`;

export const mergeExcelRegionPreviews = (
  tableIndex: number,
  regionPreviews: Array<{
    mapping: ExcelRegionMapping;
    preview: TableData;
  }>
): { previewData: TableData; rowLocations: ExcelPreviewRowLocation[] } => {
  const firstPreview = regionPreviews[0]?.preview;
  return {
    previewData: {
      tableIndex,
      headers: firstPreview?.headers ?? [],
      rows: regionPreviews.flatMap(item => item.preview.rows),
      totalRows: regionPreviews.reduce(
        (sum, item) => sum + item.preview.totalRows,
        0
      ),
      columnCount: Math.max(
        0,
        ...regionPreviews.map(item => item.preview.columnCount)
      )
    },
    rowLocations: regionPreviews.flatMap(({ mapping, preview }) =>
      preview.rows.map((_, relativeRowIndex) => ({
        regionId: mapping.regionId,
        regionIndex: mapping.regionIndex,
        relativeRowIndex,
        displayRowNumber: mapping.dataStartRow + relativeRowIndex,
        headers: preview.headers,
        mapping
      }))
    )
  };
};

export const mergeWordRegionPreviews = (
  tableIndex: number,
  regionPreviews: Array<{
    mapping: WordRegionMapping;
    preview: TableData;
  }>
): { previewData: TableData; rowLocations: ExcelPreviewRowLocation[] } => {
  const firstPreview = regionPreviews[0]?.preview;
  return {
    previewData: {
      tableIndex,
      headers: firstPreview?.headers ?? [],
      rows: regionPreviews.flatMap(item => item.preview.rows),
      totalRows: regionPreviews.reduce(
        (sum, item) => sum + item.preview.totalRows,
        0
      ),
      columnCount: Math.max(
        0,
        ...regionPreviews.map(item => item.preview.columnCount)
      )
    },
    rowLocations: regionPreviews.flatMap(({ mapping, preview }) =>
      preview.rows.map((_, relativeRowIndex) => ({
        regionId: mapping.regionId,
        regionIndex: mapping.regionIndex,
        relativeRowIndex,
        displayRowNumber: mapping.dataStartRowIndex + relativeRowIndex + 1,
        headers: preview.headers,
        mapping
      }))
    )
  };
};

export const buildExcludedRowIdentity = (location: ExcelPreviewRowLocation) =>
  `${location.regionId}:${location.relativeRowIndex}`;

export const captureExcludedRowIdentities = (
  excludedCombinedIndexes: readonly number[],
  rowLocations: readonly ExcelPreviewRowLocation[]
) => {
  const excluded = new Set(excludedCombinedIndexes);
  return rowLocations
    .filter((_, combinedIndex) => excluded.has(combinedIndex))
    .map(buildExcludedRowIdentity);
};

export const resolveExcludedCombinedIndexes = (
  identities: readonly string[],
  rowLocations: readonly ExcelPreviewRowLocation[]
) => {
  const identitySet = new Set(identities);
  return rowLocations
    .map((location, combinedIndex) => ({ location, combinedIndex }))
    .filter(item => identitySet.has(buildExcludedRowIdentity(item.location)))
    .map(item => item.combinedIndex);
};

export const getExcludedRowIndexesForRegion = (
  excludedCombinedIndexes: readonly number[],
  rowLocations: readonly ExcelPreviewRowLocation[],
  regionIndex: number,
  regionId?: string
) => {
  const excluded = new Set(excludedCombinedIndexes);
  return rowLocations
    .map((location, combinedIndex) => ({ location, combinedIndex }))
    .filter(
      item =>
        (regionId
          ? item.location.regionId === regionId
          : item.location.regionIndex === regionIndex) &&
        excluded.has(item.combinedIndex)
    )
    .map(item => item.location.relativeRowIndex);
};
