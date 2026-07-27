<script setup lang="ts">
import {
  ref,
  computed,
  nextTick,
  onActivated,
  onBeforeUnmount,
  onDeactivated,
  watch
} from "vue";
import { useEventListener } from "@vueuse/core";
import { ElMessage, type FormInstance, type FormRules } from "element-plus";
import ScoreDetailDialog from "./components/ScoreDetailDialog.vue";
import SmartFillBackfillDialog from "./components/SmartFillBackfillDialog.vue";
import SmartStructureFieldConflictDialog from "@/views/shared/SmartStructureFieldConflictDialog.vue";
import SmartFillMatchStep from "./components/SmartFillMatchStep.vue";
import SmartFillPreviewStep from "./components/SmartFillPreviewStep.vue";
import SmartFillSteps from "./components/SmartFillSteps.vue";
import SmartFillTableStep from "./components/SmartFillTableStep.vue";
import SmartFillUploadStep from "./components/SmartFillUploadStep.vue";
import SmartStructureConfirmTabs from "@/views/shared/SmartStructureConfirmTabs.vue";
import SmartStructureSummaryBanner from "@/views/shared/SmartStructureSummaryBanner.vue";
import SmartStructureAiAssistControl from "@/views/shared/SmartStructureAiAssistControl.vue";
import type { BatchTableConfigItem } from "./components/batchTableConfig.types";
import {
  type MatchPreviewItem,
  type MatchConfig as MatchConfigType,
  type MatchResult,
  type BatchTablePreviewResult,
  batchPreviewMatch,
  defaultMatchConfig
} from "@/api/matching";
import type { FileUploadResponse, TableInfo } from "@/api/document";
import type { ColumnMappingRule } from "@/api/column-mapping-rules";
import type {
  SmartConfigConfirmRequest,
  SmartConfigRecognizedTable
} from "@/api/smart-config";
import { getCustomerList, type Customer } from "@/api/customer";
import { getProcessList, type Process } from "@/api/process";
import { getMachineModelList, type MachineModel } from "@/api/machine-model";
import { hasPerms } from "@/utils/auth";
import {
  getRequestErrorMessage,
  isGloballyHandledAuthError
} from "@/utils/error-message";
import {
  createPagedOptionsRequestGate,
  loadAllPagedItems
} from "@/utils/paged-options";
import { ensurePermission } from "@/utils/permission-guard";
import { useSmartFillPreviewProgress } from "./composables/useSmartFillPreviewProgress";
import { useSmartFillBackfillState } from "./composables/useSmartFillBackfillState";
import { useSmartFillPreviewBlocking } from "./composables/useSmartFillPreviewBlocking";
import {
  useSmartFillLlmStream,
  createMatchLlmStreamRequest,
  requestMatchLlmStream
} from "./composables/useSmartFillLlmStream";
import { useSmartFillPreviewRequest } from "./composables/useSmartFillPreviewRequest";
import { useSmartFillExecution } from "./composables/useSmartFillExecution";
import { useSmartFillUploadedTables } from "./composables/useSmartFillUploadedTables";
import { useSmartFillActivation } from "./composables/useSmartFillActivation";
import { useSmartStructureRecognition } from "@/views/shared/useSmartStructureRecognition";
import {
  applySmartConfigConfirmRequestToTable,
  getSmartStructureImportSelectionDisabledReason,
  shouldShowSmartStructureManualFallback
} from "@/views/shared/smart-structure-recognition";
import {
  buildSmartFillConfigsFromRecognizedTables,
  createSmartFillSmartSteps,
  getSmartFillPrevStepState,
  SMART_FILL_ADVANCED_STEP_MATCH_CONFIG,
  SMART_FILL_ADVANCED_STEP_PREVIEW,
  SMART_FILL_ADVANCED_STEP_TABLE_CONFIG,
  SMART_FILL_STEP_MATCH_CONFIG,
  SMART_FILL_STEP_PREVIEW,
  SMART_FILL_STEP_RECOGNITION_REVIEW,
  SMART_FILL_STEP_UPLOAD_SCOPE,
  syncSmartFillDraftConfig,
  syncSmartFillConfigsToRecognizedTables
} from "./smartFill.smartRecognition";
import { runSmartFillConfirmSelection } from "./smartFill.confirmSelection";
import {
  applySmartStructureFieldSelectionsToDraft,
  applySmartStructureFieldSelectionsToTable,
  collectSmartStructureFieldConflicts,
  type SmartStructureFieldConflictItem,
  type SmartStructureFieldConflictSelection
} from "@/views/shared/smart-structure-field-conflicts";
import type { SmartFillScope } from "./smartFillExecution.helpers";
import { requiredSelectionRule, validateForm } from "@/utils/form-rules";

defineOptions({ name: "FillData" });

// 步骤
const currentStep = ref(SMART_FILL_STEP_UPLOAD_SCOPE);
const advancedMode = ref(false);
const legacySteps = [
  { title: "上传文件" },
  { title: "选择表格" },
  { title: "配置匹配" },
  { title: "预览确认" }
];
const steps = computed(() =>
  advancedMode.value ? legacySteps : createSmartFillSmartSteps()
);

// 文件上传
const uploadedFile = ref<FileUploadResponse | null>(null);
const enableStructureLlmAssistance = ref(true);
const structureLlmServiceId = ref<number | undefined>();
const isExcelFile = computed(() => uploadedFile.value?.fileType === 1);
const canUploadSourceFile = computed(() => hasPerms("btn:document:upload"));
const canPreviewMatching = computed(() =>
  hasPerms("btn:matching:preview-batch")
);
const canLlmStream = computed(() => hasPerms("btn:matching-fill:llm-stream"));
const canExecuteFill = computed(() =>
  hasPerms("btn:matching-fill:execute-batch")
);
const taskDownloadAvailable = ref(true);
const canDownloadFillResult = computed(
  () => taskDownloadAvailable.value && hasPerms("btn:matching:download")
);

// 所有表格信息
const allTables = ref<TableInfo[]>([]);
const previewTableNames = computed(() =>
  Object.fromEntries(
    allTables.value
      .filter(table => !!table.name?.trim())
      .map(table => [table.index, table.name!.trim()])
  )
);
// 批量表格配置
const batchTableConfigs = ref<BatchTableConfigItem[]>([]);
const wordColumnMappingRules = ref<ColumnMappingRule[]>([]);

// 匹配配置
const matchConfig = ref<MatchConfigType>({ ...defaultMatchConfig });
const matchConfigRef = ref<InstanceType<typeof SmartFillMatchStep> | null>(
  null
);

// 批量预览结果
const batchPreviewResults = ref<BatchTablePreviewResult[]>([]);
const batchPreviewTabsRef = ref<InstanceType<
  typeof SmartFillPreviewStep
> | null>(null);
const loadingUploadedFileTables = ref(false);
const loading = ref(false);
// 选中的表格数量
const selectedTableCount = computed(
  () => batchTableConfigs.value.filter(t => t.selected).length
);
const selectedTableIndexes = computed(() =>
  batchTableConfigs.value
    .filter(table => table.selected)
    .map(table => table.tableIndex)
);
const {
  previewElapsedSeconds,
  previewProgress,
  previewProgressStageText,
  previewProgressDetailText,
  previewProgressPercent,
  previewProgressCounterText,
  currentPreviewRequestId,
  stopPreviewProgressPolling,
  resetPreviewProgress,
  createPreviewRequestId,
  startPreviewProgressPolling,
  markPreviewProgressCompleted
} = useSmartFillPreviewProgress({ selectedTableCount });

// 详情弹窗
const detailVisible = ref(false);
const detailItem = ref<MatchPreviewItem | null>(null);

const matchScope = ref<SmartFillScope>({
  customerId: undefined,
  processId: undefined,
  machineModelId: undefined
});
const scopeFormRef = ref<FormInstance>();
const scopeFormRules: FormRules<SmartFillScope> = {
  customerId: [requiredSelectionRule("请选择客户")]
};
const selectedCustomerIdForRules = ref<number | undefined>(undefined);
const customers = ref<Customer[]>([]);
const processes = ref<Process[]>([]);
const machineModels = ref<MachineModel[]>([]);
const loadingScopeOptions = ref(false);
const scopeOptionsGate = createPagedOptionsRequestGate();

const {
  recognizing: smartRecognizing,
  recognitionAttempted: smartRecognitionAttempted,
  recognitionError: smartRecognitionError,
  confirmingTableIndex: smartConfirmingTableIndex,
  recognizedTables,
  replaceRecognizedTables,
  recognize: recognizeSmartStructure,
  confirm: confirmSmartStructure,
  cancelActiveRecognition,
  reset: resetSmartStructure
} = useSmartStructureRecognition();
const smartConfirmDrafts = ref<
  Record<number, SmartConfigConfirmRequest | null>
>({});
const smartBatchConfirmRunning = ref(false);
const smartBatchConfirmingTableIndex = ref<number | null>(null);
const smartBatchConfirmProgress = ref({ completed: 0, total: 0 });
const smartFieldConflictDialogVisible = ref(false);
const pendingSmartFieldConflicts = ref<SmartStructureFieldConflictItem[]>([]);

watch(
  () => uploadedFile.value?.fileId,
  () => {
    smartConfirmDrafts.value = {};
    smartBatchConfirmRunning.value = false;
    smartBatchConfirmingTableIndex.value = null;
    smartBatchConfirmProgress.value = { completed: 0, total: 0 };
    smartFieldConflictDialogVisible.value = false;
    pendingSmartFieldConflicts.value = [];
  }
);

const resetMatchScope = () => {
  matchScope.value = {
    customerId: undefined,
    processId: undefined,
    machineModelId: undefined
  };
  selectedCustomerIdForRules.value = undefined;
};

const selectedScopeSummary = computed(() => {
  const customer = customers.value.find(
    item => item.id === matchScope.value.customerId
  )?.name;
  if (!customer) return "";

  const process = processes.value.find(
    item => item.id === matchScope.value.processId
  )?.name;
  const model = machineModels.value.find(
    item => item.id === matchScope.value.machineModelId
  )?.name;

  return `当前匹配范围：${[customer, process, model].filter(Boolean).join(" / ")}`;
});

const loadScopeOptions = async () => {
  const request = scopeOptionsGate.begin();
  loadingScopeOptions.value = true;
  try {
    const [customerItems, processItems, machineModelItems] = await Promise.all([
      loadAllPagedItems(
        (page, pageSize, signal) =>
          getCustomerList({ page, pageSize }, { signal }),
        { getKey: item => item.id, signal: request.signal }
      ),
      loadAllPagedItems(
        (page, pageSize, signal) =>
          getProcessList({ page, pageSize }, { signal }),
        { getKey: item => item.id, signal: request.signal }
      ),
      loadAllPagedItems(
        (page, pageSize, signal) =>
          getMachineModelList({ page, pageSize }, { signal }),
        { getKey: item => item.id, signal: request.signal }
      )
    ]);

    if (!request.isCurrent()) return;
    customers.value = customerItems;
    processes.value = processItems;
    machineModels.value = machineModelItems;
  } catch (error) {
    if (!request.signal.aborted && !isGloballyHandledAuthError(error)) {
      ElMessage.error(getRequestErrorMessage(error, "加载匹配范围失败"));
    }
  } finally {
    if (request.isCurrent()) {
      loadingScopeOptions.value = false;
    }
  }
};

void loadScopeOptions();

// 所有预览项（扁平化）
const allPreviewItems = computed(() =>
  batchPreviewResults.value.flatMap(t => t.items)
);

const getCurrentScope = () => matchScope.value;

const getMatchConfigServiceStatus = () =>
  matchConfigRef.value?.getServiceStatus?.() ?? {
    hasAvailableEmbeddingService: false,
    hasAvailableLlmService: false
  };

const {
  backfillDialogVisible,
  backfillCandidates,
  pendingExecuteRequest,
  backfillingSpecs,
  selectedBackfillCandidates,
  resetPendingBackfillState,
  closeBackfillDialog,
  openBackfillDialog,
  setBackfillingSpecs,
  clearPendingExecuteRequest
} = useSmartFillBackfillState();

const {
  previewBlockingMessage,
  previewBlockingHint,
  getPrePreviewBlockingMessage,
  resetPreviewState,
  markPreviewEmptyResults,
  resolvePreviewFailure
} = useSmartFillPreviewBlocking({
  matchConfig,
  getMatchConfigServiceStatus
});

const { llmStreaming, startLlmStream, stopLlmStream, handleWindowOffline } =
  useSmartFillLlmStream({
    canLlmStream,
    batchPreviewResults,
    allPreviewItems,
    matchConfig,
    getScope: getCurrentScope,
    onStartStream: (payload, controller) => {
      // 通过类型化 API 发起 LLM 流式复核请求，controller.signal 用于取消
      return requestMatchLlmStream(payload, controller.signal);
    },
    buildLlmStreamPayload: (scope, items, config) =>
      createMatchLlmStreamRequest({
        customerId: scope.customerId,
        processId: scope.processId,
        machineModelId: scope.machineModelId,
        items,
        config
      })
  });

if (typeof window !== "undefined") {
  useEventListener(window, "offline", handleWindowOffline);
}

const handleScopeChange = (
  customerId?: number,
  processId?: number,
  machineModelId?: number
) => {
  matchScope.value = {
    customerId,
    processId,
    machineModelId
  };
  selectedCustomerIdForRules.value = customerId;
};

const clearPreviewDetail = () => {
  detailItem.value = null;
  detailVisible.value = false;
};

const refreshRuntimeAiSelection = async () => {
  const llmRequested =
    matchConfig.value.enableLlmEquivalenceAdjudication === true ||
    matchConfig.value.enableLlmSemanticPriority === true;
  const refresh = await matchConfigRef.value?.refreshAiServices?.();
  if (!refresh?.current) return false;

  const blockingMessage = getPrePreviewBlockingMessage();
  if (blockingMessage) {
    ElMessage.warning(blockingMessage);
    return false;
  }
  if (llmRequested && refresh.llm?.status === "checking") {
    ElMessage.warning("LLM 服务仍在检测，请稍后重试");
    return false;
  }
  if (
    llmRequested &&
    !matchConfig.value.enableLlmEquivalenceAdjudication &&
    !matchConfig.value.enableLlmSemanticPriority
  ) {
    ElMessage.warning("LLM 服务当前不可用，本次将关闭 AI 复核后继续");
  }
  return true;
};

const {
  executing,
  downloadingResult,
  taskId,
  lastDownloadFailed,
  getHighConfidenceThreshold,
  getAmbiguityMargin,
  handleDownloadLastResult,
  executePendingWithoutBackfill,
  confirmBackfillAndExecute,
  handleExecute,
  resetExecutionState
} = useSmartFillExecution({
  uploadedFile,
  isExcelFile,
  batchTableConfigs,
  batchPreviewResults,
  matchConfig,
  llmStreaming,
  canDownloadFillResult,
  batchPreviewTabsRef,
  getScope: getCurrentScope,
  pendingExecuteRequest,
  selectedBackfillCandidates,
  closeBackfillDialog,
  openBackfillDialog,
  setBackfillingSpecs,
  clearPendingExecuteRequest,
  ensureRuntimeAiReady: refreshRuntimeAiSelection,
  onDownload: (blob: Blob, fileName: string) => {
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    window.URL.revokeObjectURL(url);
  }
});

const { doPreview, invalidatePendingPreview, previewAbortController } =
  useSmartFillPreviewRequest({
    currentStep,
    uploadedFile,
    batchTableConfigs,
    batchPreviewResults,
    matchConfig,
    loading,
    taskId,
    lastDownloadFailed,
    getScope: getCurrentScope,
    stopLlmStream,
    startLlmStream,
    getPrePreviewBlockingMessage,
    resetPreviewState,
    markPreviewEmptyResults,
    resolvePreviewFailure,
    createPreviewRequestId,
    startPreviewProgressPolling,
    stopPreviewProgressPolling,
    resetPreviewProgress,
    markPreviewProgressCompleted,
    getCurrentPreviewRequestId: () => currentPreviewRequestId.value,
    isPreviewStep: () =>
      currentStep.value ===
      (advancedMode.value
        ? SMART_FILL_ADVANCED_STEP_PREVIEW
        : SMART_FILL_STEP_PREVIEW),
    clearPreviewDetail,
    onSendPreview: (data, controller) => {
      // 透传取消信号，确保用户切换步骤时可及时中止进行中的预览请求
      return batchPreviewMatch(data, { signal: controller.signal });
    }
  });

let taskStatusPollTimer: number | null = null;
let reconcileRetainedTask:
  | ((retainedTaskId: string | null) => Promise<void>)
  | undefined;

const stopTaskStatusPolling = () => {
  if (taskStatusPollTimer !== null && typeof window !== "undefined") {
    window.clearTimeout(taskStatusPollTimer);
  }
  taskStatusPollTimer = null;
};

const stopOwnedProgress = () => {
  stopTaskStatusPolling();
  stopPreviewProgressPolling();
};

const resumeTaskStatusPolling = (retainedTaskId: string) => {
  stopTaskStatusPolling();
  taskDownloadAvailable.value = false;
  if (typeof window === "undefined") return;

  taskStatusPollTimer = window.setTimeout(() => {
    taskStatusPollTimer = null;
    void reconcileRetainedTask?.(retainedTaskId);
  }, 900);
};

const activation = useSmartFillActivation({
  getCurrentTaskId: () => taskId.value,
  abortScope: () => {
    scopeOptionsGate.cancel();
    loadingScopeOptions.value = false;
  },
  invalidatePreview: invalidatePendingPreview,
  stopProgress: stopOwnedProgress,
  stopStream: stopLlmStream,
  cancelRecognition: cancelActiveRecognition,
  resumeProgress: resumeTaskStatusPolling,
  restoreDownload: retainedTaskId => {
    if (taskId.value !== retainedTaskId) return;
    taskDownloadAvailable.value = true;
    lastDownloadFailed.value = false;
  },
  invalidateStaleResponse: () => {
    taskDownloadAvailable.value = false;
    taskId.value = null;
    lastDownloadFailed.value = false;
    batchPreviewResults.value = [];
    clearPreviewDetail();
    resetPreviewState();
  }
});
reconcileRetainedTask = activation.reconcileOnActivation;

watch(taskId, (currentTaskId, previousTaskId) => {
  if (currentTaskId === previousTaskId) return;
  activation.cancelReconciliation();
  stopTaskStatusPolling();
});

const retryPreview = async () => {
  if (!(await refreshRuntimeAiSelection())) return;
  await doPreview();
};

onActivated(() => {
  if (
    customers.value.length === 0 &&
    processes.value.length === 0 &&
    machineModels.value.length === 0
  ) {
    void loadScopeOptions();
  }
  void activation.reconcileOnActivation(taskId.value);
});

onDeactivated(() => {
  activation.pauseForDeactivation();
});

// 页面卸载时同样清理页面拥有的后台工作。
onBeforeUnmount(() => {
  activation.pauseForDeactivation();
});

watch(currentStep, step => {
  const previewStep = advancedMode.value
    ? SMART_FILL_ADVANCED_STEP_PREVIEW
    : SMART_FILL_STEP_PREVIEW;
  if (step !== previewStep) {
    invalidatePendingPreview();
    stopLlmStream();
  }
});

watch(
  () => matchScope.value.customerId,
  (customerId, previousCustomerId) => {
    selectedCustomerIdForRules.value = customerId;
    if (customerId === previousCustomerId) return;

    invalidatePendingPreview();
    stopLlmStream();
    resetPreviewState();
    resetPendingBackfillState();
    resetExecutionState();
    batchTableConfigs.value = [];
    batchPreviewResults.value = [];
    resetSmartStructure();
    activeSmartStructureTab.value = undefined;
    advancedMode.value = false;
    currentStep.value = SMART_FILL_STEP_UPLOAD_SCOPE;
    void reloadWordColumnMappingRulesForCustomer();
  }
);

// 计算属性
const pendingSelectedSmartDraftCount = computed(
  () =>
    recognizedTables.value.filter(table => {
      if (!selectedTableIndexes.value.includes(table.tableIndex)) return false;
      const draft = smartConfirmDrafts.value[table.tableIndex];
      return (
        (table.decision !== "AutoApply" || draft?.userModifiedStructure) &&
        draft == null
      );
    }).length
);
const hasRetainedSmartRecognition = computed(
  () => recognizedTables.value.length > 0
);
const smartFillPrimaryActionText = computed(() => {
  if (advancedMode.value) return "下一步";
  switch (currentStep.value) {
    case SMART_FILL_STEP_UPLOAD_SCOPE:
      return hasRetainedSmartRecognition.value
        ? "查看识别结果"
        : "识别并进入确认";
    case SMART_FILL_STEP_RECOGNITION_REVIEW:
      if (selectedTableCount.value === 0) return "请至少选择 1 个 Sheet";
      if (smartBatchConfirmRunning.value) {
        return smartBatchConfirmProgress.value.total > 0
          ? `正在确认 ${Math.min(smartBatchConfirmProgress.value.completed + 1, smartBatchConfirmProgress.value.total)}/${smartBatchConfirmProgress.value.total}`
          : "正在确认所选 Sheet";
      }
      if (pendingSelectedSmartDraftCount.value > 0) {
        return `还有 ${pendingSelectedSmartDraftCount.value} 个已选 Sheet 待配置`;
      }
      return "确认所选 Sheet、学习并进入匹配配置";
    case SMART_FILL_STEP_MATCH_CONFIG:
      return "下一步：预览确认";
    default:
      return "下一步";
  }
});
const showManualFallback = computed(() =>
  shouldShowSmartStructureManualFallback({
    recognitionAttempted: smartRecognitionAttempted.value,
    recognizing: smartRecognizing.value,
    error: smartRecognitionError.value,
    tables: recognizedTables.value
  })
);
const canGoNext = computed(() => {
  if (!advancedMode.value) {
    switch (currentStep.value) {
      case SMART_FILL_STEP_UPLOAD_SCOPE:
        if (
          !(
            uploadedFile.value !== null &&
            !!matchScope.value.customerId &&
            !loadingUploadedFileTables.value &&
            uploadedFile.value.tableCountReady &&
            uploadedFile.value.tableCount > 0 &&
            (!enableStructureLlmAssistance.value ||
              structureLlmServiceId.value != null)
          )
        ) {
          return false;
        }
        return true;
      case SMART_FILL_STEP_RECOGNITION_REVIEW:
        return (
          selectedTableCount.value > 0 &&
          pendingSelectedSmartDraftCount.value === 0 &&
          !smartBatchConfirmRunning.value
        );
      case SMART_FILL_STEP_MATCH_CONFIG:
        return selectedTableCount.value > 0;
      case SMART_FILL_STEP_PREVIEW:
        return allPreviewItems.value.length > 0;
      default:
        return false;
    }
  }

  switch (currentStep.value) {
    case SMART_FILL_STEP_UPLOAD_SCOPE:
      return uploadedFile.value !== null && !loadingUploadedFileTables.value;
    case SMART_FILL_ADVANCED_STEP_TABLE_CONFIG:
      return selectedTableCount.value > 0;
    case SMART_FILL_ADVANCED_STEP_MATCH_CONFIG:
      return true;
    case SMART_FILL_ADVANCED_STEP_PREVIEW:
      return allPreviewItems.value.length > 0;
    default:
      return false;
  }
});

const {
  loadUploadedFileTables,
  reloadWordColumnMappingRulesForCustomer,
  ensureManualTableConfigs
} = useSmartFillUploadedTables({
  uploadedFile,
  isExcelFile,
  allTables,
  batchTableConfigs,
  wordColumnMappingRules,
  loadingUploadedFileTables,
  selectedCustomerId: selectedCustomerIdForRules
});

// 文件上传完成
const handleFileUploaded = async (file: FileUploadResponse) => {
  invalidatePendingPreview();
  stopLlmStream();
  resetPreviewState();
  resetPendingBackfillState();
  resetMatchScope();
  resetExecutionState();
  allTables.value = [];
  enableStructureLlmAssistance.value = true;
  structureLlmServiceId.value = undefined;
  uploadedFile.value = file;
  batchTableConfigs.value = [];
  batchPreviewResults.value = [];
  resetSmartStructure();
  advancedMode.value = false;
  currentStep.value = SMART_FILL_STEP_UPLOAD_SCOPE;
  await loadUploadedFileTables(file);
};

const handleUploadedFileChange = (file: FileUploadResponse | null) => {
  uploadedFile.value = file;
  if (file) return;

  invalidatePendingPreview();
  stopLlmStream();
  resetPreviewState();
  resetPendingBackfillState();
  resetExecutionState();
  allTables.value = [];
  batchTableConfigs.value = [];
  batchPreviewResults.value = [];
  resetSmartStructure();
  advancedMode.value = false;
  currentStep.value = SMART_FILL_STEP_UPLOAD_SCOPE;
};

const runSmartStructureRecognition = async () => {
  if (!uploadedFile.value) {
    ElMessage.warning("请先上传目标文档");
    return;
  }
  if (!(await validateForm(scopeFormRef.value))) return;
  if (
    !uploadedFile.value.tableCountReady ||
    uploadedFile.value.tableCount <= 0
  ) {
    ElMessage.warning(
      uploadedFile.value.tableMetadataError || "请先等待表格结构读取完成"
    );
    return;
  }
  if (enableStructureLlmAssistance.value && !structureLlmServiceId.value) {
    ElMessage.warning("当前没有可用的 LLM 服务，请先完成 AI 服务配置");
    return;
  }

  invalidatePendingPreview();
  stopLlmStream();
  resetPreviewState();
  resetPendingBackfillState();
  resetExecutionState();
  batchTableConfigs.value = [];
  batchPreviewResults.value = [];
  currentStep.value = SMART_FILL_STEP_UPLOAD_SCOPE;

  const result = await recognizeSmartStructure(
    uploadedFile.value.fileId,
    matchScope.value.customerId,
    {
      enableLlmAssistance: enableStructureLlmAssistance.value,
      llmServiceId: enableStructureLlmAssistance.value
        ? structureLlmServiceId.value
        : undefined
    }
  );
  if (!result) return;

  const configs = buildSmartFillConfigsFromRecognizedTables({
    isExcelFile: isExcelFile.value,
    tables: result.tables,
    tableInfos: allTables.value
  });
  if (configs.length === 0) {
    ElMessage.warning("识别结果需要补充列配置，请在确认页手动处理");
  }

  batchTableConfigs.value = configs;
  activeSmartStructureTab.value =
    configs.find(config => {
      const table = result.tables.find(
        item => item.tableIndex === config.tableIndex
      );
      return config.selected && table?.decision !== "AutoApply";
    })?.tableIndex ??
    configs[0]?.tableIndex ??
    result.tables[0]?.tableIndex;
  currentStep.value = SMART_FILL_STEP_RECOGNITION_REVIEW;
  await nextTick();
  document.querySelector(".smart-fill")?.scrollIntoView({ block: "start" });
};

const activeSmartStructureTab = ref<number | undefined>();
const smartFillSelectableTableIndexes = computed(() =>
  batchTableConfigs.value.map(config => config.tableIndex)
);
const smartFillSelectionDisabledReasons = computed(() =>
  Object.fromEntries(
    recognizedTables.value.map(table => [
      table.tableIndex,
      getSmartStructureImportSelectionDisabledReason(table) ||
        "当前结构无法生成填充配置，请先确认列配置"
    ])
  )
);

const handleRecognizedTableSelectionChange = (
  tableIndex: number,
  selected: boolean
) => {
  batchTableConfigs.value = batchTableConfigs.value.map(config =>
    config.tableIndex === tableIndex ? { ...config, selected } : config
  );
};

const handleSmartStructureConfirm = async (
  table: SmartConfigRecognizedTable,
  request: SmartConfigConfirmRequest
): Promise<boolean> => {
  const result = await confirmSmartStructure(request);
  if (!result) return false;

  const nextTables = recognizedTables.value.map(item =>
    item.tableIndex === table.tableIndex
      ? applySmartConfigConfirmRequestToTable(item, request)
      : item
  );
  if (!replaceRecognizedTables(nextTables, request.fileId)) {
    return false;
  }
  const selectedState = new Map(
    batchTableConfigs.value.map(config => [config.tableIndex, config.selected])
  );
  batchTableConfigs.value = buildSmartFillConfigsFromRecognizedTables({
    isExcelFile: isExcelFile.value,
    tables: nextTables,
    tableInfos: allTables.value
  }).map(config => ({
    ...config,
    selected:
      selectedState.get(config.tableIndex) ??
      (config.tableIndex === table.tableIndex ? true : config.selected)
  }));
  return true;
};

const effectiveSmartConfirmingTableIndex = computed(
  () => smartBatchConfirmingTableIndex.value ?? smartConfirmingTableIndex.value
);

const handleSmartStructureDraftChange = (
  table: SmartConfigRecognizedTable,
  request: SmartConfigConfirmRequest | null
) => {
  smartConfirmDrafts.value = {
    ...smartConfirmDrafts.value,
    [table.tableIndex]: request
  };
  batchTableConfigs.value = syncSmartFillDraftConfig({
    isExcelFile: isExcelFile.value,
    table,
    tableInfos: allTables.value,
    configs: batchTableConfigs.value,
    draft: request
  });
};

const executeSelectedSmartStructureConfirmation = async () => {
  if (smartBatchConfirmRunning.value) return;

  const drafts = new Map<number, SmartConfigConfirmRequest>();
  Object.entries(smartConfirmDrafts.value).forEach(([tableIndex, request]) => {
    if (request) drafts.set(Number(tableIndex), request);
  });

  smartBatchConfirmRunning.value = true;
  smartBatchConfirmProgress.value = { completed: 0, total: 0 };
  try {
    const result = await runSmartFillConfirmSelection({
      tables: recognizedTables.value,
      selectedTableIndexes: selectedTableIndexes.value,
      draftRequests: drafts,
      confirm: handleSmartStructureConfirm,
      onProgress: progress => {
        smartBatchConfirmingTableIndex.value =
          progress.currentTableIndex ?? null;
        smartBatchConfirmProgress.value = {
          completed: progress.completed,
          total: progress.total
        };
      }
    });

    if (result.success) {
      currentStep.value = SMART_FILL_STEP_MATCH_CONFIG;
      return;
    }

    if (result.failedTableIndex != null) {
      activeSmartStructureTab.value = result.failedTableIndex;
    }
    const failedTable = recognizedTables.value.find(
      table => table.tableIndex === result.failedTableIndex
    );
    const failedTableName =
      failedTable?.tableName ||
      (result.failedTableIndex == null
        ? ""
        : `工作表 ${result.failedTableIndex + 1}`);
    if (result.failure === "no-selected-tables") {
      ElMessage.warning("请至少选择 1 个 Sheet");
    } else if (
      result.failure === "missing-draft" ||
      result.failure === "table-not-found"
    ) {
      ElMessage.warning(`${failedTableName}配置不完整，请补齐后重试`);
    } else {
      ElMessage.error(`${failedTableName}确认学习失败，已停止后续处理`);
    }
  } finally {
    smartBatchConfirmRunning.value = false;
    smartBatchConfirmingTableIndex.value = null;
  }
};

const confirmSelectedSmartStructuresAndContinue = async () => {
  if (smartBatchConfirmRunning.value) return;
  const conflicts = collectSmartStructureFieldConflicts(
    recognizedTables.value,
    selectedTableIndexes.value
  );
  if (conflicts.length > 0) {
    pendingSmartFieldConflicts.value = conflicts;
    smartFieldConflictDialogVisible.value = true;
    return;
  }

  await executeSelectedSmartStructureConfirmation();
};

const handleSmartFieldConflictCancel = () => {
  smartFieldConflictDialogVisible.value = false;
  pendingSmartFieldConflicts.value = [];
};

const handleSmartFieldConflictConfirm = async (
  selections: SmartStructureFieldConflictSelection[]
) => {
  const previousTables = recognizedTables.value;
  const nextTables = previousTables.map(table =>
    applySmartStructureFieldSelectionsToTable(table, selections)
  );
  const nextDrafts = { ...smartConfirmDrafts.value };
  nextTables.forEach(table => {
    const request = nextDrafts[table.tableIndex];
    if (!request) return;
    nextDrafts[table.tableIndex] = applySmartStructureFieldSelectionsToDraft(
      request,
      table,
      selections
    );
  });

  smartFieldConflictDialogVisible.value = false;
  pendingSmartFieldConflicts.value = [];
  smartConfirmDrafts.value = nextDrafts;
  replaceRecognizedTables(nextTables, uploadedFile.value?.fileId);
  await nextTick();
  await executeSelectedSmartStructureConfirmation();
};

const enterAdvancedMode = () => {
  ensureManualTableConfigs();
  advancedMode.value = true;
  currentStep.value = SMART_FILL_ADVANCED_STEP_TABLE_CONFIG;
};

// 显示详情
const handleShowDetail = (item: MatchPreviewItem) => {
  detailItem.value = item;
  detailVisible.value = true;
};

// 选择变化
const handleSelect = (
  _tableIndex: number,
  _rowIndex: number,
  _spec: MatchResult | null | undefined
) => {
  // 可用于实时更新统计
};

const toggleBackfillCandidates = (checked: boolean) => {
  backfillCandidates.value.forEach(item => {
    item.selected = checked;
  });
};

// 步骤切换
const goNext = async () => {
  if (
    !advancedMode.value &&
    currentStep.value === SMART_FILL_STEP_UPLOAD_SCOPE
  ) {
    if (!hasRetainedSmartRecognition.value) {
      void runSmartStructureRecognition();
      return;
    }
    currentStep.value = SMART_FILL_STEP_RECOGNITION_REVIEW;
    return;
  }

  if (
    !advancedMode.value &&
    currentStep.value === SMART_FILL_STEP_RECOGNITION_REVIEW
  ) {
    await confirmSelectedSmartStructuresAndContinue();
    return;
  }

  const configStep = advancedMode.value
    ? SMART_FILL_ADVANCED_STEP_MATCH_CONFIG
    : SMART_FILL_STEP_MATCH_CONFIG;
  const previewStep = advancedMode.value
    ? SMART_FILL_ADVANCED_STEP_PREVIEW
    : SMART_FILL_STEP_PREVIEW;

  if (currentStep.value === configStep) {
    if (
      !ensurePermission(
        "btn:matching:preview-batch",
        "权限不足，无法执行匹配预览"
      )
    ) {
      return;
    }
    if (!(await refreshRuntimeAiSelection())) {
      return;
    }
  }
  if (!canGoNext.value || currentStep.value >= steps.value.length - 1) return;
  currentStep.value++;
  if (currentStep.value === previewStep) {
    doPreview();
  }
};

const goPrev = () => {
  if (
    advancedMode.value &&
    currentStep.value === SMART_FILL_ADVANCED_STEP_TABLE_CONFIG
  ) {
    replaceRecognizedTables(
      syncSmartFillConfigsToRecognizedTables({
        isExcelFile: isExcelFile.value,
        tables: recognizedTables.value,
        configs: batchTableConfigs.value
      }),
      uploadedFile.value?.fileId
    );
  }
  const prevState = getSmartFillPrevStepState({
    advancedMode: advancedMode.value,
    currentStep: currentStep.value
  });
  advancedMode.value = prevState.advancedMode;
  currentStep.value = prevState.currentStep;
};

// 重新开始
const handleRestart = () => {
  invalidatePendingPreview();
  stopLlmStream();
  resetPreviewState();
  resetPendingBackfillState();
  resetMatchScope();
  resetExecutionState();
  taskDownloadAvailable.value = true;
  loadingUploadedFileTables.value = false;
  currentStep.value = SMART_FILL_STEP_UPLOAD_SCOPE;
  advancedMode.value = false;
  uploadedFile.value = null;
  enableStructureLlmAssistance.value = true;
  structureLlmServiceId.value = undefined;
  allTables.value = [];
  batchTableConfigs.value = [];
  batchPreviewResults.value = [];
  matchConfig.value = { ...defaultMatchConfig };
  resetSmartStructure();
};

const retryTableMetadata = () => {
  if (uploadedFile.value) {
    void loadUploadedFileTables(uploadedFile.value, { force: true });
  }
};
</script>

<template>
  <div
    class="page smart-fill"
    :class="{
      'smart-fill--recognition-review':
        !advancedMode && currentStep === SMART_FILL_STEP_RECOGNITION_REVIEW,
      'smart-fill--preview':
        (!advancedMode && currentStep === SMART_FILL_STEP_PREVIEW) ||
        (advancedMode && currentStep === SMART_FILL_ADVANCED_STEP_PREVIEW)
    }"
  >
    <div class="page-header">
      <SmartFillSteps :steps="steps" :current-step="currentStep" />
    </div>

    <!-- 步骤内容 -->
    <el-card class="step-content">
      <SmartFillUploadStep
        v-show="currentStep === SMART_FILL_STEP_UPLOAD_SCOPE"
        :uploaded-file="uploadedFile"
        :loading-uploaded-file-tables="loadingUploadedFileTables"
        :can-upload-source-file="canUploadSourceFile"
        @update:uploaded-file="handleUploadedFileChange"
        @uploaded="handleFileUploaded"
        @retry-metadata="retryTableMetadata"
      >
        <template #extra>
          <div class="smart-fill-scope-panel">
            <el-form
              ref="scopeFormRef"
              :model="matchScope"
              :rules="scopeFormRules"
              label-width="96px"
              class="smart-fill-scope-form"
              status-icon
            >
              <el-row :gutter="16">
                <el-col :xs="24" :sm="8">
                  <el-form-item label="客户" prop="customerId">
                    <el-select
                      v-model="matchScope.customerId"
                      :loading="loadingScopeOptions"
                      filterable
                      clearable
                      placeholder="请选择客户"
                      style="width: 100%"
                      @change="
                        value =>
                          handleScopeChange(
                            value,
                            matchScope.processId,
                            matchScope.machineModelId
                          )
                      "
                    >
                      <el-option
                        v-for="customer in customers"
                        :key="customer.id"
                        :label="customer.name"
                        :value="customer.id"
                      />
                    </el-select>
                  </el-form-item>
                </el-col>
                <el-col :xs="24" :sm="8">
                  <el-form-item label="制程">
                    <el-select
                      v-model="matchScope.processId"
                      :loading="loadingScopeOptions"
                      filterable
                      clearable
                      placeholder="可选"
                      style="width: 100%"
                      @change="
                        value =>
                          handleScopeChange(
                            matchScope.customerId,
                            value,
                            matchScope.machineModelId
                          )
                      "
                    >
                      <el-option
                        v-for="process in processes"
                        :key="process.id"
                        :label="process.name"
                        :value="process.id"
                      />
                    </el-select>
                  </el-form-item>
                </el-col>
                <el-col :xs="24" :sm="8">
                  <el-form-item label="机型">
                    <el-select
                      v-model="matchScope.machineModelId"
                      :loading="loadingScopeOptions"
                      filterable
                      clearable
                      placeholder="可选"
                      style="width: 100%"
                      @change="
                        value =>
                          handleScopeChange(
                            matchScope.customerId,
                            matchScope.processId,
                            value
                          )
                      "
                    >
                      <el-option
                        v-for="model in machineModels"
                        :key="model.id"
                        :label="model.name"
                        :value="model.id"
                      />
                    </el-select>
                  </el-form-item>
                </el-col>
              </el-row>
            </el-form>
            <SmartStructureAiAssistControl
              v-model:enabled="enableStructureLlmAssistance"
              v-model:service-id="structureLlmServiceId"
            />
            <SmartStructureSummaryBanner
              v-if="smartRecognitionError"
              :tables="recognizedTables"
              :loading="smartRecognizing"
              :error="smartRecognitionError"
              @retry="runSmartStructureRecognition"
            />
            <el-alert
              v-if="hasRetainedSmartRecognition"
              type="success"
              :closable="false"
              show-icon
              title="结构识别结果已保留"
              class="smart-fill-retained-recognition"
            >
              <template #default>
                <div class="smart-fill-retained-recognition__body">
                  <span>
                    可直接返回识别确认；调整 AI 设置后，也可以主动重新识别。
                  </span>
                  <el-button
                    type="primary"
                    link
                    :loading="smartRecognizing"
                    @click="runSmartStructureRecognition"
                  >
                    重新识别
                  </el-button>
                </div>
              </template>
            </el-alert>
            <div class="smart-fill-entry-actions">
              <el-button
                v-if="showManualFallback"
                :disabled="!uploadedFile"
                @click="enterAdvancedMode"
              >
                手动处理
              </el-button>
            </div>
          </div>
        </template>
      </SmartFillUploadStep>
      <!-- 上传后表格结构读取期间的提示由 SmartFillUploadStep 内部的 el-alert 展示：正在读取表格结构，请稍候 -->

      <section
        v-show="
          !advancedMode && currentStep === SMART_FILL_STEP_RECOGNITION_REVIEW
        "
        class="step-panel smart-fill-recognition-review"
      >
        <div class="smart-fill-recognition-context">
          <div>
            <div class="smart-fill-recognition-context__title">
              结构识别结果
            </div>
            <div class="smart-fill-recognition-context__meta">
              <span>{{ uploadedFile?.fileName }}</span>
              <span v-if="selectedScopeSummary">{{
                selectedScopeSummary
              }}</span>
            </div>
          </div>
        </div>
        <SmartStructureSummaryBanner
          :tables="recognizedTables"
          :loading="smartRecognizing"
          :error="smartRecognitionError"
          @retry="runSmartStructureRecognition"
        />
        <SmartStructureConfirmTabs
          v-model:active-table-index="activeSmartStructureTab"
          :tables="recognizedTables"
          :table-infos="allTables"
          :is-excel-file="isExcelFile"
          :selected-table-indexes="selectedTableIndexes"
          :selectable-table-indexes="smartFillSelectableTableIndexes"
          :selection-disabled-reasons="smartFillSelectionDisabledReasons"
          :file-id="uploadedFile?.fileId"
          :customer-id="matchScope.customerId"
          :confirming-table-index="effectiveSmartConfirmingTableIndex"
          :show-confirm-action="false"
          :interaction-locked="smartBatchConfirmRunning"
          @draft-change="handleSmartStructureDraftChange"
          @advanced="enterAdvancedMode"
          @update:table-selected="
            (table, selected) =>
              handleRecognizedTableSelectionChange(table.tableIndex, selected)
          "
        />
        <div class="smart-fill-entry-actions">
          <el-button
            v-if="showManualFallback"
            :disabled="!uploadedFile"
            @click="enterAdvancedMode"
          >
            手动处理
          </el-button>
        </div>
      </section>

      <SmartFillTableStep
        v-show="
          advancedMode && currentStep === SMART_FILL_ADVANCED_STEP_TABLE_CONFIG
        "
        v-model:batch-table-configs="batchTableConfigs"
        :uploaded-file-id="uploadedFile?.fileId"
        :is-excel-file="isExcelFile"
        :all-tables="allTables"
        :has-uploaded-file="!!uploadedFile"
      />

      <SmartFillMatchStep
        v-show="
          (!advancedMode && currentStep === SMART_FILL_STEP_MATCH_CONFIG) ||
          (advancedMode &&
            currentStep === SMART_FILL_ADVANCED_STEP_MATCH_CONFIG)
        "
        ref="matchConfigRef"
        v-model:match-config="matchConfig"
        :can-llm-stream="canLlmStream"
        :preview-blocking-message="previewBlockingMessage"
        :preview-blocking-hint="previewBlockingHint"
      />

      <SmartFillPreviewStep
        v-show="
          (!advancedMode && currentStep === SMART_FILL_STEP_PREVIEW) ||
          (advancedMode && currentStep === SMART_FILL_ADVANCED_STEP_PREVIEW)
        "
        ref="batchPreviewTabsRef"
        :llm-streaming="llmStreaming"
        :loading="loading"
        :preview-progress="previewProgress"
        :preview-progress-stage-text="previewProgressStageText"
        :preview-progress-percent="previewProgressPercent"
        :preview-progress-detail-text="previewProgressDetailText"
        :preview-progress-counter-text="previewProgressCounterText"
        :preview-elapsed-seconds="previewElapsedSeconds"
        :selected-table-count="selectedTableCount"
        :preview-blocking-message="previewBlockingMessage"
        :preview-blocking-hint="previewBlockingHint"
        :batch-preview-results="batchPreviewResults"
        :high-confidence-threshold="getHighConfidenceThreshold()"
        :ambiguity-margin="getAmbiguityMargin()"
        :preview-table-names="previewTableNames"
        :task-id="taskId"
        :is-excel-file="isExcelFile"
        :last-download-failed="lastDownloadFailed"
        :can-download-fill-result="canDownloadFillResult"
        :all-preview-items-count="allPreviewItems.length"
        :can-preview-matching="canPreviewMatching"
        :can-execute-fill="canExecuteFill"
        :executing="executing"
        :downloading-result="downloadingResult"
        :can-upload-source-file="canUploadSourceFile"
        @go-prev="goPrev"
        @select="handleSelect"
        @show-detail="handleShowDetail"
        @preview="retryPreview"
        @execute="handleExecute"
        @download-last-result="handleDownloadLastResult"
        @restart="handleRestart"
      />

      <!-- 步骤按钮 -->
      <div class="step-actions">
        <el-button v-if="currentStep > 0 && !taskId" @click="goPrev">
          上一步
        </el-button>
        <el-button
          v-if="currentStep < steps.length - 1"
          type="primary"
          :disabled="
            !canGoNext ||
            ((advancedMode
              ? currentStep === SMART_FILL_ADVANCED_STEP_MATCH_CONFIG
              : currentStep === SMART_FILL_STEP_MATCH_CONFIG) &&
              !canPreviewMatching)
          "
          :loading="
            !advancedMode &&
            ((currentStep === SMART_FILL_STEP_UPLOAD_SCOPE &&
              smartRecognizing) ||
              (currentStep === SMART_FILL_STEP_RECOGNITION_REVIEW &&
                smartBatchConfirmRunning))
          "
          @click="goNext"
        >
          {{ smartFillPrimaryActionText }}
        </el-button>
      </div>
    </el-card>

    <SmartFillBackfillDialog
      v-model:visible="backfillDialogVisible"
      :candidates="backfillCandidates"
      :selected-count="selectedBackfillCandidates.length"
      :backfilling-specs="backfillingSpecs"
      :executing="executing"
      @toggle-all="toggleBackfillCandidates"
      @execute-without-backfill="executePendingWithoutBackfill"
      @confirm-backfill="confirmBackfillAndExecute"
    />

    <SmartStructureFieldConflictDialog
      v-model:visible="smartFieldConflictDialogVisible"
      :conflicts="pendingSmartFieldConflicts"
      :table-infos="allTables"
      :is-excel-file="isExcelFile"
      @confirm="handleSmartFieldConflictConfirm"
      @cancel="handleSmartFieldConflictCancel"
    />

    <!-- 详情弹窗 -->
    <ScoreDetailDialog
      v-model:visible="detailVisible"
      :item="detailItem"
      :ambiguity-margin="getAmbiguityMargin()"
      :high-confidence-threshold="getHighConfidenceThreshold()"
    />
  </div>
</template>

<style scoped src="./index.styles.css"></style>
