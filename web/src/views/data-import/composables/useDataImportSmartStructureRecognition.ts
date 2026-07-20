import { ref, type Ref } from "vue";
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
import { applySmartConfigConfirmRequestToTable } from "@/views/shared/smart-structure-recognition";
import {
  buildDataImportConfigsFromRecognizedTables,
  buildManualDataImportConfig,
  canSmartTableBeImported,
  createDefaultSelectedSmartTableIndexes,
  filterSelectedSmartTables,
  syncDataImportConfigsToRecognizedTables,
  SMART_STEP_CONFIRM_PREVIEW
} from "../dataImport.smartRecognition";
import type { TableImportConfig } from "../dataImport.types";

const SMART_CONFIRM_PREVIEW_ROWS = 50;

type EnsurePreviewDataLoaded = (options: {
  sourceFileId: number;
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
    recognitionAttempted: smartRecognitionAttempted,
    recognitionError: smartRecognitionError,
    confirmingTableIndex: smartConfirmingTableIndex,
    recognizedTables,
    replaceRecognizedTables,
    summary: smartStructureSummary,
    recognize: recognizeSmartStructure,
    confirm: confirmSmartStructure,
    reset: resetSmartStructure
  } = useSmartStructureRecognition();
  const smartTableInfos = ref<TableInfo[]>([]);
  const smartApplyError = ref("");
  let smartFlowVersion = 0;

  const isCurrentSmartFlow = (fileId: number, flowVersion: number) =>
    uploadedFile.value?.fileId === fileId && smartFlowVersion === flowVersion;

  const clearAppliedRecognitionState = () => {
    tableConfigs.value = [];
    selectedTableIndexes.value = [];
    selectedTables.value = [];
    activeTableIndex.value = null;
    importPreviewSelectionKeys.value = [];
    excludedRowIndexMap.value = {};
    smartTableInfos.value = [];
    smartApplyError.value = "";
  };

  const loadTablesForSmartRecognition = async (
    file: FileUploadResponse,
    flowVersion: number
  ) => {
    const res = await getFileTables(file.fileId);
    if (!isCurrentSmartFlow(file.fileId, flowVersion)) {
      return null;
    }
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
    tables = recognizedTables.value,
    sourceFile = uploadedFile.value,
    flowVersion = smartFlowVersion
  ) => {
    if (!sourceFile) {
      ElMessage.warning("请先上传文件");
      return false;
    }

    const sourceFileId = sourceFile.fileId;

    try {
      smartApplyError.value = "";
      smartStageText.value = "正在读取工作表列表...";
      const tableInfos = await loadTablesForSmartRecognition(
        sourceFile,
        flowVersion
      );
      if (!tableInfos || !isCurrentSmartFlow(sourceFileId, flowVersion)) {
        return false;
      }
      smartTableInfos.value = tableInfos;
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
        ElMessage.warning(
          importTables.length === 0
            ? "请手动勾选需要导入的 Sheet"
            : importTables.some(table => !canSmartTableBeImported(table))
              ? "已勾选的表仍需补齐列配置并确认"
              : "未识别到可导入表格，请使用手动处理"
        );
        // 没有立即可预览的配置不等于识别失败。仍需进入确认页，让用户
        // 为缺少必填列的 Sheet 调整范围并完成确认。
        return tables.length > 0;
      }

      tableConfigs.value = configs;
      selectedTableIndexes.value = configs.map(item => item.tableIndex);
      selectedTables.value = tableInfos.filter(item =>
        selectedTableIndexes.value.includes(item.index)
      );
      activeTableIndex.value = configs[0]?.tableIndex ?? null;
      importPreviewSelectionKeys.value = [];
      excludedRowIndexMap.value = {};
      const previewLoaded = await ensurePreviewDataLoaded({
        sourceFileId,
        previewRows: SMART_CONFIRM_PREVIEW_ROWS,
        initialText: "正在生成导入预览...",
        completeText: "导入预览已生成，正在进入确认页..."
      });
      if (!previewLoaded && isCurrentSmartFlow(sourceFileId, flowVersion)) {
        smartApplyError.value =
          "智能结构已识别，但导入预览生成失败，可重试或使用手动处理";
      }
      return previewLoaded;
    } catch (error) {
      if (!isCurrentSmartFlow(sourceFileId, flowVersion)) {
        return false;
      }
      smartApplyError.value =
        error instanceof Error ? error.message : "应用智能识别结果失败";
      ElMessage.error(smartApplyError.value);
      return false;
    }
  };

  const runSmartStructureRecognition = async () => {
    const sourceFile = uploadedFile.value;
    if (!sourceFile) {
      ElMessage.warning("请先上传文件");
      return false;
    }

    const flowVersion = ++smartFlowVersion;
    const sourceFileId = sourceFile.fileId;
    clearAppliedRecognitionState();

    smartStageText.value = "正在读取工作表结构...";
    try {
      const result = await recognizeSmartStructure(
        sourceFileId,
        selectedCustomerId.value
      );
      if (!result || !isCurrentSmartFlow(sourceFileId, flowVersion)) {
        return false;
      }

      selectedSmartTableIndexes.value = createDefaultSelectedSmartTableIndexes(
        result.tables
      );
      return await applySmartRecognizedTables(
        result.tables,
        sourceFile,
        flowVersion
      );
    } finally {
      if (isCurrentSmartFlow(sourceFileId, flowVersion)) {
        smartStageText.value = "";
      }
    }
  };

  const handleSmartStructureConfirm = async (
    table: SmartConfigRecognizedTable,
    request: SmartConfigConfirmRequest
  ) => {
    const sourceFileId = request.fileId;
    const result = await confirmSmartStructure(request);
    if (
      result &&
      sourceFileId != null &&
      uploadedFile.value?.fileId === sourceFileId
    ) {
      const nextTables = recognizedTables.value.map(item =>
        item.tableIndex === table.tableIndex
          ? applySmartConfigConfirmRequestToTable(item, request)
          : item
      );
      if (!replaceRecognizedTables(nextTables, sourceFileId)) {
        return;
      }
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

  const prepareAdvancedTableConfig = (tableIndex?: number) => {
    const targetTableInfo =
      smartTableInfos.value.find(item => item.index === tableIndex) ??
      smartTableInfos.value[0];
    if (!targetTableInfo) return false;

    if (
      !tableConfigs.value.some(
        item => item.tableIndex === targetTableInfo.index
      )
    ) {
      tableConfigs.value = [
        ...tableConfigs.value,
        buildManualDataImportConfig({
          isExcelFile: isExcelFile.value,
          tableInfo: targetTableInfo
        })
      ].sort((left, right) => left.tableIndex - right.tableIndex);
    }
    if (!selectedTableIndexes.value.includes(targetTableInfo.index)) {
      selectedTableIndexes.value = [
        ...selectedTableIndexes.value,
        targetTableInfo.index
      ].sort((left, right) => left - right);
    }
    selectedTables.value = smartTableInfos.value.filter(item =>
      selectedTableIndexes.value.includes(item.index)
    );
    activeTableIndex.value = targetTableInfo.index;
    return true;
  };

  const syncAdvancedConfigsToRecognizedTables = () => {
    const fileId = uploadedFile.value?.fileId;
    if (fileId == null) return false;
    return replaceRecognizedTables(
      syncDataImportConfigsToRecognizedTables({
        isExcelFile: isExcelFile.value,
        tables: recognizedTables.value,
        configs: tableConfigs.value
      }),
      fileId
    );
  };

  const resetSmartStructureState = () => {
    smartFlowVersion += 1;
    selectedSmartTableIndexes.value = [];
    clearAppliedRecognitionState();
    smartStageText.value = "";
    resetSmartStructure();
  };

  return {
    smartRecognizing,
    smartRecognitionAttempted,
    smartRecognitionError,
    smartApplyError,
    smartConfirmingTableIndex,
    smartTableInfos,
    recognizedTables,
    smartStructureSummary,
    runSmartStructureRecognition,
    handleSmartStructureConfirm,
    handleSmartTableImportSelectionChange,
    prepareAdvancedTableConfig,
    syncAdvancedConfigsToRecognizedTables,
    resetSmartStructureState
  };
}
