<script setup lang="ts">
import { computed, ref } from "vue";
import { Loading } from "@element-plus/icons-vue";
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
  <div class="step-panel">
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

    <el-empty
      v-if="!loading && previewBlockingMessage"
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
      v-else-if="!loading && batchPreviewResults.length === 0"
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
      @select="
        (tableIndex, rowIndex, spec) =>
          emit('select', tableIndex, rowIndex, spec)
      "
      @show-detail="emit('showDetail', $event)"
    />

    <!-- 填充完成提示（紧凑内联） -->
    <el-alert
      v-if="taskId"
      :title="
        isExcelFile
          ? '填充完成 — 内容已回写到当前上传文档'
          : '填充完成 — 已生成结果文档（源文档保持不变）'
      "
      :description="
        lastDownloadFailed
          ? '本次自动下载未完成，请使用下方入口重新下载结果。'
          : canDownloadFillResult
            ? '如需再次获取结果文件，可使用下方下载入口。'
            : '当前账号没有下载权限，可稍后由有权限用户下载结果。'
      "
      type="success"
      show-icon
      closable
      class="fill-done-alert"
    />

    <!-- 操作按钮 -->
    <div v-if="allPreviewItemsCount > 0" class="action-bar">
      <el-button
        v-if="canPreviewMatching"
        :loading="loading"
        @click="emit('preview')"
      >
        重新匹配
      </el-button>
      <el-button
        v-if="canExecuteFill"
        type="primary"
        :loading="executing"
        :disabled="!!taskId || llmStreaming || loading"
        @click="emit('execute')"
      >
        执行填充
      </el-button>
      <el-button
        v-if="taskId && canDownloadFillResult"
        :loading="downloadingResult"
        @click="emit('downloadLastResult')"
      >
        重新下载结果
      </el-button>
      <el-button v-if="taskId && canUploadSourceFile" @click="emit('restart')">
        继续填充其他文档
      </el-button>
    </div>
  </div>
</template>

<style scoped>
.matching-loading {
  display: flex;
  justify-content: center;
  padding: 32px 0;
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
</style>
