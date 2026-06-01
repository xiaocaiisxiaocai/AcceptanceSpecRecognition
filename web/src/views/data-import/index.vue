<script setup lang="ts">
import { ref, computed, watch, onActivated, onMounted, onUnmounted, nextTick } from "vue";
import { storeToRefs } from "pinia";
import { ElLoading, ElMessage } from "element-plus";
import TablePreview from "./components/TablePreview.vue";
import ColumnMapping from "./components/ColumnMapping.vue";
import DataImportDifferenceDialog from "./components/DataImportDifferenceDialog.vue";
import DataImportStepConfirm from "./components/DataImportStepConfirm.vue";
import DataImportStepMapping from "./components/DataImportStepMapping.vue";
import DataImportStepTableSelect from "./components/DataImportStepTableSelect.vue";
import DataImportStepTarget from "./components/DataImportStepTarget.vue";
import DataImportStepUpload from "./components/DataImportStepUpload.vue";
import ExcelColumnMapping, {
  type ExcelSheetMapping
} from "./components/ExcelColumnMapping.vue";
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
} from "./dataImport.helpers";
import {
  differenceColumnDefs,
  formatDifferenceValue,
  formatScorePercent,
  getDifferenceMatchTypeLabel,
  getDifferenceMatchTypeTagType,
  hasAiDifferenceMeta,
  isDifferenceColumnChanged
} from "./dataImport.difference-formatters";
import { applyWordRulesToWordMapping } from "@/views/shared/word-column-mapping-rules";
import type {
  DifferenceDecision,
  ImportPendingDifferenceWithTable,
  MappingClipboard,
  SkippedRowsGroup,
  TableImportConfig
} from "./dataImport.types";
import {
  createDefaultImportDuplicateAiConfig,
  useDataImportExecution
} from "./composables/useDataImportExecution";
import { useDataImportBatchExecution } from "./composables/useDataImportBatchExecution";
import { useDataImportMapping } from "./composables/useDataImportMapping";
import { useDataImportPermissions } from "./dataImport.permissions";
import { useDataImportPreviewSelection } from "./composables/useDataImportPreviewSelection";
import { useDataImportTarget } from "./composables/useDataImportTarget";
import { useDataImportStoreHook } from "@/store/modules/dataImport";
import {
  getFileTables,
  getTablePreview,
  type FileUploadResponse,
  type TableInfo,
  type TableData,
  type ColumnMapping as ColumnMappingType
} from "@/api/document";
import { hasPerms } from "@/utils/auth";

defineOptions({
  name: "ImportData"
});

const MAPPING_PREVIEW_ROWS = 50;

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

const steps = computed(() => [
  { title: "上传文件", description: isExcelFile.value ? "选择 Excel 文件" : "选择 Word/Excel 文件" },
  { title: isExcelFile.value ? "选择工作表" : "选择表格", description: "选择要导入的数据范围" },
  { title: "配置映射", description: isExcelFile.value ? "按列序号指定字段" : "设置列映射关系" },
  { title: "选择目标", description: "选择导入目标" },
  { title: "确认导入", description: "预览并确认" }
]);

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

// 让步骤条吸顶到实际滚动容器（pure-admin 使用 el-scrollbar）
const affixTarget = ref<string>("");
const affixOffset = ref<number>(0);

const refreshAffix = async () => {
  await nextTick();

  // fixedHeader=true 时：LayContent 内部有 .app-main .el-scrollbar__wrap
  const appMainWrap = document.querySelector(".app-main .el-scrollbar__wrap");
  if (appMainWrap) {
    affixTarget.value = ".app-main .el-scrollbar__wrap";
    // 关键：读取 app-main 的 padding-top（tabs/header 高度），让 affix 从一开始就“贴住”并且不盖住顶部栏
    const appMain = document.querySelector(".app-main") as HTMLElement | null;
    const pt = appMain ? parseInt(getComputedStyle(appMain).paddingTop || "0", 10) : 0;
    affixOffset.value = Number.isFinite(pt) && pt > 0 ? pt : 86;
    return;
  }

  // fixedHeader=false 时：Layout 外层有 .main-container .el-scrollbar__wrap
  const mainWrap = document.querySelector(".main-container .el-scrollbar__wrap");
  if (mainWrap) {
    affixTarget.value = ".main-container .el-scrollbar__wrap";
    // header 不固定时，Affix 贴在容器顶部即可
    affixOffset.value = 0;
    return;
  }

  // fallback：不设置 target，则 Affix 会绑定 window（但本项目通常不会走到这里）
  affixTarget.value = "";
  affixOffset.value = 0;
};

onMounted(() => {
  // 首次进入
  refreshAffix();
  // 某些情况下 layout/scroll 容器渲染更晚，做一次轻量重试
  setTimeout(refreshAffix, 50);
  setTimeout(refreshAffix, 200);
  loadAiServices();
});

onActivated(() => {
  // keep-alive 返回页面时，重新绑定一次
  refreshAffix();
  if (currentStep.value === 2 && !isExcelFile.value) {
    loadMappingRules();
  }
});

onUnmounted(() => {
  // Pinia 状态跨组件实例存在；卸载时重置，保持与原本页面本地状态一致。
  dataImportStore.resetAll();
});

// 计算属性
const canGoNext = computed(() => {
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
      return (
        selectedCustomerId.value !== undefined
      );
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

const {
  getExcelPreviewOptions,
  validateAllTableMappings
} = useDataImportMapping({
  isExcelFile,
  tableConfigs,
  excludedRowIndexMap
});

const nextDisabled = computed(() => {
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
    const res = await getEffectiveColumnMappingRules();
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
  if (activeTableIndex.value == null && selectedTableIndexes.value.length > 0) {
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
              ? { excelMapping: normalizeExcelMappingByTable(t, old.excelMapping) }
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
  importPreviewSelectionKeys.value = importPreviewSelectionKeys.value.filter(key => {
    const [tableIndex] = key.split(":");
    return activeIndexes.has(Number(tableIndex));
  });

  applyRulesToAll(false);
};

const removeSelectedTable = (tableIndex: number) => {
  if (tableConfigs.value.length <= 1) {
    ElMessage.warning(`请至少保留一个${isExcelFile.value ? "工作表" : "表格"}`);
    return;
  }

  // 从选择中移除
  selectedTableIndexes.value = selectedTableIndexes.value.filter(i => i !== tableIndex);
  selectedTables.value = selectedTables.value.filter(t => t.index !== tableIndex);
  tableConfigs.value = tableConfigs.value.filter(c => c.tableIndex !== tableIndex);
  setExcludedRowIndexes(tableIndex, []);
  importPreviewSelectionKeys.value = importPreviewSelectionKeys.value.filter(
    key => !key.startsWith(`${tableIndex}:`)
  );

  // 调整当前激活 tab
  if (activeTableIndex.value === tableIndex) {
    const nextIdx = selectedTableIndexes.value.length > 0 ? selectedTableIndexes.value[0] : null;
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
    ElMessage.warning(`请至少选择一个${isExcelFile.value ? "工作表" : "表格"}`);
    return;
  }

  activeTableIndex.value = tableConfigs.value[0]?.tableIndex ?? null;
  ElMessage.success(`已恢复${tableConfigs.value.length}个${isExcelFile.value ? "工作表" : "表格"}`);
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
  headerRowCount: isExcelFile.value ? getExcelPreviewOptions(cfg).headerRowCount : 1,
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

const ensureFullPreviewDataLoaded = async () => {
  const pendingConfigs = tableConfigs.value.filter(
    cfg => !cfg.previewData || cfg.previewData.rows.length < cfg.previewData.totalRows
  );

  if (pendingConfigs.length === 0) {
    return true;
  }

  const loading = ElLoading.service({
    lock: true,
    text: "正在生成导入预览..."
  });

  try {
    for (const cfg of pendingConfigs) {
      cfg.previewData = await loadPreviewData(cfg, 0);
    }
    return true;
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : "加载导入预览失败");
    return false;
  } finally {
    loading.close();
  }
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
    ElMessage.warning(`没有可粘贴的其他${isExcelFile.value ? "工作表" : "表格"}`);
    return;
  }

  ElMessage.success(
    `已应用到 ${pastedCount} 个其他${isExcelFile.value ? "工作表" : "表格"}`
  );
};

// 监听步骤变化
watch(currentStep, (step) => {
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

// 下一步
const ensureStepTwoSelection = async () => {
  if (selectedTableIndexes.value.length > 0 || selectedTables.value.length > 0 || tableConfigs.value.length > 0) {
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
      const more = v.missingByTable.length > 3 ? `（另有 ${v.missingByTable.length - 3} 个表格未完成映射）` : "";
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
  if (currentStep.value > 0) {
    currentStep.value--;
  }
};

const syncDifferenceDecisionMap = (items: ImportPendingDifferenceWithTable[]) => {
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
  return importPreviewGroups.value.reduce((sum, group) => sum + group.rows.length, 0);
});

const pendingDifferences = computed<ImportPendingDifferenceWithTable[]>(() => {
  return pendingImportAggregate.value?.pendingDifferences || [];
});

const pagedPendingDifferences = computed<ImportPendingDifferenceWithTable[]>(() => {
  const start = (pendingDifferencePage.value - 1) * pendingDifferencePageSize.value;
  return pendingDifferences.value.slice(
    start,
    start + pendingDifferencePageSize.value
  );
});

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
  return Array.from(new Set(pendingDifferences.value.map(item => item.tableIndex)));
});

const {
  openDifferenceConfirmDialog,
  handleConfirmPendingDifferences,
  handleImport,
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

const pendingDifferenceDisplayStart = computed(() => {
  if (pendingDifferences.value.length === 0) return 0;
  return (pendingDifferencePage.value - 1) * pendingDifferencePageSize.value + 1;
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
      Math.ceil(pendingDifferences.value.length / pendingDifferencePageSize.value)
    );
    if (pendingDifferencePage.value > maxPage) {
      pendingDifferencePage.value = maxPage;
    }
  },
  { immediate: true }
);

const skippedRowsGroups = computed<SkippedRowsGroup[]>(() =>
  buildSkippedRowsGroups(importResult.value?.skippedRows || [], tableConfigs.value)
);
</script>

<template>
  <div class="page data-import">
    <div class="page-header">
      <div>
        <div class="page-title">数据导入</div>
        <div class="page-subtitle">导入验收规格数据，支持 Word/Excel</div>
      </div>
    </div>
    <!-- 步骤条 -->
    <el-affix v-if="affixTarget" :offset="affixOffset" :target="affixTarget">
      <div class="steps-affix">
        <el-card class="steps-card">
          <el-steps :active="currentStep" finish-status="success">
            <el-step
              v-for="(step, index) in steps"
              :key="index"
              :title="step.title"
              :description="step.description"
            />
          </el-steps>
        </el-card>
      </div>
    </el-affix>
    <div v-else class="steps-affix">
      <el-card class="steps-card">
        <el-steps :active="currentStep" finish-status="success">
          <el-step
            v-for="(step, index) in steps"
            :key="index"
            :title="step.title"
            :description="step.description"
          />
        </el-steps>
      </el-card>
    </div>

    <div class="data-import-body">
      <!-- 步骤内容 -->
      <el-card class="step-content">
      <!-- 步骤1: 上传文件 -->
      <DataImportStepUpload
        v-show="currentStep === 0"
        v-model="uploadedFile"
        :can-upload-source-file="canUploadSourceFile"
        :can-import-any="canImportAny"
        :upload-accept="uploadAccept"
        :upload-blocked-message="uploadBlockedMessage"
        @uploaded="handleFileUploaded"
      />

      <!-- 步骤2: 选择表格 -->
      <DataImportStepTableSelect
        v-show="currentStep === 1"
        v-model="selectedTableIndexes"
        :uploaded-file="uploadedFile"
        :is-excel-file="isExcelFile"
        @selected-multiple="handleTablesSelected"
      />

      <!-- 步骤3: 配置映射 -->
        <DataImportStepMapping
          v-show="currentStep === 2"
          :is-excel-file="isExcelFile"
          :uploaded-file="uploadedFile"
          :table-configs="tableConfigs"
          :can-paste-clipboard="canPasteClipboard"
          :mapping-rules-count="mappingRules.length"
          :loading-mapping-rules="loadingMappingRules"
          :mapping-clipboard-source-index="mappingClipboardSourceIndex"
          :active-table-index="activeTableIndex"
          :get-excel-preview-options="getExcelPreviewOptions"
          @copy-mapping="copyActiveMappingConfig"
          @paste-mapping="pasteMappingConfigToOthers"
          @reload-rules="loadMappingRules"
          @reapply-rules="() => applyRulesToAll(true)"
          @update:active-table-index="value => (activeTableIndex = value)"
          @tab-remove="handleTabRemove"
          @restore-tables="restoreSelectedTablesForMapping"
          @go-prev="goPrev"
      >

        <el-tabs
          v-if="uploadedFile && tableConfigs.length > 0"
          v-model="activeTableIndex"
          type="border-card"
          :closable="tableConfigs.length > 1"
          @tab-remove="handleTabRemove"
        >
        <el-tab-pane
          v-for="cfg in tableConfigs"
          :key="cfg.tableIndex"
          :name="cfg.tableIndex"
          :label="getTableConfigTabLabel(cfg)"
          lazy
        >
            <!-- 表格预览 -->
            <div class="preview-section">
              <h4>{{ isExcelFile ? "工作表预览" : "表格预览" }}</h4>
              <TablePreview
                :file-id="uploadedFile.fileId"
                :table-index="cfg.tableIndex"
                :preview-rows="MAPPING_PREVIEW_ROWS"
                :header-row-index="
                  isExcelFile
                    ? getExcelPreviewOptions(cfg).headerRowIndex
                    : (cfg.wordMapping?.headerRowIndex ?? 0)
                "
                :header-row-count="
                  isExcelFile ? getExcelPreviewOptions(cfg).headerRowCount : 1
                "
                :data-start-row-index="
                  isExcelFile
                    ? getExcelPreviewOptions(cfg).dataStartRowIndex
                    : (cfg.wordMapping?.dataStartRowIndex ?? 1)
                "
                :data-end-row-index="
                  isExcelFile ? getExcelPreviewOptions(cfg).dataEndRowIndex : undefined
                "
                :mapping="isExcelFile ? undefined : cfg.wordMapping"
                @loaded="(data) => handlePreviewLoaded(cfg.tableIndex, data)"
              />
            </div>

            <!-- 列映射配置 -->
            <div class="mapping-section">
              <ExcelColumnMapping
                v-if="isExcelFile"
                :model-value="cfg.excelMapping"
                :used-range-start-row="cfg.tableInfo?.usedRangeStartRow"
                :used-range-end-row="
                  cfg.tableInfo
                    ? cfg.tableInfo.usedRangeStartRow + cfg.tableInfo.rowCount - 1
                    : undefined
                "
                :used-range-start-column="cfg.tableInfo?.usedRangeStartColumn"
                @update:model-value="(value) => updateExcelMapping(cfg.tableIndex, value)"
              />
              <ColumnMapping
                v-else
                :table-data="cfg.previewData"
                v-model="cfg.wordMapping"
              />
            </div>
          </el-tab-pane>
        </el-tabs>
      </DataImportStepMapping>

      <!-- 步骤4: 选择目标 -->
      <DataImportStepTarget
        v-show="currentStep === 3"
        :customers="customers"
        :processes="processes"
        :machine-models="machineModels"
        :selected-customer-id="selectedCustomerId"
        :selected-process-id="selectedProcessId"
        :selected-machine-model-id="selectedMachineModelId"
        :loading-customers="loadingCustomers"
        :loading-processes="loadingProcesses"
        :loading-machine-models="loadingMachineModels"
        @update:selected-customer-id="value => (selectedCustomerId = value)"
        @update:selected-process-id="value => (selectedProcessId = value)"
        @update:selected-machine-model-id="value => (selectedMachineModelId = value)"
      />

      <!-- 步骤5: 确认导入 -->
      <DataImportStepConfirm v-show="currentStep === 4" :import-result="importResult">

        <!-- 导入结果 -->
        <div v-if="importResult" class="import-result">
          <el-result
            :icon="importResult.failedCount === 0 ? 'success' : 'warning'"
            :title="importResult.failedCount === 0 ? '导入成功' : '导入完成'"
          >
            <template #sub-title>
              <div class="result-stats">
                <div class="stat-item success">
                  <span class="stat-value">{{ importResult.successCount }}</span>
                  <span class="stat-label">成功</span>
                </div>
                <div class="stat-item warning">
                  <span class="stat-value">{{ importResult.skippedCount }}</span>
                  <span class="stat-label">跳过</span>
                </div>
                <div class="stat-item danger">
                  <span class="stat-value">{{ importResult.failedCount }}</span>
                  <span class="stat-label">失败</span>
                </div>
              </div>
            </template>
            <template #extra>
              <el-button
                v-if="canUploadSourceFile && canImportAny"
                type="primary"
                @click="handleRestart"
              >
                继续导入
              </el-button>
            </template>
          </el-result>

          <!-- 错误详情 -->
          <div v-if="importResult.errors.length > 0" class="error-list">
            <h4>错误详情</h4>
            <el-table :data="importResult.errors" max-height="200" size="small">
              <el-table-column prop="tableIndex" label="表格" width="80">
                <template #default="{ row }">
                  {{ row.tableIndex + 1 }}
                </template>
              </el-table-column>
              <el-table-column prop="rowIndex" label="行号" width="80">
                <template #default="{ row }">
                  {{ row.rowIndex + 1 }}
                </template>
              </el-table-column>
              <el-table-column prop="message" label="错误信息" />
            </el-table>
          </div>

          <div v-if="importResult.skippedCount > 0" class="error-list">
            <h4>未导入（跳过）详情</h4>
            <el-alert
              v-if="!importResult.skippedRows.length"
              type="info"
              :closable="false"
              show-icon
              title="已跳过部分数据（未开启明细预览）"
              description="如需查看具体哪些行被跳过，请在导入前开启“预览未导入明细”。"
            />
            <div v-else>
              <div
                v-for="group in skippedRowsGroups"
                :key="`skip-group-${group.tableIndex}`"
                class="skipped-group"
              >
                <div v-if="skippedRowsGroups.length > 1" class="skipped-group-title">
                  表格 {{ group.tableIndex + 1 }}
                </div>
                <el-table :data="group.rows" max-height="220" size="small">
                  <el-table-column prop="tableIndex" label="表格" width="80">
                    <template #default="{ row }">
                      {{ row.tableIndex + 1 }}
                    </template>
                  </el-table-column>
                  <el-table-column prop="rowIndex" label="行号" width="100" />
                  <el-table-column
                    prop="message"
                    label="跳过原因"
                    min-width="220"
                    show-overflow-tooltip
                  />
                  <el-table-column
                    v-for="col in group.columns"
                    :key="`skip-col-${group.tableIndex}-${col.index}`"
                    :label="col.label"
                    min-width="140"
                  >
                    <template #default="{ row }">
                      <div class="skipped-cell-value">{{ row.rowValues?.[col.index] || "" }}</div>
                    </template>
                  </el-table-column>
                </el-table>
              </div>
            </div>
          </div>
        </div>

        <!-- 导入确认 -->
        <div v-else class="import-confirm">
          <div v-if="hasPendingDifferenceConfirmation" class="difference-entry">
            <el-alert
              type="warning"
              :closable="false"
              show-icon
              :title="`检测到 ${pendingDifferences.length} 条重复、差异或 AI 疑似重复数据，请在弹窗中逐条确认是否覆盖已有记录。`"
              description="左侧为数据库已有数据，右侧为本次待导入数据。未命中的数据已按当前流程处理。"
            />
            <div class="difference-entry__actions">
              <span v-if="hasCommittedImportProgress" class="difference-entry__summary">
                已完成无重复数据处理：成功 {{ committedImportAggregate?.successCount || 0 }} 条，跳过
                {{ committedImportAggregate?.skippedCount || 0 }} 条，失败
                {{ committedImportAggregate?.failedCount || 0 }} 条
              </span>
              <el-button type="warning" @click="openDifferenceConfirmDialog">
                打开重复确认弹窗
              </el-button>
            </div>
          </div>

          <el-alert
            v-if="!canImportCurrentFile"
            type="warning"
            :closable="false"
            show-icon
            :title="currentImportPermissionMessage"
            class="mb-4"
          />
          <el-descriptions class="import-confirm-desc" :column="3" border size="small">
            <el-descriptions-item label="源文件" :span="2">
              {{ uploadedFile?.fileName }}
            </el-descriptions-item>
            <el-descriptions-item label="表格">
              共 {{ tableConfigs.length }} 个（{{
                tableConfigs.map(t => t.tableIndex + 1).join("、")
              }}）
            </el-descriptions-item>
            <el-descriptions-item label="目标客户">
              {{ customers.find((c) => c.id === selectedCustomerId)?.name }}
            </el-descriptions-item>
            <el-descriptions-item label="目标制程">
              {{ processes.find((p) => p.id === selectedProcessId)?.name || "-" }}
            </el-descriptions-item>
              <el-descriptions-item label="目标机型">
                {{ selectedMachineModelName }}
              </el-descriptions-item>
            <el-descriptions-item label="预计导入">
              {{ previewDataCount }} 条数据
            </el-descriptions-item>
          </el-descriptions>

          <div class="duplicate-ai-panel">
            <div class="duplicate-ai-panel__header">
              <div>
                <div class="duplicate-ai-panel__title">AI 疑似重复识别</div>
                <div class="duplicate-ai-panel__desc">
                  规则命中优先；未命中时再用 Embedding 召回候选，并可选用 LLM 复核。
                </div>
              </div>
              <el-switch
                v-model="importDuplicateAiConfig.enableSemanticDuplicateCheck"
                active-text="开启"
                inactive-text="关闭"
              />
            </div>
            <el-form label-width="132px" class="duplicate-ai-form">
              <el-row :gutter="16">
                <el-col :span="12">
                  <el-form-item label="Embedding 服务">
                    <el-select
                      v-model="importDuplicateAiConfig.embeddingServiceId"
                      placeholder="请选择 Embedding 服务"
                      :disabled="!importDuplicateAiConfig.enableSemanticDuplicateCheck"
                      :loading="loadingAiServices"
                      style="width: 100%"
                      filterable
                      clearable
                      :teleported="true"
                      popper-class="app-select-popper"
                    >
                      <el-option
                        v-for="service in embeddingServices"
                        :key="service.id"
                        :label="`${service.name}（${service.embeddingModel || '-'}）`"
                        :value="service.id"
                      />
                    </el-select>
                  </el-form-item>
                </el-col>
                <el-col :span="12">
                  <el-form-item label="候选数量 TopK">
                    <el-input-number
                      v-model="importDuplicateAiConfig.semanticTopK"
                      :min="1"
                      :max="10"
                      :disabled="!importDuplicateAiConfig.enableSemanticDuplicateCheck"
                    />
                  </el-form-item>
                </el-col>
                <el-col :span="12">
                  <el-form-item label="召回阈值">
                    <el-slider
                      v-model="importDuplicateAiConfig.semanticMinScore"
                      :min="0"
                      :max="1"
                      :step="0.01"
                      :disabled="!importDuplicateAiConfig.enableSemanticDuplicateCheck"
                      :format-tooltip="(val: number) => `${(val * 100).toFixed(0)}%`"
                      show-input
                      :show-input-controls="false"
                    />
                  </el-form-item>
                </el-col>
                <el-col :span="12">
                  <el-form-item label="高置信阈值">
                    <el-slider
                      v-model="importDuplicateAiConfig.highConfidenceThreshold"
                      :min="0.5"
                      :max="1"
                      :step="0.01"
                      :disabled="!importDuplicateAiConfig.enableSemanticDuplicateCheck"
                      :format-tooltip="(val: number) => `${(val * 100).toFixed(0)}%`"
                      show-input
                      :show-input-controls="false"
                    />
                  </el-form-item>
                </el-col>
              </el-row>
              <div class="duplicate-ai-panel__llm">
                <div class="llm-toggle">
                  <span>启用 LLM 复核</span>
                  <el-switch
                    v-model="importDuplicateAiConfig.enableLlmDuplicateReview"
                    :disabled="!importDuplicateAiConfig.enableSemanticDuplicateCheck"
                  />
                </div>
                <el-row :gutter="16">
                  <el-col :span="12">
                    <el-form-item label="LLM 服务">
                      <el-select
                        v-model="importDuplicateAiConfig.llmServiceId"
                        placeholder="请选择 LLM 服务"
                        :disabled="
                          !importDuplicateAiConfig.enableSemanticDuplicateCheck ||
                          !importDuplicateAiConfig.enableLlmDuplicateReview
                        "
                        :loading="loadingAiServices"
                        style="width: 100%"
                        filterable
                        clearable
                        :teleported="true"
                        popper-class="app-select-popper"
                      >
                        <el-option
                          v-for="service in llmServices"
                          :key="service.id"
                          :label="`${service.name}（${service.llmModel || '-'}）`"
                          :value="service.id"
                        />
                      </el-select>
                    </el-form-item>
                  </el-col>
                  <el-col :span="12">
                    <el-form-item label="LLM 通过阈值">
                      <el-slider
                        v-model="importDuplicateAiConfig.llmPassScore"
                        :min="0"
                        :max="1"
                        :step="0.01"
                        :disabled="
                          !importDuplicateAiConfig.enableSemanticDuplicateCheck ||
                          !importDuplicateAiConfig.enableLlmDuplicateReview
                        "
                        :format-tooltip="(val: number) => `${(val * 100).toFixed(0)}%`"
                        show-input
                        :show-input-controls="false"
                      />
                    </el-form-item>
                  </el-col>
                </el-row>
              </div>
            </el-form>
          </div>

          <div class="import-preview-panel">
            <div class="import-preview-toolbar">
              <div class="import-preview-summary">
                <span class="summary-title">待导入数据清单</span>
                <span class="summary-meta">当前保留 {{ previewDataCount }} 条</span>
                <span v-if="removedPreviewRowCount > 0" class="summary-meta warning">
                  已剔除 {{ removedPreviewRowCount }} 条
                </span>
              </div>
              <div class="import-preview-actions">
                <el-button
                  size="small"
                  type="danger"
                  plain
                  :disabled="hasPendingDifferenceConfirmation || selectedImportPreviewRowsCount === 0"
                  @click="handleRemoveSelectedPreviewRows"
                >
                  批量删除（{{ selectedImportPreviewRowsCount }}）
                </el-button>
                <el-button
                  size="small"
                  :disabled="hasPendingDifferenceConfirmation || removedPreviewRowCount === 0"
                  @click="handleRestoreRemovedPreviewRows"
                >
                  恢复已删除
                </el-button>
              </div>
            </div>

            <el-alert
              type="info"
              :closable="false"
              show-icon
              title="这里删除的是本次待导入清单，删除后只是不参与本次导入，不会修改原文件。"
            />

            <div v-if="previewDataCount > 0" class="import-preview-groups">
              <div
                v-for="group in importPreviewGroups"
                :key="`import-preview-${group.tableIndex}`"
                class="import-preview-group"
              >
                <div class="import-preview-group__header">
                  <span>{{ group.label }}</span>
                  <span class="group-count">保留 {{ group.rows.length }} 条</span>
                </div>
                <el-table
                  :data="group.rows"
                  border
                  size="small"
                  max-height="280"
                  row-key="key"
                  reserve-selection
                  @selection-change="rows => handleImportPreviewSelectionChange(group.tableIndex, rows)"
                >
                  <el-table-column type="selection" width="48" />
                  <el-table-column prop="displayRowNumber" label="行号" width="80" />
                  <el-table-column prop="project" label="项目" min-width="140" show-overflow-tooltip>
                    <template #default="{ row }">
                      {{ row.project || "-" }}
                    </template>
                  </el-table-column>
                  <el-table-column
                    prop="specification"
                    label="规格"
                    min-width="260"
                    show-overflow-tooltip
                  >
                    <template #default="{ row }">
                      {{ row.specification || "-" }}
                    </template>
                  </el-table-column>
                  <el-table-column
                    prop="acceptance"
                    label="验收"
                    min-width="160"
                    show-overflow-tooltip
                  >
                    <template #default="{ row }">
                      {{ row.acceptance || "-" }}
                    </template>
                  </el-table-column>
                  <el-table-column prop="remark" label="备注" min-width="160" show-overflow-tooltip>
                    <template #default="{ row }">
                      {{ row.remark || "-" }}
                    </template>
                  </el-table-column>
                  <el-table-column label="操作" width="100" fixed="right">
                    <template #default="{ row }">
                      <el-button
                        type="danger"
                        link
                        :disabled="hasPendingDifferenceConfirmation"
                        @click="handleRemoveSinglePreviewRow(row)"
                      >
                        删除
                      </el-button>
                    </template>
                  </el-table-column>
                </el-table>
              </div>
            </div>
            <el-empty
              v-else
              description="当前没有待导入数据，可恢复已删除数据或返回上一步调整配置。"
            />
          </div>

          <div class="import-actions">
            <div class="skip-preview-switch">
              <span class="label">预览未导入明细</span>
              <el-switch
                v-model="previewSkippedRows"
                :disabled="importing || hasPendingDifferenceConfirmation"
                active-text="开启"
                inactive-text="关闭"
              />
            </div>
            <div v-if="importing" class="import-progress-panel">
              <div class="import-progress-panel__title">{{ importProgressText }}</div>
              <div class="import-progress-panel__desc">{{ importProgressDescription }}</div>
            </div>
            <el-button
              v-if="canImportCurrentFile"
              type="primary"
              size="large"
              :loading="importing"
              :disabled="!hasPendingDifferenceConfirmation && previewDataCount === 0"
              @click="handleImport"
            >
              {{ importPrimaryButtonText }}
            </el-button>
          </div>
        </div>
      </DataImportStepConfirm>

      <!-- 步骤按钮 -->
      <div class="step-actions">
        <el-button
          v-if="currentStep > 0 && !importResult && !hasPendingDifferenceConfirmation"
          @click="goPrev"
        >
          上一步
        </el-button>
        <el-button
          v-if="currentStep < steps.length - 1"
          type="primary"
          :disabled="nextDisabled"
          @click="goNext"
        >
          下一步
        </el-button>
      </div>

      <DataImportDifferenceDialog
        v-model="differenceConfirmDialogVisible"
      >
        <div class="difference-dialog__summary">
          <el-alert
            type="warning"
            :closable="false"
            show-icon
            :title="`检测到 ${pendingDifferences.length} 条重复、差异或 AI 疑似重复数据，请逐条确认是否覆盖已有记录。`"
            description="左侧为数据库已有数据，右侧为本次待导入数据。选择“部分覆盖”时，仅更新验收和备注，不改项目和规格。"
          />
          <div class="difference-dialog__toolbar">
            <div class="difference-dialog__stats">
              <span>覆盖 {{ pendingImportDecisionCount }} 条</span>
              <span>部分覆盖 {{ pendingPartialDecisionCount }} 条</span>
              <span>跳过 {{ pendingSkipDecisionCount }} 条</span>
              <span>未确认 {{ pendingUndecidedCount }} 条</span>
            </div>
            <div class="difference-dialog__batch-actions">
              <el-button size="small" @click="applyDifferenceDecisionToAll('skip')">
                全部跳过
              </el-button>
              <el-button size="small" type="primary" plain @click="applyDifferenceDecisionToAll('partial')">
                全部部分覆盖
              </el-button>
              <el-button size="small" type="warning" plain @click="applyDifferenceDecisionToAll('import')">
                全部覆盖
              </el-button>
            </div>
          </div>
        </div>

        <div class="difference-dialog__list">
          <div
            v-for="item in pagedPendingDifferences"
            :key="item.key"
            class="difference-card"
          >
            <div class="difference-card__header">
              <div class="difference-card__meta">
                <span>表格 {{ item.tableIndex + 1 }}</span>
                <span>行号 {{ item.rowIndex }}</span>
                <el-tag
                  :type="getDifferenceMatchTypeTagType(item.matchType)"
                  effect="light"
                  size="small"
                >
                  {{ getDifferenceMatchTypeLabel(item.matchType) }}
                </el-tag>
                <el-tag
                  v-if="item.isHighConfidence"
                  type="success"
                  effect="plain"
                  size="small"
                >
                  高置信
                </el-tag>
              </div>
              <el-tag
                v-if="differenceDecisionMap[item.key] === 'import'"
                type="warning"
                size="small"
              >
                已选择覆盖
              </el-tag>
              <el-tag
                v-else-if="differenceDecisionMap[item.key] === 'partial'"
                type="primary"
                size="small"
              >
                已选择部分覆盖
              </el-tag>
              <el-tag
                v-else-if="differenceDecisionMap[item.key] === 'skip'"
                type="info"
                size="small"
              >
                已选择跳过
              </el-tag>
              <el-tag v-else type="danger" size="small">
                待选择
              </el-tag>
            </div>

            <div class="difference-card__content">
              <div v-if="hasAiDifferenceMeta(item)" class="difference-card__ai-meta">
                <span v-if="item.embeddingScore !== undefined">
                  Embedding：{{ formatScorePercent(item.embeddingScore) }}
                </span>
                <span v-if="item.llmScore !== undefined">
                  LLM：{{ formatScorePercent(item.llmScore) }}
                </span>
                <span v-if="item.finalScore !== undefined">
                  最终：{{ formatScorePercent(item.finalScore) }}
                </span>
              </div>
              <div v-if="item.reviewReason" class="difference-card__reason">
                {{ item.reviewReason }}
              </div>
              <div class="difference-sheet">
                <div class="difference-sheet__panel">
                  <div class="difference-sheet__panel-title">数据库已有</div>
                  <div class="difference-sheet__table">
                    <div
                      v-for="column in differenceColumnDefs"
                      :key="`existing-head-${item.key}-${column.key}`"
                      class="difference-sheet__head"
                    >
                      {{ column.label }}
                    </div>
                    <div
                      v-for="column in differenceColumnDefs"
                      :key="`existing-cell-${item.key}-${column.key}`"
                      class="difference-sheet__cell"
                      :class="{ 'is-changed': isDifferenceColumnChanged(item, column) }"
                    >
                      {{ formatDifferenceValue(column.getExisting(item)) }}
                    </div>
                  </div>
                </div>

                <div class="difference-sheet__panel difference-sheet__panel--incoming">
                  <div class="difference-sheet__panel-title">本次导入</div>
                  <div class="difference-sheet__table">
                    <div
                      v-for="column in differenceColumnDefs"
                      :key="`incoming-head-${item.key}-${column.key}`"
                      class="difference-sheet__head"
                    >
                      {{ column.label }}
                    </div>
                    <div
                      v-for="column in differenceColumnDefs"
                      :key="`incoming-cell-${item.key}-${column.key}`"
                      class="difference-sheet__cell"
                      :class="{ 'is-changed': isDifferenceColumnChanged(item, column) }"
                    >
                      {{ formatDifferenceValue(column.getIncoming(item)) }}
                    </div>
                  </div>
                </div>
              </div>
            </div>

            <div class="difference-card__footer">
              <el-radio-group v-model="differenceDecisionMap[item.key]" size="small">
                <el-radio-button label="import">覆盖已有</el-radio-button>
                <el-radio-button label="partial">部分覆盖</el-radio-button>
                <el-radio-button label="skip">跳过</el-radio-button>
              </el-radio-group>
            </div>
          </div>
        </div>

        <div v-if="pendingDifferences.length > 0" class="difference-dialog__pagination">
          <span class="difference-dialog__pagination-summary">
            当前显示 {{ pendingDifferenceDisplayStart }} - {{ pendingDifferenceDisplayEnd }} 条，共
            {{ pendingDifferences.length }} 条
          </span>
          <el-pagination
            v-model:current-page="pendingDifferencePage"
            v-model:page-size="pendingDifferencePageSize"
            background
            small
            layout="total, sizes, prev, pager, next"
            :page-sizes="[20, 50, 100]"
            :total="pendingDifferences.length"
          />
        </div>

        <template #footer>
          <div class="difference-dialog__footer">
            <span class="difference-dialog__footer-tip">
              {{ differenceDialogFooterTip }}
            </span>
            <div class="difference-dialog__footer-actions">
              <el-button @click="differenceConfirmDialogVisible = false">
                稍后处理
              </el-button>
              <el-button
                type="primary"
                :loading="importing"
                :disabled="pendingUndecidedCount > 0"
                @click="handleConfirmPendingDifferences"
              >
                {{ confirmDifferenceButtonText }}
              </el-button>
            </div>
          </div>
        </template>
      </DataImportDifferenceDialog>
      </el-card>
    </div>
  </div>
</template>

<style scoped>
.data-import {
  padding: 24px;
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.steps-affix {
  width: 100%;
  background: var(--el-bg-color);
  /* 不要顶部 padding，否则 Affix 需要先滚动一段才会触发“固定” */
  padding: 0 0 16px;
  /* 让固定时更有层次感，避免和内容“糊在一起” */
  border-bottom: 1px solid var(--el-border-color-lighter);
  /* 防止底下内容滚动时“透出来” */
  box-shadow: 0 2px 8px rgb(0 0 0 / 6%);
  z-index: 900;
}

.data-import-body {
  padding: 0;
  /* 给固定底部操作栏预留空间，避免遮挡内容 */
  padding-bottom: 84px;
  padding-top: 4px;
}

.steps-card {
  margin-bottom: 0;
}

.step-content {
  min-height: 500px;
}

.step-panel {
  padding: 20px 0;
}

.step-title {
  font-size: 18px;
  font-weight: 600;
  color: var(--color-text);
  margin-bottom: 8px;
}

.step-desc {
  font-size: 14px;
  color: #6b7280;
  margin-bottom: 24px;
}

.preview-section {
  margin-bottom: 24px;
}

.preview-section h4,
.error-list h4 {
  font-size: 14px;
  font-weight: 500;
  color: #4b5563;
  margin-bottom: 12px;
}

.mapping-section {
  margin-top: 24px;
}

.mapping-quick-actions {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 12px;
}

.mapping-empty-state {
  padding: 32px 0;
}

.mapping-clipboard-tip {
  font-size: 12px;
  color: #6b7280;
}

.target-form {
  max-width: 500px;
}

.w-full {
  width: 100%;
}

.import-confirm {
  width: 100%;
  max-width: 900px;
  margin: 0 auto;
}

.difference-entry {
  margin-bottom: 16px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.difference-entry__actions {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
}

.difference-entry__summary {
  font-size: 13px;
  color: #8a5a00;
}

.import-confirm-desc {
  width: 100%;
}

.import-confirm-desc :deep(.el-descriptions__cell) {
  padding: 6px 10px;
}

.import-confirm-desc :deep(.el-descriptions__label) {
  width: 80px;
  color: #6b7280;
}

.duplicate-ai-panel {
  margin-top: 16px;
  border: 1px solid #dbe7f8;
  border-radius: 12px;
  padding: 16px;
  background: linear-gradient(180deg, #f7fbff 0%, #fff 100%);
}

.duplicate-ai-panel__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  margin-bottom: 12px;
  flex-wrap: wrap;
}

.duplicate-ai-panel__title {
  font-size: 15px;
  font-weight: 600;
  color: #1f2937;
}

.duplicate-ai-panel__desc {
  margin-top: 4px;
  font-size: 12px;
  color: #6b7280;
  line-height: 1.6;
}

.duplicate-ai-form :deep(.el-form-item) {
  margin-bottom: 14px;
}

.duplicate-ai-panel__llm {
  margin-top: 8px;
  padding-top: 12px;
  border-top: 1px dashed #dbe7f8;
}

.llm-toggle {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 12px;
  font-size: 13px;
  color: #4b5563;
}

.import-preview-panel {
  margin-top: 20px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.import-preview-toolbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
}

.import-preview-summary {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
}

.summary-title {
  font-size: 14px;
  font-weight: 600;
  color: #374151;
}

.summary-meta {
  font-size: 13px;
  color: #6b7280;
}

.summary-meta.warning {
  color: #e67e22;
}

.import-preview-actions {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
}

.import-preview-groups {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.import-preview-group {
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 8px;
  padding: 12px;
  background: #fff;
}

.import-preview-group__header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 10px;
  font-size: 13px;
  font-weight: 600;
  color: #374151;
}

.group-count {
  color: #6b7280;
  font-weight: 500;
}

.import-actions {
  margin-top: 20px;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 12px;
}

.import-progress-panel {
  width: 100%;
  max-width: 720px;
  border: 1px solid #dbe7f8;
  border-radius: 12px;
  padding: 14px 16px;
  background: linear-gradient(180deg, #f7fbff 0%, #fff 100%);
}

.import-progress-panel__title {
  font-size: 14px;
  font-weight: 600;
  color: #1d4ed8;
}

.import-progress-panel__desc {
  margin-top: 6px;
  font-size: 12px;
  line-height: 1.6;
  color: #64748b;
}

.skip-preview-switch {
  display: flex;
  align-items: center;
  gap: 10px;
  color: #4b5563;
  font-size: 13px;
}

.skip-preview-switch .label {
  font-weight: 500;
}

.import-result {
  width: 100%;
  max-width: 1200px;
  margin: 0 auto;
}

.result-stats {
  display: flex;
  justify-content: center;
  gap: 48px;
  margin-top: 16px;
}

.stat-item {
  text-align: center;
}

.stat-value {
  display: block;
  font-size: 32px;
  font-weight: 600;
}

.stat-label {
  display: block;
  font-size: 14px;
  color: #6b7280;
  margin-top: 4px;
}

.stat-item.success .stat-value {
  color: #67c23a;
}

.stat-item.warning .stat-value {
  color: #e6a23c;
}

.stat-item.danger .stat-value {
  color: #f56c6c;
}

.error-list {
  margin-top: 24px;
}

.skipped-group + .skipped-group {
  margin-top: 12px;
}

.skipped-group-title {
  margin-bottom: 8px;
  font-size: 12px;
  color: #6b7280;
}

.skipped-cell-value {
  white-space: pre-wrap;
  word-break: break-word;
  color: #4b5563;
  line-height: 1.5;
  font-size: 12px;
}

.difference-dialog__summary {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.difference-dialog__toolbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
}

.difference-dialog__stats {
  display: flex;
  gap: 12px;
  flex-wrap: wrap;
  font-size: 13px;
  color: #6b7280;
}

.difference-dialog__batch-actions {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
}

.difference-dialog__list {
  max-height: calc(100vh - 320px);
  margin-top: 16px;
  overflow: auto;
  padding-right: 4px;
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.difference-dialog__pagination {
  margin-top: 16px;
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
}

.difference-dialog__pagination-summary {
  font-size: 13px;
  color: #6b7280;
}

.difference-card {
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 10px;
  background: #fff;
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.difference-card__header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
}

.difference-card__meta {
  display: flex;
  gap: 12px;
  flex-wrap: wrap;
  font-size: 13px;
  color: #4b5563;
}

.difference-card__content {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.difference-card__ai-meta {
  display: flex;
  gap: 12px;
  flex-wrap: wrap;
  font-size: 12px;
  color: #2563eb;
}

.difference-card__reason {
  padding: 10px 12px;
  border-radius: 8px;
  background: #f8fafc;
  font-size: 12px;
  line-height: 1.6;
  color: #475569;
}

.difference-sheet {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 16px;
}

.difference-sheet__panel {
  border: 1px solid #e5e7eb;
  border-radius: 10px;
  overflow: hidden;
  background: #fff;
}

.difference-sheet__panel--incoming {
  border-color: #f3d19e;
}

.difference-sheet__panel-title {
  padding: 10px 12px;
  font-size: 14px;
  font-weight: 600;
  color: #374151;
  background: #f8fafc;
}

.difference-sheet__panel--incoming .difference-sheet__panel-title {
  background: #fff8eb;
}

.difference-sheet__table {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  border-top: 1px solid #e5e7eb;
}

.difference-sheet__head {
  padding: 10px 12px;
  font-size: 12px;
  font-weight: 600;
  color: #6b7280;
  background: #f9fafb;
  border-right: 1px solid #e5e7eb;
}

.difference-sheet__head:nth-child(4n) {
  border-right: none;
}

.difference-sheet__cell {
  min-height: 88px;
  padding: 12px;
  font-size: 13px;
  color: #1f2937;
  line-height: 1.6;
  white-space: pre-wrap;
  word-break: break-word;
  background: #fff;
  border-top: 1px solid #e5e7eb;
  border-right: 1px solid #e5e7eb;
}

.difference-sheet__cell:nth-child(8n) {
  border-right: none;
}

.difference-sheet__cell.is-changed {
  background: #fff4db;
  color: #8a5a00;
}

.difference-sheet__panel--incoming .difference-sheet__cell.is-changed {
  background: #ffe9bf;
}

.difference-card__footer {
  display: flex;
  justify-content: flex-end;
}

.difference-dialog__footer {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 16px;
  flex-wrap: wrap;
}

.difference-dialog__footer-tip {
  font-size: 13px;
  color: #6b7280;
}

.difference-dialog__footer-actions {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
}

@media (width <= 900px) {
  .difference-sheet {
    grid-template-columns: 1fr;
  }

  .difference-sheet__table {
    grid-template-columns: 1fr;
  }

  .difference-sheet__head {
    border-right: none;
    border-bottom: 1px solid #e5e7eb;
  }

  .difference-sheet__cell {
    min-height: auto;
    border-right: none;
  }
}

.step-actions {
  position: fixed;
  left: 0;
  right: 0;
  bottom: 0;
  z-index: 1000;
  margin-top: 0;
  padding: 12px 0;
  border-top: 1px solid #e4e7ed;
  background: var(--el-bg-color);
  display: flex;
  justify-content: center;
  gap: 16px;
}
</style>
