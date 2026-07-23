import type { TableInfo } from "@/api/document";
import type {
  SmartConfigRecognizedRegion,
  SmartConfigRecognizedTable
} from "@/api/smart-config";
import type { TableImportConfig } from "./dataImport.types";
import {
  createDefaultExcelMapping,
  defaultWordMapping,
  normalizeExcelMappingByTable
} from "./dataImport.helpers";
import {
  getSmartStructureImportReadinessReason,
  getRecognizedTableInfo,
  toActualColumnNumber,
  toActualRowNumber
} from "@/views/shared/smart-structure-recognition";

export type DataImportSmartStep = {
  title: string;
};

export const SMART_STEP_UPLOAD_TARGET = 0;
export const SMART_STEP_CONFIRM_PREVIEW = 1;
export const SMART_STEP_COMPLETE = 2;

export const ADVANCED_STEP_TABLE_SELECT = 1;
export const ADVANCED_STEP_MAPPING = 2;

export type DataImportStepState = {
  advancedMode: boolean;
  currentStep: number;
};

export const createDataImportSmartSteps = (): DataImportSmartStep[] => [
  { title: "上传/目标" },
  { title: "确认/预览" },
  { title: "完成" }
];

export const getDataImportAdvancedStep = (target: "tableSelect" | "mapping") =>
  target === "tableSelect" ? ADVANCED_STEP_TABLE_SELECT : ADVANCED_STEP_MAPPING;

export const buildDataImportPreviewStageText = (
  current: number,
  total: number,
  tableName?: string | null
) => {
  const normalizedCurrent = Math.max(1, current);
  const normalizedTotal = Math.max(normalizedCurrent, total);
  const name = tableName?.trim();
  return name
    ? `正在生成导入预览：第 ${normalizedCurrent}/${normalizedTotal} 张（${name}）`
    : `正在生成导入预览：第 ${normalizedCurrent}/${normalizedTotal} 张`;
};

export const getDataImportPrevStepState = ({
  advancedMode,
  currentStep
}: DataImportStepState): DataImportStepState => {
  if (!advancedMode) {
    return {
      advancedMode,
      currentStep:
        currentStep > SMART_STEP_UPLOAD_TARGET ? currentStep - 1 : currentStep
    };
  }

  return {
    // 高级模式没有第 0 步页面；从表格选择页返回时切回智能上传页。
    advancedMode: currentStep > ADVANCED_STEP_TABLE_SELECT,
    currentStep:
      currentStep > ADVANCED_STEP_TABLE_SELECT
        ? currentStep - 1
        : SMART_STEP_UPLOAD_TARGET
  };
};

export const getDataImportPreviewLoadState = (configs: TableImportConfig[]) => {
  return configs.reduce(
    (state, cfg) => {
      const previewData = cfg.previewData;
      if (!previewData) {
        return {
          loadedRows: state.loadedRows,
          totalRows: state.totalRows,
          hasPartialPreview: true
        };
      }

      const loadedRows = state.loadedRows + previewData.rows.length;
      const totalRows = state.totalRows + previewData.totalRows;
      return {
        loadedRows,
        totalRows,
        hasPartialPreview:
          state.hasPartialPreview ||
          previewData.rows.length < previewData.totalRows
      };
    },
    {
      loadedRows: 0,
      totalRows: 0,
      hasPartialPreview: false
    }
  );
};

export const getDataImportPreviewTotalCount = (
  configs: TableImportConfig[],
  excludedRowIndexMap: Record<number, number[]>
) => {
  return configs.reduce((sum, cfg) => {
    const totalRows = cfg.previewData?.totalRows ?? 0;
    const excludedCount = excludedRowIndexMap[cfg.tableIndex]?.length ?? 0;
    return sum + Math.max(0, totalRows - excludedCount);
  }, 0);
};

export const canSmartTableBeImported = (table: SmartConfigRecognizedTable) =>
  getSmartStructureImportReadinessReason(table) === "";

export const shouldSelectSmartTableByDefault = (
  table: SmartConfigRecognizedTable
) =>
  canSmartTableBeImported(table) &&
  table.decision !== "Reject" &&
  (table.recommendation === "Recommended" ||
    table.recommendation === "NeedConfirm") &&
  table.confidence > 0;

export const createDefaultSelectedSmartTableIndexes = (
  tables: SmartConfigRecognizedTable[]
) =>
  tables
    .filter(shouldSelectSmartTableByDefault)
    .map(table => table.tableIndex)
    .sort((a, b) => a - b);

export const filterSelectedSmartTables = (
  tables: SmartConfigRecognizedTable[],
  selectedTableIndexes: number[]
) => {
  const selectedSet = new Set(selectedTableIndexes);
  return tables
    .filter(table => selectedSet.has(table.tableIndex))
    .sort((a, b) => a.tableIndex - b.tableIndex);
};

export const buildDataImportConfigsFromRecognizedTables = ({
  isExcelFile,
  tables,
  tableInfos
}: {
  isExcelFile: boolean;
  tables: SmartConfigRecognizedTable[];
  tableInfos: TableInfo[];
}): TableImportConfig[] => {
  return tables
    .filter(canSmartTableBeImported)
    .sort((a, b) => a.tableIndex - b.tableIndex)
    .map(table => {
      const tableInfo = getRecognizedTableInfo(tableInfos, table);
      const base: TableImportConfig = {
        tableIndex: table.tableIndex,
        tableInfo,
        isSpecificationOnly: table.isSpecificationOnly,
        previewData: null
      };

      if (!isExcelFile) {
        const sourceRegions = table.regions?.length ? table.regions : [table];
        const wordMappings = sourceRegions.map((region, regionIndex) => ({
          regionId:
            "regionId" in region
              ? region.regionId
              : `table-${table.tableIndex}-region-${regionIndex}`,
          regionIndex:
            "regionIndex" in region ? region.regionIndex : regionIndex,
          projectColumn: region.projectColumnIndex ?? undefined,
          specificationColumn: region.specificationColumnIndex ?? undefined,
          acceptanceColumn: region.acceptanceColumnIndex ?? undefined,
          remarkColumn: region.remarkColumnIndex ?? undefined,
          headerRowIndex: region.headerRowIndex,
          headerRowCount: Math.max(1, region.headerRowCount),
          dataStartRowIndex: region.dataStartRowIndex,
          dataEndRowIndex: region.dataEndRowIndex ?? undefined,
          isSpecificationOnly: region.isSpecificationOnly
        }));
        return {
          ...base,
          wordMapping: { ...wordMappings[0] },
          recognizedWordMappings: wordMappings
        };
      }

      const sourceRegions = table.regions?.length ? table.regions : [table];
      const excelMappings = sourceRegions.map((region, regionIndex) => ({
        ...normalizeExcelMappingByTable(tableInfo, {
          projectColumn: toActualColumnNumber(
            tableInfo,
            region.projectColumnIndex
          ),
          specificationColumn: toActualColumnNumber(
            tableInfo,
            region.specificationColumnIndex
          ),
          acceptanceColumn: toActualColumnNumber(
            tableInfo,
            region.acceptanceColumnIndex
          ),
          remarkColumn: toActualColumnNumber(
            tableInfo,
            region.remarkColumnIndex
          ),
          headerRowStart: toActualRowNumber(tableInfo, region.headerRowIndex),
          headerRowCount: Math.max(1, region.headerRowCount),
          dataStartRow: toActualRowNumber(tableInfo, region.dataStartRowIndex),
          dataEndRow:
            region.dataEndRowIndex == null
              ? Math.max(
                  toActualRowNumber(tableInfo, region.dataStartRowIndex),
                  (tableInfo?.usedRangeStartRow ?? 1) +
                    Math.max(0, (tableInfo?.rowCount ?? 1) - 1)
                )
              : toActualRowNumber(tableInfo, region.dataEndRowIndex)
        }),
        regionId:
          "regionId" in region
            ? region.regionId
            : `table-${table.tableIndex}-region-${regionIndex}`,
        regionIndex: "regionIndex" in region ? region.regionIndex : regionIndex,
        isSpecificationOnly: region.isSpecificationOnly
      }));
      const excelMapping = excelMappings[0];

      return {
        ...base,
        excelMapping,
        recognizedExcelMapping: { ...excelMapping },
        recognizedExcelMappings: excelMappings.map(mapping => ({ ...mapping }))
      };
    });
};

export const buildManualDataImportConfig = ({
  isExcelFile,
  tableInfo
}: {
  isExcelFile: boolean;
  tableInfo: TableInfo;
}): TableImportConfig => ({
  tableIndex: tableInfo.index,
  tableInfo,
  previewData: null,
  ...(isExcelFile
    ? { excelMapping: createDefaultExcelMapping(tableInfo) }
    : { wordMapping: defaultWordMapping() })
});

const toRelativeRowIndex = (tableInfo: TableInfo | undefined, row: number) =>
  Math.max(0, row - (tableInfo?.usedRangeStartRow ?? 1));

const toRelativeColumnIndex = (
  tableInfo: TableInfo | undefined,
  column?: number
) =>
  column == null
    ? undefined
    : Math.max(0, column - (tableInfo?.usedRangeStartColumn ?? 1));

const getRegionId = (
  region: SmartConfigRecognizedTable | SmartConfigRecognizedRegion
): string | undefined => {
  const value = "regionId" in region ? region.regionId : undefined;
  return typeof value === "string" ? value : undefined;
};

/** 将高级执行配置投影回智能识别结构，保持摘要、确认学习和执行单一真相。 */
export const syncDataImportConfigsToRecognizedTables = ({
  isExcelFile,
  tables,
  configs
}: {
  isExcelFile: boolean;
  tables: SmartConfigRecognizedTable[];
  configs: TableImportConfig[];
}): SmartConfigRecognizedTable[] => {
  const configByTableIndex = new Map(
    configs.map(config => [config.tableIndex, config])
  );

  return tables.map(table => {
    const config = configByTableIndex.get(table.tableIndex);
    if (!config) return table;
    const previousRegions = table.regions?.length ? table.regions : [table];

    if (isExcelFile) {
      const mappings = config.recognizedExcelMappings?.length
        ? config.recognizedExcelMappings
        : config.excelMapping
          ? [config.excelMapping]
          : [];
      if (mappings.length === 0) return table;

      const regions = mappings.map(
        (mapping, index): SmartConfigRecognizedRegion => {
          const candidateRegionId =
            "regionId" in mapping ? mapping.regionId : undefined;
          const mappingRegionId =
            typeof candidateRegionId === "string"
              ? candidateRegionId
              : undefined;
          const previous =
            previousRegions.find(
              region => getRegionId(region) === mappingRegionId
            ) ??
            previousRegions[index] ??
            previousRegions[0];
          const headers =
            index === 0 && config.previewData?.headers?.length
              ? config.previewData.headers
              : previous.headers;
          return {
            ...previous,
            regionId:
              mappingRegionId ||
              getRegionId(previous) ||
              `table-${table.tableIndex}-region-${index}`,
            regionIndex: index,
            headers: [...headers],
            projectColumnIndex: toRelativeColumnIndex(
              config.tableInfo,
              mapping.projectColumn
            ),
            specificationColumnIndex: toRelativeColumnIndex(
              config.tableInfo,
              mapping.specificationColumn
            ),
            acceptanceColumnIndex: toRelativeColumnIndex(
              config.tableInfo,
              mapping.acceptanceColumn
            ),
            remarkColumnIndex: toRelativeColumnIndex(
              config.tableInfo,
              mapping.remarkColumn
            ),
            headerRowIndex: toRelativeRowIndex(
              config.tableInfo,
              mapping.headerRowStart
            ),
            headerRowCount: Math.max(1, mapping.headerRowCount),
            dataStartRowIndex: toRelativeRowIndex(
              config.tableInfo,
              mapping.dataStartRow
            ),
            dataEndRowIndex:
              mapping.dataEndRow == null
                ? undefined
                : toRelativeRowIndex(config.tableInfo, mapping.dataEndRow),
            isSpecificationOnly:
              "isSpecificationOnly" in mapping
                ? Boolean(mapping.isSpecificationOnly)
                : table.isSpecificationOnly
          };
        }
      );
      const primary = regions[0];
      return {
        ...table,
        headers: [...primary.headers],
        projectColumnIndex: primary.projectColumnIndex,
        specificationColumnIndex: primary.specificationColumnIndex,
        acceptanceColumnIndex: primary.acceptanceColumnIndex,
        remarkColumnIndex: primary.remarkColumnIndex,
        headerRowIndex: primary.headerRowIndex,
        headerRowCount: primary.headerRowCount,
        dataStartRowIndex: primary.dataStartRowIndex,
        dataEndRowIndex: primary.dataEndRowIndex,
        isSpecificationOnly: primary.isSpecificationOnly,
        regions
      };
    }

    const mappings = config.recognizedWordMappings?.length
      ? config.recognizedWordMappings
      : config.wordMapping
        ? [
            {
              ...config.wordMapping,
              regionId:
                getRegionId(previousRegions[0]) ||
                `table-${table.tableIndex}-region-0`,
              regionIndex: 0,
              headerRowCount: previousRegions[0].headerRowCount,
              dataEndRowIndex: previousRegions[0].dataEndRowIndex,
              isSpecificationOnly:
                config.isSpecificationOnly ?? table.isSpecificationOnly
            }
          ]
        : [];
    if (mappings.length === 0) return table;
    const regions = mappings.map(
      (mapping, index): SmartConfigRecognizedRegion => {
        const previous =
          previousRegions.find(
            region => getRegionId(region) === mapping.regionId
          ) ??
          previousRegions[index] ??
          previousRegions[0];
        const headers =
          index === 0 && config.previewData?.headers?.length
            ? config.previewData.headers
            : previous.headers;
        return {
          ...previous,
          regionId: mapping.regionId,
          regionIndex: index,
          headers: [...headers],
          projectColumnIndex: mapping.projectColumn,
          specificationColumnIndex: mapping.specificationColumn,
          acceptanceColumnIndex: mapping.acceptanceColumn,
          remarkColumnIndex: mapping.remarkColumn,
          headerRowIndex: mapping.headerRowIndex,
          headerRowCount: Math.max(1, mapping.headerRowCount),
          dataStartRowIndex: mapping.dataStartRowIndex,
          dataEndRowIndex: mapping.dataEndRowIndex,
          isSpecificationOnly: mapping.isSpecificationOnly
        };
      }
    );
    const primary = regions[0];
    return {
      ...table,
      headers: [...primary.headers],
      projectColumnIndex: primary.projectColumnIndex,
      specificationColumnIndex: primary.specificationColumnIndex,
      acceptanceColumnIndex: primary.acceptanceColumnIndex,
      remarkColumnIndex: primary.remarkColumnIndex,
      headerRowIndex: primary.headerRowIndex,
      headerRowCount: primary.headerRowCount,
      dataStartRowIndex: primary.dataStartRowIndex,
      isSpecificationOnly: primary.isSpecificationOnly,
      dataEndRowIndex: primary.dataEndRowIndex,
      regions
    };
  });
};
