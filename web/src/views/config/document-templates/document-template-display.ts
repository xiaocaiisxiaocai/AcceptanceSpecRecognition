import type { DocumentTemplateRegion } from "@/api/document-templates";
import { toExcelColumnLabel } from "@/views/shared/smart-structure-recognition";

const explicitTimezonePattern = /(?:Z|[+-]\d{2}:\d{2})$/i;

export const normalizeApiUtcDateTime = (value: string) =>
  explicitTimezonePattern.test(value) ? value : `${value}Z`;

export const formatTemplateColumnRange = (
  columnIndex: number | null | undefined,
  startRowIndex: number,
  endRowIndex: number | null | undefined
) => {
  if (columnIndex == null || columnIndex < 0) return "-";
  const column = toExcelColumnLabel(columnIndex + 1);
  const startRow = Math.max(0, startRowIndex) + 1;
  return endRowIndex == null
    ? `${column}${startRow}:${column}末行`
    : `${column}${startRow}:${column}${Math.max(startRowIndex, endRowIndex) + 1}`;
};

export const formatTemplateHeaderRange = (region: DocumentTemplateRegion) => {
  const startRow = Math.max(0, region.headerRowIndex) + 1;
  const endRow = startRow + Math.max(1, region.headerRowCount) - 1;
  return startRow === endRow
    ? `第 ${startRow} 行`
    : `第 ${startRow}–${endRow} 行`;
};

export const formatTemplateDataRange = (region: DocumentTemplateRegion) => {
  const startRow = Math.max(0, region.dataStartRowIndex) + 1;
  return region.dataEndRowIndex == null
    ? `第 ${startRow} 行至末行`
    : `第 ${startRow}–${Math.max(region.dataStartRowIndex, region.dataEndRowIndex) + 1} 行`;
};

export const getTemplateRegionRanges = (region: DocumentTemplateRegion) => [
  {
    key: "project",
    label: "项目",
    value: region.isSpecificationOnly
      ? "仅规格表"
      : formatTemplateColumnRange(
          region.projectColumnIndex,
          region.dataStartRowIndex,
          region.dataEndRowIndex
        )
  },
  {
    key: "specification",
    label: "规格",
    value: formatTemplateColumnRange(
      region.specificationColumnIndex,
      region.dataStartRowIndex,
      region.dataEndRowIndex
    )
  },
  {
    key: "acceptance",
    label: "验收",
    value: formatTemplateColumnRange(
      region.acceptanceColumnIndex,
      region.dataStartRowIndex,
      region.dataEndRowIndex
    )
  },
  {
    key: "remark",
    label: "备注",
    value: formatTemplateColumnRange(
      region.remarkColumnIndex,
      region.dataStartRowIndex,
      region.dataEndRowIndex
    )
  }
];
