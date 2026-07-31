<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, watch } from "vue";
import { ElMessage } from "element-plus";
import TablePreview from "./components/TablePreview.vue";
import ColumnMapping from "./components/ColumnMapping.vue";
import DataImportConfirmPanel from "./components/DataImportConfirmPanel.vue";
import DataImportPreviewPanel from "./components/DataImportPreviewPanel.vue";
import DataImportDifferenceConfirmDialog from "./components/DataImportDifferenceConfirmDialog.vue";
import DataImportStepConfirm from "./components/DataImportStepConfirm.vue";
import DataImportStepMapping from "./components/DataImportStepMapping.vue";
import DataImportStepTableSelect from "./components/DataImportStepTableSelect.vue";
import DataImportStepTarget from "./components/DataImportStepTarget.vue";
import DataImportStepUpload from "./components/DataImportStepUpload.vue";
import ExcelColumnMapping from "./components/ExcelColumnMapping.vue";
import { useDataImportPage } from "./composables/useDataImportPage";
import {
  runSmartStructureBatchConfirmImportAction,
  type SmartStructureBatchConfirmProgress
} from "./dataImport.confirmImport";
import SmartStructureConfirmTabs from "@/views/shared/SmartStructureConfirmTabs.vue";
import SmartStructureAiAssistControl from "@/views/shared/SmartStructureAiAssistControl.vue";
import SmartStructureFieldConflictDialog from "@/views/shared/SmartStructureFieldConflictDialog.vue";
import type {
  SmartConfigConfirmRequest,
  SmartConfigRecognizedTable
} from "@/api/smart-config";
import {
  applySmartStructureFieldSelectionsToDraft,
  applySmartStructureFieldSelectionsToTable,
  collectSmartStructureFieldConflicts,
  type SmartStructureFieldConflictItem,
  type SmartStructureFieldConflictSelection
} from "@/views/shared/smart-structure-field-conflicts";
import {
  applySmartConfigConfirmRequestToTable,
  canSelectSmartStructureTable,
  getSmartStructureImportReadinessReason,
  getSmartStructureImportSelectionDisabledReason,
  shouldShowSmartStructureManualFallback
} from "@/views/shared/smart-structure-recognition";
// 边界说明：useDataImportPage 内部组合 useDataImportMapping、
// useDataImportPreviewSelection、useDataImportExecution；差异弹窗由
// DataImportDifferenceConfirmDialog 继续组合 DataImportDifferenceDialog。

defineOptions({
  name: "ImportData"
});

const fieldConflictDialogVisible = ref(false);
const pendingFieldConflicts = ref<SmartStructureFieldConflictItem[]>([]);
const dataImportFieldConflictContext = ref<"initial" | "batch" | null>(null);
let pendingInitialFieldConflictTables: SmartConfigRecognizedTable[] = [];
let smartDraftPreviewTimer: ReturnType<typeof setTimeout> | null = null;
let smartDraftPreviewRunning = false;
let smartDraftPreviewQueued = false;
let pendingInitialFieldConflictResolver:
  | ((tables: SmartConfigRecognizedTable[] | null) => void)
  | null = null;

const finishPendingInitialFieldConflict = (
  tables: SmartConfigRecognizedTable[] | null
) => {
  const resolve = pendingInitialFieldConflictResolver;
  pendingInitialFieldConflictResolver = null;
  pendingInitialFieldConflictTables = [];
  resolve?.(tables);
};

const resolveDataImportFieldConflicts = (
  tables: SmartConfigRecognizedTable[],
  selectedTableIndexes: number[]
): Promise<SmartConfigRecognizedTable[] | null> => {
  const conflicts = collectSmartStructureFieldConflicts(
    tables,
    selectedTableIndexes
  );
  if (conflicts.length === 0) return Promise.resolve(tables);

  pendingInitialFieldConflictTables = tables;
  pendingFieldConflicts.value = conflicts;
  dataImportFieldConflictContext.value = "initial";
  fieldConflictDialogVisible.value = true;
  currentStep.value = 1;
  return new Promise(resolve => {
    pendingInitialFieldConflictResolver = resolve;
  });
};

onBeforeUnmount(() => {
  if (smartDraftPreviewTimer) {
    clearTimeout(smartDraftPreviewTimer);
    smartDraftPreviewTimer = null;
  }
  finishPendingInitialFieldConflict(null);
});

const {
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
  replaceRecognizedTables,
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
  importPreviewSelectionKeys,
  selectedImportPreviewRowsCount,
  irrelevantPreviewRowCount,
  allIrrelevantPreviewRowsSelected,
  someIrrelevantPreviewRowsSelected,
  handleImportPreviewSelectionChange,
  handleSelectIrrelevantRowsChange,
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
  previewSmartRecognizedTables,
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
} = useDataImportPage({
  resolveInitialFieldConflicts: resolveDataImportFieldConflicts
});

const isCompletionStep = computed(
  () =>
    (!advancedMode.value && currentStep.value === 2) ||
    (advancedMode.value && currentStep.value === 4)
);

const retryTableMetadata = () => {
  if (uploadedFile.value) {
    void loadUploadedFileMetadata(uploadedFile.value, { force: true });
  }
};

const activeTableTabModel = computed({
  get: () => activeTableIndex.value ?? undefined,
  set: value => {
    activeTableIndex.value = typeof value === "number" ? value : Number(value);
  }
});

const activeSmartStructureTab = ref<number | undefined>();
const smartConfirmDrafts = ref<
  Record<number, SmartConfigConfirmRequest | null>
>({});
const batchConfirmImportRunning = ref(false);
const batchConfirmingTableIndex = ref<number | null>(null);
const batchConfirmProgress = ref<{
  phase: SmartStructureBatchConfirmProgress["phase"];
  current: number;
  total: number;
  tableName: string;
}>({ phase: "validating", current: 0, total: 0, tableName: "" });

watch(
  () => uploadedFile.value?.fileId,
  () => {
    if (smartDraftPreviewTimer) {
      clearTimeout(smartDraftPreviewTimer);
      smartDraftPreviewTimer = null;
    }
    smartDraftPreviewQueued = false;
    finishPendingInitialFieldConflict(null);
    smartConfirmDrafts.value = {};
    fieldConflictDialogVisible.value = false;
    pendingFieldConflicts.value = [];
    dataImportFieldConflictContext.value = null;
    batchConfirmImportRunning.value = false;
    batchConfirmingTableIndex.value = null;
    batchConfirmProgress.value = {
      phase: "validating",
      current: 0,
      total: 0,
      tableName: ""
    };
  }
);
const smartStructureSelectableTableIndexes = computed(() =>
  recognizedTables.value
    .filter(canSelectSmartStructureTable)
    .map(table => table.tableIndex)
);
const smartStructureSelectionDisabledReasons = computed(() =>
  Object.fromEntries(
    recognizedTables.value.map(table => [
      table.tableIndex,
      getSmartStructureImportSelectionDisabledReason(table)
    ])
  )
);
const smartStructureSelectionPendingReasons = computed(() =>
  Object.fromEntries(
    recognizedTables.value.map(table => {
      const draft = smartConfirmDrafts.value[table.tableIndex];
      const effectiveTable = draft
        ? applySmartConfigConfirmRequestToTable(table, draft)
        : table;
      return [
        table.tableIndex,
        getSmartStructureImportReadinessReason(effectiveTable)
      ];
    })
  )
);
const pendingSelectedSmartTableCount = computed(
  () =>
    recognizedTables.value.filter(table => {
      if (!selectedSmartTableIndexes.value.includes(table.tableIndex)) {
        return false;
      }
      const hasDraftState = Object.prototype.hasOwnProperty.call(
        smartConfirmDrafts.value,
        table.tableIndex
      );
      const draft = smartConfirmDrafts.value[table.tableIndex];
      return (
        (hasDraftState && draft == null) ||
        (table.decision !== "AutoApply" && draft == null)
      );
    }).length
);
const selectedSmartTablesRequiringConfirmationCount = computed(
  () =>
    recognizedTables.value.filter(table => {
      if (!selectedSmartTableIndexes.value.includes(table.tableIndex)) {
        return false;
      }
      const draft = smartConfirmDrafts.value[table.tableIndex];
      return (
        table.decision !== "AutoApply" || Boolean(draft?.userModifiedStructure)
      );
    }).length
);
const effectiveSmartConfirmingTableIndex = computed(
  () => batchConfirmingTableIndex.value ?? smartConfirmingTableIndex.value
);
const handleSmartStructureDraftChange = (
  table: SmartConfigRecognizedTable,
  request: SmartConfigConfirmRequest | null
) => {
  smartConfirmDrafts.value = {
    ...smartConfirmDrafts.value,
    [table.tableIndex]: request
  };
  if (!request?.userModifiedStructure) return;

  if (smartDraftPreviewTimer) {
    clearTimeout(smartDraftPreviewTimer);
  }
  smartDraftPreviewTimer = setTimeout(() => {
    smartDraftPreviewTimer = null;
    void refreshSmartDraftPreview();
  }, 250);
};

const buildSmartDraftPreviewTables = () =>
  recognizedTables.value.map(table => {
    const draft = smartConfirmDrafts.value[table.tableIndex];
    return draft ? applySmartConfigConfirmRequestToTable(table, draft) : table;
  });

const refreshSmartDraftPreview = async () => {
  if (smartDraftPreviewRunning) {
    smartDraftPreviewQueued = true;
    return;
  }

  smartDraftPreviewRunning = true;
  try {
    do {
      smartDraftPreviewQueued = false;
      await previewSmartRecognizedTables(buildSmartDraftPreviewTables());
    } while (smartDraftPreviewQueued);
  } finally {
    smartDraftPreviewRunning = false;
  }
};
const smartBatchImportButtonText = computed(() =>
  !batchConfirmImportRunning.value
    ? "确认所选 Sheet、学习并开始导入"
    : batchConfirmProgress.value.phase === "confirming"
      ? `正在确认 ${batchConfirmProgress.value.current}/${batchConfirmProgress.value.total}`
      : batchConfirmProgress.value.phase === "refreshing"
        ? "正在刷新配置"
        : batchConfirmProgress.value.phase === "importing"
          ? "正在开始导入"
          : "正在检查配置"
);
const smartBatchProgressText = computed(() => {
  if (!batchConfirmImportRunning.value) return importProgressText.value;
  if (batchConfirmProgress.value.phase === "confirming") {
    return `正在确认第 ${batchConfirmProgress.value.current}/${batchConfirmProgress.value.total} 张 Sheet`;
  }
  if (batchConfirmProgress.value.phase === "refreshing") {
    return "正在统一刷新导入配置";
  }
  if (batchConfirmProgress.value.phase === "importing") {
    return "确认完成，正在开始导入";
  }
  return "正在检查所选 Sheet 配置";
});
const smartBatchProgressDescription = computed(() =>
  batchConfirmImportRunning.value && batchConfirmProgress.value.tableName
    ? `当前：${batchConfirmProgress.value.tableName}。全部确认成功后将自动开始导入。`
    : importProgressDescription.value
);
const updateBatchConfirmProgress = (
  progress: SmartStructureBatchConfirmProgress
) => {
  const table = recognizedTables.value.find(
    item => item.tableIndex === progress.currentTableIndex
  );
  batchConfirmingTableIndex.value = progress.currentTableIndex ?? null;
  batchConfirmProgress.value = {
    phase: progress.phase,
    current:
      progress.phase === "confirming"
        ? Math.min(progress.total, progress.completed + 1)
        : progress.completed,
    total: progress.total,
    tableName:
      table?.tableName || (table ? `工作表 ${table.tableIndex + 1}` : "")
  };
};
const executeSmartStructureBatchConfirmImport = async () => {
  if (batchConfirmImportRunning.value || importing.value) return;

  if (pendingSelectedSmartTableCount.value > 0) {
    const pendingTable = recognizedTables.value.find(
      table =>
        selectedSmartTableIndexes.value.includes(table.tableIndex) &&
        smartConfirmDrafts.value[table.tableIndex] == null
    );
    if (pendingTable) activeSmartStructureTab.value = pendingTable.tableIndex;
    ElMessage.warning("请先补齐已勾选 Sheet 的必填列或有效范围");
    return;
  }

  batchConfirmImportRunning.value = true;
  batchConfirmProgress.value = {
    phase: "validating",
    current: 0,
    total: 0,
    tableName: ""
  };

  try {
    const drafts = new Map<number, SmartConfigConfirmRequest>();
    Object.entries(smartConfirmDrafts.value).forEach(
      ([tableIndex, request]) => {
        if (request) drafts.set(Number(tableIndex), request);
      }
    );

    const result = await runSmartStructureBatchConfirmImportAction({
      tables: recognizedTables.value,
      selectedTableIndexes: selectedSmartTableIndexes.value,
      draftRequests: drafts,
      requiresConfirmation: (table, request) =>
        table.decision !== "AutoApply" ||
        Boolean(request?.userModifiedStructure),
      confirm: (table, request) =>
        handleSmartStructureConfirm(table, request, {
          refreshPreview: false
        }),
      refresh: applyCurrentSmartRecognizedTables,
      importData: handleImport,
      onProgress: updateBatchConfirmProgress
    });

    if (result.success) return;

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
    if (result.failure === "missing-draft") {
      ElMessage.warning(`${failedTableName}配置不完整，请补齐后重试`);
    } else if (result.failure === "no-selected-tables") {
      ElMessage.warning("请至少勾选一张需要导入的 Sheet");
    } else if (result.failure === "confirm-failed") {
      ElMessage.error(`${failedTableName}确认学习失败，已停止本次导入`);
    } else if (result.failure === "refresh-failed") {
      ElMessage.error("全部 Sheet 已确认，但刷新导入配置失败，请重试");
    } else if (result.failure === "import-failed") {
      ElMessage.error("确认学习已完成，但开始导入失败，请重试");
    } else {
      ElMessage.error("批量确认未完成，请检查 Sheet 配置后重试");
    }
  } finally {
    batchConfirmImportRunning.value = false;
    batchConfirmingTableIndex.value = null;
  }
};
const handleSmartStructureBatchConfirmImport = async () => {
  if (batchConfirmImportRunning.value || importing.value) return;

  if (pendingSelectedSmartTableCount.value > 0) {
    const pendingTable = recognizedTables.value.find(
      table =>
        selectedSmartTableIndexes.value.includes(table.tableIndex) &&
        smartConfirmDrafts.value[table.tableIndex] == null
    );
    if (pendingTable) activeSmartStructureTab.value = pendingTable.tableIndex;
    ElMessage.warning("请先补齐已勾选 Sheet 的必填列或有效范围");
    return;
  }

  const conflicts = collectSmartStructureFieldConflicts(
    recognizedTables.value,
    selectedSmartTableIndexes.value
  );
  if (conflicts.length > 0) {
    pendingFieldConflicts.value = conflicts;
    dataImportFieldConflictContext.value = "batch";
    fieldConflictDialogVisible.value = true;
    return;
  }

  await executeSmartStructureBatchConfirmImport();
};
const handleFieldConflictCancel = () => {
  if (dataImportFieldConflictContext.value === "initial") {
    finishPendingInitialFieldConflict(null);
  }
  fieldConflictDialogVisible.value = false;
  pendingFieldConflicts.value = [];
  dataImportFieldConflictContext.value = null;
};
const handleFieldConflictConfirm = async (
  selections: SmartStructureFieldConflictSelection[]
) => {
  const currentTables =
    dataImportFieldConflictContext.value === "initial"
      ? pendingInitialFieldConflictTables
      : recognizedTables.value;
  const nextTables = currentTables.map(table =>
    applySmartStructureFieldSelectionsToTable(table, selections)
  );
  if (dataImportFieldConflictContext.value === "initial") {
    fieldConflictDialogVisible.value = false;
    pendingFieldConflicts.value = [];
    dataImportFieldConflictContext.value = null;
    finishPendingInitialFieldConflict(nextTables);
    return;
  }
  const nextDrafts = { ...smartConfirmDrafts.value };
  currentTables.forEach(table => {
    const request = nextDrafts[table.tableIndex];
    if (!request) return;
    nextDrafts[table.tableIndex] = applySmartStructureFieldSelectionsToDraft(
      request,
      table,
      selections
    );
  });

  smartConfirmDrafts.value = nextDrafts;
  replaceRecognizedTables(nextTables, uploadedFile.value?.fileId);
  fieldConflictDialogVisible.value = false;
  pendingFieldConflicts.value = [];
  dataImportFieldConflictContext.value = null;
  await nextTick();
  await executeSmartStructureBatchConfirmImport();
};
const showManualFallback = computed(
  () =>
    !!smartApplyError.value ||
    shouldShowSmartStructureManualFallback({
      recognitionAttempted: smartRecognitionAttempted.value,
      recognizing: smartRecognizing.value,
      error: smartRecognitionError.value,
      tables: recognizedTables.value
    })
);
const smartEntryError = computed(
  () => smartRecognitionError.value || smartApplyError.value
);
const activeSmartStructureTable = computed(() =>
  recognizedTables.value.find(
    table => table.tableIndex === activeSmartStructureTab.value
  )
);
const activeSmartStructureTableSelected = computed(
  () =>
    activeSmartStructureTable.value != null &&
    selectedSmartTableIndexes.value.includes(
      activeSmartStructureTable.value.tableIndex
    )
);
const activeSmartStructureReadinessReason = computed(() => {
  const table = activeSmartStructureTable.value;
  if (!table) return "";

  const draft = smartConfirmDrafts.value[table.tableIndex];
  const effectiveTable = draft
    ? applySmartConfigConfirmRequestToTable(table, draft)
    : table;
  return getSmartStructureImportReadinessReason(effectiveTable);
});
const activeSmartStructureReadinessDescription = computed(() =>
  activeSmartStructureTableSelected.value &&
  activeSmartStructureReadinessReason.value
    ? `${activeSmartStructureReadinessReason.value}。补齐并确认前不计入下方导入汇总，也不能开始导入。`
    : ""
);
</script>

<template>
  <div
    class="page data-import"
    :class="{ 'data-import--complete': isCompletionStep }"
  >
    <div class="page-header">
      <div class="wizard-steps">
        <el-steps :active="currentStep" finish-status="success">
          <el-step
            v-for="(step, index) in steps"
            :key="index"
            :title="step.title"
          />
        </el-steps>
      </div>
    </div>

    <div class="data-import-body">
      <!-- 步骤内容 -->
      <el-card class="step-content">
        <!-- 智能流程步骤1: 上传文件与选择目标 -->
        <DataImportStepUpload
          v-show="!advancedMode && currentStep === 0"
          v-model="uploadedFile"
          :can-upload-source-file="canUploadSourceFile"
          :can-import-any="canImportAny"
          :upload-accept="uploadAccept"
          :upload-blocked-message="uploadBlockedMessage"
          :smart-recognition-error="smartEntryError"
          :smart-recognizing="smartRecognizing"
          @uploaded="handleFileUploaded"
          @retry-metadata="retryTableMetadata"
          @retry="runSmartStructureRecognition"
        >
          <template #extra>
            <div class="upload-target-panel">
              <DataImportStepTarget
                :customers="customers"
                :processes="processes"
                :machine-models="machineModels"
                :selected-customer-id="selectedCustomerId"
                :selected-process-id="selectedProcessId"
                :selected-machine-model-id="selectedMachineModelId"
                :loading-customers="loadingCustomers"
                :loading-processes="loadingProcesses"
                :loading-machine-models="loadingMachineModels"
                compact
                @update:selected-customer-id="
                  value => (selectedCustomerId = value)
                "
                @update:selected-process-id="
                  value => (selectedProcessId = value)
                "
                @update:selected-machine-model-id="
                  value => (selectedMachineModelId = value)
                "
              />
              <SmartStructureAiAssistControl
                v-model:enabled="enableStructureLlmAssistance"
                v-model:service-id="structureLlmServiceId"
              />
              <div class="smart-entry-actions">
                <el-button
                  v-if="showManualFallback"
                  @click="enterAdvancedMode('tableSelect')"
                >
                  手动处理
                </el-button>
              </div>
            </div>
          </template>
        </DataImportStepUpload>

        <!-- 高级模式步骤2: 选择表格 -->
        <DataImportStepTableSelect
          v-if="advancedMode && currentStep === 1"
          v-model="selectedTableIndexes"
          :uploaded-file="uploadedFile"
          :is-excel-file="isExcelFile"
          @selected-multiple="handleTablesSelected"
        />

        <!-- 高级模式步骤3: 配置映射 -->
        <DataImportStepMapping
          v-if="advancedMode && currentStep === 2"
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
          <div class="advanced-mode-toolbar">
            <el-button type="primary" plain @click="exitAdvancedMode">
              返回智能确认
            </el-button>
          </div>
          <el-tabs
            v-if="uploadedFile && tableConfigs.length > 0"
            v-model="activeTableTabModel"
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
                    isExcelFile
                      ? getExcelPreviewOptions(cfg).dataEndRowIndex
                      : undefined
                  "
                  :mapping="isExcelFile ? undefined : cfg.wordMapping"
                  :preview-loader="loadAdvancedPreview"
                  @loaded="data => handlePreviewLoaded(cfg.tableIndex, data)"
                />
              </div>

              <!-- 列映射配置 -->
              <div class="mapping-section">
                <ExcelColumnMapping
                  v-if="isExcelFile"
                  :model-value="cfg.excelMapping"
                  :detected-mapping="cfg.recognizedExcelMapping"
                  :preview-data="cfg.previewData"
                  :used-range-start-row="cfg.tableInfo?.usedRangeStartRow"
                  :used-range-end-row="
                    cfg.tableInfo?.usedRangeStartRow !== undefined
                      ? cfg.tableInfo.usedRangeStartRow +
                        cfg.tableInfo.rowCount -
                        1
                      : undefined
                  "
                  :used-range-start-column="cfg.tableInfo?.usedRangeStartColumn"
                  @update:model-value="
                    value => updateExcelMapping(cfg.tableIndex, value)
                  "
                />
                <ColumnMapping
                  v-else
                  v-model="cfg.wordMapping"
                  :table-data="cfg.previewData"
                />
              </div>
            </el-tab-pane>
          </el-tabs>
        </DataImportStepMapping>

        <!-- 高级模式步骤4: 选择目标 -->
        <DataImportStepTarget
          v-show="advancedMode && currentStep === 3"
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
          @update:selected-machine-model-id="
            value => (selectedMachineModelId = value)
          "
        />

        <!-- 智能流程步骤2: 确认结构与预览 -->
        <div
          v-show="!advancedMode && currentStep === 1"
          class="step-panel smart-confirm-step"
        >
          <div class="smart-confirm-workspace">
            <div class="smart-confirm-workspace__configuration">
              <div
                class="smart-recognition-toolbar"
                :class="{ 'has-error': Boolean(smartRecognitionError) }"
              >
                <span
                  v-if="smartRecognitionError"
                  class="smart-recognition-toolbar__error"
                  role="alert"
                >
                  {{ smartRecognitionError }}
                </span>
                <el-button
                  type="primary"
                  plain
                  :loading="smartRecognizing"
                  @click="runSmartStructureRecognition"
                >
                  重新识别
                </el-button>
              </div>
              <SmartStructureConfirmTabs
                v-model:active-table-index="activeSmartStructureTab"
                :tables="recognizedTables"
                :table-infos="smartTableInfos"
                :is-excel-file="isExcelFile"
                :inline-excel-region-editor="isExcelFile"
                :selected-table-indexes="selectedSmartTableIndexes"
                :selectable-table-indexes="smartStructureSelectableTableIndexes"
                :selection-disabled-reasons="
                  smartStructureSelectionDisabledReasons
                "
                :selection-pending-reasons="
                  smartStructureSelectionPendingReasons
                "
                :file-id="uploadedFile?.fileId"
                :customer-id="selectedCustomerId"
                :confirming-table-index="effectiveSmartConfirmingTableIndex"
                :show-confirm-action="false"
                :show-range-summary-subtitle="false"
                :interaction-locked="batchConfirmImportRunning || importing"
                ready-label="可导入"
                unavailable-label="跳过"
                @draft-change="handleSmartStructureDraftChange"
                @advanced="
                  table => enterAdvancedMode('mapping', table.tableIndex)
                "
                @update:table-selected="handleSmartTableImportSelectionChange"
              />
              <el-alert
                v-if="
                  activeSmartStructureTable &&
                  activeSmartStructureTableSelected &&
                  activeSmartStructureReadinessReason
                "
                type="warning"
                :closable="false"
                show-icon
                title="当前表已勾选，仍需配置"
                :description="activeSmartStructureReadinessDescription"
                class="smart-import-scope-alert"
              />
              <DataImportConfirmPanel
                :import-result="importResult"
                :is-excel-file="isExcelFile"
                :can-upload-source-file="canUploadSourceFile"
                :can-import-any="canImportAny"
                :can-import-current-file="canImportCurrentFile"
                :current-import-permission-message="
                  currentImportPermissionMessage
                "
                :has-pending-difference-confirmation="
                  hasPendingDifferenceConfirmation
                "
                :pending-differences-count="pendingDifferences.length"
                :has-committed-import-progress="hasCommittedImportProgress"
                :committed-success-count="
                  committedImportAggregate?.successCount || 0
                "
                :committed-skipped-count="
                  committedImportAggregate?.skippedCount || 0
                "
                :committed-failed-count="
                  committedImportAggregate?.failedCount || 0
                "
                :uploaded-file-name="uploadedFile?.fileName"
                :table-configs="tableConfigs"
                :selected-sheet-count="selectedSmartTableIndexes.length"
                :pending-selected-sheet-count="pendingSelectedSmartTableCount"
                :customers="customers"
                :processes="processes"
                :selected-customer-id="selectedCustomerId"
                :selected-process-id="selectedProcessId"
                :selected-machine-model-name="selectedMachineModelName"
                :preview-data-count="previewDataCount"
                :preview-load-state="previewLoadState"
                :import-duplicate-ai-config="importDuplicateAiConfig"
                :loading-ai-services="loadingAiServices"
                :embedding-selection="embeddingSelection"
                :llm-selection="llmSelection"
                :removed-preview-row-count="removedPreviewRowCount"
                :selected-import-preview-row-keys="importPreviewSelectionKeys"
                :selected-import-preview-rows-count="
                  selectedImportPreviewRowsCount
                "
                :irrelevant-preview-row-count="irrelevantPreviewRowCount"
                :all-irrelevant-preview-rows-selected="
                  allIrrelevantPreviewRowsSelected
                "
                :some-irrelevant-preview-rows-selected="
                  someIrrelevantPreviewRowsSelected
                "
                :import-preview-groups="importPreviewGroups"
                :importing="importing || batchConfirmImportRunning"
                :import-progress-text="smartBatchProgressText"
                :import-progress-description="smartBatchProgressDescription"
                :import-primary-button-text="smartBatchImportButtonText"
                :skipped-rows-groups="skippedRowsGroups"
                :show-import-action="false"
                :show-preview-list="false"
                :show-summary-bar="false"
                :allow-empty-preview-action="
                  selectedSmartTablesRequiringConfirmationCount > 0
                "
                @restart="handleRestart"
                @open-difference-confirm-dialog="openDifferenceConfirmDialog"
                @remove-selected-preview-rows="handleRemoveSelectedPreviewRows"
                @restore-removed-preview-rows="handleRestoreRemovedPreviewRows"
                @select-irrelevant-rows-change="
                  handleSelectIrrelevantRowsChange
                "
                @import-preview-selection-change="
                  handleImportPreviewSelectionChange
                "
                @remove-single-preview-row="handleRemoveSinglePreviewRow"
                @load-full-preview="ensureFullPreviewDataLoaded"
                @import="handleSmartStructureBatchConfirmImport"
              />
            </div>

            <aside class="smart-confirm-workspace__preview">
              <DataImportPreviewPanel
                v-model:active-table-index="activeSmartStructureTab"
                :preview-data-count="previewDataCount"
                :preview-load-state="previewLoadState"
                :removed-preview-row-count="removedPreviewRowCount"
                :selected-import-preview-row-keys="importPreviewSelectionKeys"
                :selected-import-preview-rows-count="
                  selectedImportPreviewRowsCount
                "
                :irrelevant-preview-row-count="irrelevantPreviewRowCount"
                :all-irrelevant-preview-rows-selected="
                  allIrrelevantPreviewRowsSelected
                "
                :some-irrelevant-preview-rows-selected="
                  someIrrelevantPreviewRowsSelected
                "
                :import-preview-groups="importPreviewGroups"
                :has-pending-difference-confirmation="
                  hasPendingDifferenceConfirmation
                "
                show-heading
                auto-load-full
                tabbed-groups
                @remove-selected-preview-rows="handleRemoveSelectedPreviewRows"
                @restore-removed-preview-rows="handleRestoreRemovedPreviewRows"
                @select-irrelevant-rows-change="
                  handleSelectIrrelevantRowsChange
                "
                @import-preview-selection-change="
                  handleImportPreviewSelectionChange
                "
                @remove-single-preview-row="handleRemoveSinglePreviewRow"
                @load-full-preview="ensureFullPreviewDataLoaded"
              />
            </aside>
          </div>
        </div>

        <!-- 智能流程步骤3 / 高级模式步骤5: 完成 -->
        <DataImportStepConfirm
          v-show="
            (!advancedMode && currentStep === 2) ||
            (advancedMode && currentStep === 4)
          "
          :import-result="importResult"
        >
          <DataImportConfirmPanel
            :import-result="importResult"
            :is-excel-file="isExcelFile"
            :can-upload-source-file="canUploadSourceFile"
            :can-import-any="canImportAny"
            :can-import-current-file="canImportCurrentFile"
            :current-import-permission-message="currentImportPermissionMessage"
            :has-pending-difference-confirmation="
              hasPendingDifferenceConfirmation
            "
            :pending-differences-count="pendingDifferences.length"
            :has-committed-import-progress="hasCommittedImportProgress"
            :committed-success-count="
              committedImportAggregate?.successCount || 0
            "
            :committed-skipped-count="
              committedImportAggregate?.skippedCount || 0
            "
            :committed-failed-count="committedImportAggregate?.failedCount || 0"
            :uploaded-file-name="uploadedFile?.fileName"
            :table-configs="tableConfigs"
            :customers="customers"
            :processes="processes"
            :selected-customer-id="selectedCustomerId"
            :selected-process-id="selectedProcessId"
            :selected-machine-model-name="selectedMachineModelName"
            :preview-data-count="previewDataCount"
            :preview-load-state="previewLoadState"
            :import-duplicate-ai-config="importDuplicateAiConfig"
            :loading-ai-services="loadingAiServices"
            :embedding-selection="embeddingSelection"
            :llm-selection="llmSelection"
            :removed-preview-row-count="removedPreviewRowCount"
            :selected-import-preview-row-keys="importPreviewSelectionKeys"
            :selected-import-preview-rows-count="selectedImportPreviewRowsCount"
            :irrelevant-preview-row-count="irrelevantPreviewRowCount"
            :all-irrelevant-preview-rows-selected="
              allIrrelevantPreviewRowsSelected
            "
            :some-irrelevant-preview-rows-selected="
              someIrrelevantPreviewRowsSelected
            "
            :import-preview-groups="importPreviewGroups"
            :importing="importing"
            :import-progress-text="importProgressText"
            :import-progress-description="importProgressDescription"
            :import-primary-button-text="importPrimaryButtonText"
            :skipped-rows-groups="skippedRowsGroups"
            @restart="handleRestart"
            @open-difference-confirm-dialog="openDifferenceConfirmDialog"
            @remove-selected-preview-rows="handleRemoveSelectedPreviewRows"
            @restore-removed-preview-rows="handleRestoreRemovedPreviewRows"
            @select-irrelevant-rows-change="handleSelectIrrelevantRowsChange"
            @import-preview-selection-change="
              handleImportPreviewSelectionChange
            "
            @remove-single-preview-row="handleRemoveSinglePreviewRow"
            @load-full-preview="ensureFullPreviewDataLoaded"
            @import="handleImport"
          />
        </DataImportStepConfirm>

        <!-- 步骤按钮 -->
        <div v-if="!isCompletionStep" class="step-actions">
          <el-button
            v-if="
              currentStep > 0 &&
              !importResult &&
              !hasPendingDifferenceConfirmation
            "
            @click="goPrev"
          >
            上一步
          </el-button>
          <el-button
            v-if="
              !advancedMode &&
              currentStep === 1 &&
              !importResult &&
              canImportCurrentFile
            "
            type="primary"
            :loading="importing || batchConfirmImportRunning"
            :disabled="
              pendingSelectedSmartTableCount > 0 ||
              (selectedSmartTablesRequiringConfirmationCount === 0 &&
                !hasPendingDifferenceConfirmation &&
                previewDataCount === 0)
            "
            @click="handleSmartStructureBatchConfirmImport"
          >
            {{ smartBatchImportButtonText }}
          </el-button>
          <el-button
            v-if="!advancedMode && currentStep === 0"
            type="primary"
            :disabled="nextDisabled"
            :loading="smartRecognizing"
            @click="goNext"
          >
            {{ smartStageText || "识别并进入确认" }}
          </el-button>
          <el-button
            v-else-if="advancedMode && currentStep < steps.length - 1"
            type="primary"
            :disabled="nextDisabled"
            @click="goNext"
          >
            下一步
          </el-button>
        </div>

        <DataImportDifferenceConfirmDialog
          v-model="differenceConfirmDialogVisible"
          v-model:pending-difference-page="pendingDifferencePage"
          v-model:pending-difference-page-size="pendingDifferencePageSize"
          :is-excel-file="isExcelFile"
          :pending-differences="pendingDifferences"
          :paged-pending-differences="pagedPendingDifferences"
          :difference-decision-map="differenceDecisionMap"
          :pending-import-decision-count="pendingImportDecisionCount"
          :pending-partial-decision-count="pendingPartialDecisionCount"
          :pending-skip-decision-count="pendingSkipDecisionCount"
          :pending-undecided-count="pendingUndecidedCount"
          :pending-difference-display-start="pendingDifferenceDisplayStart"
          :pending-difference-display-end="pendingDifferenceDisplayEnd"
          :difference-dialog-footer-tip="differenceDialogFooterTip"
          :confirm-difference-button-text="confirmDifferenceButtonText"
          :importing="importing"
          @apply-decision-to-all="applyDifferenceDecisionToAll"
          @update-decision="
            (key, decision) => (differenceDecisionMap[key] = decision)
          "
          @confirm="handleConfirmPendingDifferences"
        />
        <SmartStructureFieldConflictDialog
          v-model:visible="fieldConflictDialogVisible"
          :conflicts="pendingFieldConflicts"
          :table-infos="smartTableInfos"
          :is-excel-file="isExcelFile"
          @cancel="handleFieldConflictCancel"
          @confirm="handleFieldConflictConfirm"
        />
      </el-card>
    </div>
  </div>
</template>

<style scoped src="./index.styles.css"></style>
