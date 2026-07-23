<script setup lang="ts">
import { computed, ref, watch } from "vue";
import {
  CircleCheckFilled,
  DocumentAdd,
  Download,
  Loading,
  Refresh,
  WarningFilled
} from "@element-plus/icons-vue";
import BatchPreviewTabs from "./BatchPreviewTabs.vue";
import type {
  BatchPreviewProgressResponse,
  MatchPreviewItem,
  BatchTablePreviewResult
} from "@/api/matching";

const props = defineProps<{
  llmStreaming: boolean;
  loading: boolean;
  previewProgress: BatchPreviewProgressResponse | null;
  previewProgressStageText: string;
  previewProgressPercent: number;
  previewProgressDetailText: string;
  previewProgressCounterText: string;
  previewElapsedSeconds: number;
  selectedTableCount: number;
  previewBlockingMessage: string;
  previewBlockingHint: string;
  batchPreviewResults: BatchTablePreviewResult[];
  highConfidenceThreshold: number;
  ambiguityMargin: number;
  previewTableNames: Record<number, string>;
  taskId: string | null;
  isExcelFile: boolean;
  lastDownloadFailed: boolean;
  canDownloadFillResult: boolean;
  allPreviewItemsCount: number;
  canPreviewMatching: boolean;
  canExecuteFill: boolean;
  executing: boolean;
  downloadingResult: boolean;
  canUploadSourceFile: boolean;
}>();

const emit = defineEmits<{
  (e: "goPrev"): void;
  (
    e: "select",
    tableIndex: number,
    rowIndex: number,
    spec: MatchPreviewItem["bestMatch"] | null
  ): void;
  (e: "showDetail", item: MatchPreviewItem): void;
  (e: "preview"): void;
  (e: "execute"): void;
  (e: "downloadLastResult"): void;
  (e: "restart"): void;
}>();

const batchPreviewTabsRef = ref<InstanceType<typeof BatchPreviewTabs> | null>(
  null
);
const hasPostFillChanges = ref(false);

watch(
  () => props.taskId,
  () => {
    hasPostFillChanges.value = false;
  }
);

const handleSelect = (
  tableIndex: number,
  rowIndex: number,
  spec: MatchPreviewItem["bestMatch"] | null
) => {
  if (props.taskId) {
    hasPostFillChanges.value = true;
  }
  emit("select", tableIndex, rowIndex, spec);
};

defineExpose<{
  getAllSelections: NonNullable<
    InstanceType<typeof BatchPreviewTabs>["getAllSelections"]
  >;
  getAllEditedBackfillItems: NonNullable<
    InstanceType<typeof BatchPreviewTabs>["getAllEditedBackfillItems"]
  >;
}>({
  getAllSelections: () =>
    batchPreviewTabsRef.value?.getAllSelections() ?? new Map(),
  getAllEditedBackfillItems: () =>
    batchPreviewTabsRef.value?.getAllEditedBackfillItems() ?? []
});

const loadingHintText = computed(() => {
  const stage = props.previewProgress?.stage;
  if (stage === "embedding_source" || stage === "embedding_candidates") {
    return `正在对 ${props.selectedTableCount} 个表格生成语义特征，视模型与数据量可能需要数秒`;
  }
  return `正在对 ${props.selectedTableCount} 个表格执行匹配与 AI 等价裁决，视数据量与 LLM 响应可能需要数分钟`;
});
</script>

<template>
  <div class="step-panel smart-fill-preview-step">
    <!-- LLM 流式处理提示 -->
    <el-alert
      v-if="llmStreaming"
      title="AI 正在处理中..."
      description="LLM 正在逐行复核中，请等待完成后再执行填充"
      type="info"
      show-icon
      :closable="false"
      class="llm-streaming-alert"
    />

    <!-- 匹配进行中 -->
    <div v-if="loading" class="matching-loading">
      <div class="matching-loading__card">
        <div class="matching-loading__header">
          <el-icon class="is-loading matching-loading__icon" :size="20"
            ><Loading
          /></el-icon>
          <span class="matching-loading__title">正在匹配中，请耐心等待...</span>
          <span class="matching-loading__elapsed"
            >{{ previewElapsedSeconds }}s</span
          >
        </div>

        <div class="matching-loading__stage">
          {{ previewProgressStageText }}
        </div>

        <el-progress
          :percentage="previewProgressPercent"
          :stroke-width="8"
          :show-text="false"
          status=""
          class="matching-loading__bar"
        />

        <div class="matching-loading__stats">
          <span class="matching-loading__detail">{{
            previewProgressDetailText
          }}</span>
          <span
            v-if="previewProgressCounterText"
            class="matching-loading__counter"
          >
            {{ previewProgressCounterText }}
          </span>
        </div>

        <div class="matching-loading__hint">{{ loadingHintText }}</div>
      </div>
    </div>

    <template v-else>
      <el-empty
        v-if="previewBlockingMessage"
        :description="previewBlockingMessage"
        class="preview-empty-state"
      >
        <template #description>
          <div class="preview-empty-state__body">
            <div class="preview-empty-state__title">
              {{ previewBlockingMessage }}
            </div>
            <div v-if="previewBlockingHint" class="preview-empty-state__hint">
              {{ previewBlockingHint }}
            </div>
          </div>
        </template>
      </el-empty>

      <el-empty
        v-else-if="batchPreviewResults.length === 0"
        description="当前没有预览结果"
        class="preview-empty-state"
      >
        <template #description>
          <div class="preview-empty-state__body">
            <div class="preview-empty-state__title">当前没有预览结果</div>
            <div class="preview-empty-state__hint">
              页面状态可能已失效，请返回上一步重新匹配。
            </div>
          </div>
        </template>
        <el-button v-if="!taskId" @click="emit('goPrev')">返回上一步</el-button>
      </el-empty>

      <BatchPreviewTabs
        v-else
        ref="batchPreviewTabsRef"
        :results="batchPreviewResults"
        :loading="loading"
        :high-confidence-threshold="highConfidenceThreshold"
        :ambiguity-margin="ambiguityMargin"
        :llm-streaming="llmStreaming"
        :table-names="previewTableNames"
        @select="handleSelect"
        @show-detail="emit('showDetail', $event)"
      >
        <template #pagination-actions>
          <div
            v-if="allPreviewItemsCount > 0"
            class="preview-pagination-actions"
          >
            <div
              v-if="taskId"
              class="fill-complete-status"
              :class="{
                'fill-complete-status--warning':
                  lastDownloadFailed || hasPostFillChanges
              }"
            >
              <el-icon :size="17">
                <WarningFilled
                  v-if="lastDownloadFailed || hasPostFillChanges"
                />
                <CircleCheckFilled v-else />
              </el-icon>
              <div class="fill-complete-status__copy">
                <strong>{{
                  hasPostFillChanges
                    ? "修改待重新填充"
                    : lastDownloadFailed
                      ? "填充完成，下载未完成"
                      : "填充完成"
                }}</strong>
                <span>
                  {{
                    hasPostFillChanges
                      ? "当前文档仍是上次填充结果"
                      : lastDownloadFailed
                        ? "请重新下载结果"
                        : isExcelFile
                          ? "已回写当前文档"
                          : "已生成结果文档"
                  }}
                </span>
              </div>
            </div>

            <div class="preview-pagination-actions__buttons">
              <el-button
                v-if="!taskId && canPreviewMatching"
                :icon="Refresh"
                :loading="loading"
                @click="emit('preview')"
              >
                重新匹配
              </el-button>
              <el-button
                v-if="!taskId && canExecuteFill"
                type="primary"
                :loading="executing"
                :disabled="llmStreaming || loading"
                @click="emit('execute')"
              >
                执行填充
              </el-button>
              <el-button
                v-if="taskId && hasPostFillChanges && canExecuteFill"
                type="primary"
                :loading="executing"
                :disabled="llmStreaming || loading"
                @click="emit('execute')"
              >
                重新填充
              </el-button>
              <el-button
                v-if="taskId && canDownloadFillResult"
                :icon="Download"
                :loading="downloadingResult"
                @click="emit('downloadLastResult')"
              >
                重新下载
              </el-button>
              <el-button
                v-if="taskId && canUploadSourceFile"
                type="primary"
                plain
                :icon="DocumentAdd"
                @click="emit('restart')"
              >
                继续填充
              </el-button>
            </div>
          </div>
        </template>
      </BatchPreviewTabs>
    </template>
  </div>
</template>

<style scoped>
.smart-fill-preview-step {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
  padding-bottom: 0;
  overflow: hidden;
}

.preview-pagination-actions {
  display: flex;
  flex: 1;
  flex-wrap: wrap;
  gap: 10px;
  align-items: center;
  min-width: 0;
}

.preview-pagination-actions__buttons {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.preview-pagination-actions__buttons :deep(.el-button + .el-button) {
  margin-left: 0;
}

.fill-complete-status {
  display: flex;
  gap: 7px;
  align-items: center;
  min-width: 0;
  padding: 4px 10px;
  color: var(--app-success);
  background: var(--app-success-bg);
  border: 1px solid color-mix(in srgb, var(--app-success) 24%, transparent);
  border-radius: 6px;
}

.fill-complete-status--warning {
  color: var(--el-color-warning-dark-2);
  background: var(--app-warning-bg);
  border-color: color-mix(in srgb, var(--el-color-warning) 28%, transparent);
}

.fill-complete-status__copy {
  display: flex;
  gap: 6px;
  align-items: baseline;
  min-width: 0;
  white-space: nowrap;
}

.fill-complete-status__copy strong {
  font-size: 13px;
  font-weight: 600;
}

.fill-complete-status__copy span {
  overflow: hidden;
  text-overflow: ellipsis;
  font-size: 12px;
  color: var(--app-text-secondary);
}

.matching-loading {
  display: flex;
  flex: 1;
  align-items: center;
  justify-content: center;
  min-height: 0;
  padding: 24px 0;
}

.matching-loading__card {
  display: flex;
  flex-direction: column;
  gap: 14px;
  width: min(560px, 100%);
  padding: 24px;
  background: var(--app-info-bg);
  border: 1px solid var(--app-info-bg);
  border-radius: 12px;
}

.matching-loading__header {
  display: flex;
  gap: 10px;
  align-items: center;
}

.matching-loading__icon {
  flex-shrink: 0;
  color: var(--el-color-primary);
}

.matching-loading__title {
  flex: 1;
  font-size: 15px;
  font-weight: 600;
  color: var(--color-text);
}

.matching-loading__elapsed {
  padding: 2px 8px;
  font-size: 13px;
  font-variant-numeric: tabular-nums;
  color: var(--app-text-disabled);
  background: var(--app-border-light);
  border-radius: 20px;
}

.matching-loading__stage {
  font-size: 13px;
  font-weight: 500;
  color: var(--el-color-primary);
}

.matching-loading__bar {
  margin: 0;
}

.matching-loading__stats {
  display: flex;
  gap: 8px;
  justify-content: space-between;
  font-size: 12px;
  color: var(--app-text-secondary);
}

.matching-loading__detail {
  flex: 1;
}

.matching-loading__counter {
  padding: 1px 8px;
  font-size: 12px;
  font-weight: 600;
  color: var(--app-text-secondary);
  background: var(--app-primary-light);
  border-radius: 10px;
}

.matching-loading__hint {
  padding-top: 10px;
  font-size: 12px;
  line-height: 1.6;
  color: var(--app-text-disabled);
  border-top: 1px solid var(--app-border);
}

@media (width <= 960px) {
  .fill-complete-status {
    flex: 1 1 100%;
  }
}
</style>
