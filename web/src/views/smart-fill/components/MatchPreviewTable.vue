<script setup lang="ts">
import { ref, computed } from "vue";
import type { MatchPreviewItem, MatchResult } from "@/api/matching";

const props = defineProps<{
  items: MatchPreviewItem[];
  loading?: boolean;
}>();

const emit = defineEmits<{
  (e: "select", rowIndex: number, spec: MatchResult | null): void;
  (e: "showDetail", item: MatchPreviewItem): void;
}>();

// 选中的匹配（rowIndex -> specId）
const selectedSpecs = ref<Map<number, number | null>>(new Map());

// 初始化选中项（默认选择最佳匹配）
const initSelections = () => {
  selectedSpecs.value.clear();
  props.items.forEach((item) => {
    if (item.bestMatch) {
      selectedSpecs.value.set(item.rowIndex, item.bestMatch.specId);
    } else {
      selectedSpecs.value.set(item.rowIndex, null);
    }
  });
};

// 监听items变化
import { watch } from "vue";
watch(() => props.items, initSelections, { immediate: true });

// 获取选中的specId
const getSelectedSpecId = (rowIndex: number) => {
  return selectedSpecs.value.get(rowIndex);
};

// 选择匹配
const handleSelect = (item: MatchPreviewItem, spec: MatchResult | null) => {
  selectedSpecs.value.set(item.rowIndex, spec?.specId ?? null);
  emit("select", item.rowIndex, spec);
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
    const selections: Array<{ rowIndex: number; specId: number }> = [];
    selectedSpecs.value.forEach((specId, rowIndex) => {
      if (specId !== null) {
        selections.push({ rowIndex, specId });
      }
    });
    return selections;
  },
  initSelections
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

      <!-- 匹配结果选择 -->
      <el-table-column label="匹配结果" min-width="280">
        <template #default="{ row }">
          <div v-if="row.candidates.length > 0" class="match-select">
            <el-select
              :model-value="getSelectedSpecId(row.rowIndex)"
              placeholder="选择匹配项"
              clearable
              @change="(val: number | null) => handleSelect(row, row.candidates.find((c: MatchResult) => c.specId === val) || null)"
              style="width: 100%"
            >
              <el-option
                v-for="candidate in row.candidates"
                :key="candidate.specId"
                :value="candidate.specId"
                :label="`${candidate.project} - ${candidate.specification}`"
              >
                <div class="candidate-option">
                  <span class="candidate-text">
                    {{ candidate.project }} - {{ candidate.specification }}
                  </span>
                  <span class="candidate-score">
                    {{ formatScore(candidate.score) }}
                  </span>
                </div>
              </el-option>
            </el-select>
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
      <el-table-column label="验收标准" min-width="150">
        <template #default="{ row }">
          <template v-if="getSelectedSpecId(row.rowIndex)">
            <span class="acceptance-text">
              {{
                row.candidates.find(
                  (c: MatchResult) => c.specId === getSelectedSpecId(row.rowIndex)
                )?.acceptance || "-"
              }}
            </span>
          </template>
          <span v-else class="acceptance-none">-</span>
        </template>
      </el-table-column>

      <!-- 操作 -->
      <el-table-column label="操作" width="80" align="center" fixed="right">
        <template #default="{ row }">
          <el-button
            v-if="row.hasMatch"
            type="primary"
            link
            size="small"
            @click="emit('showDetail', row)"
          >
            详情
          </el-button>
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
  background: #f5f7fa;
  border-radius: 4px;
  margin-bottom: 12px;
  font-size: 14px;
  color: #606266;
}

.divider {
  color: #dcdfe6;
}

.selected {
  color: #409eff;
  font-weight: 500;
}

.source-data {
  line-height: 1.5;
}

.source-project {
  font-weight: 500;
  color: #303133;
}

.source-spec {
  font-size: 12px;
  color: #909399;
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

.match-select {
  width: 100%;
}

.candidate-option {
  display: flex;
  justify-content: space-between;
  align-items: center;
  width: 100%;
}

.candidate-text {
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.candidate-score {
  flex-shrink: 0;
  margin-left: 8px;
  color: #409eff;
  font-weight: 500;
}

.no-match {
  text-align: center;
}

.score {
  font-weight: 600;
  color: #409eff;
}

.score-none,
.acceptance-none {
  color: #c0c4cc;
}

.acceptance-text {
  font-size: 13px;
  color: #606266;
}
</style>
