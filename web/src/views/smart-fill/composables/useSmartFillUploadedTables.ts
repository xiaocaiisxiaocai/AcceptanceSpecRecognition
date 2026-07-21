import type { ComputedRef, Ref } from "vue";
import { ElMessage } from "element-plus";
import type { FileUploadResponse, TableInfo } from "@/api/document";
import { loadFileTablesOnce } from "@/views/shared/file-table-metadata";
import { getRequestErrorMessage } from "@/utils/error-message";
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
  let tableLoadVersion = 0;
  let ruleLoadVersion = 0;
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

  const refreshWordColumnMappingRules = async (
    customerId?: number,
    expectedFileId = uploadedFile.value?.fileId
  ) => {
    const requestVersion = ++ruleLoadVersion;
    if (isExcelFile.value) {
      wordColumnMappingRules.value = [];
      return true;
    }

    let rulesRes;
    try {
      rulesRes = await getEffectiveColumnMappingRules(customerId);
    } catch (error) {
      if (
        requestVersion !== ruleLoadVersion ||
        selectedCustomerId?.value !== customerId ||
        uploadedFile.value?.fileId !== expectedFileId
      ) {
        return false;
      }
      throw error;
    }
    if (
      requestVersion !== ruleLoadVersion ||
      selectedCustomerId?.value !== customerId ||
      uploadedFile.value?.fileId !== expectedFileId
    ) {
      return false;
    }
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

  const loadUploadedFileTables = async (
    file: FileUploadResponse,
    options: { force?: boolean } = {}
  ) => {
    const requestVersion = ++tableLoadVersion;
    ruleLoadVersion += 1;
    loadingUploadedFileTables.value = true;
    allTables.value = [];
    batchTableConfigs.value = [];
    if (uploadedFile.value?.fileId === file.fileId) {
      uploadedFile.value = {
        ...uploadedFile.value,
        tableCountReady: false,
        tableMetadataStatus: "loading",
        tableMetadataError: undefined
      };
    }

    let tables: TableInfo[] = [];
    try {
      tables = await loadFileTablesOnce(file.fileId, options);
      if (
        requestVersion !== tableLoadVersion ||
        uploadedFile.value?.fileId !== file.fileId
      ) {
        return false;
      }
    } catch (error) {
      if (
        requestVersion === tableLoadVersion &&
        uploadedFile.value?.fileId === file.fileId
      ) {
        const message = getRequestErrorMessage(error, "获取表格列表失败");
        uploadedFile.value = {
          ...uploadedFile.value,
          tableCountReady: false,
          tableMetadataStatus: "error",
          tableMetadataError: message
        };
        ElMessage.warning(message);
      }
      if (requestVersion === tableLoadVersion) {
        loadingUploadedFileTables.value = false;
      }
      return false;
    }

    if (uploadedFile.value?.fileId !== file.fileId) return false;

    uploadedFile.value = {
      ...uploadedFile.value,
      tableCount: tables.length,
      tableCountReady: true,
      tableMetadataStatus: "ready",
      tableMetadataError: undefined
    };

    try {
      if (file.fileType !== 1) {
        await refreshWordColumnMappingRules(
          selectedCustomerId?.value,
          file.fileId
        );
      } else {
        wordColumnMappingRules.value = [];
      }
    } catch {
      if (
        requestVersion === tableLoadVersion &&
        uploadedFile.value?.fileId === file.fileId
      ) {
        ElMessage.warning("加载列映射规则失败，已按默认列位初始化");
      }
    } finally {
      if (requestVersion === tableLoadVersion) {
        loadingUploadedFileTables.value = false;
      }
    }

    if (uploadedFile.value?.fileId !== file.fileId) return false;

    allTables.value = tables;
    batchTableConfigs.value = tables.map(t =>
      buildDefaultTableConfig(t, tables.length === 1)
    );
    return true;
  };

  const ensureManualTableConfigs = () => {
    if (batchTableConfigs.value.length > 0 || allTables.value.length === 0) {
      return batchTableConfigs.value.length > 0;
    }
    batchTableConfigs.value = allTables.value.map(table =>
      buildDefaultTableConfig(table, allTables.value.length === 1)
    );
    return true;
  };

  return {
    loadUploadedFileTables,
    reloadWordColumnMappingRulesForCustomer,
    ensureManualTableConfigs
  };
}
