import type { Ref } from "vue";
import { ElMessage } from "element-plus";
import {
  getFileTables,
  type FileUploadResponse,
  type TableInfo
} from "@/api/document";
import type {
  SmartConfigConfirmRequest,
  SmartConfigRecognizedTable
} from "@/api/smart-config";
import { useSmartStructureRecognition } from "@/views/shared/useSmartStructureRecognition";
import {
  buildDataImportConfigsFromRecognizedTables,
  createDefaultSelectedSmartTableIndexes,
  filterSelectedSmartTables,
  SMART_STEP_CONFIRM_PREVIEW
} from "../dataImport.smartRecognition";
import type { TableImportConfig } from "../dataImport.types";

const SMART_CONFIRM_PREVIEW_ROWS = 50;

type EnsurePreviewDataLoaded = (options: {
  previewRows: number;
  initialText: string;
  completeText?: string;
}) => Promise<boolean>;

export function useDataImportSmartStructureRecognition({
  uploadedFile,
  selectedCustomerId,
  isExcelFile,
  currentStep,
  tableConfigs,
  selectedTableIndexes,
  selectedTables,
  activeTableIndex,
  importPreviewSelectionKeys,
  excludedRowIndexMap,
  smartStageText,
  selectedSmartTableIndexes,
  ensurePreviewDataLoaded
}: {
  uploadedFile: Ref<FileUploadResponse | null>;
  selectedCustomerId: Ref<number | undefined>;
  isExcelFile: Ref<boolean>;
  currentStep: Ref<number>;
  tableConfigs: Ref<TableImportConfig[]>;
  selectedTableIndexes: Ref<number[]>;
  selectedTables: Ref<TableInfo[]>;
  activeTableIndex: Ref<number | null>;
  importPreviewSelectionKeys: Ref<string[]>;
  excludedRowIndexMap: Ref<Record<number, number[]>>;
  smartStageText: Ref<string>;
  selectedSmartTableIndexes: Ref<number[]>;
  ensurePreviewDataLoaded: EnsurePreviewDataLoaded;
}) {
  const {
    recognizing: smartRecognizing,
    confirmingTableIndex: smartConfirmingTableIndex,
    recognizedTables,
    summary: smartStructureSummary,
    recognize: recognizeSmartStructure,
    confirm: confirmSmartStructure,
    reset: resetSmartStructure
  } = useSmartStructureRecognition();

  const loadTablesForSmartRecognition = async (file: FileUploadResponse) => {
    const res = await getFileTables(file.fileId);
    if (res.code !== 0 || !res.data?.length) {
      throw new Error(res.message || "获取表格列表失败");
    }

    if (uploadedFile.value?.fileId === file.fileId) {
      uploadedFile.value = {
        ...uploadedFile.value,
        tableCount: res.data.length,
        tableCountReady: true
      };
    }

    return res.data;
  };

  const applySmartRecognizedTables = async (
    tables = recognizedTables.value
  ) => {
    if (!uploadedFile.value) {
      ElMessage.warning("请先上传文件");
      return false;
    }

    try {
      smartStageText.value = "正在读取工作表列表...";
      const tableInfos = await loadTablesForSmartRecognition(
        uploadedFile.value
      );
      smartStageText.value = "正在应用识别到的结构...";
      const importTables = filterSelectedSmartTables(
        tables,
        selectedSmartTableIndexes.value
      );
      const configs = buildDataImportConfigsFromRecognizedTables({
        isExcelFile: isExcelFile.value,
        tables: importTables,
        tableInfos
      });

      if (configs.length === 0) {
        tableConfigs.value = [];
        selectedTableIndexes.value = [];
        selectedTables.value = [];
        activeTableIndex.value = null;
        ElMessage.warning("未识别到可导入表格，请进入高级手动配置");
        return false;
      }

      tableConfigs.value = configs;
      selectedTableIndexes.value = configs.map(item => item.tableIndex);
      selectedTables.value = tableInfos.filter(item =>
        selectedTableIndexes.value.includes(item.index)
      );
      activeTableIndex.value = configs[0]?.tableIndex ?? null;
      importPreviewSelectionKeys.value = [];
      excludedRowIndexMap.value = {};
      return await ensurePreviewDataLoaded({
        previewRows: SMART_CONFIRM_PREVIEW_ROWS,
        initialText: "正在生成导入预览...",
        completeText: "导入预览已生成，正在进入确认页..."
      });
    } catch (error) {
      ElMessage.error(
        error instanceof Error ? error.message : "应用智能识别结果失败"
      );
      return false;
    }
  };

  const runSmartStructureRecognition = async () => {
    if (!uploadedFile.value) {
      ElMessage.warning("请先上传文件");
      return false;
    }

    smartStageText.value = "正在读取工作表结构...";
    try {
      const result = await recognizeSmartStructure(
        uploadedFile.value.fileId,
        selectedCustomerId.value
      );
      if (!result) {
        return false;
      }

      selectedSmartTableIndexes.value = createDefaultSelectedSmartTableIndexes(
        result.tables
      );
      return await applySmartRecognizedTables(result.tables);
    } finally {
      smartStageText.value = "";
    }
  };

  const replaceRecognizedTableWithConfirmRequest = (
    table: SmartConfigRecognizedTable,
    request: SmartConfigConfirmRequest
  ): SmartConfigRecognizedTable => ({
    ...table,
    tableName: request.templateName || table.tableName,
    headers: request.headers,
    projectColumnIndex: request.projectColumnIndex,
    specificationColumnIndex: request.specificationColumnIndex,
    acceptanceColumnIndex: request.acceptanceColumnIndex,
    remarkColumnIndex: request.remarkColumnIndex,
    headerRowIndex: request.headerRowIndex,
    headerRowCount: request.headerRowCount,
    dataStartRowIndex: request.dataStartRowIndex,
    dataEndRowIndex: request.dataEndRowIndex,
    isSpecificationOnly: request.isSpecificationOnly,
    decision: "AutoApply"
  });

  const handleSmartStructureConfirm = async (
    table: SmartConfigRecognizedTable,
    request: SmartConfigConfirmRequest
  ) => {
    const result = await confirmSmartStructure(request);
    if (result) {
      const nextTables = recognizedTables.value.map(item =>
        item.tableIndex === table.tableIndex
          ? replaceRecognizedTableWithConfirmRequest(item, request)
          : item
      );
      if (!selectedSmartTableIndexes.value.includes(table.tableIndex)) {
        selectedSmartTableIndexes.value = [
          ...selectedSmartTableIndexes.value,
          table.tableIndex
        ].sort((a, b) => a - b);
      }
      await applySmartRecognizedTables(nextTables);
    }
  };

  const handleSmartTableImportSelectionChange = async (
    table: SmartConfigRecognizedTable,
    checked: boolean
  ) => {
    selectedSmartTableIndexes.value = checked
      ? Array.from(
          new Set([...selectedSmartTableIndexes.value, table.tableIndex])
        ).sort((a, b) => a - b)
      : selectedSmartTableIndexes.value.filter(
          tableIndex => tableIndex !== table.tableIndex
        );

    if (currentStep.value === SMART_STEP_CONFIRM_PREVIEW) {
      await applySmartRecognizedTables();
    }
  };

  const resetSmartStructureState = () => {
    selectedSmartTableIndexes.value = [];
    smartStageText.value = "";
    resetSmartStructure();
  };

  return {
    smartRecognizing,
    smartConfirmingTableIndex,
    recognizedTables,
    smartStructureSummary,
    runSmartStructureRecognition,
    handleSmartStructureConfirm,
    handleSmartTableImportSelectionChange,
    resetSmartStructureState
  };
}
