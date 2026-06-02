<script setup lang="ts">
import { computed } from "vue";
import TablePreview from "./components/TablePreview.vue";
import ColumnMapping from "./components/ColumnMapping.vue";
import DataImportConfirmPanel from "./components/DataImportConfirmPanel.vue";
import DataImportDifferenceConfirmDialog from "./components/DataImportDifferenceConfirmDialog.vue";
import DataImportDifferenceDialog from "./components/DataImportDifferenceDialog.vue";
import DataImportStepConfirm from "./components/DataImportStepConfirm.vue";
import DataImportStepMapping from "./components/DataImportStepMapping.vue";
import DataImportStepTableSelect from "./components/DataImportStepTableSelect.vue";
import DataImportStepTarget from "./components/DataImportStepTarget.vue";
import DataImportStepUpload from "./components/DataImportStepUpload.vue";
import ExcelColumnMapping from "./components/ExcelColumnMapping.vue";
import { useDataImportPage } from "./composables/useDataImportPage";
// 映射、预览选择、导入执行 composable（由 useDataImportPage 内部组合）
import { useDataImportMapping } from "./composables/useDataImportMapping";
import { useDataImportPreviewSelection } from "./composables/useDataImportPreviewSelection";
import { useDataImportExecution } from "./composables/useDataImportExecution";

defineOptions({
  name: "ImportData"
});

const {
  MAPPING_PREVIEW_ROWS,
  currentStep,
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
  affixTarget,
  affixOffset,
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
} = useDataImportPage();

const activeTableTabModel = computed({
  get: () => activeTableIndex.value ?? undefined,
  set: value => {
    activeTableIndex.value = typeof value === "number" ? value : Number(value);
  }
});
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
                  cfg.tableInfo?.usedRangeStartRow !== undefined
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
        <DataImportConfirmPanel
          v-model:preview-skipped-rows="previewSkippedRows"
          :import-result="importResult"
          :can-upload-source-file="canUploadSourceFile"
          :can-import-any="canImportAny"
          :can-import-current-file="canImportCurrentFile"
          :current-import-permission-message="currentImportPermissionMessage"
          :has-pending-difference-confirmation="hasPendingDifferenceConfirmation"
          :pending-differences-count="pendingDifferences.length"
          :has-committed-import-progress="hasCommittedImportProgress"
          :committed-success-count="committedImportAggregate?.successCount || 0"
          :committed-skipped-count="committedImportAggregate?.skippedCount || 0"
          :committed-failed-count="committedImportAggregate?.failedCount || 0"
          :uploaded-file-name="uploadedFile?.fileName"
          :table-configs="tableConfigs"
          :customers="customers"
          :processes="processes"
          :selected-customer-id="selectedCustomerId"
          :selected-process-id="selectedProcessId"
          :selected-machine-model-name="selectedMachineModelName"
          :preview-data-count="previewDataCount"
          :import-duplicate-ai-config="importDuplicateAiConfig"
          :loading-ai-services="loadingAiServices"
          :embedding-services="embeddingServices"
          :llm-services="llmServices"
          :removed-preview-row-count="removedPreviewRowCount"
          :selected-import-preview-rows-count="selectedImportPreviewRowsCount"
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
          @import-preview-selection-change="handleImportPreviewSelectionChange"
          @remove-single-preview-row="handleRemoveSinglePreviewRow"
          @import="handleImport"
        />
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

      <DataImportDifferenceConfirmDialog
        v-model="differenceConfirmDialogVisible"
        v-model:pending-difference-page="pendingDifferencePage"
        v-model:pending-difference-page-size="pendingDifferencePageSize"
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
        @update-decision="(key, decision) => (differenceDecisionMap[key] = decision)"
        @confirm="handleConfirmPendingDifferences"
      />
      </el-card>
    </div>
  </div>
</template>

<style scoped src="./index.styles.css"></style>
