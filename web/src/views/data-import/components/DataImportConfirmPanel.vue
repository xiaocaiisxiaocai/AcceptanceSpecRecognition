<script setup lang="ts">
import { computed, ref } from "vue";
import { ArrowRight, InfoFilled } from "@element-plus/icons-vue";
import type { AiServiceSelection } from "@/api/ai-service";
import type { Customer } from "@/api/customer";
import type { Process } from "@/api/process";
import { getDistinctAiServiceModel } from "@/views/shared/ai-service-display";
import {
  getRuntimeAiSelectionStatusText,
  isRuntimeAiSelectionAvailable
} from "@/utils/runtime-ai-selection";
import type {
  CombinedImportResult,
  ImportDuplicateAiConfig,
  ImportPreviewGroup,
  ImportPreviewRow,
  SkippedRowsGroup,
  TableImportConfig
} from "../dataImport.types";
import {
  mergeSkippedPreviewCellValues,
  shouldBackfillProjectFromSpecification
} from "../dataImport.helpers";

const props = withDefaults(
  defineProps<{
    importResult: CombinedImportResult | null;
    isExcelFile: boolean;
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
    selectedSheetCount?: number;
    pendingSelectedSheetCount?: number;
    customers: Customer[];
    processes: Process[];
    selectedCustomerId?: number;
    selectedProcessId?: number;
    selectedMachineModelName: string;
    previewDataCount: number;
    importDuplicateAiConfig: ImportDuplicateAiConfig;
    loadingAiServices: boolean;
    embeddingSelection: AiServiceSelection;
    llmSelection: AiServiceSelection;
    removedPreviewRowCount: number;
    selectedImportPreviewRowsCount: number;
    importPreviewGroups: ImportPreviewGroup[];
    previewLoadState: {
      loadedRows: number;
      totalRows: number;
      hasPartialPreview: boolean;
    };
    importing: boolean;
    importProgressText: string;
    importProgressDescription: string;
    importPrimaryButtonText: string;
    skippedRowsGroups: SkippedRowsGroup[];
    showImportAction?: boolean;
    allowEmptyPreviewAction?: boolean;
  }>(),
  {
    showImportAction: true,
    allowEmptyPreviewAction: false
  }
);

const emit = defineEmits<{
  restart: [];
  openDifferenceConfirmDialog: [];
  removeSelectedPreviewRows: [];
  restoreRemovedPreviewRows: [];
  importPreviewSelectionChange: [tableIndex: number, rows: ImportPreviewRow[]];
  removeSinglePreviewRow: [row: ImportPreviewRow];
  loadFullPreview: [];
  import: [];
}>();

// 父组件以引用方式传入配置对象，子组件通过该代理直接编辑其字段；
// mutation 经同一引用回传父组件，行为与直接改 prop 一致，同时规避 vue/no-mutating-props。
const duplicateAiConfig = computed(() => props.importDuplicateAiConfig);
const activeCollapseNames = ref<string[]>([]);
const showDuplicateAdvanced = ref(false);
const hasAvailableEmbeddingService = computed(() =>
  isRuntimeAiSelectionAvailable(props.embeddingSelection)
);
const hasAvailableLlmService = computed(() =>
  isRuntimeAiSelectionAvailable(props.llmSelection)
);
const embeddingServiceModel = computed(() =>
  getDistinctAiServiceModel(
    props.embeddingSelection.name,
    props.embeddingSelection.model
  )
);
const llmServiceModel = computed(() =>
  getDistinctAiServiceModel(props.llmSelection.name, props.llmSelection.model)
);
const embeddingStatusText = computed(() =>
  getRuntimeAiSelectionStatusText(props.embeddingSelection, "Embedding")
);
const llmStatusText = computed(() =>
  getRuntimeAiSelectionStatusText(props.llmSelection, "LLM")
);
const duplicateCheckSummary = computed(() => {
  if (!duplicateAiConfig.value.enableSemanticDuplicateCheck) {
    return "仅使用规则检查";
  }

  const parts = [
    `每条最多 ${duplicateAiConfig.value.semanticTopK} 个候选`,
    `最低相似度 ${(duplicateAiConfig.value.semanticMinScore * 100).toFixed(0)}%`
  ];
  if (duplicateAiConfig.value.enableLlmDuplicateReview) {
    parts.push("LLM 复核已开启");
  }
  return parts.join(" · ");
});
const handleCollapseChange = (
  activeNames: string | number | Array<string | number>
) => {
  const names = Array.isArray(activeNames) ? activeNames : [activeNames];
  if (
    names.includes("preview-list") &&
    props.previewLoadState.hasPartialPreview
  ) {
    emit("loadFullPreview");
  }
};
const hasSpecificationOnlyBackfillTables = computed(() =>
  props.tableConfigs.some(shouldBackfillProjectFromSpecification)
);
const formatResultRowNumber = (rowIndex: number) =>
  props.isExcelFile ? rowIndex : rowIndex + 1;
const effectiveSelectedSheetCount = computed(
  () => props.selectedSheetCount ?? props.tableConfigs.length
);
const effectivePendingSelectedSheetCount = computed(
  () => props.pendingSelectedSheetCount ?? 0
);
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
            {{ formatResultRowNumber(row.rowIndex) }}
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
        title="已跳过部分数据，但未返回可展示的行明细"
        description="请查看导入日志或重新导入；系统已默认请求保留未导入明细。"
      />
      <div v-else>
        <div
          v-for="group in skippedRowsGroups"
          :key="`skip-group-${group.tableIndex}-${group.regionId ?? 'default'}`"
          class="skipped-group"
        >
          <div v-if="skippedRowsGroups.length > 1" class="skipped-group-title">
            表格 {{ group.tableIndex + 1
            }}<template v-if="group.regionIndex !== undefined"
              >，区域 {{ group.regionIndex + 1 }}</template
            >
          </div>
          <el-table
            :data="group.rows"
            max-height="220"
            size="small"
            class="skipped-rows-table"
          >
            <el-table-column prop="tableIndex" label="表格" width="80">
              <template #default="{ row }">
                {{ row.tableIndex + 1 }}
              </template>
            </el-table-column>
            <el-table-column
              label="行号"
              width="min(100px, calc(100vw - 32px))"
            >
              <template #default="{ row }">
                {{ formatResultRowNumber(row.rowIndex) }}
              </template>
            </el-table-column>
            <el-table-column
              prop="message"
              label="跳过原因"
              min-width="min(220px, calc(100vw - 32px))"
              show-overflow-tooltip
            />
            <el-table-column
              v-for="col in group.columns"
              :key="`skip-col-${group.tableIndex}-${col.indexes.join('-')}`"
              :label="col.label"
              min-width="min(140px, calc(100vw - 32px))"
            >
              <template #default="{ row }">
                <div class="skipped-cell-value">
                  {{
                    mergeSkippedPreviewCellValues(row.rowValues, col.indexes)
                  }}
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
    <el-alert
      v-if="hasSpecificationOnlyBackfillTables"
      type="warning"
      :closable="false"
      show-icon
      title="项目将使用规格内容自动补齐"
      description="仅规格导入会将项目和规格写成同一内容，可能增加跨项目匹配风险；请确认没有独立项目列，再开始导入。"
      class="mb-4"
    />
    <el-alert
      v-if="effectivePendingSelectedSheetCount > 0"
      type="warning"
      :closable="false"
      show-icon
      :title="`已勾选 ${effectivePendingSelectedSheetCount} 张待配置 Sheet`"
      description="请切换到对应 Sheet 补齐必填列；全部可确认后，即可使用文件级主操作统一学习并导入。"
      class="mb-4"
    />
    <div class="import-summary-bar">
      <div class="import-summary-bar__meta">
        <span class="import-summary-bar__file" :title="uploadedFileName">
          {{ uploadedFileName || "-" }}
        </span>
        <span>
          客户：{{
            customers.find(c => c.id === selectedCustomerId)?.name || "-"
          }}
        </span>
        <span>
          制程：{{
            processes.find(p => p.id === selectedProcessId)?.name || "-"
          }}
        </span>
        <span>机型：{{ selectedMachineModelName || "-" }}</span>
        <span>已勾选 {{ effectiveSelectedSheetCount }} 张 Sheet</span>
        <span v-if="effectivePendingSelectedSheetCount > 0">
          可导入 {{ tableConfigs.length }} 张，待配置
          {{ effectivePendingSelectedSheetCount }} 张
        </span>
        <span class="import-summary-bar__count"
          >已配置合计预计 {{ previewDataCount }} 条</span
        >
      </div>
      <div class="import-summary-bar__actions">
        <el-button
          v-if="showImportAction && canImportCurrentFile"
          type="primary"
          :loading="importing"
          :disabled="
            effectivePendingSelectedSheetCount > 0 ||
            (!allowEmptyPreviewAction &&
              !hasPendingDifferenceConfirmation &&
              previewDataCount === 0)
          "
          @click="emit('import')"
        >
          {{ importPrimaryButtonText }}
        </el-button>
      </div>
      <div v-if="importing" class="import-summary-bar__progress">
        <strong>{{ importProgressText }}</strong>
        <span>{{ importProgressDescription }}</span>
      </div>
    </div>

    <el-collapse
      v-model="activeCollapseNames"
      class="confirm-panel-collapse"
      @change="handleCollapseChange"
    >
      <el-collapse-item name="duplicate-ai">
        <template #title>
          <div class="collapse-title">
            <span class="collapse-title__main">导入设置</span>
            <span
              class="collapse-status"
              :class="{
                active: duplicateAiConfig.enableSemanticDuplicateCheck
              }"
            >
              疑似重复检查{{
                loadingAiServices || embeddingSelection.status === "checking"
                  ? "检测中"
                  : duplicateAiConfig.enableSemanticDuplicateCheck
                    ? "已开启"
                    : "未开启"
              }}
            </span>
          </div>
        </template>
        <div class="duplicate-ai-panel">
          <div class="duplicate-ai-panel__header">
            <div class="duplicate-ai-panel__heading">
              <div class="duplicate-ai-panel__mark" aria-hidden="true">AI</div>
              <div>
                <div class="duplicate-ai-panel__title">AI 疑似重复检查</div>
                <div class="duplicate-ai-panel__desc">
                  先按规则检查；未命中时比较“项目 +
                  规格”的语义相似度。命中结果只会进入人工确认，不会自动覆盖。
                </div>
              </div>
            </div>
            <div class="duplicate-ai-panel__control">
              <span>{{
                loadingAiServices || embeddingSelection.status === "checking"
                  ? "检测中"
                  : duplicateAiConfig.enableSemanticDuplicateCheck
                    ? "已开启"
                    : "已关闭"
              }}</span>
              <el-switch
                v-model="duplicateAiConfig.enableSemanticDuplicateCheck"
                aria-label="启用 AI 疑似重复识别"
                :disabled="loadingAiServices || !hasAvailableEmbeddingService"
              />
            </div>
          </div>
          <div
            v-if="!duplicateAiConfig.enableSemanticDuplicateCheck"
            class="duplicate-ai-panel__inactive"
          >
            AI
            疑似重复检查当前关闭，本次导入仅使用完全相同、同项目同规格等规则判断。
            <span class="duplicate-ai-panel__runtime-status">
              {{ embeddingStatusText }}
            </span>
          </div>
          <el-form v-else label-position="top" class="duplicate-ai-form">
            <el-row :gutter="16">
              <el-col :xs="24" :md="12">
                <el-form-item label="Embedding 服务">
                  <div
                    v-if="hasAvailableEmbeddingService"
                    class="duplicate-ai-service"
                    role="status"
                    aria-live="polite"
                  >
                    <span>自动使用</span>
                    <strong>{{ embeddingSelection.name }}</strong>
                    <small v-if="embeddingServiceModel">
                      {{ embeddingServiceModel }}
                    </small>
                  </div>
                  <el-alert
                    v-else
                    type="info"
                    :closable="false"
                    :title="embeddingStatusText"
                  />
                </el-form-item>
              </el-col>
              <el-col :xs="24" :md="12">
                <div class="duplicate-ai-panel__llm">
                  <div class="llm-toggle">
                    <div>
                      <strong>LLM 二次复核</strong>
                      <span>对语义候选进一步判断是否为同一条规格</span>
                    </div>
                    <div class="llm-toggle__control">
                      <span>{{
                        loadingAiServices || llmSelection.status === "checking"
                          ? "检测中"
                          : duplicateAiConfig.enableLlmDuplicateReview
                            ? "已开启"
                            : "未开启"
                      }}</span>
                      <el-switch
                        v-model="duplicateAiConfig.enableLlmDuplicateReview"
                        aria-label="启用 LLM 二次复核"
                        :disabled="loadingAiServices || !hasAvailableLlmService"
                      />
                    </div>
                  </div>
                </div>
              </el-col>
              <el-col
                v-if="duplicateAiConfig.enableLlmDuplicateReview"
                :xs="24"
                :md="12"
              >
                <el-form-item label="LLM 服务">
                  <div
                    v-if="hasAvailableLlmService"
                    class="duplicate-ai-service"
                    role="status"
                    aria-live="polite"
                  >
                    <span>自动使用</span>
                    <strong>{{ llmSelection.name }}</strong>
                    <small v-if="llmServiceModel">{{ llmServiceModel }}</small>
                  </div>
                  <el-alert
                    v-else
                    type="info"
                    :closable="false"
                    :title="llmStatusText"
                  />
                </el-form-item>
              </el-col>
            </el-row>

            <div class="duplicate-ai-summary">
              <div>
                <span>当前策略</span>
                <strong>{{ duplicateCheckSummary }}</strong>
              </div>
              <div class="duplicate-ai-summary__services">
                <span>
                  Embedding：{{ embeddingSelection.model || "运行时自动选择" }}
                </span>
                <span v-if="duplicateAiConfig.enableLlmDuplicateReview">
                  LLM：{{ llmSelection.model || llmStatusText }}
                </span>
              </div>
            </div>

            <button
              type="button"
              class="duplicate-ai-advanced-toggle"
              :aria-expanded="showDuplicateAdvanced"
              aria-controls="duplicate-ai-advanced-options"
              @click="showDuplicateAdvanced = !showDuplicateAdvanced"
            >
              <span>
                <strong>高级参数</strong>
                <small>候选数量、相似度门槛与置信标签</small>
              </span>
              <el-icon :class="{ rotated: showDuplicateAdvanced }">
                <ArrowRight />
              </el-icon>
            </button>

            <el-collapse-transition>
              <div
                v-show="showDuplicateAdvanced"
                id="duplicate-ai-advanced-options"
                class="duplicate-ai-advanced"
              >
                <el-row :gutter="16">
                  <el-col :xs="24" :md="12">
                    <el-form-item label="每条最多候选数">
                      <el-input-number
                        v-model="duplicateAiConfig.semanticTopK"
                        :min="1"
                        :max="10"
                        style="width: 100%"
                      />
                      <div class="duplicate-ai-field-tip">
                        规则未命中后，每条数据最多保留多少个语义候选。
                      </div>
                    </el-form-item>
                  </el-col>
                  <el-col :xs="24" :md="12">
                    <el-form-item label="最低相似度">
                      <el-slider
                        v-model="duplicateAiConfig.semanticMinScore"
                        :min="0"
                        :max="1"
                        :step="0.01"
                        :format-tooltip="
                          (val: number) => `${(val * 100).toFixed(0)}%`
                        "
                        show-input
                        :show-input-controls="false"
                      />
                      <div class="duplicate-ai-field-tip">
                        低于该分数的候选不会进入疑似重复清单。
                      </div>
                    </el-form-item>
                  </el-col>
                  <el-col :xs="24" :md="12">
                    <el-form-item label="高置信标签阈值">
                      <el-slider
                        v-model="duplicateAiConfig.highConfidenceThreshold"
                        :min="0.5"
                        :max="1"
                        :step="0.01"
                        :format-tooltip="
                          (val: number) => `${(val * 100).toFixed(0)}%`
                        "
                        show-input
                        :show-input-controls="false"
                      />
                      <div class="duplicate-ai-field-tip">
                        仅控制确认弹窗中的“高置信”标签，不会自动覆盖数据。
                      </div>
                    </el-form-item>
                  </el-col>
                  <el-col
                    v-if="duplicateAiConfig.enableLlmDuplicateReview"
                    :xs="24"
                    :md="12"
                  >
                    <el-form-item label="LLM 通过阈值">
                      <el-slider
                        v-model="duplicateAiConfig.llmPassScore"
                        :min="0"
                        :max="1"
                        :step="0.01"
                        :format-tooltip="
                          (val: number) => `${(val * 100).toFixed(0)}%`
                        "
                        show-input
                        :show-input-controls="false"
                      />
                      <div class="duplicate-ai-field-tip">
                        LLM 评分达到该值后，候选才会进入人工确认。
                      </div>
                    </el-form-item>
                  </el-col>
                </el-row>
              </div>
            </el-collapse-transition>
          </el-form>
        </div>
      </el-collapse-item>

      <el-collapse-item name="preview-list">
        <template #title>
          <div class="collapse-title">
            <span class="collapse-title__main">待导入清单</span>
            <span class="collapse-subtitle">
              已配置 Sheet 合计 {{ previewDataCount }} 条
              <template v-if="removedPreviewRowCount > 0">
                · 已移出 {{ removedPreviewRowCount }} 条
              </template>
            </span>
          </div>
        </template>
        <div class="import-preview-panel">
          <div class="import-preview-toolbar">
            <div class="import-preview-summary">
              <div class="preview-metric primary">
                <strong>{{ previewDataCount }}</strong>
                <span>待导入</span>
              </div>
              <div class="preview-metric">
                <strong>{{ previewLoadState.loadedRows }}</strong>
                <span>当前显示</span>
              </div>
              <div
                v-if="removedPreviewRowCount > 0"
                class="preview-metric warning"
              >
                <strong>{{ removedPreviewRowCount }}</strong>
                <span>已移出</span>
              </div>
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
                移出所选（{{ selectedImportPreviewRowsCount }}）
              </el-button>
              <el-button
                size="small"
                :disabled="
                  hasPendingDifferenceConfirmation ||
                  removedPreviewRowCount === 0
                "
                @click="emit('restoreRemovedPreviewRows')"
              >
                恢复移出项
              </el-button>
            </div>
          </div>

          <div class="import-preview-note">
            <el-icon><InfoFilled /></el-icon>
            <span>
              移出仅影响本次导入，不会修改原文件。
              <template v-if="previewLoadState.hasPartialPreview">
                当前为前
                {{ previewLoadState.loadedRows }}
                条预览，导入前会自动补齐完整数据。
              </template>
            </span>
          </div>

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
                  min-width="min(140px, calc(100vw - 32px))"
                  show-overflow-tooltip
                >
                  <template #default="{ row }">
                    {{ row.project || "-" }}
                  </template>
                </el-table-column>
                <el-table-column
                  prop="specification"
                  label="规格"
                  min-width="min(260px, calc(100vw - 32px))"
                  show-overflow-tooltip
                >
                  <template #default="{ row }">
                    {{ row.specification || "-" }}
                  </template>
                </el-table-column>
                <el-table-column
                  prop="acceptance"
                  label="验收"
                  min-width="min(160px, calc(100vw - 32px))"
                  show-overflow-tooltip
                >
                  <template #default="{ row }">
                    {{ row.acceptance || "-" }}
                  </template>
                </el-table-column>
                <el-table-column
                  prop="remark"
                  label="备注"
                  min-width="min(160px, calc(100vw - 32px))"
                  show-overflow-tooltip
                >
                  <template #default="{ row }">
                    {{ row.remark || "-" }}
                  </template>
                </el-table-column>
                <el-table-column
                  label="操作"
                  width="min(100px, calc(100vw - 32px))"
                  fixed="right"
                >
                  <template #default="{ row }">
                    <el-button
                      type="danger"
                      link
                      :disabled="hasPendingDifferenceConfirmation"
                      @click="emit('removeSinglePreviewRow', row)"
                    >
                      移出
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
  </div>
</template>

<style scoped>
.skipped-rows-table :deep(.el-table__header .cell) {
  overflow: hidden;
  text-overflow: ellipsis;
  word-break: keep-all;
  white-space: nowrap;
}

.import-summary-bar {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  gap: 10px 16px;
  align-items: center;
  padding: 12px;
  margin-bottom: 12px;
  background: var(--app-info-bg);
  border: 1px solid var(--app-border);
  border-radius: 8px;
}

.import-summary-bar__meta,
.import-summary-bar__actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px 14px;
  align-items: center;
  min-width: 0;
  font-size: 13px;
  color: var(--app-text-secondary);
}

.import-summary-bar__actions {
  justify-content: flex-end;
  color: var(--app-text-primary);
}

.import-summary-bar__file {
  min-width: 0;
  max-width: min(340px, 42vw);
  overflow: hidden;
  text-overflow: ellipsis;
  font-weight: 600;
  color: var(--app-text-primary);
  white-space: nowrap;
}

.import-summary-bar__count {
  font-weight: 600;
  color: var(--app-primary);
}

.import-summary-bar__progress {
  display: flex;
  grid-column: 1 / -1;
  gap: 8px;
  align-items: baseline;
  padding-top: 8px;
  font-size: 12px;
  color: var(--app-text-secondary);
  border-top: 1px dashed var(--app-border);
}

.import-summary-bar__progress strong {
  color: var(--app-primary);
}

.confirm-panel-collapse {
  margin-top: 10px;
  border-top: 1px solid var(--app-border);
  border-bottom: 1px solid var(--app-border);
}

.confirm-panel-collapse :deep(.el-collapse-item__header) {
  height: 48px;
  padding: 0 10px;
  font-weight: 600;
}

.confirm-panel-collapse :deep(.el-collapse-item__content) {
  padding: 0 10px 16px;
}

.collapse-title {
  display: flex;
  gap: 10px;
  align-items: center;
  min-width: 0;
}

.collapse-title__main {
  font-size: 14px;
  font-weight: 650;
  color: var(--app-text-primary);
}

.collapse-subtitle {
  overflow: hidden;
  text-overflow: ellipsis;
  font-size: 12px;
  font-weight: 400;
  color: var(--app-text-secondary);
  white-space: nowrap;
}

.collapse-status {
  padding: 2px 8px;
  font-size: 11px;
  font-weight: 500;
  line-height: 18px;
  color: var(--app-text-secondary);
  background: var(--el-fill-color);
  border: 1px solid var(--app-border);
  border-radius: 999px;
}

.collapse-status.active {
  color: var(--el-color-success-dark-2);
  background: var(--el-color-success-light-9);
  border-color: var(--el-color-success-light-5);
}

.duplicate-ai-panel {
  overflow: hidden;
  background: var(--app-bg-card);
  border: 1px solid var(--app-border);
  border-radius: 10px;
}

.duplicate-ai-panel__header,
.duplicate-ai-panel__heading,
.duplicate-ai-panel__control,
.llm-toggle,
.llm-toggle__control {
  display: flex;
  align-items: center;
}

.duplicate-ai-panel__header {
  gap: 18px;
  justify-content: space-between;
  padding: 14px 16px;
  background: linear-gradient(
    110deg,
    var(--app-info-bg) 0%,
    var(--app-bg-card) 72%
  );
  border-bottom: 1px solid var(--app-border);
}

.duplicate-ai-panel__heading {
  gap: 12px;
  min-width: 0;
}

.duplicate-ai-panel__mark {
  display: grid;
  flex: 0 0 36px;
  place-items: center;
  width: 36px;
  height: 36px;
  font-size: 12px;
  font-weight: 750;
  color: var(--app-primary);
  letter-spacing: 0.06em;
  background: var(--el-color-primary-light-9);
  border: 1px solid var(--el-color-primary-light-7);
  border-radius: 9px;
}

.duplicate-ai-panel__title {
  font-size: 14px;
  font-weight: 650;
  color: var(--app-text-primary);
}

.duplicate-ai-panel__desc {
  margin-top: 3px;
  font-size: 12px;
  line-height: 1.5;
  color: var(--app-text-secondary);
}

.duplicate-ai-panel__control,
.llm-toggle__control {
  flex: 0 0 auto;
  gap: 8px;
  font-size: 12px;
  color: var(--app-text-secondary);
}

.duplicate-ai-panel__inactive {
  padding: 13px 16px;
  font-size: 12px;
  line-height: 1.6;
  color: var(--app-text-secondary);
  background: var(--el-fill-color-extra-light);
}

.duplicate-ai-panel__runtime-status {
  display: block;
  margin-top: 4px;
  font-weight: 600;
  color: var(--el-text-color-primary);
}

.duplicate-ai-service {
  display: flex;
  gap: 8px;
  align-items: center;
  width: 100%;
  min-height: 40px;
  padding: 8px 11px;
  background: var(--el-fill-color-extra-light);
  border: 1px solid var(--app-border);
  border-radius: 8px;
}

.duplicate-ai-service span,
.duplicate-ai-service small {
  font-size: 12px;
  color: var(--app-text-secondary);
}

.duplicate-ai-service strong {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  font-size: 13px;
  color: var(--app-text-primary);
  white-space: nowrap;
}

.duplicate-ai-form {
  padding: 16px 16px 2px;
}

.duplicate-ai-form :deep(.el-form-item) {
  margin-bottom: 16px;
}

.duplicate-ai-form :deep(.el-form-item__label) {
  height: auto;
  padding: 0 0 7px;
  font-size: 12px;
  font-weight: 600;
  line-height: 1.4;
  color: var(--app-text-secondary);
}

.duplicate-ai-form :deep(.el-slider__runway.show-input) {
  margin-right: 16px;
}

.duplicate-ai-panel__llm {
  padding: 11px 12px;
  margin-bottom: 16px;
  background: var(--el-fill-color-extra-light);
  border: 1px solid var(--app-border);
  border-radius: 8px;
}

.llm-toggle {
  gap: 16px;
  justify-content: space-between;
}

.llm-toggle > div:first-child {
  display: flex;
  flex-direction: column;
  gap: 3px;
  min-width: 0;
}

.llm-toggle strong {
  font-size: 13px;
  font-weight: 650;
  color: var(--app-text-primary);
}

.llm-toggle > div:first-child span {
  font-size: 12px;
  color: var(--app-text-secondary);
}

.duplicate-ai-summary {
  display: flex;
  gap: 16px;
  align-items: flex-start;
  justify-content: space-between;
  padding: 11px 13px;
  margin-bottom: 10px;
  background: var(--el-color-primary-light-9);
  border: 1px solid var(--el-color-primary-light-7);
  border-radius: 8px;
}

.duplicate-ai-summary > div:first-child,
.duplicate-ai-summary__services {
  display: flex;
  flex-direction: column;
  gap: 3px;
}

.duplicate-ai-summary span,
.duplicate-ai-summary strong {
  font-size: 12px;
  line-height: 1.5;
}

.duplicate-ai-summary span {
  color: var(--app-text-secondary);
}

.duplicate-ai-summary strong {
  font-weight: 650;
  color: var(--app-text-primary);
}

.duplicate-ai-summary__services {
  min-width: 0;
  text-align: right;
}

.duplicate-ai-summary__services span {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.duplicate-ai-advanced-toggle {
  display: flex;
  gap: 16px;
  align-items: center;
  justify-content: space-between;
  width: 100%;
  padding: 10px 12px;
  margin: 0 0 12px;
  font: inherit;
  color: var(--app-text-primary);
  text-align: left;
  cursor: pointer;
  background: transparent;
  border: 1px solid var(--app-border);
  border-radius: 8px;
  transition:
    color 180ms ease,
    background-color 180ms ease,
    border-color 180ms ease;
}

.duplicate-ai-advanced-toggle:hover {
  color: var(--app-primary);
  background: var(--el-fill-color-extra-light);
  border-color: var(--el-color-primary-light-5);
}

.duplicate-ai-advanced-toggle:focus-visible {
  outline: 2px solid var(--app-primary);
  outline-offset: 2px;
}

.duplicate-ai-advanced-toggle > span {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.duplicate-ai-advanced-toggle strong {
  font-size: 13px;
  font-weight: 650;
}

.duplicate-ai-advanced-toggle small {
  font-size: 12px;
  color: var(--app-text-secondary);
}

.duplicate-ai-advanced-toggle .el-icon {
  flex: 0 0 auto;
  transition: transform 180ms ease;
}

.duplicate-ai-advanced-toggle .el-icon.rotated {
  transform: rotate(90deg);
}

.duplicate-ai-advanced {
  padding: 14px 14px 0;
  margin-bottom: 14px;
  background: var(--el-fill-color-extra-light);
  border: 1px solid var(--app-border);
  border-radius: 8px;
}

.duplicate-ai-field-tip {
  width: 100%;
  margin-top: 5px;
  font-size: 12px;
  line-height: 1.5;
  color: var(--app-text-secondary);
}

.import-preview-panel {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.import-preview-toolbar {
  display: flex;
  flex-wrap: wrap;
  gap: 14px;
  align-items: center;
  justify-content: space-between;
  padding: 11px 14px;
  background: var(--el-fill-color-extra-light);
  border: 1px solid var(--app-border);
  border-radius: 8px;
}

.import-preview-summary,
.import-preview-actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
}

.import-preview-summary {
  gap: 0;
}

.preview-metric {
  display: flex;
  gap: 6px;
  align-items: baseline;
  padding: 0 14px;
  color: var(--app-text-secondary);
  border-left: 1px solid var(--app-border);
}

.preview-metric:first-child {
  padding-left: 0;
  border-left: 0;
}

.preview-metric strong {
  font-size: 18px;
  font-weight: 700;
  color: var(--app-text-primary);
  letter-spacing: -0.03em;
}

.preview-metric span {
  font-size: 12px;
}

.preview-metric.primary strong {
  color: var(--app-primary);
}

.preview-metric.warning strong {
  color: var(--app-warning);
}

.import-preview-actions {
  gap: 8px;
}

.import-preview-note {
  display: flex;
  gap: 7px;
  align-items: flex-start;
  padding: 0 2px;
  font-size: 12px;
  line-height: 1.6;
  color: var(--app-text-secondary);
}

.import-preview-note .el-icon {
  flex: 0 0 auto;
  margin-top: 3px;
  color: var(--el-color-info);
}

.import-preview-groups {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.import-preview-group {
  overflow: hidden;
  background: var(--app-bg-card);
  border: 1px solid var(--app-border);
  border-radius: 8px;
}

.import-preview-group__header {
  display: flex;
  gap: 12px;
  align-items: center;
  justify-content: space-between;
  padding: 9px 12px;
  font-size: 13px;
  font-weight: 600;
  color: var(--app-text-primary);
  background: var(--el-fill-color-extra-light);
  border-bottom: 1px solid var(--app-border);
}

.group-count {
  font-size: 12px;
  font-weight: 500;
  color: var(--app-text-secondary);
}

.import-preview-group :deep(.el-table) {
  --el-table-border-color: var(--app-border);

  border: 0;
}

.import-preview-group :deep(.el-table__inner-wrapper::before) {
  display: none;
}

@media (width <= 900px) {
  .import-summary-bar {
    grid-template-columns: 1fr;
  }

  .import-summary-bar__actions {
    justify-content: space-between;
  }

  .import-summary-bar__actions :deep(.el-button) {
    flex: 1;
    min-height: 40px;
  }

  .duplicate-ai-panel__header,
  .llm-toggle {
    align-items: flex-start;
  }

  .import-preview-toolbar {
    align-items: stretch;
  }

  .import-preview-summary,
  .import-preview-actions {
    width: 100%;
  }

  .import-preview-actions :deep(.el-button) {
    flex: 1;
    min-height: 40px;
    margin-left: 0;
  }
}

@media (width <= 560px) {
  .collapse-subtitle {
    display: none;
  }

  .duplicate-ai-panel__header {
    flex-direction: column;
  }

  .duplicate-ai-panel__control {
    justify-content: space-between;
    width: 100%;
    padding-left: 48px;
  }

  .duplicate-ai-summary {
    flex-direction: column;
  }

  .duplicate-ai-summary__services {
    width: 100%;
    text-align: left;
  }

  .preview-metric {
    padding: 0 10px;
  }
}

@media (prefers-reduced-motion: reduce) {
  .duplicate-ai-advanced-toggle,
  .duplicate-ai-advanced-toggle .el-icon {
    transition: none;
  }
}
</style>
