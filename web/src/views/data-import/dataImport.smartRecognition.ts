import type { TableInfo } from "@/api/document";
import type { SmartConfigRecognizedTable } from "@/api/smart-config";
import type { TableImportConfig } from "./dataImport.types";
import { normalizeExcelMappingByTable } from "./dataImport.helpers";

export type DataImportSmartStep = {
  title: string;
  description: string;
};

export const SMART_STEP_UPLOAD_TARGET = 0;
export const SMART_STEP_CONFIRM_PREVIEW = 1;
export const SMART_STEP_COMPLETE = 2;

export const ADVANCED_STEP_TABLE_SELECT = 1;
export const ADVANCED_STEP_MAPPING = 2;

export const createDataImportSmartSteps = (): DataImportSmartStep[] => [
  { title: "上传/目标", description: "上传文件并选择业务归属" },
  { title: "确认/预览", description: "确认识别结构并预览待导入数据" },
  { title: "完成", description: "执行导入并查看结果" }
];

export const getDataImportAdvancedStep = (
  target: "tableSelect" | "mapping"
) => (target === "tableSelect" ? ADVANCED_STEP_TABLE_SELECT : ADVANCED_STEP_MAPPING);

const toActualRowNumber = (tableInfo: TableInfo | undefined, rowIndex: number) =>
  Math.max(1, (tableInfo?.usedRangeStartRow ?? 1) + rowIndex);

const toActualColumnNumber = (
  tableInfo: TableInfo | undefined,
  columnIndex?: number
) =>
  columnIndex === undefined
    ? undefined
    : Math.max(1, (tableInfo?.usedRangeStartColumn ?? 1) + columnIndex);

const getTableInfo = (
  tableInfos: TableInfo[],
  table: SmartConfigRecognizedTable
) => tableInfos.find(item => item.index === table.tableIndex);

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
    .filter(
      table =>
        table.decision !== "Reject" &&
        table.specificationColumnIndex !== undefined
    )
    .sort((a, b) => a.tableIndex - b.tableIndex)
    .map(table => {
      const tableInfo = getTableInfo(tableInfos, table);
      const base: TableImportConfig = {
        tableIndex: table.tableIndex,
        tableInfo,
        previewData: null
      };

      if (!isExcelFile) {
        return {
          ...base,
          wordMapping: {
            projectColumn: table.projectColumnIndex,
            specificationColumn: table.specificationColumnIndex,
            acceptanceColumn: table.acceptanceColumnIndex,
            remarkColumn: table.remarkColumnIndex,
            headerRowIndex: table.headerRowIndex,
            dataStartRowIndex: table.dataStartRowIndex
          }
        };
      }

      return {
        ...base,
        excelMapping: normalizeExcelMappingByTable(tableInfo, {
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
            table.dataEndRowIndex === undefined
              ? Math.max(
                  toActualRowNumber(tableInfo, table.dataStartRowIndex),
                  (tableInfo?.usedRangeStartRow ?? 1) +
                    Math.max(0, (tableInfo?.rowCount ?? 1) - 1)
                )
              : toActualRowNumber(tableInfo, table.dataEndRowIndex)
        })
      };
    });
};
