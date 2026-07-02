import type { ComputedRef, Ref } from "vue";
import { ElMessage } from "element-plus";
import {
  getFileTables,
  type FileUploadResponse,
  type TableInfo
} from "@/api/document";
import {
  getEffectiveColumnMappingRules,
  type ColumnMappingRule
} from "@/api/column-mapping-rules";
import { matchWordTableColumnsByRules } from "@/views/shared/word-column-mapping-rules";
import type { BatchTableConfigItem } from "../components/batchTableConfig.types";

type UseSmartFillUploadedTablesOptions = {
  uploadedFile: Ref<FileUploadResponse | null>;
  isExcelFile: ComputedRef<boolean>;
  allTables: Ref<TableInfo[]>;
  batchTableConfigs: Ref<BatchTableConfigItem[]>;
  wordColumnMappingRules: Ref<ColumnMappingRule[]>;
  loadingUploadedFileTables: Ref<boolean>;
  selectedCustomerId?: Ref<number | undefined>;
};

export function useSmartFillUploadedTables({
  uploadedFile,
  isExcelFile,
  allTables,
  batchTableConfigs,
  wordColumnMappingRules,
  loadingUploadedFileTables,
  selectedCustomerId
}: UseSmartFillUploadedTablesOptions) {
  const applyRuleColumnsToTableConfig = (
    table: TableInfo,
    config: BatchTableConfigItem
  ): BatchTableConfigItem => {
    if (isExcelFile.value) {
      return config;
    }

    const totalColumns = Math.max(table.columnCount, table.headers.length, 1);
    const clampColumnIndex = (preferredIndex: number) =>
      Math.min(preferredIndex, totalColumns - 1);
    const matchedWordColumns = matchWordTableColumnsByRules(
      table.headers,
      wordColumnMappingRules.value,
      {
        fallbackToSequential: true
      }
    );

    return {
      ...config,
      projectColumnIndex: clampColumnIndex(
        matchedWordColumns.projectColumnIndex ?? 0
      ),
      specificationColumnIndex: clampColumnIndex(
        matchedWordColumns.specificationColumnIndex ?? 1
      ),
      acceptanceColumnIndex: clampColumnIndex(
        matchedWordColumns.acceptanceColumnIndex ?? 2
      ),
      remarkColumnIndex:
        matchedWordColumns.remarkColumnIndex !== undefined
          ? clampColumnIndex(matchedWordColumns.remarkColumnIndex)
          : totalColumns > 3
            ? 3
            : undefined,
      mappingAutoDetected: true
    };
  };

  const buildDefaultTableConfig = (
    table: TableInfo,
    selected: boolean
  ): BatchTableConfigItem => {
    const usedStartRow = Math.max(1, table.usedRangeStartRow ?? 1);
    const totalColumns = Math.max(table.columnCount, table.headers.length, 1);
    const clampColumnIndex = (preferredIndex: number) =>
      Math.min(preferredIndex, totalColumns - 1);
    const baseConfig: BatchTableConfigItem = {
      tableIndex: table.index,
      projectColumnIndex: clampColumnIndex(0),
      specificationColumnIndex: clampColumnIndex(1),
      acceptanceColumnIndex: clampColumnIndex(2),
      remarkColumnIndex: totalColumns > 3 ? 3 : undefined,
      headerRowStart: usedStartRow,
      headerRowCount: 1,
      dataStartRow: usedStartRow + 1,
      filterEmptySourceRows: undefined,
      selected,
      tableInfo: table
    };

    return applyRuleColumnsToTableConfig(table, baseConfig);
  };

  const refreshWordColumnMappingRules = async (customerId?: number) => {
    if (isExcelFile.value) {
      wordColumnMappingRules.value = [];
      return true;
    }

    const rulesRes = await getEffectiveColumnMappingRules(customerId);
    if (rulesRes.code === 0) {
      wordColumnMappingRules.value = rulesRes.data || [];
      return true;
    }

    wordColumnMappingRules.value = [];
    ElMessage.warning(
      rulesRes.message || "加载列映射规则失败，已按默认列位初始化"
    );
    return false;
  };

  const reloadWordColumnMappingRulesForCustomer = async () => {
    if (isExcelFile.value || !uploadedFile.value) {
      return;
    }

    try {
      const loaded = await refreshWordColumnMappingRules(
        selectedCustomerId?.value
      );
      if (!loaded) {
        return;
      }

      batchTableConfigs.value = batchTableConfigs.value.map(config =>
        config.mappingAutoDetected === false
          ? config
          : applyRuleColumnsToTableConfig(config.tableInfo, config)
      );
    } catch {
      ElMessage.warning("加载列映射规则失败，已保留当前列配置");
    }
  };

  const loadUploadedFileTables = async (file: FileUploadResponse) => {
    loadingUploadedFileTables.value = true;

    let tables: TableInfo[] = [];
    let tableMetaLoaded = false;
    try {
      const tablesRes = await getFileTables(file.fileId);
      if (tablesRes.code === 0) {
        tables = tablesRes.data;
        tableMetaLoaded = true;
      } else {
        throw new Error(tablesRes.message || "获取表格列表失败");
      }

      if (file.fileType !== 1) {
        await refreshWordColumnMappingRules(selectedCustomerId?.value);
      } else {
        wordColumnMappingRules.value = [];
      }
    } catch {
      ElMessage.warning("获取表格列表失败");
    } finally {
      if (uploadedFile.value?.fileId === file.fileId) {
        uploadedFile.value = {
          ...uploadedFile.value,
          tableCount: tables.length,
          tableCountReady: true
        };
      }
      loadingUploadedFileTables.value = false;
    }

    if (uploadedFile.value?.fileId !== file.fileId) return;
    if (!tableMetaLoaded) return;

    allTables.value = tables;
    batchTableConfigs.value = tables.map(t =>
      buildDefaultTableConfig(t, tables.length === 1)
    );
  };

  return {
    loadUploadedFileTables,
    reloadWordColumnMappingRulesForCustomer
  };
}
