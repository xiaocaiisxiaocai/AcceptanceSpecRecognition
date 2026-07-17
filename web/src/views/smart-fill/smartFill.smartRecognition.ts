import type { TableInfo } from "@/api/document";
import type { SmartConfigRecognizedTable } from "@/api/smart-config";
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
  const selectedTableIndexSet = new Set(selectedTableIndexes);
  return (
    selectedTableIndexSet.size > 0 &&
    !tables.some(
      table =>
        selectedTableIndexSet.has(table.tableIndex) &&
        table.decision === "NeedConfirm"
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

const getFallbackProjectColumn = (table: SmartConfigRecognizedTable) =>
  table.projectColumnIndex ?? 0;

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
    .filter(
      table =>
        table.decision !== "Reject" &&
        table.recommendation !== "Skip" &&
        table.specificationColumnIndex != null
    )
    .sort((a, b) => a.tableIndex - b.tableIndex)
    .map(table => {
      const tableInfo = getRecognizedTableInfo(tableInfos, table);
      // MatchingPreview 的列坐标与表格解析器一致，始终使用已用区域内的
      // 0-based 相对索引；只有 Excel 行号需要转换成工作表绝对行号。
      // 数据导入接口使用的是 Excel 绝对列号，两者不能共用列号转换。
      const projectColumnIndex = getFallbackProjectColumn(table);

      return {
        tableIndex: table.tableIndex,
        projectColumnIndex,
        specificationColumnIndex: table.specificationColumnIndex!,
        acceptanceColumnIndex:
          table.acceptanceColumnIndex ?? table.specificationColumnIndex!,
        remarkColumnIndex: table.remarkColumnIndex ?? undefined,
        headerRowStart: isExcelFile
          ? toActualRowNumber(tableInfo, table.headerRowIndex)
          : table.headerRowIndex + 1,
        headerRowCount: Math.max(1, table.headerRowCount),
        dataStartRow: isExcelFile
          ? toActualRowNumber(tableInfo, table.dataStartRowIndex)
          : table.dataStartRowIndex + 1,
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
