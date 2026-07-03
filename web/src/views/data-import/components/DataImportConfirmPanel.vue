<script setup lang="ts">
import { computed, ref } from "vue";
import type { AiServiceConfig } from "@/api/ai-service";
import type { Customer } from "@/api/customer";
import type { Process } from "@/api/process";
import type {
  CombinedImportResult,
  ImportDuplicateAiConfig,
  ImportPreviewGroup,
  ImportPreviewRow,
  SkippedRowsGroup,
  TableImportConfig
} from "../dataImport.types";

const props = defineProps<{
  importResult: CombinedImportResult | null;
  canUploadSourceFile: boolean;
  canImportAny: boolean;
  canImportCurrentFile: boolean;
  currentImportPermissionMessage: string;
  hasPendingDifferenceConfirmation: boolean;
  pendingDifferencesCount: number;
  hasCommittedImportProgress: boolean;
  committedSuccessCount: number;
  committedSkippedCount: number;
  committedFailedCount: number;
  uploadedFileName?: string;
  tableConfigs: TableImportConfig[];
  customers: Customer[];
  processes: Process[];
  selectedCustomerId?: number;
  selectedProcessId?: number;
  selectedMachineModelName: string;
  previewDataCount: number;
  importDuplicateAiConfig: ImportDuplicateAiConfig;
  loadingAiServices: boolean;
  embeddingServices: AiServiceConfig[];
  llmServices: AiServiceConfig[];
  removedPreviewRowCount: number;
  selectedImportPreviewRowsCount: number;
  importPreviewGroups: ImportPreviewGroup[];
  previewLoadState: {
    loadedRows: number;
    totalRows: number;
    hasPartialPreview: boolean;
  };
  previewSkippedRows: boolean;
  importing: boolean;
  importProgressText: string;
  importProgressDescription: string;
  importPrimaryButtonText: string;
  skippedRowsGroups: SkippedRowsGroup[];
}>();

const emit = defineEmits<{
  restart: [];
  openDifferenceConfirmDialog: [];
  removeSelectedPreviewRows: [];
  restoreRemovedPreviewRows: [];
  importPreviewSelectionChange: [tableIndex: number, rows: ImportPreviewRow[]];
  removeSinglePreviewRow: [row: ImportPreviewRow];
  "update:previewSkippedRows": [value: boolean];
  import: [];
}>();

const previewSkippedRowsModel = computed({
  get: () => props.previewSkippedRows,
  set: value => emit("update:previewSkippedRows", value)
});

// 父组件以引用方式传入配置对象，子组件通过该代理直接编辑其字段；
// mutation 经同一引用回传父组件，行为与直接改 prop 一致，同时规避 vue/no-mutating-props。
const duplicateAiConfig = computed(() => props.importDuplicateAiConfig);
const activeCollapseNames = ref<string[]>([]);
</script>

<template>
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
          @click="emit('restart')"
        >
          继续导入
        </el-button>
      </template>
    </el-result>

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
                <div class="skipped-cell-value">
                  {{ row.rowValues?.[col.index] || "" }}
                </div>
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
        :title="`检测到 ${pendingDifferencesCount} 条重复、差异或 AI 疑似重复数据，请在弹窗中逐条确认是否覆盖已有记录。`"
        description="左侧为数据库已有数据，右侧为本次待导入数据。未命中的数据已按当前流程处理。"
      />
      <div class="difference-entry__actions">
        <span
          v-if="hasCommittedImportProgress"
          class="difference-entry__summary"
        >
          已完成无重复数据处理：成功 {{ committedSuccessCount }} 条，跳过
          {{ committedSkippedCount }} 条，失败 {{ committedFailedCount }} 条
        </span>
        <el-button type="warning" @click="emit('openDifferenceConfirmDialog')">
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
    <div class="import-confirm-overview">
      <div class="overview-item overview-item--wide">
        <span class="overview-label">源文件</span>
        <span class="overview-value" :title="uploadedFileName">
          {{ uploadedFileName || "-" }}
        </span>
      </div>
      <div class="overview-item">
        <span class="overview-label">目标客户</span>
        <span class="overview-value">
          {{ customers.find(c => c.id === selectedCustomerId)?.name || "-" }}
        </span>
      </div>
      <div class="overview-item">
        <span class="overview-label">目标制程</span>
        <span class="overview-value">
          {{ processes.find(p => p.id === selectedProcessId)?.name || "-" }}
        </span>
      </div>
      <div class="overview-item">
        <span class="overview-label">目标机型</span>
        <span class="overview-value">{{
          selectedMachineModelName || "-"
        }}</span>
      </div>
      <div class="overview-item">
        <span class="overview-label">表格</span>
        <span class="overview-value">{{ tableConfigs.length }} 个</span>
      </div>
      <div class="overview-item">
        <span class="overview-label">预计导入</span>
        <span class="overview-value strong">{{ previewDataCount }} 条</span>
      </div>
    </div>

    <el-collapse v-model="activeCollapseNames" class="confirm-panel-collapse">
      <el-collapse-item name="duplicate-ai">
        <template #title>
          <div class="collapse-title">
            <span>导入设置</span>
            <span class="collapse-subtitle">
              AI 疑似重复识别：{{
                duplicateAiConfig.enableSemanticDuplicateCheck ? "开启" : "关闭"
              }}
            </span>
          </div>
        </template>
        <div class="duplicate-ai-panel">
          <div class="duplicate-ai-panel__header">
            <div>
              <div class="duplicate-ai-panel__title">AI 疑似重复识别</div>
              <div class="duplicate-ai-panel__desc">
                规则命中优先；未命中时再用 Embedding 召回候选，并可选用 LLM
                复核。
              </div>
            </div>
            <el-switch
              v-model="duplicateAiConfig.enableSemanticDuplicateCheck"
              active-text="开启"
              inactive-text="关闭"
            />
          </div>
          <el-form label-width="132px" class="duplicate-ai-form">
            <el-row :gutter="16">
              <el-col :span="12">
                <el-form-item label="Embedding 服务">
                  <el-select
                    v-model="duplicateAiConfig.embeddingServiceId"
                    placeholder="请选择 Embedding 服务"
                    :disabled="!duplicateAiConfig.enableSemanticDuplicateCheck"
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
                    v-model="duplicateAiConfig.semanticTopK"
                    :min="1"
                    :max="10"
                    :disabled="!duplicateAiConfig.enableSemanticDuplicateCheck"
                  />
                </el-form-item>
              </el-col>
              <el-col :span="12">
                <el-form-item label="召回阈值">
                  <el-slider
                    v-model="duplicateAiConfig.semanticMinScore"
                    :min="0"
                    :max="1"
                    :step="0.01"
                    :disabled="!duplicateAiConfig.enableSemanticDuplicateCheck"
                    :format-tooltip="
                      (val: number) => `${(val * 100).toFixed(0)}%`
                    "
                    show-input
                    :show-input-controls="false"
                  />
                </el-form-item>
              </el-col>
              <el-col :span="12">
                <el-form-item label="高置信阈值">
                  <el-slider
                    v-model="duplicateAiConfig.highConfidenceThreshold"
                    :min="0.5"
                    :max="1"
                    :step="0.01"
                    :disabled="!duplicateAiConfig.enableSemanticDuplicateCheck"
                    :format-tooltip="
                      (val: number) => `${(val * 100).toFixed(0)}%`
                    "
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
                  v-model="duplicateAiConfig.enableLlmDuplicateReview"
                  :disabled="!duplicateAiConfig.enableSemanticDuplicateCheck"
                />
              </div>
              <el-row :gutter="16">
                <el-col :span="12">
                  <el-form-item label="LLM 服务">
                    <el-select
                      v-model="duplicateAiConfig.llmServiceId"
                      placeholder="请选择 LLM 服务"
                      :disabled="
                        !duplicateAiConfig.enableSemanticDuplicateCheck ||
                        !duplicateAiConfig.enableLlmDuplicateReview
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
                      v-model="duplicateAiConfig.llmPassScore"
                      :min="0"
                      :max="1"
                      :step="0.01"
                      :disabled="
                        !duplicateAiConfig.enableSemanticDuplicateCheck ||
                        !duplicateAiConfig.enableLlmDuplicateReview
                      "
                      :format-tooltip="
                        (val: number) => `${(val * 100).toFixed(0)}%`
                      "
                      show-input
                      :show-input-controls="false"
                    />
                  </el-form-item>
                </el-col>
              </el-row>
            </div>
          </el-form>
        </div>
      </el-collapse-item>

      <el-collapse-item name="preview-list">
        <template #title>
          <div class="collapse-title">
            <span>待导入清单</span>
            <span class="collapse-subtitle">
              当前保留 {{ previewDataCount }} 条
              <template v-if="removedPreviewRowCount > 0">
                ，已剔除 {{ removedPreviewRowCount }} 条
              </template>
            </span>
          </div>
        </template>
        <div class="import-preview-panel">
          <div class="import-preview-toolbar">
            <div class="import-preview-summary">
              <span class="summary-title">待导入数据清单</span>
              <span class="summary-meta"
                >当前保留 {{ previewDataCount }} 条</span
              >
              <span
                v-if="removedPreviewRowCount > 0"
                class="summary-meta warning"
              >
                已剔除 {{ removedPreviewRowCount }} 条
              </span>
              <span
                v-if="previewLoadState.hasPartialPreview"
                class="summary-meta"
              >
                当前先显示前 {{ previewLoadState.loadedRows }} 条，导入前会自动补齐完整数据
              </span>
            </div>
            <div class="import-preview-actions">
              <el-button
                size="small"
                type="danger"
                plain
                :disabled="
                  hasPendingDifferenceConfirmation ||
                  selectedImportPreviewRowsCount === 0
                "
                @click="emit('removeSelectedPreviewRows')"
              >
                批量删除（{{ selectedImportPreviewRowsCount }}）
              </el-button>
              <el-button
                size="small"
                :disabled="
                  hasPendingDifferenceConfirmation ||
                  removedPreviewRowCount === 0
                "
                @click="emit('restoreRemovedPreviewRows')"
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
                row-key="key"
                reserve-selection
                @selection-change="
                  rows =>
                    emit('importPreviewSelectionChange', group.tableIndex, rows)
                "
              >
                <el-table-column type="selection" width="48" />
                <el-table-column
                  prop="displayRowNumber"
                  label="行号"
                  width="80"
                />
                <el-table-column
                  prop="project"
                  label="项目"
                  min-width="140"
                  show-overflow-tooltip
                >
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
                <el-table-column
                  prop="remark"
                  label="备注"
                  min-width="160"
                  show-overflow-tooltip
                >
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
                      @click="emit('removeSinglePreviewRow', row)"
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
      </el-collapse-item>
    </el-collapse>

    <div class="import-actions">
      <div class="skip-preview-switch">
        <span class="label">预览未导入明细</span>
        <el-switch
          v-model="previewSkippedRowsModel"
          :disabled="importing || hasPendingDifferenceConfirmation"
          active-text="开启"
          inactive-text="关闭"
        />
      </div>
      <div v-if="importing" class="import-progress-panel">
        <div class="import-progress-panel__title">{{ importProgressText }}</div>
        <div class="import-progress-panel__desc">
          {{ importProgressDescription }}
        </div>
      </div>
      <el-button
        v-if="canImportCurrentFile"
        type="primary"
        size="large"
        :loading="importing"
        :disabled="!hasPendingDifferenceConfirmation && previewDataCount === 0"
        @click="emit('import')"
      >
        {{ importPrimaryButtonText }}
      </el-button>
    </div>
  </div>
</template>
