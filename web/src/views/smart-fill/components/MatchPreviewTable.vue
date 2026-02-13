<script setup lang="ts">
import { ref, computed } from "vue";
import type { MatchPreviewItem } from "@/api/matching";

const props = defineProps<{
  items: MatchPreviewItem[];
  loading?: boolean;
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

// 初始化选中项（默认选择最佳匹配）
const initSelections = () => {
  selectedSpecs.value.clear();
  props.items.forEach((item) => {
    if (item.bestMatch) {
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

// 统计信息
const stats = computed(() => {
  const total = props.items.length;
  const matched = props.items.filter((i) => i.hasMatch).length;
  const selected = Array.from(selectedSpecs.value.values()).filter(
    (v) => v !== null
  ).length;
  return { total, matched, selected };
});

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
    <!-- 统计栏 -->
    <div class="stats-bar">
      <span>共 {{ stats.total }} 行</span>
      <span class="divider">|</span>
      <span>已匹配 {{ stats.matched }} 行</span>
      <span class="divider">|</span>
      <span class="selected">已选择 {{ stats.selected }} 行</span>
    </div>

    <!-- 表格 -->
    <el-table
      :data="items"
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

      <!-- 得分 -->
      <el-table-column label="得分" width="80" align="center">
        <template #default="{ row }">
          <span v-if="row.bestMatch" class="score">
            {{ formatScore(row.bestMatch.score) }}
          </span>
          <span v-else class="score-none">-</span>
        </template>
      </el-table-column>

      <!-- 验收标准预览 -->
      <el-table-column label="验收标准" min-width="180">
        <template #default="{ row }">
          <template v-if="getSelection(row.rowIndex)?.type === 'best'">
            <span class="acceptance-text">
              {{ row.bestMatch?.acceptance || "-" }}
            </span>
          </template>
          <template v-else-if="isLlmSelected(row.rowIndex)">
            <span class="acceptance-text">
              {{ row.llmSuggestion?.acceptance || row.llmSuggestionDraft || "-" }}
            </span>
          </template>
          <span v-else class="acceptance-none">-</span>
        </template>
      </el-table-column>

      <!-- 不匹配原因 -->
      <el-table-column label="不匹配原因" min-width="160">
        <template #default="{ row }">
          <span v-if="!row.hasMatch" class="reason-text">
            {{ row.noMatchReason || "-" }}
          </span>
          <span v-else class="reason-none">-</span>
        </template>
      </el-table-column>

      <!-- 操作 -->
      <el-table-column label="操作" width="160" align="center" fixed="right">
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
              v-if="row.llmSuggestion || row.llmSuggestionDraft"
              size="small"
              type="success"
              :disabled="!hasSuggestionContent(row)"
              @click="handleSelectSuggestion(row)"
            >
              {{ isLlmSelected(row.rowIndex) ? "已选建议" : "采用建议" }}
            </el-button>
            <el-button
              v-if="getSelection(row.rowIndex)"
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
  gap: 8px;
  padding: 12px 16px;
  background: #f8f5ff;
  border-radius: 8px;
  margin-bottom: 12px;
  font-size: 14px;
  color: #4b5563;
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

.score {
  font-weight: 600;
  color: var(--color-primary);
}

.score-none,
.acceptance-none {
  color: #c0c4cc;
}

.reason-text {
  color: #6b7280;
  font-size: 12px;
}

.reason-none {
  color: #c0c4cc;
}

.action-buttons {
  display: flex;
  flex-direction: column;
  gap: 4px;
  align-items: center;
}

.acceptance-text {
  font-size: 13px;
  color: #4b5563;
}
</style>
