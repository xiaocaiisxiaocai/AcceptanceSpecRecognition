import type { TableInfo } from "@/api/document";
import type { SmartConfigRecognizedTable } from "@/api/smart-config";
import type { BatchTableConfigItem } from "./components/batchTableConfig.types";

export type SmartFillSmartStep = {
  title: string;
  description: string;
};

export const createSmartFillSmartSteps = (): SmartFillSmartStep[] => [
  { title: "上传/归属", description: "上传目标文档并选择业务归属" },
  { title: "匹配配置", description: "确认匹配参数" },
  { title: "预览确认", description: "确认匹配结果" }
];

const getTableInfo = (
  tableInfos: TableInfo[],
  table: SmartConfigRecognizedTable
) => tableInfos.find(item => item.index === table.tableIndex);

const toActualRowNumber = (tableInfo: TableInfo | undefined, rowIndex: number) =>
  Math.max(1, (tableInfo?.usedRangeStartRow ?? 1) + rowIndex);

const toActualColumnNumber = (
  tableInfo: TableInfo | undefined,
  columnIndex?: number
) =>
  columnIndex === undefined
    ? undefined
    : Math.max(1, (tableInfo?.usedRangeStartColumn ?? 1) + columnIndex);

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
        table.specificationColumnIndex !== undefined
    )
    .sort((a, b) => a.tableIndex - b.tableIndex)
    .map(table => {
      const tableInfo = getTableInfo(tableInfos, table);
      const projectColumnIndex = isExcelFile
        ? (toActualColumnNumber(tableInfo, table.projectColumnIndex) ??
          Math.max(1, tableInfo?.usedRangeStartColumn ?? 1))
        : getFallbackProjectColumn(table);

      return {
        tableIndex: table.tableIndex,
        projectColumnIndex,
        specificationColumnIndex: isExcelFile
          ? toActualColumnNumber(tableInfo, table.specificationColumnIndex)!
          : table.specificationColumnIndex!,
        acceptanceColumnIndex: isExcelFile
          ? (toActualColumnNumber(tableInfo, table.acceptanceColumnIndex) ??
            projectColumnIndex)
          : (table.acceptanceColumnIndex ?? table.specificationColumnIndex!),
        remarkColumnIndex: isExcelFile
          ? toActualColumnNumber(tableInfo, table.remarkColumnIndex)
          : table.remarkColumnIndex,
        headerRowStart: isExcelFile
          ? toActualRowNumber(tableInfo, table.headerRowIndex)
          : table.headerRowIndex + 1,
        headerRowCount: Math.max(1, table.headerRowCount),
        dataStartRow: isExcelFile
          ? toActualRowNumber(tableInfo, table.dataStartRowIndex)
          : table.dataStartRowIndex + 1,
        filterEmptySourceRows: undefined,
        selected: true,
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
