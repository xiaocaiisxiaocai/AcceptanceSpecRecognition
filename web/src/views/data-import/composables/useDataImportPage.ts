import { ref, computed, watch, onActivated, onMounted, onUnmounted } from "vue";
import { storeToRefs } from "pinia";
import { ElLoading, ElMessage } from "element-plus";
import {
  getEffectiveColumnMappingRules,
  type ColumnMappingRule
} from "@/api/column-mapping-rules";
import {
  createDefaultExcelMapping,
  buildSkippedRowsGroups,
  defaultExcelMapping,
  defaultWordMapping,
  normalizeExcelMappingByTable
} from "../dataImport.helpers";
import { applyWordRulesToWordMapping } from "@/views/shared/word-column-mapping-rules";
import type {
  DifferenceDecision,
  ExcelRegionMapping,
  ExcelSheetMapping,
  ImportPendingDifferenceWithTable,
  MappingClipboard,
  SkippedRowsGroup,
  TableImportConfig,
  WordRegionMapping
} from "../dataImport.types";
import {
  createDefaultImportDuplicateAiConfig,
  useDataImportExecution
} from "./useDataImportExecution";
import { useDataImportBatchExecution } from "./useDataImportBatchExecution";
import { useDataImportMapping } from "./useDataImportMapping";
import { useDataImportPermissions } from "../dataImport.permissions";
import { useDataImportPreviewSelection } from "./useDataImportPreviewSelection";
import { useDataImportTarget } from "./useDataImportTarget";
import { useDataImportStoreHook } from "@/store/modules/dataImport";
import {
  getFileTables,
  getTablePreview,
  type FileUploadResponse,
  type TableInfo,
  type TableData
} from "@/api/document";
import { hasPerms } from "@/utils/auth";
import { getRequestErrorMessage } from "@/utils/error-message";
import {
  buildDataImportPreviewStageText,
  createDataImportSmartSteps,
  getDataImportPreviewLoadState,
  getDataImportPreviewTotalCount,
  getDataImportPrevStepState,
  getDataImportAdvancedStep,
  SMART_STEP_COMPLETE,
  SMART_STEP_CONFIRM_PREVIEW,
  SMART_STEP_UPLOAD_TARGET
} from "../dataImport.smartRecognition";
import { useDataImportSmartStructureRecognition } from "./useDataImportSmartStructureRecognition";
import {
  buildImportDifferenceDecisionKey,
  buildImportRegionKey,
  captureExcludedRowIdentities,
  mergeExcelRegionPreviews,
  mergeWordRegionPreviews,
  replaceExcelRegionMapping,
  resolveExcludedCombinedIndexes
} from "../dataImport.regions";
import {
  DATA_IMPORT_PREVIEW_WINDOW_COLUMNS,
  loadBoundedFullTablePreview
} from "../dataImport.preview";

const MAPPING_PREVIEW_ROWS = 50;

export function useDataImportPage() {
  const previewLoadVersions = new Map<number, number>();
  const dataImportStore = useDataImportStoreHook();
  const {
    currentStep,
    uploadedFile,
    isExcelFile,
    selectedTableIndexes,
    selectedTables,
    activeTableIndex,
    tableConfigs,
    selectedCustomerId,
    selectedProcessId,
    selectedMachineModelId,
    importDuplicateAiConfig
  } = storeToRefs(dataImportStore);

  const advancedMode = ref(false);
  const legacySteps = computed(() => [
    { title: "上传文件" },
    { title: isExcelFile.value ? "选择工作表" : "选择表格" },
    { title: "配置映射" },
    { title: "选择目标" },
    { title: "确认导入" }
  ]);
  const steps = computed(() =>
    advancedMode.value ? legacySteps.value : createDataImportSmartSteps()
  );

  const {
    canUploadSourceFile,
    canImportAny,
    canImportCurrentFile,
    currentImportPermissionCode,
    currentImportPermissionMessage,
    uploadAccept,
    uploadBlockedMessage
  } = useDataImportPermissions({ isExcelFile, hasPermission: hasPerms });

  const mappingClipboard = ref<MappingClipboard | null>(null);
  const mappingClipboardSourceIndex = ref<number | null>(null);
  const mappingRules = ref<ColumnMappingRule[]>([]);
  const loadingMappingRules = ref(false);
  const mappingRulesCustomerId = ref<number | null | undefined>(undefined);
  let mappingRulesRequestVersion = 0;
  const smartStageText = ref("");
  const selectedSmartTableIndexes = ref<number[]>([]);
  const enableStructureLlmAssistance = ref(true);
  const structureLlmServiceId = ref<number | undefined>();

  const {
    importing,
    importResult,
    pendingImportAggregate,
    committedImportAggregate,
    differenceDecisionMap,
    differenceConfirmDialogVisible
  } = useDataImportExecution();
  const importProgressText = ref("");
  const pendingDifferencePage = ref(1);
  const pendingDifferencePageSize = ref(20);
  const {
    customers,
    processes,
    machineModels,
    selectedMachineModelName,
    loadingCustomers,
    loadingProcesses,
    loadingMachineModels,
    loadingAiServices,
    embeddingSelection,
    llmSelection,
    loadCustomers,
    loadProcesses,
    loadMachineModels,
    loadAiServices,
    resetTargetSelection
  } = useDataImportTarget(importDuplicateAiConfig, {
    selectedCustomerId,
    selectedProcessId,
    selectedMachineModelId
  });
  onMounted(() => {
    loadAiServices();
    loadCustomers();
    loadProcesses();
    loadMachineModels();
  });

  onActivated(() => {
    void loadAiServices();
    if (advancedMode.value && currentStep.value === 2 && !isExcelFile.value) {
      loadMappingRules();
    }
  });

  onUnmounted(() => {
    // Pinia 状态跨组件实例存在；卸载时重置，保持与原本页面本地状态一致。
    dataImportStore.resetAll();
  });

  // 计算属性
  const canGoNext = computed(() => {
    if (!advancedMode.value) {
      switch (currentStep.value) {
        case SMART_STEP_UPLOAD_TARGET:
          return (
            uploadedFile.value !== null &&
            uploadedFile.value.tableCountReady &&
            uploadedFile.value.tableCount > 0 &&
            selectedCustomerId.value !== undefined &&
            (!enableStructureLlmAssistance.value ||
              structureLlmServiceId.value != null)
          );
        case SMART_STEP_CONFIRM_PREVIEW:
          return tableConfigs.value.length > 0;
        case SMART_STEP_COMPLETE:
          return true;
        default:
          return false;
      }
    }

    switch (currentStep.value) {
      case 0:
        return uploadedFile.value !== null;
      case 1:
        return (
          selectedTableIndexes.value.length > 0 ||
          selectedTables.value.length > 0 ||
          tableConfigs.value.length > 0 ||
          (uploadedFile.value?.tableCount ?? 0) > 0
        );
      case 2:
        // 映射步骤允许点击“下一步”，在 goNext 中做校验并提示缺失项，避免按钮直接置灰导致“卡死”
        return tableConfigs.value.length > 0;
      case 3:
        return selectedCustomerId.value !== undefined;
      case 4:
        return true;
      default:
        return false;
    }
  });

  const {
    excludedRowIndexMap,
    importPreviewSelectionKeys,
    getExcludedRowIndexes,
    setExcludedRowIndexes,
    importPreviewGroups,
    removedPreviewRowCount,
    selectedImportPreviewRowsCount,
    handleImportPreviewSelectionChange,
    handleRemoveSinglePreviewRow,
    handleRemoveSelectedPreviewRows,
    handleRestoreRemovedPreviewRows
  } = useDataImportPreviewSelection({
    isExcelFile,
    tableConfigs
  });

  const { getExcelPreviewOptions, validateAllTableMappings } =
    useDataImportMapping({
      isExcelFile,
      tableConfigs,
      excludedRowIndexMap
    });

  const nextDisabled = computed(() => {
    if (!advancedMode.value) {
      return !canGoNext.value || smartRecognizing.value;
    }

    // 步骤1（选择工作表）不置灰，点击后在 goNext 内做兜底同步，避免被状态不同步卡住
    if (currentStep.value === 1) return false;
    // 步骤2（配置映射）永不置灰：允许点击后提示缺失项
    if (currentStep.value === 2) return !canGoNext.value;
    return !canGoNext.value;
  });

  const resetImportFlowState = ({
    preserveTargetSelection = false
  }: {
    preserveTargetSelection?: boolean;
  } = {}) => {
    currentStep.value = 0;
    selectedTableIndexes.value = [];
    selectedTables.value = [];
    activeTableIndex.value = null;
    tableConfigs.value = [];
    mappingClipboard.value = null;
    mappingClipboardSourceIndex.value = null;
    excludedRowIndexMap.value = {};
    importPreviewSelectionKeys.value = [];
    importResult.value = null;
    mappingRules.value = [];
    mappingRulesCustomerId.value = undefined;
    mappingRulesRequestVersion += 1;
    loadingMappingRules.value = false;
    pendingDifferencePage.value = 1;
    pendingDifferencePageSize.value = 20;
    importProgressText.value = "";
    resetPendingDifferenceState();
    resetSmartStructureState();
    advancedMode.value = false;
    enableStructureLlmAssistance.value = true;
    structureLlmServiceId.value = undefined;

    if (!preserveTargetSelection) {
      resetTargetSelection();
    }
  };

  // 文件上传完成
  const handleFileUploaded = async (file: FileUploadResponse) => {
    resetImportFlowState({ preserveTargetSelection: true });
    enableStructureLlmAssistance.value = true;
    structureLlmServiceId.value = undefined;
    uploadedFile.value = file;
    await loadUploadedFileMetadata(file);
  };

  const applyRulesToConfig = (cfg: TableImportConfig, overwrite: boolean) => {
    if (isExcelFile.value) {
      return;
    }

    const headers = cfg.tableInfo?.headers || cfg.previewData?.headers || [];
    if (!headers.length) {
      return;
    }

    cfg.wordMapping = applyWordRulesToWordMapping(
      cfg.wordMapping ?? defaultWordMapping(),
      headers,
      mappingRules.value,
      overwrite
    );
  };

  const applyRulesToAll = (overwrite: boolean) => {
    if (isExcelFile.value || mappingRules.value.length === 0) {
      return;
    }

    tableConfigs.value.forEach(cfg => {
      applyRulesToConfig(cfg, overwrite);
    });
  };

  const loadMappingRules = async () => {
    if (isExcelFile.value) {
      mappingRulesRequestVersion += 1;
      mappingRules.value = [];
      mappingRulesCustomerId.value = undefined;
      loadingMappingRules.value = false;
      return;
    }

    const requestVersion = ++mappingRulesRequestVersion;
    const customerId = selectedCustomerId.value;
    loadingMappingRules.value = true;
    try {
      const res = await getEffectiveColumnMappingRules(customerId);
      if (
        requestVersion !== mappingRulesRequestVersion ||
        customerId !== selectedCustomerId.value ||
        isExcelFile.value
      ) {
        return;
      }
      if (res.code === 0) {
        mappingRules.value = res.data || [];
        mappingRulesCustomerId.value = customerId ?? null;
        applyRulesToAll(false);
      } else {
        ElMessage.error(res.message || "加载列映射规则失败");
      }
    } catch {
      if (
        requestVersion === mappingRulesRequestVersion &&
        customerId === selectedCustomerId.value
      ) {
        ElMessage.error("加载列映射规则失败");
      }
    } finally {
      if (requestVersion === mappingRulesRequestVersion) {
        loadingMappingRules.value = false;
      }
    }
  };

  // 表格选择（多选）
  const handleTablesSelected = (tables: TableInfo[]) => {
    selectedTables.value = tables;
    selectedTableIndexes.value = tables.map(t => t.index).sort((a, b) => a - b);
    if (
      activeTableIndex.value == null &&
      selectedTableIndexes.value.length > 0
    ) {
      activeTableIndex.value = selectedTableIndexes.value[0];
    }

    const existing = new Map(tableConfigs.value.map(c => [c.tableIndex, c]));
    const next: TableImportConfig[] = [];
    for (const t of tables) {
      const old = existing.get(t.index);
      next.push(
        old
          ? {
              ...old,
              tableInfo: t,
              ...(isExcelFile.value
                ? {
                    excelMapping: normalizeExcelMappingByTable(
                      t,
                      old.excelMapping
                    )
                  }
                : { wordMapping: old.wordMapping ?? defaultWordMapping() })
            }
          : {
              tableIndex: t.index,
              tableInfo: t,
              ...(isExcelFile.value
                ? { excelMapping: createDefaultExcelMapping(t) }
                : { wordMapping: defaultWordMapping() }),
              previewData: null
            }
      );
    }
    tableConfigs.value = next.sort((a, b) => a.tableIndex - b.tableIndex);
    const activeIndexes = new Set(next.map(item => item.tableIndex));
    excludedRowIndexMap.value = Object.fromEntries(
      Object.entries(excludedRowIndexMap.value).filter(([key]) =>
        activeIndexes.has(Number(key))
      )
    );
    importPreviewSelectionKeys.value = importPreviewSelectionKeys.value.filter(
      key => {
        const [tableIndex] = key.split(":");
        return activeIndexes.has(Number(tableIndex));
      }
    );

    applyRulesToAll(false);
  };

  const removeSelectedTable = (tableIndex: number) => {
    if (tableConfigs.value.length <= 1) {
      ElMessage.warning(
        `请至少保留一个${isExcelFile.value ? "工作表" : "表格"}`
      );
      return;
    }

    // 从选择中移除
    selectedTableIndexes.value = selectedTableIndexes.value.filter(
      i => i !== tableIndex
    );
    selectedTables.value = selectedTables.value.filter(
      t => t.index !== tableIndex
    );
    tableConfigs.value = tableConfigs.value.filter(
      c => c.tableIndex !== tableIndex
    );
    setExcludedRowIndexes(tableIndex, []);
    importPreviewSelectionKeys.value = importPreviewSelectionKeys.value.filter(
      key => !key.startsWith(`${tableIndex}:`)
    );

    // 调整当前激活 tab
    if (activeTableIndex.value === tableIndex) {
      const nextIdx =
        selectedTableIndexes.value.length > 0
          ? selectedTableIndexes.value[0]
          : null;
      activeTableIndex.value = nextIdx;
    }
  };

  const handleTabRemove = (name: string | number) => {
    const idx = typeof name === "number" ? name : Number(name);
    if (!Number.isFinite(idx)) return;
    removeSelectedTable(idx);
  };

  const restoreSelectedTablesForMapping = async () => {
    const ok = await ensureStepTwoSelection();
    if (!ok || tableConfigs.value.length === 0) {
      ElMessage.warning(
        `请至少选择一个${isExcelFile.value ? "工作表" : "表格"}`
      );
      return;
    }

    activeTableIndex.value = tableConfigs.value[0]?.tableIndex ?? null;
    ElMessage.success(
      `已恢复${tableConfigs.value.length}个${isExcelFile.value ? "工作表" : "表格"}`
    );
  };

  const handlePreviewLoaded = (tableIndex: number, data: TableData) => {
    const cfg = tableConfigs.value.find(c => c.tableIndex === tableIndex);
    if (cfg) {
      if (isExcelFile.value && (cfg.recognizedExcelMappings?.length ?? 0) > 1) {
        return;
      }
      cfg.previewData = data;
      applyRulesToConfig(cfg, false);
    }
  };

  const buildPreviewQuery = (
    cfg: TableImportConfig,
    previewRows: number,
    excelMapping = cfg.excelMapping
  ) => ({
    previewRows,
    headerRowIndex: isExcelFile.value
      ? getExcelPreviewOptions({ ...cfg, excelMapping }).headerRowIndex
      : (cfg.wordMapping?.headerRowIndex ?? 0),
    headerRowCount: isExcelFile.value
      ? getExcelPreviewOptions({ ...cfg, excelMapping }).headerRowCount
      : 1,
    dataStartRowIndex: isExcelFile.value
      ? getExcelPreviewOptions({ ...cfg, excelMapping }).dataStartRowIndex
      : (cfg.wordMapping?.dataStartRowIndex ?? 1),
    dataEndRowIndex: isExcelFile.value
      ? getExcelPreviewOptions({ ...cfg, excelMapping }).dataEndRowIndex
      : undefined
  });

  const loadPreviewData = async (
    cfg: TableImportConfig,
    previewRows: number,
    sourceFileId = uploadedFile.value?.fileId
  ): Promise<TableData> => {
    const previewConfigFingerprint = JSON.stringify({
      excelMapping: cfg.excelMapping,
      recognizedExcelMappings: cfg.recognizedExcelMappings,
      wordMapping: cfg.wordMapping,
      recognizedWordMappings: cfg.recognizedWordMappings
    });
    const requestVersion = (previewLoadVersions.get(cfg.tableIndex) ?? 0) + 1;
    previewLoadVersions.set(cfg.tableIndex, requestVersion);
    const ensureCurrentRequest = () => {
      if (previewLoadVersions.get(cfg.tableIndex) !== requestVersion) {
        throw new Error("预览配置已更新，已忽略旧预览结果");
      }
      const currentFingerprint = JSON.stringify({
        excelMapping: cfg.excelMapping,
        recognizedExcelMappings: cfg.recognizedExcelMappings,
        wordMapping: cfg.wordMapping,
        recognizedWordMappings: cfg.recognizedWordMappings
      });
      if (currentFingerprint !== previewConfigFingerprint) {
        throw new Error("预览配置已更新，已忽略旧预览结果");
      }
    };
    const requestPreview = async (
      options: NonNullable<Parameters<typeof getTablePreview>[2]>,
      fallbackMessage: string
    ) => {
      const readResponse = async (
        requestOptions: NonNullable<Parameters<typeof getTablePreview>[2]>
      ) => {
        const response = await getTablePreview(
          sourceFileId!,
          cfg.tableIndex,
          requestOptions
        );
        if (uploadedFile.value?.fileId !== sourceFileId) {
          throw new Error("源文件已变更，已取消旧文件预览");
        }
        if (response.code !== 0 || !response.data) {
          throw new Error(response.message || fallbackMessage);
        }
        ensureCurrentRequest();
        return response.data;
      };

      if (previewRows > 0) {
        return await readResponse(options);
      }

      const previewColumns = Math.min(
        DATA_IMPORT_PREVIEW_WINDOW_COLUMNS,
        Math.max(1, cfg.tableInfo?.columnCount ?? 1)
      );
      return await loadBoundedFullTablePreview({
        loadWindow: ({ rowOffset, previewRows: windowRows }) =>
          readResponse({
            ...options,
            previewRows: windowRows,
            rowOffset,
            columnOffset: 0,
            previewColumns
          })
      });
    };
    if (sourceFileId == null || uploadedFile.value?.fileId !== sourceFileId) {
      throw new Error("源文件不存在，无法加载预览");
    }

    const regionMappings = isExcelFile.value
      ? cfg.recognizedExcelMappings?.length
        ? cfg.recognizedExcelMappings
        : null
      : cfg.recognizedWordMappings?.length
        ? cfg.recognizedWordMappings
        : null;
    if (!regionMappings) {
      const preview = await requestPreview(
        buildPreviewQuery(cfg, previewRows),
        "加载预览失败"
      );
      cfg.excelPreviewRowLocations = undefined;
      return preview;
    }

    const regionPreviews: Array<{
      mapping: ExcelRegionMapping | WordRegionMapping;
      preview: TableData;
    }> = [];
    // 合并预览从“每段前 N 行”升级为全量时，各区域在合并数组中的偏移会变化。
    // 先保存区域内稳定坐标，加载后再反解为新的合并索引，避免剔除行串区。
    const excludedRowIdentities = captureExcludedRowIdentities(
      getExcludedRowIndexes(cfg.tableIndex),
      cfg.excelPreviewRowLocations ?? []
    );
    for (const mapping of regionMappings) {
      const previewOptions = isExcelFile.value
        ? buildPreviewQuery(cfg, previewRows, mapping as ExcelRegionMapping)
        : {
            previewRows,
            headerRowIndex: (mapping as WordRegionMapping).headerRowIndex,
            headerRowCount: (mapping as WordRegionMapping).headerRowCount,
            dataStartRowIndex: (mapping as WordRegionMapping).dataStartRowIndex,
            dataEndRowIndex: (mapping as WordRegionMapping).dataEndRowIndex
          };
      const preview = await requestPreview(
        previewOptions,
        `区域 ${mapping.regionIndex + 1} 预览失败`
      );
      regionPreviews.push({ mapping, preview });
    }

    const merged = isExcelFile.value
      ? mergeExcelRegionPreviews(
          cfg.tableIndex,
          regionPreviews as Array<{
            mapping: ExcelRegionMapping;
            preview: TableData;
          }>
        )
      : mergeWordRegionPreviews(
          cfg.tableIndex,
          regionPreviews as Array<{
            mapping: WordRegionMapping;
            preview: TableData;
          }>
        );
    ensureCurrentRequest();
    cfg.excelPreviewRowLocations = merged.rowLocations;
    if (excludedRowIdentities.length > 0) {
      setExcludedRowIndexes(
        cfg.tableIndex,
        resolveExcludedCombinedIndexes(
          excludedRowIdentities,
          merged.rowLocations
        )
      );
    }
    return merged.previewData;
  };

  const loadAdvancedPreview = async (
    tableIndex: number,
    options: { previewRows?: number }
  ) => {
    const cfg = tableConfigs.value.find(item => item.tableIndex === tableIndex);
    if (!cfg) {
      throw new Error("表格配置不存在，无法加载预览");
    }
    const previewData = await loadPreviewData(
      cfg,
      options.previewRows ?? MAPPING_PREVIEW_ROWS
    );
    cfg.previewData = previewData;
    applyRulesToConfig(cfg, false);
    return previewData;
  };

  const ensurePreviewDataLoaded = async ({
    sourceFileId,
    previewRows,
    initialText,
    completeText
  }: {
    sourceFileId: number;
    previewRows: number;
    initialText: string;
    completeText?: string;
  }) => {
    if (uploadedFile.value?.fileId !== sourceFileId) {
      return false;
    }
    const pendingConfigs = tableConfigs.value.filter(cfg => {
      if (!cfg.previewData) {
        return true;
      }

      if (previewRows <= 0) {
        return cfg.previewData.rows.length < cfg.previewData.totalRows;
      }

      return (
        cfg.previewData.rows.length <
        Math.min(previewRows, cfg.previewData.totalRows)
      );
    });

    if (pendingConfigs.length === 0) {
      return true;
    }

    smartStageText.value = initialText;

    try {
      for (const [index, cfg] of pendingConfigs.entries()) {
        if (uploadedFile.value?.fileId !== sourceFileId) {
          return false;
        }
        smartStageText.value = buildDataImportPreviewStageText(
          index + 1,
          pendingConfigs.length,
          cfg.tableInfo?.name
        );
        const previewData = await loadPreviewData(
          cfg,
          previewRows,
          sourceFileId
        );
        if (
          uploadedFile.value?.fileId !== sourceFileId ||
          !tableConfigs.value.includes(cfg)
        ) {
          return false;
        }
        cfg.previewData = previewData;
      }
      if (completeText) {
        smartStageText.value = completeText;
      }
      return true;
    } catch (error) {
      if (uploadedFile.value?.fileId !== sourceFileId) {
        return false;
      }
      ElMessage.error(getRequestErrorMessage(error, "加载导入预览失败"));
      return false;
    }
  };

  const loadFullPreviewData = async () => {
    const pendingConfigs = tableConfigs.value.filter(
      cfg =>
        !cfg.previewData ||
        cfg.previewData.rows.length < cfg.previewData.totalRows
    );

    if (pendingConfigs.length === 0) {
      return true;
    }

    const loading = ElLoading.service({
      lock: true,
      text: "正在生成导入预览..."
    });

    try {
      for (const [index, cfg] of pendingConfigs.entries()) {
        smartStageText.value = buildDataImportPreviewStageText(
          index + 1,
          pendingConfigs.length,
          cfg.tableInfo?.name
        );
        cfg.previewData = await loadPreviewData(cfg, 0);
      }
      return true;
    } catch (error) {
      ElMessage.error(getRequestErrorMessage(error, "加载导入预览失败"));
      return false;
    } finally {
      loading.close();
    }
  };

  let fullPreviewLoadPromise: Promise<boolean> | null = null;
  const ensureFullPreviewDataLoaded = async () => {
    if (fullPreviewLoadPromise) {
      return fullPreviewLoadPromise;
    }

    fullPreviewLoadPromise = loadFullPreviewData();
    try {
      return await fullPreviewLoadPromise;
    } finally {
      fullPreviewLoadPromise = null;
    }
  };

  const {
    smartRecognizing,
    tableMetadataLoading,
    tableMetadataError,
    smartRecognitionAttempted,
    smartRecognitionError,
    smartApplyError,
    smartConfirmingTableIndex,
    smartTableInfos,
    recognizedTables,
    smartStructureSummary,
    loadUploadedFileMetadata,
    runSmartStructureRecognition,
    handleSmartStructureConfirm,
    applyCurrentSmartRecognizedTables,
    handleSmartTableImportSelectionChange,
    prepareAdvancedTableConfig,
    syncAdvancedConfigsToRecognizedTables,
    resetSmartStructureState
  } = useDataImportSmartStructureRecognition({
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
    enableLlmAssistance: enableStructureLlmAssistance,
    llmServiceId: structureLlmServiceId,
    ensurePreviewDataLoaded
  });

  const enterAdvancedMode = (
    target: "tableSelect" | "mapping" = "tableSelect",
    tableIndex?: number
  ) => {
    if (target === "mapping") {
      prepareAdvancedTableConfig(tableIndex);
    }
    advancedMode.value = true;
    currentStep.value = getDataImportAdvancedStep(target);
  };

  const exitAdvancedMode = () => {
    syncAdvancedConfigsToRecognizedTables();
    advancedMode.value = false;
    currentStep.value = SMART_STEP_CONFIRM_PREVIEW;
  };

  const updateExcelMapping = (tableIndex: number, value: ExcelSheetMapping) => {
    const cfg = tableConfigs.value.find(c => c.tableIndex === tableIndex);
    if (!cfg) return;
    const previousMapping = cfg.excelMapping;
    const normalizedMapping = normalizeExcelMappingByTable(
      cfg.tableInfo,
      value
    );
    if (cfg.recognizedExcelMappings?.length) {
      cfg.recognizedExcelMappings = replaceExcelRegionMapping({
        regions: cfg.recognizedExcelMappings,
        mapping: normalizedMapping,
        previousMapping
      });
    }
    cfg.excelMapping = normalizedMapping;
    cfg.excelPreviewRowLocations = undefined;
    cfg.previewData = null;
    setExcludedRowIndexes(tableIndex, []);
    importPreviewSelectionKeys.value = importPreviewSelectionKeys.value.filter(
      key => !key.startsWith(`${tableIndex}:`)
    );
  };

  const getActiveTableConfig = (): TableImportConfig | null => {
    if (tableConfigs.value.length === 0) return null;
    if (activeTableIndex.value === null) return tableConfigs.value[0];
    return (
      tableConfigs.value.find(c => c.tableIndex === activeTableIndex.value) ??
      tableConfigs.value[0]
    );
  };

  const getTableConfigTabLabel = (cfg: TableImportConfig) => {
    const base = `${isExcelFile.value ? "工作表" : "表格"} ${cfg.tableIndex + 1}`;
    const sheetName = cfg.tableInfo?.name?.trim();
    return sheetName ? `${base}（${sheetName}）` : base;
  };

  const canPasteClipboard = computed(() => {
    if (!mappingClipboard.value) return false;
    if (isExcelFile.value) return mappingClipboard.value.kind === "excel";
    return mappingClipboard.value.kind === "word";
  });

  const copyActiveMappingConfig = () => {
    const activeCfg = getActiveTableConfig();
    if (!activeCfg) {
      ElMessage.warning(`请先选择一个${isExcelFile.value ? "工作表" : "表格"}`);
      return;
    }

    if (isExcelFile.value) {
      const normalized = normalizeExcelMappingByTable(
        activeCfg.tableInfo,
        activeCfg.excelMapping ?? defaultExcelMapping()
      );
      mappingClipboard.value = {
        kind: "excel",
        value: { ...normalized }
      };
    } else {
      mappingClipboard.value = {
        kind: "word",
        value: { ...(activeCfg.wordMapping ?? defaultWordMapping()) }
      };
    }

    mappingClipboardSourceIndex.value = activeCfg.tableIndex;
    ElMessage.success(
      `已复制${isExcelFile.value ? "工作表" : "表格"} ${activeCfg.tableIndex + 1} 的字段配置`
    );
  };

  const pasteMappingConfigToOthers = () => {
    const activeCfg = getActiveTableConfig();
    if (!activeCfg) {
      ElMessage.warning(`请先选择一个${isExcelFile.value ? "工作表" : "表格"}`);
      return;
    }

    if (!mappingClipboard.value || !canPasteClipboard.value) {
      ElMessage.warning(
        `请先复制${isExcelFile.value ? "Excel 列序号配置" : "列映射配置"}`
      );
      return;
    }

    let pastedCount = 0;
    for (const cfg of tableConfigs.value) {
      if (cfg.tableIndex === activeCfg.tableIndex) continue;

      if (isExcelFile.value && mappingClipboard.value.kind === "excel") {
        const previousMapping = cfg.excelMapping;
        const normalizedMapping = normalizeExcelMappingByTable(cfg.tableInfo, {
          ...mappingClipboard.value.value
        });
        cfg.excelMapping = normalizedMapping;
        cfg.recognizedExcelMapping = { ...normalizedMapping };
        if (cfg.recognizedExcelMappings?.length) {
          cfg.recognizedExcelMappings = replaceExcelRegionMapping({
            regions: cfg.recognizedExcelMappings,
            mapping: normalizedMapping,
            previousMapping
          });
        }
        cfg.excelPreviewRowLocations = undefined;
        cfg.previewData = null;
        setExcludedRowIndexes(cfg.tableIndex, []);
        importPreviewSelectionKeys.value =
          importPreviewSelectionKeys.value.filter(
            key => !key.startsWith(`${cfg.tableIndex}:`)
          );
        pastedCount++;
        continue;
      }

      if (!isExcelFile.value && mappingClipboard.value.kind === "word") {
        const nextMapping = { ...mappingClipboard.value.value };
        cfg.wordMapping = nextMapping;
        if (cfg.recognizedWordMappings?.length) {
          cfg.recognizedWordMappings = cfg.recognizedWordMappings.map(
            (region, regionIndex) =>
              regionIndex === 0 ? { ...region, ...nextMapping } : region
          );
        }
        cfg.excelPreviewRowLocations = undefined;
        cfg.previewData = null;
        setExcludedRowIndexes(cfg.tableIndex, []);
        importPreviewSelectionKeys.value =
          importPreviewSelectionKeys.value.filter(
            key => !key.startsWith(`${cfg.tableIndex}:`)
          );
        pastedCount++;
      }
    }

    if (pastedCount === 0) {
      ElMessage.warning(
        `没有可粘贴的其他${isExcelFile.value ? "工作表" : "表格"}`
      );
      return;
    }

    ElMessage.success(
      `已应用到 ${pastedCount} 个其他${isExcelFile.value ? "工作表" : "表格"}`
    );
  };

  // 监听步骤变化
  watch(currentStep, step => {
    if (!advancedMode.value) {
      if (step === SMART_STEP_UPLOAD_TARGET) {
        if (customers.value.length === 0) loadCustomers();
        if (processes.value.length === 0) loadProcesses();
        if (machineModels.value.length === 0) loadMachineModels();
      }
      return;
    }

    if (
      step === 2 &&
      !isExcelFile.value &&
      mappingRulesCustomerId.value !== (selectedCustomerId.value ?? null) &&
      !loadingMappingRules.value
    ) {
      loadMappingRules();
    }
    if (step === 3 && customers.value.length === 0) {
      loadCustomers();
    }
    if (step === 3 && processes.value.length === 0) {
      loadProcesses();
    }
    if (step === 3 && machineModels.value.length === 0) {
      loadMachineModels();
    }
  });

  watch(selectedCustomerId, () => {
    mappingRulesRequestVersion += 1;
    mappingRules.value = [];
    mappingRulesCustomerId.value = undefined;
    loadingMappingRules.value = false;
    if (advancedMode.value && !isExcelFile.value) {
      void loadMappingRules();
    }
  });

  // 下一步
  const ensureStepTwoSelection = async () => {
    if (
      selectedTableIndexes.value.length > 0 ||
      selectedTables.value.length > 0 ||
      tableConfigs.value.length > 0
    ) {
      return true;
    }

    if (!uploadedFile.value?.fileId) {
      ElMessage.warning("请先上传文件");
      return false;
    }

    try {
      const res = await getFileTables(uploadedFile.value.fileId);
      if (res.code !== 0 || !res.data?.length) {
        ElMessage.warning("请至少选择一个工作表");
        return false;
      }

      // 兜底：父状态丢失时自动补齐为“全选”，避免界面卡死在步骤2
      handleTablesSelected(res.data);
      return true;
    } catch {
      ElMessage.warning("请至少选择一个工作表");
      return false;
    }
  };

  const goNext = async () => {
    if (!advancedMode.value) {
      if (currentStep.value === SMART_STEP_UPLOAD_TARGET) {
        if (!selectedCustomerId.value) {
          ElMessage.warning("请先选择客户");
          return;
        }

        const ok = await runSmartStructureRecognition();
        if (!ok) return;
        currentStep.value = SMART_STEP_CONFIRM_PREVIEW;
        return;
      }

      if (currentStep.value === SMART_STEP_CONFIRM_PREVIEW) {
        const loaded = await ensureFullPreviewDataLoaded();
        if (!loaded) return;
        currentStep.value = SMART_STEP_COMPLETE;
        return;
      }

      return;
    }

    // 步骤1（选择工作表）：不依赖 canGoNext，统一走兜底同步
    if (currentStep.value === 1) {
      if (selectedTableIndexes.value.length === 0) {
        if (selectedTables.value.length > 0) {
          selectedTableIndexes.value = selectedTables.value
            .map(t => t.index)
            .sort((a, b) => a - b);
        } else if (tableConfigs.value.length > 0) {
          selectedTableIndexes.value = tableConfigs.value
            .map(c => c.tableIndex)
            .sort((a, b) => a - b);
        } else {
          const ok = await ensureStepTwoSelection();
          if (!ok) return;
        }
      }

      if (currentStep.value < steps.value.length - 1) currentStep.value++;
      return;
    }

    if (!canGoNext.value) return;

    // 步骤3：配置映射。这里做完整校验，缺失则提示并跳转到对应表格，避免“按钮置灰卡死”
    if (currentStep.value === 2) {
      const v = validateAllTableMappings();
      if (!v.ok) {
        const first = v.missingByTable[0];
        activeTableIndex.value = first.tableIndex;
        const summary = v.missingByTable
          .slice(0, 3)
          .map(x => `表格${x.tableIndex + 1}：缺 ${x.missing.join("、")}`)
          .join("；");
        const more =
          v.missingByTable.length > 3
            ? `（另有 ${v.missingByTable.length - 3} 个表格未完成映射）`
            : "";
        ElMessage.warning(`请先完成列映射：${summary}${more}`);
        return;
      }
    }

    if (currentStep.value === 3) {
      const loaded = await ensureFullPreviewDataLoaded();
      if (!loaded) return;
    }

    if (currentStep.value < steps.value.length - 1) currentStep.value++;
  };

  // 上一步
  const goPrev = () => {
    const prevState = getDataImportPrevStepState({
      advancedMode: advancedMode.value,
      currentStep: currentStep.value
    });
    advancedMode.value = prevState.advancedMode;
    currentStep.value = prevState.currentStep;
  };

  const syncDifferenceDecisionMap = (
    items: ImportPendingDifferenceWithTable[]
  ) => {
    const nextMap: Record<string, DifferenceDecision | undefined> = {};

    for (const item of items) {
      const decisionKey = buildImportDifferenceDecisionKey(item);
      nextMap[decisionKey] = differenceDecisionMap.value[decisionKey];
    }

    differenceDecisionMap.value = nextMap;
  };

  const resetPendingDifferenceState = () => {
    pendingImportAggregate.value = null;
    committedImportAggregate.value = null;
    differenceConfirmDialogVisible.value = false;
    differenceDecisionMap.value = {};
  };

  const applyDifferenceDecisionToAll = (decision: DifferenceDecision) => {
    const nextMap = { ...differenceDecisionMap.value };

    for (const item of pendingImportAggregate.value?.pendingDifferences || []) {
      nextMap[buildImportDifferenceDecisionKey(item)] = decision;
    }

    differenceDecisionMap.value = nextMap;
  };

  // 重新开始
  const handleRestart = () => {
    resetImportFlowState();
    uploadedFile.value = null;
    importDuplicateAiConfig.value = createDefaultImportDuplicateAiConfig({
      embeddingServiceId:
        embeddingSelection.value.status === "available"
          ? (embeddingSelection.value.serviceId ?? undefined)
          : undefined,
      llmServiceId:
        llmSelection.value.status === "available"
          ? (llmSelection.value.serviceId ?? undefined)
          : undefined
    });
  };

  // 预览数据条数（totalRows 已是纯数据行数，无需再减表头）
  const previewDataCount = computed(() => {
    return getDataImportPreviewTotalCount(
      tableConfigs.value,
      excludedRowIndexMap.value
    );
  });

  const previewLoadState = computed(() =>
    getDataImportPreviewLoadState(tableConfigs.value)
  );

  const pendingDifferences = computed<ImportPendingDifferenceWithTable[]>(
    () => {
      return pendingImportAggregate.value?.pendingDifferences || [];
    }
  );

  const pagedPendingDifferences = computed<ImportPendingDifferenceWithTable[]>(
    () => {
      const start =
        (pendingDifferencePage.value - 1) * pendingDifferencePageSize.value;
      return pendingDifferences.value.slice(
        start,
        start + pendingDifferencePageSize.value
      );
    }
  );

  const pendingUndecidedCount = computed(() => {
    return pendingDifferences.value.filter(
      item =>
        !differenceDecisionMap.value[buildImportDifferenceDecisionKey(item)]
    ).length;
  });

  const pendingImportDecisionCount = computed(() => {
    return pendingDifferences.value.filter(
      item =>
        differenceDecisionMap.value[buildImportDifferenceDecisionKey(item)] ===
        "import"
    ).length;
  });

  const pendingPartialDecisionCount = computed(() => {
    return pendingDifferences.value.filter(
      item =>
        differenceDecisionMap.value[buildImportDifferenceDecisionKey(item)] ===
        "partial"
    ).length;
  });

  const pendingSkipDecisionCount = computed(() => {
    return pendingDifferences.value.filter(
      item =>
        differenceDecisionMap.value[buildImportDifferenceDecisionKey(item)] ===
        "skip"
    ).length;
  });

  const hasPendingDifferenceConfirmation = computed(() => {
    return pendingDifferences.value.length > 0;
  });

  const hasCommittedImportProgress = computed(() => {
    const aggregate = committedImportAggregate.value;
    if (!aggregate) return false;
    return (
      aggregate.successCount > 0 ||
      aggregate.failedCount > 0 ||
      aggregate.skippedCount > 0
    );
  });

  const pendingTableIndexes = computed<number[]>(() => {
    return Array.from(
      new Set(pendingDifferences.value.map(item => item.tableIndex))
    );
  });
  const pendingRegionKeys = computed<string[]>(() => {
    return Array.from(
      new Set(
        pendingDifferences.value.map(item =>
          buildImportRegionKey(item.tableIndex, item.regionId)
        )
      )
    );
  });

  const ensureImportRuntimeAiReady = async () => {
    const semanticDuplicateCheckRequested =
      importDuplicateAiConfig.value.enableSemanticDuplicateCheck;
    const llmReviewRequested =
      semanticDuplicateCheckRequested &&
      importDuplicateAiConfig.value.enableLlmDuplicateReview;
    const refresh = await loadAiServices();
    if (!refresh.current) return false;

    const embedding = refresh.embedding ?? embeddingSelection.value;
    const llm = refresh.llm ?? llmSelection.value;
    let message = "";
    let blocked = false;

    if (semanticDuplicateCheckRequested && embedding.status === "checking") {
      message =
        "Embedding 服务仍在检测，请稍后重试；也可关闭 AI 疑似重复检查后仅使用规则导入";
      blocked = true;
    } else if (
      semanticDuplicateCheckRequested &&
      !importDuplicateAiConfig.value.enableSemanticDuplicateCheck
    ) {
      message = "Embedding 服务当前不可用，本次导入将仅使用规则检查";
    } else if (llmReviewRequested && llm.status === "checking") {
      message =
        "LLM 服务仍在检测，请稍后重试；也可关闭 LLM 二次复核后继续 Embedding 检查";
      blocked = true;
    } else if (
      llmReviewRequested &&
      !importDuplicateAiConfig.value.enableLlmDuplicateReview
    ) {
      message = "LLM 服务当前不可用，本次仅执行 Embedding 疑似重复识别";
    }

    if (message) ElMessage.warning(message);
    return !blocked;
  };

  const {
    openDifferenceConfirmDialog,
    handleConfirmPendingDifferences,
    handleImport: executeImport,
    importProgressDescription,
    importPrimaryButtonText,
    confirmDifferenceButtonText
  } = useDataImportBatchExecution({
    isExcelFile,
    uploadedFile,
    tableConfigs,
    selectedCustomerId,
    selectedProcessId,
    selectedMachineModelId,
    importing,
    importResult,
    pendingImportAggregate,
    committedImportAggregate,
    differenceDecisionMap,
    differenceConfirmDialogVisible,
    importDuplicateAiConfig,
    importProgressText,
    pendingDifferences,
    pendingUndecidedCount,
    pendingTableIndexes,
    pendingRegionKeys,
    previewDataCount,
    hasPendingDifferenceConfirmation,
    currentImportPermissionCode,
    currentImportPermissionMessage,
    getExcludedRowIndexes,
    resetPendingDifferenceState,
    syncDifferenceDecisionMap,
    ensureRuntimeAiReady: ensureImportRuntimeAiReady
  });

  const handleImport = async () => {
    if (!(await ensureImportRuntimeAiReady())) return;

    smartStageText.value = "正在准备完整导入数据...";
    const loaded = await ensureFullPreviewDataLoaded();
    smartStageText.value = "";
    if (!loaded) return;

    await executeImport();
    if (!advancedMode.value && importResult.value) {
      currentStep.value = SMART_STEP_COMPLETE;
    }
  };

  const pendingDifferenceDisplayStart = computed(() => {
    if (pendingDifferences.value.length === 0) return 0;
    return (
      (pendingDifferencePage.value - 1) * pendingDifferencePageSize.value + 1
    );
  });

  const pendingDifferenceDisplayEnd = computed(() => {
    if (pendingDifferences.value.length === 0) return 0;
    return Math.min(
      pendingDifferencePage.value * pendingDifferencePageSize.value,
      pendingDifferences.value.length
    );
  });

  const differenceDialogFooterTip = computed(() => {
    return importing.value
      ? importProgressText.value
      : `未确认 ${pendingUndecidedCount.value} 条`;
  });

  watch(
    () => pendingImportAggregate.value?.pendingDifferences,
    () => {
      pendingDifferencePage.value = 1;
    }
  );

  watch(
    [pendingDifferences, pendingDifferencePageSize],
    () => {
      const maxPage = Math.max(
        1,
        Math.ceil(
          pendingDifferences.value.length / pendingDifferencePageSize.value
        )
      );
      if (pendingDifferencePage.value > maxPage) {
        pendingDifferencePage.value = maxPage;
      }
    },
    { immediate: true }
  );

  const skippedRowsGroups = computed<SkippedRowsGroup[]>(() =>
    buildSkippedRowsGroups(
      importResult.value?.skippedRows || [],
      tableConfigs.value
    )
  );

  return {
    MAPPING_PREVIEW_ROWS,
    currentStep,
    advancedMode,
    uploadedFile,
    isExcelFile,
    selectedTableIndexes,
    selectedTables,
    activeTableIndex,
    tableConfigs,
    selectedCustomerId,
    selectedProcessId,
    selectedMachineModelId,
    importDuplicateAiConfig,
    steps,
    smartRecognizing,
    tableMetadataLoading,
    tableMetadataError,
    enableStructureLlmAssistance,
    structureLlmServiceId,
    smartRecognitionAttempted,
    smartRecognitionError,
    smartApplyError,
    smartStageText,
    selectedSmartTableIndexes,
    smartConfirmingTableIndex,
    smartTableInfos,
    recognizedTables,
    smartStructureSummary,
    canUploadSourceFile,
    canImportAny,
    canImportCurrentFile,
    currentImportPermissionMessage,
    uploadAccept,
    uploadBlockedMessage,
    mappingClipboardSourceIndex,
    mappingRules,
    loadingMappingRules,
    importing,
    importResult,
    committedImportAggregate,
    differenceDecisionMap,
    differenceConfirmDialogVisible,
    importProgressText,
    pendingDifferencePage,
    pendingDifferencePageSize,
    customers,
    processes,
    machineModels,
    selectedMachineModelName,
    loadingCustomers,
    loadingProcesses,
    loadingMachineModels,
    loadingAiServices,
    embeddingSelection,
    llmSelection,
    importPreviewGroups,
    removedPreviewRowCount,
    selectedImportPreviewRowsCount,
    handleImportPreviewSelectionChange,
    handleRemoveSinglePreviewRow,
    handleRemoveSelectedPreviewRows,
    handleRestoreRemovedPreviewRows,
    getExcelPreviewOptions,
    nextDisabled,
    handleFileUploaded,
    loadUploadedFileMetadata,
    runSmartStructureRecognition,
    handleSmartStructureConfirm,
    applyCurrentSmartRecognizedTables,
    handleSmartTableImportSelectionChange,
    enterAdvancedMode,
    exitAdvancedMode,
    applyRulesToAll,
    loadMappingRules,
    handleTablesSelected,
    handlePreviewLoaded,
    loadAdvancedPreview,
    updateExcelMapping,
    getTableConfigTabLabel,
    canPasteClipboard,
    copyActiveMappingConfig,
    pasteMappingConfigToOthers,
    goNext,
    goPrev,
    handleRestart,
    previewDataCount,
    previewLoadState,
    ensureFullPreviewDataLoaded,
    pendingDifferences,
    pagedPendingDifferences,
    pendingUndecidedCount,
    pendingImportDecisionCount,
    pendingPartialDecisionCount,
    pendingSkipDecisionCount,
    hasPendingDifferenceConfirmation,
    hasCommittedImportProgress,
    openDifferenceConfirmDialog,
    handleConfirmPendingDifferences,
    handleImport,
    importProgressDescription,
    importPrimaryButtonText,
    confirmDifferenceButtonText,
    pendingDifferenceDisplayStart,
    pendingDifferenceDisplayEnd,
    differenceDialogFooterTip,
    skippedRowsGroups,
    applyDifferenceDecisionToAll,
    handleTabRemove,
    restoreSelectedTablesForMapping
  };
}
