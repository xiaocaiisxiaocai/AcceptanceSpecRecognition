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
  ExcelSheetMapping,
  ImportPendingDifferenceWithTable,
  MappingClipboard,
  SkippedRowsGroup,
  TableImportConfig
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

const MAPPING_PREVIEW_ROWS = 50;

export function useDataImportPage() {
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
    {
      title: "上传文件",
      description: isExcelFile.value
        ? "选择 Excel 文件"
        : "选择 Word/Excel 文件"
    },
    {
      title: isExcelFile.value ? "选择工作表" : "选择表格",
      description: "选择要导入的数据范围"
    },
    {
      title: "配置映射",
      description: isExcelFile.value ? "按列序号指定字段" : "设置列映射关系"
    },
    { title: "选择目标", description: "选择导入目标" },
    { title: "确认导入", description: "预览并确认" }
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
  const smartStageText = ref("");
  const selectedSmartTableIndexes = ref<number[]>([]);

  const {
    importing,
    importResult,
    pendingImportAggregate,
    committedImportAggregate,
    previewSkippedRows,
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
    embeddingServices,
    llmServices,
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
            selectedCustomerId.value !== undefined
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
    previewSkippedRows.value = false;
    mappingRules.value = [];
    loadingMappingRules.value = false;
    pendingDifferencePage.value = 1;
    pendingDifferencePageSize.value = 20;
    importProgressText.value = "";
    resetPendingDifferenceState();
    resetSmartStructureState();
    advancedMode.value = false;

    if (!preserveTargetSelection) {
      resetTargetSelection();
    }
  };

  // 文件上传完成
  const handleFileUploaded = (file: FileUploadResponse) => {
    resetImportFlowState({ preserveTargetSelection: true });
    uploadedFile.value = file;
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
      mappingRules.value = [];
      return;
    }

    loadingMappingRules.value = true;
    try {
      const res = await getEffectiveColumnMappingRules(
        selectedCustomerId.value
      );
      if (res.code === 0) {
        mappingRules.value = res.data || [];
        applyRulesToAll(false);
      } else {
        ElMessage.error(res.message || "加载列映射规则失败");
      }
    } catch {
      ElMessage.error("加载列映射规则失败");
    } finally {
      loadingMappingRules.value = false;
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
      cfg.previewData = data;
      applyRulesToConfig(cfg, false);
    }
  };

  const buildPreviewQuery = (cfg: TableImportConfig, previewRows: number) => ({
    previewRows,
    headerRowIndex: isExcelFile.value
      ? getExcelPreviewOptions(cfg).headerRowIndex
      : (cfg.wordMapping?.headerRowIndex ?? 0),
    headerRowCount: isExcelFile.value
      ? getExcelPreviewOptions(cfg).headerRowCount
      : 1,
    dataStartRowIndex: isExcelFile.value
      ? getExcelPreviewOptions(cfg).dataStartRowIndex
      : (cfg.wordMapping?.dataStartRowIndex ?? 1),
    dataEndRowIndex: isExcelFile.value
      ? getExcelPreviewOptions(cfg).dataEndRowIndex
      : undefined
  });

  const loadPreviewData = async (
    cfg: TableImportConfig,
    previewRows: number
  ): Promise<TableData> => {
    if (!uploadedFile.value) {
      throw new Error("源文件不存在，无法加载预览");
    }

    const res = await getTablePreview(
      uploadedFile.value.fileId,
      cfg.tableIndex,
      buildPreviewQuery(cfg, previewRows)
    );

    if (res.code !== 0 || !res.data) {
      throw new Error(res.message || "加载预览失败");
    }

    return res.data;
  };

  const ensurePreviewDataLoaded = async ({
    previewRows,
    initialText,
    completeText
  }: {
    previewRows: number;
    initialText: string;
    completeText?: string;
  }) => {
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
        smartStageText.value = buildDataImportPreviewStageText(
          index + 1,
          pendingConfigs.length,
          cfg.tableInfo?.name
        );
        cfg.previewData = await loadPreviewData(cfg, previewRows);
      }
      if (completeText) {
        smartStageText.value = completeText;
      }
      return true;
    } catch (error) {
      ElMessage.error(
        error instanceof Error ? error.message : "加载导入预览失败"
      );
      return false;
    }
  };

  const ensureFullPreviewDataLoaded = async () => {
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
      ElMessage.error(
        error instanceof Error ? error.message : "加载导入预览失败"
      );
      return false;
    } finally {
      loading.close();
    }
  };

  const {
    smartRecognizing,
    smartRecognitionError,
    smartConfirmingTableIndex,
    recognizedTables,
    smartStructureSummary,
    runSmartStructureRecognition,
    handleSmartStructureConfirm,
    handleSmartTableImportSelectionChange,
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
    ensurePreviewDataLoaded
  });

  const enterAdvancedMode = (
    target: "tableSelect" | "mapping" = "tableSelect"
  ) => {
    advancedMode.value = true;
    currentStep.value = getDataImportAdvancedStep(target);
  };

  const exitAdvancedMode = () => {
    advancedMode.value = false;
    currentStep.value = SMART_STEP_CONFIRM_PREVIEW;
  };

  const updateExcelMapping = (tableIndex: number, value: ExcelSheetMapping) => {
    const cfg = tableConfigs.value.find(c => c.tableIndex === tableIndex);
    if (!cfg) return;
    cfg.excelMapping = normalizeExcelMappingByTable(cfg.tableInfo, value);
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
        cfg.excelMapping = normalizeExcelMappingByTable(cfg.tableInfo, {
          ...mappingClipboard.value.value
        });
        pastedCount++;
        continue;
      }

      if (!isExcelFile.value && mappingClipboard.value.kind === "word") {
        cfg.wordMapping = { ...mappingClipboard.value.value };
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
      mappingRules.value.length === 0 &&
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
    if (
      advancedMode.value &&
      currentStep.value === 2 &&
      !isExcelFile.value &&
      !loadingMappingRules.value
    ) {
      loadMappingRules();
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
      nextMap[item.key] = differenceDecisionMap.value[item.key];
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
      nextMap[item.key] = decision;
    }

    differenceDecisionMap.value = nextMap;
  };

  // 重新开始
  const handleRestart = () => {
    resetImportFlowState();
    uploadedFile.value = null;
    importDuplicateAiConfig.value = createDefaultImportDuplicateAiConfig({
      embeddingServiceId: embeddingServices.value[0]?.id,
      llmServiceId: llmServices.value[0]?.id
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
      item => !differenceDecisionMap.value[item.key]
    ).length;
  });

  const pendingImportDecisionCount = computed(() => {
    return pendingDifferences.value.filter(
      item => differenceDecisionMap.value[item.key] === "import"
    ).length;
  });

  const pendingPartialDecisionCount = computed(() => {
    return pendingDifferences.value.filter(
      item => differenceDecisionMap.value[item.key] === "partial"
    ).length;
  });

  const pendingSkipDecisionCount = computed(() => {
    return pendingDifferences.value.filter(
      item => differenceDecisionMap.value[item.key] === "skip"
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
    previewSkippedRows,
    differenceDecisionMap,
    differenceConfirmDialogVisible,
    importDuplicateAiConfig,
    importProgressText,
    pendingDifferences,
    pendingUndecidedCount,
    pendingTableIndexes,
    previewDataCount,
    hasPendingDifferenceConfirmation,
    currentImportPermissionCode,
    currentImportPermissionMessage,
    getExcludedRowIndexes,
    resetPendingDifferenceState,
    syncDifferenceDecisionMap
  });

  const handleImport = async () => {
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
    activeTableIndex,
    tableConfigs,
    selectedCustomerId,
    selectedProcessId,
    selectedMachineModelId,
    importDuplicateAiConfig,
    steps,
    smartRecognizing,
    smartRecognitionError,
    smartStageText,
    selectedSmartTableIndexes,
    smartConfirmingTableIndex,
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
    previewSkippedRows,
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
    embeddingServices,
    llmServices,
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
    runSmartStructureRecognition,
    handleSmartStructureConfirm,
    handleSmartTableImportSelectionChange,
    enterAdvancedMode,
    exitAdvancedMode,
    applyRulesToAll,
    loadMappingRules,
    handleTablesSelected,
    handlePreviewLoaded,
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
