<script setup lang="ts">
import { computed } from "vue";
import {
  type MatchPreviewItem,
  MatchingStrategy
} from "@/api/matching";

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

const formatLlmScore = (score?: number) => {
  if (score === undefined || score === null) return "-";
  return `${score.toFixed(1)}分`;
};

const getStrategyText = (strategy?: MatchingStrategy) => {
  return strategy === MatchingStrategy.MultiStage ? "多阶段重排" : "基础单阶段";
};

const formatOptionalScore = (score?: number) => {
  if (score === undefined || score === null) return "-";
  return formatScore(score);
};

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

      <!-- 最佳匹配 -->
      <div class="candidates-section">
        <h4>最佳匹配</h4>
        <template v-if="item.bestMatch">
          <el-descriptions :column="2" border size="small">
            <el-descriptions-item label="项目">
              {{ item.bestMatch.project }}
            </el-descriptions-item>
            <el-descriptions-item label="规格">
              {{ item.bestMatch.specification }}
            </el-descriptions-item>
            <el-descriptions-item label="匹配策略">
              {{ getStrategyText(item.bestMatch.matchingStrategy) }}
            </el-descriptions-item>
            <el-descriptions-item label="验收标准">
              {{ item.bestMatch.acceptance || "-" }}
            </el-descriptions-item>
            <el-descriptions-item label="最终得分">
              {{ formatScore(item.bestMatch.score) }}
            </el-descriptions-item>
            <el-descriptions-item label="Embedding得分">
              {{ formatScore(item.bestMatch.embeddingScore) }}
            </el-descriptions-item>
            <el-descriptions-item label="召回候选数">
              {{ item.bestMatch.recalledCandidateCount || "-" }}
            </el-descriptions-item>
            <el-descriptions-item label="Top1/Top2分差">
              {{ formatOptionalScore(item.bestMatch.scoreGap) }}
            </el-descriptions-item>
            <el-descriptions-item label="高歧义">
              <el-tag
                :type="item.bestMatch.isAmbiguous ? 'warning' : 'success'"
                size="small"
              >
                {{ item.bestMatch.isAmbiguous ? "是" : "否" }}
              </el-tag>
            </el-descriptions-item>
            <el-descriptions-item label="LLM复核得分">
              {{ formatLlmScore(item.bestMatch.llmScore) }}
            </el-descriptions-item>
          </el-descriptions>

          <div v-if="item.bestMatch.rerankSummary" class="rerank-info">
            <div class="llm-label">重排摘要</div>
            <div class="llm-text">
              {{ item.bestMatch.rerankSummary }}
            </div>
          </div>

          <div class="llm-review">
            <div class="llm-label">LLM复核原因</div>
            <div class="llm-text">
              {{ item.bestMatch.llmReason || item.llmReviewDraft || "-" }}
            </div>
            <div class="llm-label">LLM复核过程</div>
            <div class="llm-text">
              {{ item.bestMatch.llmCommentary || "-" }}
            </div>
            <div v-if="item.llmReviewError" class="llm-error">
              LLM复核失败：{{ item.llmReviewError }}
            </div>
          </div>
        </template>
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
  color: var(--color-text);
  margin-bottom: 12px;
}

.score-value {
  font-weight: 600;
  color: var(--color-primary);
}

.llm-review {
  margin-top: 12px;
}

.rerank-info {
  margin-top: 12px;
}

.llm-label {
  font-size: 12px;
  color: #6b7280;
  margin-top: 6px;
}

.llm-text {
  font-size: 13px;
  color: #4b5563;
  margin-top: 4px;
  white-space: pre-wrap;
}

.llm-error {
  margin-top: 8px;
  font-size: 12px;
  color: #f56c6c;
}
</style>
