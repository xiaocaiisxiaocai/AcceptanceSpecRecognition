<script setup lang="ts">
import { computed } from "vue";
import type { MatchPreviewItem, MatchResult } from "@/api/matching";

const props = defineProps<{
  visible: boolean;
  item: MatchPreviewItem | null;
}>();

const emit = defineEmits<{
  (e: "update:visible", value: boolean): void;
}>();

const dialogVisible = computed({
  get: () => props.visible,
  set: (val) => emit("update:visible", val)
});

// 格式化得分
const formatScore = (score: number) => (score * 100).toFixed(1) + "%";

// 获取置信度样式
const getConfidenceClass = (level: string): "success" | "warning" | "danger" | "info" => {
  const map: Record<string, "success" | "warning" | "danger" | "info"> = {
    high: "success",
    medium: "warning",
    low: "danger"
  };
  return map[level] || "info";
};

// 获取置信度文本
const getConfidenceText = (level: string) => {
  const map: Record<string, string> = {
    high: "高",
    medium: "中",
    low: "低"
  };
  return map[level] || "无";
};
</script>

<template>
  <el-dialog
    v-model="dialogVisible"
    title="匹配详情"
    width="700px"
    destroy-on-close
  >
    <template v-if="item">
      <!-- 源数据信息 -->
      <div class="source-info">
        <h4>源数据</h4>
        <el-descriptions :column="2" border size="small">
          <el-descriptions-item label="项目">
            {{ item.sourceProject }}
          </el-descriptions-item>
          <el-descriptions-item label="规格">
            {{ item.sourceSpecification }}
          </el-descriptions-item>
          <el-descriptions-item label="置信度">
            <el-tag :type="getConfidenceClass(item.confidenceLevel)" size="small">
              {{ getConfidenceText(item.confidenceLevel) }}
            </el-tag>
          </el-descriptions-item>
          <el-descriptions-item label="最佳得分">
            {{ item.bestMatch ? formatScore(item.bestMatch.score) : "-" }}
          </el-descriptions-item>
        </el-descriptions>
      </div>

      <!-- 候选列表 -->
      <div class="candidates-section">
        <h4>候选匹配 ({{ item.candidates.length }})</h4>
        <el-table
          v-if="item.candidates.length > 0"
          :data="item.candidates"
          max-height="300"
          size="small"
          border
        >
          <el-table-column label="项目" prop="project" min-width="100" />
          <el-table-column label="规格" prop="specification" min-width="120" />
          <el-table-column label="验收标准" prop="acceptance" min-width="150" show-overflow-tooltip />
          <el-table-column label="综合得分" width="90" align="center">
            <template #default="{ row }">
              <span class="score-value">{{ formatScore(row.score) }}</span>
            </template>
          </el-table-column>
          <el-table-column label="算法得分" width="200">
            <template #default="{ row }">
              <div class="algorithm-scores">
                <span v-if="row.levenshteinScore !== undefined" class="algo-item">
                  Lev: {{ formatScore(row.levenshteinScore) }}
                </span>
                <span v-if="row.jaccardScore !== undefined" class="algo-item">
                  Jac: {{ formatScore(row.jaccardScore) }}
                </span>
                <span v-if="row.cosineScore !== undefined" class="algo-item">
                  Cos: {{ formatScore(row.cosineScore) }}
                </span>
              </div>
            </template>
          </el-table-column>
        </el-table>
        <el-empty v-else description="无匹配结果" :image-size="60" />
      </div>
    </template>

    <template #footer>
      <el-button @click="dialogVisible = false">关闭</el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.source-info {
  margin-bottom: 20px;
}

.source-info h4,
.candidates-section h4 {
  font-size: 14px;
  font-weight: 500;
  color: #303133;
  margin-bottom: 12px;
}

.score-value {
  font-weight: 600;
  color: #409eff;
}

.algorithm-scores {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  font-size: 12px;
}

.algo-item {
  color: #909399;
}
</style>
