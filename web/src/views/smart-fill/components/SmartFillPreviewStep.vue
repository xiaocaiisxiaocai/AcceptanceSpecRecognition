<script setup lang="ts">
import { ref } from "vue";
import { Loading } from "@element-plus/icons-vue";
import BatchPreviewTabs from "./BatchPreviewTabs.vue";
import type { MatchPreviewItem, BatchTablePreviewResult } from "@/api/matching";

defineProps<{
  llmStreaming: boolean;
  loading: boolean;
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

const batchPreviewTabsRef = ref<InstanceType<typeof BatchPreviewTabs> | null>(null);

defineExpose<{
  getAllSelections: NonNullable<InstanceType<typeof BatchPreviewTabs>["getAllSelections"]>;
  getAllEditedBackfillItems: NonNullable<
    InstanceType<typeof BatchPreviewTabs>["getAllEditedBackfillItems"]
  >;
}>({
  getAllSelections: () => batchPreviewTabsRef.value?.getAllSelections() ?? new Map(),
  getAllEditedBackfillItems: () =>
    batchPreviewTabsRef.value?.getAllEditedBackfillItems() ?? []
});
</script>

<template>
  <div class="step-panel">
    <h3 class="step-title">匹配预览</h3>
    <p class="step-desc">确认匹配结果，可手动调整选择</p>

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

    <!-- 匹配进行中遮罩 -->
    <div v-if="loading" class="loading-overlay">
      <el-icon class="is-loading" :size="32"><Loading /></el-icon>
      <p class="loading-text">正在匹配中，请耐心等待...</p>
      <div class="preview-progress-panel">
        <div class="preview-progress-panel__header">
          <span>{{ previewProgressStageText }}</span>
          <span>{{ previewProgressPercent }}%</span>
        </div>
        <el-progress
          :percentage="previewProgressPercent"
          :stroke-width="10"
          :show-text="false"
        />
        <div class="preview-progress-panel__meta">
          <span>{{ previewProgressDetailText }}</span>
          <span v-if="previewProgressCounterText">
            {{ previewProgressCounterText }}
          </span>
          <span>已等待 {{ previewElapsedSeconds }} 秒</span>
        </div>
      </div>
      <p class="loading-hint">
        正在对 {{ selectedTableCount }} 个表格执行 Embedding
        向量匹配，视数据量可能需要数十秒
      </p>
    </div>

    <el-empty
      v-if="!loading && previewBlockingMessage"
      :description="previewBlockingMessage"
      class="preview-empty-state"
    >
      <template #description>
        <div class="preview-empty-state__body">
          <div class="preview-empty-state__title">{{ previewBlockingMessage }}</div>
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
      @select="(tableIndex, rowIndex, spec) => emit('select', tableIndex, rowIndex, spec)"
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
      <el-button v-if="canPreviewMatching" @click="emit('preview')" :loading="loading">
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
