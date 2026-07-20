import type { TableInfo } from "@/api/document";
import type {
  SmartConfigRecognizedRegion,
  SmartConfigRecognizedTable
} from "@/api/smart-config";
import type { BatchTableConfigItem } from "./components/batchTableConfig.types";
import {
  getRecognizedTableInfo,
  toActualRowNumber
} from "@/views/shared/smart-structure-recognition";

export type SmartFillSmartStep = {
  title: string;
};

export const SMART_FILL_STEP_UPLOAD_SCOPE = 0;
export const SMART_FILL_ADVANCED_STEP_TABLE_CONFIG = 1;

export type SmartFillStepState = {
  advancedMode: boolean;
  currentStep: number;
};

export const canContinueFromSmartRecognition = (
  tables: SmartConfigRecognizedTable[],
  selectedTableIndexes: number[]
) => {
  const tableByIndex = new Map(tables.map(table => [table.tableIndex, table]));
  const selectedTableIndexSet = new Set(selectedTableIndexes);
  return (
    selectedTableIndexSet.size > 0 &&
    [...selectedTableIndexSet].every(
      tableIndex => tableByIndex.get(tableIndex)?.decision === "AutoApply"
    )
  );
};

export const createSmartFillSmartSteps = (): SmartFillSmartStep[] => [
  { title: "上传/归属" },
  { title: "匹配配置" },
  { title: "预览确认" }
];

export const getSmartFillPrevStepState = ({
  advancedMode,
  currentStep
}: SmartFillStepState): SmartFillStepState => {
  if (!advancedMode) {
    return {
      advancedMode,
      currentStep:
        currentStep > SMART_FILL_STEP_UPLOAD_SCOPE
          ? currentStep - 1
          : currentStep
    };
  }

  return {
    // 高级模式没有上传步骤；从高级第一步返回时切回智能上传流程。
    advancedMode: currentStep > SMART_FILL_ADVANCED_STEP_TABLE_CONFIG,
    currentStep:
      currentStep > SMART_FILL_ADVANCED_STEP_TABLE_CONFIG
        ? currentStep - 1
        : SMART_FILL_STEP_UPLOAD_SCOPE
  };
};

export const shouldSelectSmartFillTableByDefault = (
  table: SmartConfigRecognizedTable
) =>
  table.decision === "AutoApply" &&
  table.recommendation === "Recommended" &&
  table.confidence > 0;

const getFallbackProjectColumn = (
  columnIndex: number | null | undefined,
  specificationColumnIndex: number,
  isSpecificationOnly: boolean
) => (isSpecificationOnly ? specificationColumnIndex : (columnIndex ?? 0));

export const buildSmartFillConfigsFromRecognizedTables = ({
  isExcelFile,
  tables,
  tableInfos
}: {
  isExcelFile: boolean;
  tables: SmartConfigRecognizedTable[];
  tableInfos: TableInfo[];
}): BatchTableConfigItem[] => {
  return tables
    .filter(table => {
      if (table.decision === "Reject" || table.recommendation === "Skip") {
        return false;
      }
      const regions = table.regions?.length ? table.regions : [table];
      return regions.every(
        region =>
          region.specificationColumnIndex != null &&
          region.acceptanceColumnIndex != null
      );
    })
    .sort((a, b) => a.tableIndex - b.tableIndex)
    .map(table => {
      const tableInfo = getRecognizedTableInfo(tableInfos, table);
      const sourceRegions = table.regions?.length
        ? table.regions
        : [
            {
              regionId: `table-${table.tableIndex}-region-0`,
              regionIndex: 0,
              projectColumnIndex: table.projectColumnIndex,
              specificationColumnIndex: table.specificationColumnIndex,
              acceptanceColumnIndex: table.acceptanceColumnIndex,
              remarkColumnIndex: table.remarkColumnIndex,
              headerRowIndex: table.headerRowIndex,
              headerRowCount: table.headerRowCount,
              dataStartRowIndex: table.dataStartRowIndex,
              dataEndRowIndex: table.dataEndRowIndex
            }
          ];
      const regions = sourceRegions.map(region => ({
        regionId: region.regionId,
        regionIndex: region.regionIndex,
        projectColumnIndex: getFallbackProjectColumn(
          region.projectColumnIndex,
          region.specificationColumnIndex!,
          "isSpecificationOnly" in region
            ? region.isSpecificationOnly
            : table.isSpecificationOnly
        ),
        specificationColumnIndex: region.specificationColumnIndex!,
        acceptanceColumnIndex: region.acceptanceColumnIndex!,
        remarkColumnIndex: region.remarkColumnIndex ?? undefined,
        headerRowStart: isExcelFile
          ? toActualRowNumber(tableInfo, region.headerRowIndex)
          : region.headerRowIndex + 1,
        headerRowCount: Math.max(1, region.headerRowCount),
        dataStartRow: isExcelFile
          ? toActualRowNumber(tableInfo, region.dataStartRowIndex)
          : region.dataStartRowIndex + 1,
        dataEndRow:
          region.dataEndRowIndex == null
            ? undefined
            : isExcelFile
              ? toActualRowNumber(tableInfo, region.dataEndRowIndex)
              : region.dataEndRowIndex + 1
      }));
      const primary = regions[0];

      return {
        tableIndex: table.tableIndex,
        projectColumnIndex: primary.projectColumnIndex,
        specificationColumnIndex: primary.specificationColumnIndex,
        acceptanceColumnIndex: primary.acceptanceColumnIndex,
        remarkColumnIndex: primary.remarkColumnIndex,
        headerRowStart: primary.headerRowStart,
        headerRowCount: primary.headerRowCount,
        dataStartRow: primary.dataStartRow,
        dataEndRow: primary.dataEndRow,
        regions,
        filterEmptySourceRows: undefined,
        selected: shouldSelectSmartFillTableByDefault(table),
        tableInfo: tableInfo ?? {
          index: table.tableIndex,
          name: table.tableName ?? undefined,
          rowCount: 0,
          columnCount: table.headers.length,
          isNested: false,
          headers: table.headers,
          hasMergedCells: false
        },
        mappingAutoDetected: true
      };
    });
};

/** 将高级配置回写到识别结构，确保摘要、确认学习与实际预览共用一份区域真相。 */
export const syncSmartFillConfigsToRecognizedTables = ({
  isExcelFile,
  tables,
  configs
}: {
  isExcelFile: boolean;
  tables: SmartConfigRecognizedTable[];
  configs: BatchTableConfigItem[];
}): SmartConfigRecognizedTable[] => {
  const configByTable = new Map(
    configs.map(config => [config.tableIndex, config])
  );
  return tables.map(table => {
    const config = configByTable.get(table.tableIndex);
    if (!config) return table;
    const previousRegions = table.regions?.length ? table.regions : [table];
    const configuredRegions = config.regions?.length
      ? config.regions
      : [
          {
            regionId:
              previousRegions[0] && "regionId" in previousRegions[0]
                ? previousRegions[0].regionId
                : `table-${table.tableIndex}-region-0`,
            regionIndex: 0,
            projectColumnIndex: config.projectColumnIndex,
            specificationColumnIndex: config.specificationColumnIndex,
            acceptanceColumnIndex: config.acceptanceColumnIndex,
            remarkColumnIndex: config.remarkColumnIndex,
            headerRowStart: config.headerRowStart,
            headerRowCount: config.headerRowCount,
            dataStartRow: config.dataStartRow,
            dataEndRow: config.dataEndRow
          }
        ];
    const usedStartRow = config.tableInfo.usedRangeStartRow ?? 1;
    const toRelativeRow = (row: number | undefined, fallback: number) =>
      row == null
        ? fallback
        : Math.max(0, row - (isExcelFile ? usedStartRow : 1));
    const regions = configuredRegions.map(
      (region, index): SmartConfigRecognizedRegion => {
        const previous =
          previousRegions.find(
            item => "regionId" in item && item.regionId === region.regionId
          ) ??
          previousRegions[index] ??
          previousRegions[0];
        return {
          ...previous,
          regionId:
            region.regionId ??
            ("regionId" in previous ? previous.regionId : undefined) ??
            `table-${table.tableIndex}-region-${index}`,
          regionIndex: index,
          projectColumnIndex: region.projectColumnIndex,
          specificationColumnIndex: region.specificationColumnIndex,
          acceptanceColumnIndex: region.acceptanceColumnIndex,
          remarkColumnIndex: region.remarkColumnIndex,
          headerRowIndex: toRelativeRow(
            region.headerRowStart,
            previous.headerRowIndex
          ),
          headerRowCount: Math.max(
            1,
            region.headerRowCount ?? previous.headerRowCount
          ),
          dataStartRowIndex: toRelativeRow(
            region.dataStartRow,
            previous.dataStartRowIndex
          ),
          dataEndRowIndex:
            region.dataEndRow == null
              ? previous.dataEndRowIndex
              : toRelativeRow(
                  region.dataEndRow,
                  previous.dataEndRowIndex ?? previous.dataStartRowIndex
                )
        };
      }
    );
    const primary = regions[0];
    return {
      ...table,
      projectColumnIndex: primary.projectColumnIndex,
      specificationColumnIndex: primary.specificationColumnIndex,
      acceptanceColumnIndex: primary.acceptanceColumnIndex,
      remarkColumnIndex: primary.remarkColumnIndex,
      headerRowIndex: primary.headerRowIndex,
      headerRowCount: primary.headerRowCount,
      dataStartRowIndex: primary.dataStartRowIndex,
      dataEndRowIndex: primary.dataEndRowIndex,
      regions
    };
  });
};
