import type { TableInfo } from "@/api/document";
import type { SmartConfigRecognizedTable } from "@/api/smart-config";
import type { TableImportConfig } from "./dataImport.types";
import { normalizeExcelMappingByTable } from "./dataImport.helpers";
import {
  getSmartStructureImportReadinessReason,
  getRecognizedTableInfo,
  toActualColumnNumber,
  toActualRowNumber
} from "@/views/shared/smart-structure-recognition";

export type DataImportSmartStep = {
  title: string;
  description: string;
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
  { title: "上传/目标", description: "上传文件并选择业务归属" },
  { title: "确认/预览", description: "确认识别结构并预览待导入数据" },
  { title: "完成", description: "执行导入并查看结果" }
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
  table.decision === "AutoApply" &&
  table.recommendation === "Recommended" &&
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
        return {
          ...base,
          wordMapping: {
            projectColumn: table.projectColumnIndex ?? undefined,
            specificationColumn: table.specificationColumnIndex ?? undefined,
            acceptanceColumn: table.acceptanceColumnIndex ?? undefined,
            remarkColumn: table.remarkColumnIndex ?? undefined,
            headerRowIndex: table.headerRowIndex,
            dataStartRowIndex: table.dataStartRowIndex
          }
        };
      }

      const excelMapping = normalizeExcelMappingByTable(tableInfo, {
        projectColumn: toActualColumnNumber(
          tableInfo,
          table.projectColumnIndex
        ),
        specificationColumn: toActualColumnNumber(
          tableInfo,
          table.specificationColumnIndex
        ),
        acceptanceColumn: toActualColumnNumber(
          tableInfo,
          table.acceptanceColumnIndex
        ),
        remarkColumn: toActualColumnNumber(tableInfo, table.remarkColumnIndex),
        headerRowStart: toActualRowNumber(tableInfo, table.headerRowIndex),
        headerRowCount: Math.max(1, table.headerRowCount),
        dataStartRow: toActualRowNumber(tableInfo, table.dataStartRowIndex),
        dataEndRow:
          table.dataEndRowIndex == null
            ? Math.max(
                toActualRowNumber(tableInfo, table.dataStartRowIndex),
                (tableInfo?.usedRangeStartRow ?? 1) +
                  Math.max(0, (tableInfo?.rowCount ?? 1) - 1)
              )
            : toActualRowNumber(tableInfo, table.dataEndRowIndex)
      });

      return {
        ...base,
        excelMapping,
        recognizedExcelMapping: { ...excelMapping }
      };
    });
};
