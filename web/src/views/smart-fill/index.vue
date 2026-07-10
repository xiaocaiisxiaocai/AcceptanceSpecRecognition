<script setup lang="ts">
import { ref, computed, onBeforeUnmount, watch } from "vue";
import { useEventListener } from "@vueuse/core";
import { ElMessage } from "element-plus";
import ScoreDetailDialog from "./components/ScoreDetailDialog.vue";
import SmartFillBackfillDialog from "./components/SmartFillBackfillDialog.vue";
import SmartFillMatchStep from "./components/SmartFillMatchStep.vue";
import SmartFillPreviewStep from "./components/SmartFillPreviewStep.vue";
import SmartFillSteps from "./components/SmartFillSteps.vue";
import SmartFillTableStep from "./components/SmartFillTableStep.vue";
import SmartFillUploadStep from "./components/SmartFillUploadStep.vue";
import SmartStructureConfirmCard from "@/views/shared/SmartStructureConfirmCard.vue";
import SmartStructureSummaryBanner from "@/views/shared/SmartStructureSummaryBanner.vue";
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
import { getRequestErrorMessage } from "@/utils/error-message";
import { loadAllPagedItems } from "@/utils/paged-options";
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
import { useSmartStructureRecognition } from "@/views/shared/useSmartStructureRecognition";
import { createSmartStructureDisplayGroups } from "@/views/shared/smart-structure-recognition";
import {
  buildSmartFillConfigsFromRecognizedTables,
  createSmartFillSmartSteps,
  getSmartFillPrevStepState
} from "./smartFill.smartRecognition";

defineOptions({ name: "FillData" });

// 步骤
const currentStep = ref(0);
const advancedMode = ref(false);
const legacySteps = [
  { title: "上传文件", description: "选择目标文档" },
  { title: "选择表格", description: "选择要填充的表格并配置列索引" },
  { title: "配置匹配", description: "设置匹配参数" },
  { title: "预览确认", description: "确认匹配结果" }
];
const steps = computed(() =>
  advancedMode.value ? legacySteps : createSmartFillSmartSteps()
);

// 文件上传
const uploadedFile = ref<FileUploadResponse | null>(null);
const isExcelFile = computed(() => uploadedFile.value?.fileType === 1);
const canUploadSourceFile = computed(() => hasPerms("btn:document:upload"));
const canPreviewMatching = computed(() =>
  hasPerms("btn:matching:preview-batch")
);
const canLlmStream = computed(() => hasPerms("btn:matching-fill:llm-stream"));
const canExecuteFill = computed(() =>
  hasPerms("btn:matching-fill:execute-batch")
);
const canDownloadFillResult = computed(() => hasPerms("btn:matching:download"));

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

const getEffectiveFilterEmptySourceRows = (tableConfig: {
  filterEmptySourceRows?: boolean;
}) =>
  tableConfig.filterEmptySourceRows ??
  matchConfig.value.filterEmptySourceRows ??
  true;

// 详情弹窗
const detailVisible = ref(false);
const detailItem = ref<MatchPreviewItem | null>(null);

const matchScope = ref<{
  customerId?: number;
  processId?: number;
  machineModelId?: number;
}>({
  customerId: undefined,
  processId: undefined,
  machineModelId: undefined
});
const selectedCustomerIdForRules = ref<number | undefined>(undefined);
const customers = ref<Customer[]>([]);
const processes = ref<Process[]>([]);
const machineModels = ref<MachineModel[]>([]);
const loadingScopeOptions = ref(false);
let scopeOptionsController: AbortController | undefined;

const {
  recognizing: smartRecognizing,
  confirmingTableIndex: smartConfirmingTableIndex,
  recognizedTables,
  recognize: recognizeSmartStructure,
  confirm: confirmSmartStructure,
  reset: resetSmartStructure
} = useSmartStructureRecognition();

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
  scopeOptionsController?.abort();
  const controller = new AbortController();
  scopeOptionsController = controller;
  loadingScopeOptions.value = true;
  try {
    const [customerItems, processItems, machineModelItems] = await Promise.all([
      loadAllPagedItems(
        (page, pageSize, signal) =>
          getCustomerList({ page, pageSize }, { signal }),
        { getKey: item => item.id, signal: controller.signal }
      ),
      loadAllPagedItems(
        (page, pageSize, signal) =>
          getProcessList({ page, pageSize }, { signal }),
        { getKey: item => item.id, signal: controller.signal }
      ),
      loadAllPagedItems(
        (page, pageSize, signal) =>
          getMachineModelList({ page, pageSize }, { signal }),
        { getKey: item => item.id, signal: controller.signal }
      )
    ]);

    if (scopeOptionsController !== controller) return;
    customers.value = customerItems;
    processes.value = processItems;
    machineModels.value = machineModelItems;
  } catch (error) {
    if (!controller.signal.aborted) {
      ElMessage.error(getRequestErrorMessage(error, "加载匹配范围失败"));
    }
  } finally {
    if (scopeOptionsController === controller) {
      scopeOptionsController = undefined;
      loadingScopeOptions.value = false;
    }
  }
};

void loadScopeOptions();

// 所有预览项（扁平化）
const allPreviewItems = computed(() =>
  batchPreviewResults.value.flatMap(t => t.items)
);

const getCurrentScope = () =>
  matchScope.value.customerId ||
  matchScope.value.processId ||
  matchScope.value.machineModelId
    ? matchScope.value
    : (matchConfigRef.value?.getScope?.() ?? matchScope.value);

const getMatchConfigServiceStatus = () =>
  matchConfigRef.value?.getServiceStatus?.() ?? {
    hasAvailableEmbeddingService: true,
    hasAvailableLlmService: true
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
  const customerChanged = matchScope.value.customerId !== customerId;
  matchScope.value = {
    customerId,
    processId,
    machineModelId
  };
  selectedCustomerIdForRules.value = customerId;

  if (customerChanged) {
    void reloadWordColumnMappingRulesForCustomer();
  }
};

const clearPreviewDetail = () => {
  detailItem.value = null;
  detailVisible.value = false;
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
  getEffectiveFilterEmptySourceRows,
  pendingExecuteRequest,
  selectedBackfillCandidates,
  closeBackfillDialog,
  openBackfillDialog,
  setBackfillingSpecs,
  clearPendingExecuteRequest,
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
    getEffectiveFilterEmptySourceRows,
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
    isPreviewStep: () => currentStep.value === (advancedMode.value ? 3 : 2),
    clearPreviewDetail,
    onSendPreview: (data, controller) => {
      // 透传取消信号，确保用户切换步骤时可及时中止进行中的预览请求
      return batchPreviewMatch(data, { signal: controller.signal });
    }
  });

// 页面卸载时清理进行中的预览/流式请求，防止离页后继续占用资源
onBeforeUnmount(() => {
  scopeOptionsController?.abort();
  invalidatePendingPreview();
  stopLlmStream();
});

watch(currentStep, step => {
  const previewStep = advancedMode.value ? 3 : 2;
  if (step !== previewStep) {
    invalidatePendingPreview();
    stopLlmStream();
  }
});

// 计算属性
const canGoNext = computed(() => {
  if (!advancedMode.value) {
    switch (currentStep.value) {
      case 0:
        return (
          uploadedFile.value !== null &&
          !!matchScope.value.customerId &&
          !loadingUploadedFileTables.value
        );
      case 1:
        return selectedTableCount.value > 0;
      case 2:
        return allPreviewItems.value.length > 0;
      default:
        return false;
    }
  }

  switch (currentStep.value) {
    case 0:
      return uploadedFile.value !== null && !loadingUploadedFileTables.value;
    case 1:
      return selectedTableCount.value > 0;
    case 2:
      return true;
    case 3:
      return allPreviewItems.value.length > 0;
    default:
      return false;
  }
});

const { loadUploadedFileTables, reloadWordColumnMappingRulesForCustomer } =
  useSmartFillUploadedTables({
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
  uploadedFile.value = file;
  batchTableConfigs.value = [];
  batchPreviewResults.value = [];
  resetSmartStructure();
  advancedMode.value = false;
  await loadUploadedFileTables(file);
};

const runSmartStructureRecognition = async () => {
  if (!uploadedFile.value) {
    ElMessage.warning("请先上传目标文档");
    return;
  }
  if (!matchScope.value.customerId) {
    ElMessage.warning("请先选择客户");
    return;
  }

  const result = await recognizeSmartStructure(
    uploadedFile.value.fileId,
    matchScope.value.customerId
  );
  if (!result) return;

  const configs = buildSmartFillConfigsFromRecognizedTables({
    isExcelFile: isExcelFile.value,
    tables: result.tables,
    tableInfos: allTables.value
  });
  if (configs.length === 0) {
    ElMessage.warning("未识别到可填充表格，请进入高级手动配置");
    return;
  }

  batchTableConfigs.value = configs;
  currentStep.value = 1;
};

const firstNeedConfirmTableIndex = computed(
  () =>
    recognizedTables.value.find(
      table =>
        table.recommendation !== "Recommended" &&
        table.recommendation !== "Skip"
    )?.tableIndex ?? null
);
const smartStructureDisplayGroups = computed(() =>
  createSmartStructureDisplayGroups(recognizedTables.value)
);

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
  if (!result) return;

  const nextTables = recognizedTables.value.map(item =>
    item.tableIndex === table.tableIndex
      ? replaceRecognizedTableWithConfirmRequest(item, request)
      : item
  );
  batchTableConfigs.value = buildSmartFillConfigsFromRecognizedTables({
    isExcelFile: isExcelFile.value,
    tables: nextTables,
    tableInfos: allTables.value
  });
};

const enterAdvancedMode = () => {
  advancedMode.value = true;
  currentStep.value = 1;
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
const goNext = () => {
  if (!advancedMode.value && currentStep.value === 0) {
    void runSmartStructureRecognition();
    return;
  }

  const configStep = advancedMode.value ? 2 : 1;
  const previewStep = advancedMode.value ? 3 : 2;

  if (currentStep.value === configStep) {
    if (
      !ensurePermission(
        "btn:matching:preview-batch",
        "权限不足，无法执行匹配预览"
      )
    ) {
      return;
    }
    const prePreviewBlockingMessage = getPrePreviewBlockingMessage();
    if (prePreviewBlockingMessage) {
      ElMessage.warning(prePreviewBlockingMessage);
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
  loadingUploadedFileTables.value = false;
  currentStep.value = 0;
  advancedMode.value = false;
  uploadedFile.value = null;
  allTables.value = [];
  batchTableConfigs.value = [];
  batchPreviewResults.value = [];
  matchConfig.value = { ...defaultMatchConfig };
  resetSmartStructure();
};
</script>

<template>
  <div class="page smart-fill">
    <div class="page-header">
      <div>
        <div class="page-title">智能填充</div>
      </div>
      <SmartFillSteps :steps="steps" :current-step="currentStep" />
    </div>

    <!-- 步骤内容 -->
    <el-card class="step-content">
      <SmartFillUploadStep
        v-show="currentStep === 0"
        v-model:uploaded-file="uploadedFile"
        :loading-uploaded-file-tables="loadingUploadedFileTables"
        :can-upload-source-file="canUploadSourceFile"
        @uploaded="handleFileUploaded"
      >
        <template #extra>
          <div class="smart-fill-scope-panel">
            <el-form label-width="96px" class="smart-fill-scope-form">
              <el-row :gutter="16">
                <el-col :xs="24" :sm="8">
                  <el-form-item label="客户" required>
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
            <SmartStructureSummaryBanner
              v-if="recognizedTables.length > 0"
              :tables="recognizedTables"
              :loading="smartRecognizing"
              @retry="runSmartStructureRecognition"
            />
            <div
              v-if="recognizedTables.length > 0"
              class="smart-fill-confirm-list"
            >
              <section
                v-for="group in smartStructureDisplayGroups"
                :key="group.key"
                class="smart-fill-confirm-group"
              >
                <div class="smart-fill-confirm-group-title">
                  <el-tag size="small" :type="group.tagType" effect="plain">
                    {{ group.title }}
                  </el-tag>
                  <span>{{ group.tables.length }} 张</span>
                </div>
                <SmartStructureConfirmCard
                  v-for="table in group.tables"
                  :key="table.tableIndex"
                  :table="table"
                  :customer-id="matchScope.customerId"
                  :confirming="smartConfirmingTableIndex === table.tableIndex"
                  :import-selected="table.recommendation !== 'Skip'"
                  :import-selectable="false"
                  :default-expanded="
                    table.tableIndex === firstNeedConfirmTableIndex
                  "
                  @confirm="
                    request => handleSmartStructureConfirm(table, request)
                  "
                  @advanced="enterAdvancedMode"
                />
              </section>
            </div>
            <div class="smart-fill-entry-actions">
              <el-button
                type="primary"
                :disabled="
                  !uploadedFile ||
                  !matchScope.customerId ||
                  loadingUploadedFileTables
                "
                :loading="smartRecognizing"
                @click="runSmartStructureRecognition"
              >
                智能识别并配置
              </el-button>
              <el-button :disabled="!uploadedFile" @click="enterAdvancedMode">
                高级手动配置
              </el-button>
            </div>
          </div>
        </template>
      </SmartFillUploadStep>
      <!-- 上传后表格结构读取期间的提示由 SmartFillUploadStep 内部的 el-alert 展示：正在读取表格结构，请稍候 -->

      <SmartFillTableStep
        v-show="advancedMode && currentStep === 1"
        v-model:batch-table-configs="batchTableConfigs"
        :uploaded-file-id="uploadedFile?.fileId"
        :is-excel-file="isExcelFile"
        :all-tables="allTables"
        :has-uploaded-file="!!uploadedFile"
      />

      <SmartFillMatchStep
        v-show="
          (!advancedMode && currentStep === 1) ||
          (advancedMode && currentStep === 2)
        "
        ref="matchConfigRef"
        v-model:match-config="matchConfig"
        :can-llm-stream="canLlmStream"
        :preview-blocking-message="previewBlockingMessage"
        :preview-blocking-hint="previewBlockingHint"
        :scope-summary="selectedScopeSummary"
        @scope-change="handleScopeChange"
      />

      <SmartFillPreviewStep
        v-show="
          (!advancedMode && currentStep === 2) ||
          (advancedMode && currentStep === 3)
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
        @preview="doPreview"
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
            ((advancedMode ? currentStep === 2 : currentStep === 1) &&
              !canPreviewMatching)
          "
          :loading="!advancedMode && currentStep === 0 && smartRecognizing"
          @click="goNext"
        >
          {{ !advancedMode && currentStep === 0 ? "识别并配置" : "下一步" }}
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
