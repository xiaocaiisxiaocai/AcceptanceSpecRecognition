<script setup lang="ts">
import { ref, computed } from "vue";
import type { MatchPreviewItem } from "@/api/matching";

const props = defineProps<{
  items: MatchPreviewItem[];
  loading?: boolean;
  /** LLM 流式处理是否进行中 */
  llmStreaming?: boolean;
}>();

const emit = defineEmits<{
  (e: "select", rowIndex: number, spec: MatchPreviewItem["bestMatch"] | null): void;
  (e: "showDetail", item: MatchPreviewItem): void;
}>();

type Selection =
  | { type: "best" }
  | { type: "llm"; acceptance?: string; remark?: string };

// 选中的匹配（rowIndex -> Selection）
const selectedSpecs = ref<Map<number, Selection | null>>(new Map());

// 无明确答案的占位行：默认不自动选中匹配，保持填充为空
const isNoAnswerPlaceholderRow = (item: MatchPreviewItem) => {
  const project = (item.sourceProject || "").trim();
  const specification = (item.sourceSpecification || "").trim();
  if (specification) return false;

  const placeholderProjects = new Set(["其他", "-", "/", "无", "n/a", "na"]);
  return placeholderProjects.has(project.toLowerCase());
};

// 初始化选中项（默认选择最佳匹配）
const initSelections = () => {
  selectedSpecs.value.clear();
  props.items.forEach((item) => {
    if (item.bestMatch && !isNoAnswerPlaceholderRow(item)) {
      selectedSpecs.value.set(item.rowIndex, { type: "best" });
    } else {
      selectedSpecs.value.set(item.rowIndex, null);
    }
  });
};

// 监听items变化
import { watch } from "vue";
watch(() => props.items, initSelections, { immediate: true });

// 获取选中的specId
const getSelection = (rowIndex: number) => selectedSpecs.value.get(rowIndex);

const isLlmSelected = (rowIndex: number) => {
  return selectedSpecs.value.get(rowIndex)?.type === "llm";
};

const hasSuggestionContent = (item: MatchPreviewItem) => {
  return Boolean(item.llmSuggestion?.acceptance || item.llmSuggestion?.remark);
};

// 选择匹配
const handleSelectBest = (item: MatchPreviewItem) => {
  if (item.bestMatch) {
    selectedSpecs.value.set(item.rowIndex, { type: "best" });
  } else {
    selectedSpecs.value.set(item.rowIndex, null);
  }
  emit("select", item.rowIndex, item.bestMatch ?? null);
};

const handleSelectSuggestion = (item: MatchPreviewItem) => {
  selectedSpecs.value.set(item.rowIndex, {
    type: "llm",
    acceptance: item.llmSuggestion?.acceptance,
    remark: item.llmSuggestion?.remark
  });
  emit("select", item.rowIndex, null);
};

const handleClearSelection = (item: MatchPreviewItem) => {
  selectedSpecs.value.set(item.rowIndex, null);
  emit("select", item.rowIndex, null);
};

const clearSelectionByRow = (rowIndex: number) => {
  selectedSpecs.value.set(rowIndex, null);
};

// 获取置信度样式
const getConfidenceClass = (level: string) => {
  switch (level) {
    case "high":
      return "confidence-high";
    case "medium":
      return "confidence-medium";
    case "low":
      return "confidence-low";
    default:
      return "confidence-none";
  }
};

// 获取置信度文本
const getConfidenceText = (level: string) => {
  switch (level) {
    case "high":
      return "高";
    case "medium":
      return "中";
    case "low":
      return "低";
    default:
      return "无";
  }
};

// 格式化得分
const formatScore = (score: number) => {
  return (score * 100).toFixed(1) + "%";
};

// LLM 复核状态
type LlmStatus = "none" | "waiting" | "streaming" | "done" | "error";

const getReviewStatus = (item: MatchPreviewItem): LlmStatus => {
  if (item.llmReviewError) return "error";
  if (item.bestMatch?.isLlmReviewed) return "done";
  if (item.llmReviewDraft !== undefined) return "streaming";
  if (props.llmStreaming && item.hasMatch) return "waiting";
  return "none";
};

const getSuggestionStatus = (item: MatchPreviewItem): LlmStatus => {
  if (item.llmSuggestionError) return "error";
  if (item.llmSuggestion) return "done";
  if (item.llmSuggestionDraft !== undefined) return "streaming";
  if (props.llmStreaming) return "waiting";
  return "none";
};

const formatLlmScore = (score?: number) => {
  if (score === undefined || score === null) return "-";
  return `${score.toFixed(0)}`;
};

// 统计信息
const stats = computed(() => {
  const total = props.items.length;
  const matched = props.items.filter((i) => i.hasMatch).length;
  const perfect = props.items.filter(
    (i) => i.hasMatch && i.bestMatch && i.bestMatch.score >= 0.9995
  ).length;
  const imperfect = total - perfect;
  const selected = Array.from(selectedSpecs.value.values()).filter(
    (v) => v !== null
  ).length;
  return { total, matched, perfect, imperfect, selected };
});

// 筛选
type ScoreFilter = "all" | "perfect" | "imperfect";
const scoreFilter = ref<ScoreFilter>("all");

const filteredItems = computed(() => {
  if (scoreFilter.value === "all") return props.items;
  if (scoreFilter.value === "perfect") {
    return props.items.filter(
      (i) => i.hasMatch && i.bestMatch && i.bestMatch.score >= 0.9995
    );
  }
  // imperfect: 低于100% 或无匹配
  return props.items.filter(
    (i) => !i.hasMatch || !i.bestMatch || i.bestMatch.score < 0.9995
  );
});

// 是否存在未匹配行（控制"不匹配原因"列显隐）
const hasUnmatched = computed(() => props.items.some((i) => !i.hasMatch));

// 暴露方法
defineExpose({
  getSelections: () => {
    const selections: Array<{
      rowIndex: number;
      specId?: number;
      useLlmSuggestion?: boolean;
      acceptance?: string;
      remark?: string;
    }> = [];
    selectedSpecs.value.forEach((selection, rowIndex) => {
      if (!selection) return;
      if (selection.type === "best") {
        const item = props.items.find((i) => i.rowIndex === rowIndex);
        if (item?.bestMatch) {
          selections.push({ rowIndex, specId: item.bestMatch.specId });
        }
      } else {
        selections.push({
          rowIndex,
          useLlmSuggestion: true,
          acceptance: selection.acceptance,
          remark: selection.remark
        });
      }
    });
    return selections;
  },
  initSelections,
  clearSelectionByRow
});
</script>

<template>
  <div class="match-preview-table">
    <!-- 统计栏 + 筛选 -->
    <div class="stats-bar">
      <div class="stats-info">
        <span>共 {{ stats.total }} 行</span>
        <span class="divider">|</span>
        <span>已匹配 {{ stats.matched }} 行</span>
        <span class="divider">|</span>
        <span class="selected">已选择 {{ stats.selected }} 行</span>
      </div>
      <el-radio-group
        v-model="scoreFilter"
        size="small"
        class="score-filter"
      >
        <el-radio-button value="all">
          全部 ({{ stats.total }})
        </el-radio-button>
        <el-radio-button value="perfect">
          100% ({{ stats.perfect }})
        </el-radio-button>
        <el-radio-button value="imperfect">
          需关注 ({{ stats.imperfect }})
        </el-radio-button>
      </el-radio-group>
    </div>

    <!-- 表格 -->
    <el-table
      :data="filteredItems"
      v-loading="loading"
      stripe
      border
      max-height="500"
      row-key="rowIndex"
    >
      <!-- 行号 -->
      <el-table-column label="行" width="60" align="center">
        <template #default="{ row }">
          {{ row.rowIndex + 1 }}
        </template>
      </el-table-column>

      <!-- 源数据 -->
      <el-table-column label="源数据" min-width="200">
        <template #default="{ row }">
          <div class="source-data">
            <div class="source-project">{{ row.sourceProject }}</div>
            <div class="source-spec">{{ row.sourceSpecification }}</div>
          </div>
        </template>
      </el-table-column>

      <!-- 置信度 -->
      <el-table-column label="置信度" width="80" align="center">
        <template #default="{ row }">
          <el-tag
            :class="getConfidenceClass(row.confidenceLevel)"
            size="small"
            effect="dark"
          >
            {{ getConfidenceText(row.confidenceLevel) }}
          </el-tag>
        </template>
      </el-table-column>

      <!-- 最佳匹配 -->
      <el-table-column label="最佳匹配" min-width="260">
        <template #default="{ row }">
          <div v-if="row.bestMatch" class="match-best">
            <div class="match-text">
              {{ row.bestMatch.project }} - {{ row.bestMatch.specification }}
            </div>
            <div class="match-score">{{ formatScore(row.bestMatch.score) }}</div>
          </div>
          <div v-else class="no-match">
            <el-tag type="info" size="small">无匹配</el-tag>
          </div>
        </template>
      </el-table-column>

      <!-- AI状态 -->
      <el-table-column label="AI状态" width="130" align="center">
        <template #default="{ row }">
          <div class="ai-status-cell">
            <!-- LLM 复核状态 -->
            <template v-if="getReviewStatus(row) !== 'none'">
              <div class="ai-status-row">
                <span class="ai-label">复核</span>
                <el-tag
                  v-if="getReviewStatus(row) === 'waiting'"
                  size="small"
                  type="info"
                >
                  等待中
                </el-tag>
                <el-tag
                  v-else-if="getReviewStatus(row) === 'streaming'"
                  size="small"
                  type="warning"
                  class="ai-streaming"
                >
                  复核中...
                </el-tag>
                <el-tag
                  v-else-if="getReviewStatus(row) === 'done'"
                  size="small"
                  type="success"
                >
                  {{ formatLlmScore(row.bestMatch?.llmScore) }}分
                </el-tag>
                <el-tag
                  v-else-if="getReviewStatus(row) === 'error'"
                  size="small"
                  type="danger"
                >
                  失败
                </el-tag>
              </div>
            </template>

            <!-- LLM 建议状态 -->
            <template v-if="getSuggestionStatus(row) !== 'none'">
              <div class="ai-status-row">
                <span class="ai-label">建议</span>
                <el-tag
                  v-if="getSuggestionStatus(row) === 'waiting'"
                  size="small"
                  type="info"
                >
                  等待中
                </el-tag>
                <el-tag
                  v-else-if="getSuggestionStatus(row) === 'streaming'"
                  size="small"
                  type="warning"
                  class="ai-streaming"
                >
                  生成中...
                </el-tag>
                <el-tag
                  v-else-if="getSuggestionStatus(row) === 'done'"
                  size="small"
                  type="success"
                >
                  已生成
                </el-tag>
                <el-tag
                  v-else-if="getSuggestionStatus(row) === 'error'"
                  size="small"
                  type="danger"
                >
                  失败
                </el-tag>
              </div>
            </template>
          </div>
        </template>
      </el-table-column>

      <!-- 验收标准预览 -->
      <el-table-column label="验收标准" min-width="180">
        <template #default="{ row }">
          <!-- 已选择最佳匹配 -->
          <template v-if="getSelection(row.rowIndex)?.type === 'best'">
            <span class="acceptance-text">
              {{ row.bestMatch?.acceptance || "-" }}
            </span>
          </template>
          <!-- 已选择LLM建议 -->
          <template v-else-if="isLlmSelected(row.rowIndex)">
            <span class="acceptance-text suggestion-selected">
              {{ row.llmSuggestion?.acceptance || "-" }}
            </span>
          </template>
          <!-- 未选择但有LLM建议内容：预览 -->
          <template v-else-if="row.llmSuggestion?.acceptance">
            <span class="suggestion-preview">
              {{ row.llmSuggestion.acceptance }}
            </span>
          </template>
          <span v-else class="acceptance-none">-</span>
        </template>
      </el-table-column>

      <!-- 备注预览 -->
      <el-table-column label="备注" min-width="150">
        <template #default="{ row }">
          <template v-if="getSelection(row.rowIndex)?.type === 'best'">
            <span class="acceptance-text">
              {{ row.bestMatch?.remark || "-" }}
            </span>
          </template>
          <template v-else-if="isLlmSelected(row.rowIndex)">
            <span class="acceptance-text suggestion-selected">
              {{ row.llmSuggestion?.remark || "-" }}
            </span>
          </template>
          <!-- 未选择但有LLM建议备注：预览 -->
          <template v-else-if="row.llmSuggestion?.remark">
            <span class="suggestion-preview">
              {{ row.llmSuggestion.remark }}
            </span>
          </template>
          <span v-else class="acceptance-none">-</span>
        </template>
      </el-table-column>

      <!-- 不匹配原因 / AI说明 -->
      <el-table-column v-if="hasUnmatched" label="不匹配原因" min-width="200">
        <template #default="{ row }">
          <div v-if="!row.hasMatch || row.llmSuggestion?.reason" class="reason-cell">
            <div v-if="!row.hasMatch && row.noMatchReason" class="reason-text">
              {{ row.noMatchReason }}
            </div>
            <div
              v-if="row.llmSuggestion?.reason"
              class="suggestion-reason"
            >
              AI: {{ row.llmSuggestion.reason }}
            </div>
          </div>
          <span v-else class="reason-none">-</span>
        </template>
      </el-table-column>

      <!-- 操作 -->
      <el-table-column label="操作" width="140" align="center" fixed="right">
        <template #default="{ row }">
          <div class="action-buttons">
            <el-button
              v-if="row.bestMatch"
              type="primary"
              link
              size="small"
              @click="emit('showDetail', row)"
            >
              详情
            </el-button>
            <el-button
              v-if="row.bestMatch && getSelection(row.rowIndex)?.type !== 'best'"
              size="small"
              @click="handleSelectBest(row)"
            >
              使用匹配
            </el-button>
            <el-button
              v-if="hasSuggestionContent(row)"
              size="small"
              type="success"
              @click="handleSelectSuggestion(row)"
            >
              {{ isLlmSelected(row.rowIndex) ? "已选建议" : "采用建议" }}
            </el-button>
            <el-button
              v-if="getSelection(row.rowIndex)"
              link
              size="small"
              @click="handleClearSelection(row)"
            >
              不填充
            </el-button>
          </div>
        </template>
      </el-table-column>
    </el-table>
  </div>
</template>

<style scoped>
.match-preview-table {
  width: 100%;
}

.stats-bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 12px 16px;
  background: #f8f5ff;
  border-radius: 8px;
  margin-bottom: 12px;
  font-size: 14px;
  color: #4b5563;
}

.stats-info {
  display: flex;
  align-items: center;
  gap: 8px;
}

.score-filter {
  flex-shrink: 0;
}

.divider {
  color: #dcdfe6;
}

.selected {
  color: var(--color-primary);
  font-weight: 500;
}

.source-data {
  line-height: 1.5;
}

.source-project {
  font-weight: 500;
  color: var(--color-text);
}

.source-spec {
  font-size: 12px;
  color: #6b7280;
  margin-top: 4px;
}

.confidence-high {
  background-color: #67c23a !important;
  border-color: #67c23a !important;
}

.confidence-medium {
  background-color: #e6a23c !important;
  border-color: #e6a23c !important;
}

.confidence-low {
  background-color: #f56c6c !important;
  border-color: #f56c6c !important;
}

.confidence-none {
  background-color: #909399 !important;
  border-color: #909399 !important;
}

.no-match {
  text-align: center;
}

.match-best {
  display: flex;
  justify-content: space-between;
  gap: 8px;
}

.match-text {
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: var(--color-text);
  font-weight: 500;
}

.match-score {
  flex-shrink: 0;
  color: var(--color-primary);
  font-weight: 600;
}

.acceptance-none {
  color: #c0c4cc;
}

.reason-text {
  color: #6b7280;
  font-size: 12px;
}

.reason-cell {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.reason-none {
  color: #c0c4cc;
}

.action-buttons {
  display: flex;
  flex-direction: row;
  flex-wrap: wrap;
  gap: 4px;
  align-items: center;
  justify-content: center;
}

.acceptance-text {
  font-size: 13px;
  color: #4b5563;
}

.suggestion-selected {
  color: #67c23a;
}

.suggestion-preview {
  font-size: 13px;
  color: #a78bfa;
  font-style: italic;
}

.suggestion-reason {
  font-size: 12px;
  color: #9ca3af;
  font-style: italic;
}

.ai-status-cell {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.ai-status-row {
  display: flex;
  align-items: center;
  gap: 4px;
}

.ai-label {
  font-size: 11px;
  color: #9ca3af;
  min-width: 24px;
}

.ai-streaming {
  animation: ai-pulse 1.2s ease-in-out infinite;
}

@keyframes ai-pulse {
  0%,
  100% {
    opacity: 1;
  }
  50% {
    opacity: 0.5;
  }
}
</style>
