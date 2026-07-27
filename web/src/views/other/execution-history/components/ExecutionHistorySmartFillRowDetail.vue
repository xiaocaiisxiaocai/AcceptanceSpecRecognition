<script setup lang="ts">
import { computed } from "vue";
import type { ExecutionHistorySmartFillRow } from "@/api/execution-history";

const props = defineProps<{
  row: ExecutionHistorySmartFillRow;
  detailRow?: ExecutionHistorySmartFillRow;
  loading: boolean;
  errorMessage?: string;
}>();

defineEmits<{
  retry: [];
}>();

const displayRow = computed(() => props.detailRow ?? props.row);
const bestMatch = computed(() => displayRow.value.previewSnapshot.bestMatch);
const candidates = computed(() => bestMatch.value?.topCandidates ?? []);
const scoreDetails = computed(() =>
  Object.entries(bestMatch.value?.scoreDetails ?? {})
);

const scoreLabels: Record<string, string> = {
  embedding: "语义相似度",
  rerank: "重排得分",
  exact: "精确匹配"
};

const formatScore = (score?: number) =>
  score === undefined || score === null ? "-" : `${(score * 100).toFixed(1)}%`;

const getDecisionText = (decision?: string) => {
  switch (decision) {
    case "autoApply":
      return "自动采用";
    case "manualReview":
      return "人工复核";
    case "reject":
      return "不采用";
    default:
      return "-";
  }
};

const getVerdictText = (verdict?: string) => {
  switch (verdict) {
    case "equivalent":
      return "等价";
    case "different":
      return "不等价";
    case "uncertain":
      return "不确定";
    default:
      return "-";
  }
};
</script>

<template>
  <section
    class="row-detail"
    aria-label="执行历史行完整详情"
    data-testid="execution-history-smart-fill-row-detail"
  >
    <div class="row-detail__header">
      <strong>第 {{ row.rowIndex + 1 }} 行完整回放</strong>
      <el-tag v-if="detailRow" type="success" effect="plain">完整归档</el-tag>
      <el-tag v-else type="info" effect="plain">精简概要</el-tag>
    </div>

    <el-skeleton v-if="loading" :rows="3" animated />

    <template v-else-if="errorMessage">
      <el-alert
        :title="errorMessage"
        type="warning"
        show-icon
        :closable="false"
      />
      <el-button
        class="retry-button"
        type="primary"
        link
        @click="$emit('retry')"
      >
        重试加载该行详情
      </el-button>
    </template>

    <template v-else>
      <el-descriptions :column="2" border size="small">
        <el-descriptions-item label="源项目">
          {{ displayRow.sourceProject || "-" }}
        </el-descriptions-item>
        <el-descriptions-item label="源规格">
          {{ displayRow.sourceSpecification || "-" }}
        </el-descriptions-item>
        <el-descriptions-item label="最佳候选">
          <template v-if="bestMatch">
            {{ bestMatch.project || "-" }} /
            {{ bestMatch.specification || "-" }}
          </template>
          <template v-else>无候选</template>
        </el-descriptions-item>
        <el-descriptions-item label="规则基础分">
          {{ formatScore(bestMatch?.score) }}
        </el-descriptions-item>
        <el-descriptions-item label="匹配证据">
          {{ bestMatch?.evidenceSummary?.join("；") || "-" }}
        </el-descriptions-item>
        <el-descriptions-item label="冲突证据">
          {{ bestMatch?.conflictSummary?.join("；") || "-" }}
        </el-descriptions-item>
        <el-descriptions-item label="AI 裁决">
          {{ getVerdictText(bestMatch?.llmEquivalence?.verdict) }}
          <template v-if="bestMatch?.llmEquivalence">
            · {{ formatScore(bestMatch.llmEquivalence.confidence) }}
          </template>
        </el-descriptions-item>
        <el-descriptions-item label="裁决说明">
          {{
            bestMatch?.llmEquivalence?.reason ||
            bestMatch?.reviewReason ||
            bestMatch?.reviewCommentary ||
            "-"
          }}
        </el-descriptions-item>
        <el-descriptions-item label="人工覆盖验收">
          {{ displayRow.executionSnapshot.overrideAcceptance || "-" }}
        </el-descriptions-item>
        <el-descriptions-item label="人工覆盖备注">
          {{ displayRow.executionSnapshot.overrideRemark || "-" }}
        </el-descriptions-item>
        <el-descriptions-item label="最终验收">
          {{ displayRow.executionSnapshot.finalAcceptance || "-" }}
        </el-descriptions-item>
        <el-descriptions-item label="最终备注">
          {{ displayRow.executionSnapshot.finalRemark || "-" }}
        </el-descriptions-item>
      </el-descriptions>

      <div v-if="scoreDetails.length > 0" class="evidence-list">
        <span class="evidence-list__label">评分证据</span>
        <el-tag
          v-for="[name, score] in scoreDetails"
          :key="name"
          effect="plain"
          size="small"
        >
          {{ scoreLabels[name] ?? name }} {{ formatScore(score) }}
        </el-tag>
      </div>

      <div class="candidate-section">
        <div class="candidate-section__title">
          候选明细（{{ candidates.length }}）
        </div>
        <el-table
          v-if="candidates.length > 0"
          :data="candidates"
          border
          size="small"
        >
          <el-table-column prop="rank" label="排名" width="70" />
          <el-table-column prop="project" label="项目" min-width="120" />
          <el-table-column
            prop="specification"
            label="规格"
            min-width="180"
            show-overflow-tooltip
          />
          <el-table-column label="规则基础分" width="110">
            <template #default="{ row: candidate }">
              {{ formatScore(candidate.score) }}
            </template>
          </el-table-column>
          <el-table-column label="系统决策" width="100">
            <template #default="{ row: candidate }">
              {{ getDecisionText(candidate.decision) }}
            </template>
          </el-table-column>
        </el-table>
        <el-empty v-else description="无候选明细" :image-size="48" />
      </div>
    </template>
  </section>
</template>

<style scoped>
.row-detail {
  padding: 12px;
  overflow: auto;
  background: var(--el-fill-color-extra-light);
  border: 1px solid var(--el-border-color-light);
  border-radius: 4px;
}

.row-detail__header,
.evidence-list {
  display: flex;
  gap: 8px;
  align-items: center;
}

.row-detail__header {
  margin-bottom: 10px;
}

.retry-button {
  margin-top: 8px;
}

.evidence-list {
  flex-wrap: wrap;
  margin-top: 10px;
}

.evidence-list__label,
.candidate-section__title {
  color: var(--el-text-color-secondary);
}

.candidate-section {
  margin-top: 10px;
}

.candidate-section__title {
  margin-bottom: 6px;
  font-size: 13px;
}
</style>
