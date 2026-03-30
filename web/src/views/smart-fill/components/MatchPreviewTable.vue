<script setup lang="ts">
import { computed, ref, watch } from "vue";
import {
  DEFAULT_HIGH_CONFIDENCE_THRESHOLD,
  LLM_REVIEW_PASS_THRESHOLD,
  type MatchIssue,
  type MatchPreviewItem
} from "@/api/matching";

const props = defineProps<{
  items: MatchPreviewItem[];
  loading?: boolean;
  highConfidenceThreshold?: number;
  /** LLM 流式处理是否进行中 */
  llmStreaming?: boolean;
}>();

const emit = defineEmits<{
  (e: "select", rowIndex: number, spec: MatchPreviewItem["bestMatch"] | null): void;
  (e: "showDetail", item: MatchPreviewItem): void;
}>();

type ReviewStatus =
  | "none"
  | "direct"
  | "manual"
  | "pending"
  | "waiting"
  | "streaming"
  | "passed"
  | "rejected"
  | "blocked"
  | "error";

type Selection = { type: "best"; manualConfirmed: boolean };

const selectedSpecs = ref<Map<number, Selection | null>>(new Map());

const isNoAnswerPlaceholderRow = (item: MatchPreviewItem) => {
  const project = (item.sourceProject || "").trim();
  const specification = (item.sourceSpecification || "").trim();
  if (specification) return false;

  const placeholderProjects = new Set(["其他", "-", "/", "无", "n/a", "na"]);
  return placeholderProjects.has(project.toLowerCase());
};

const normalizeReviewScore = (score?: number) => {
  if (score === undefined || score === null) return 0;
  if (score > 0 && score <= 1) {
    return Math.min(score * 100, 100);
  }
  return Math.min(score, 100);
};

const effectiveHighConfidenceThreshold = computed(
  () => props.highConfidenceThreshold ?? DEFAULT_HIGH_CONFIDENCE_THRESHOLD
);

const getDecision = (item: MatchPreviewItem) =>
  item.bestMatch?.decision ?? "manualReview";

const isAutoApply = (item: MatchPreviewItem) => getDecision(item) === "autoApply";

const isRejectDecision = (item: MatchPreviewItem) => getDecision(item) === "reject";

const isHighConfidence = (item: MatchPreviewItem) =>
  isAutoApply(item) &&
  (item.bestMatch?.score ?? 0) >= effectiveHighConfidenceThreshold.value;

const requiresReview = (item: MatchPreviewItem) =>
  !!item.bestMatch &&
  getDecision(item) === "manualReview" &&
  !item.bestMatch.hasHardConflict;

const isReviewPassed = (item: MatchPreviewItem) =>
  normalizeReviewScore(item.bestMatch?.llmScore) >= LLM_REVIEW_PASS_THRESHOLD;

const isReviewInFlight = (item: MatchPreviewItem) =>
  requiresReview(item) &&
  !item.bestMatch?.isLlmReviewed &&
  !item.llmReviewError &&
  (props.llmStreaming || item.llmReviewDraft !== undefined);

const canUseBestMatch = (item: MatchPreviewItem) => {
  if (!item.bestMatch || isNoAnswerPlaceholderRow(item)) {
    return false;
  }

  if (isRejectDecision(item)) {
    return false;
  }

  if (isReviewInFlight(item)) {
    return false;
  }

  return true;
};

const initSelections = () => {
  selectedSpecs.value.clear();
  props.items.forEach(item => {
    if (item.bestMatch && isHighConfidence(item) && !isNoAnswerPlaceholderRow(item)) {
      selectedSpecs.value.set(item.rowIndex, {
        type: "best",
        manualConfirmed: false
      });
    } else {
      selectedSpecs.value.set(item.rowIndex, null);
    }
  });
};

watch(
  () => [props.items, props.highConfidenceThreshold],
  () => initSelections(),
  { immediate: true }
);

const getSelection = (rowIndex: number) => selectedSpecs.value.get(rowIndex);

const handleSelectBest = (item: MatchPreviewItem) => {
  if (!canUseBestMatch(item)) {
    return;
  }

  selectedSpecs.value.set(item.rowIndex, {
    type: "best",
    manualConfirmed: true
  });
  emit("select", item.rowIndex, item.bestMatch ?? null);
};

const handleClearSelection = (item: MatchPreviewItem) => {
  selectedSpecs.value.set(item.rowIndex, null);
  emit("select", item.rowIndex, null);
};

const clearSelectionByRow = (rowIndex: number) => {
  selectedSpecs.value.set(rowIndex, null);
};

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

const formatScore = (score: number) => `${(score * 100).toFixed(1)}%`;

const getPrimaryIssue = (item: MatchPreviewItem): MatchIssue | undefined =>
  item.bestMatch?.issues?.[0];

const formatIssueComparison = (issue?: MatchIssue) => {
  if (!issue?.sourceValue && !issue?.candidateValue) {
    return "";
  }

  if (issue.sourceValue && issue.candidateValue) {
    return `源值 ${issue.sourceValue}，候选 ${issue.candidateValue}`;
  }

  if (issue.sourceValue) {
    return `源值 ${issue.sourceValue}`;
  }

  return `候选 ${issue?.candidateValue}`;
};

const getDecisionText = (decision?: string) => {
  switch (decision) {
    case "autoApply":
      return "自动采用";
    case "reject":
      return "拒绝";
    case "manualReview":
    default:
      return "人工确认";
  }
};

const getDecisionTagType = (
  decision?: string
): "success" | "warning" | "danger" | "info" => {
  switch (decision) {
    case "autoApply":
      return "success";
    case "reject":
      return "danger";
    case "manualReview":
      return "warning";
    default:
      return "info";
  }
};

const getReviewStatus = (item: MatchPreviewItem): ReviewStatus => {
  if (!item.bestMatch) return "none";
  if (item.bestMatch.hasHardConflict || isRejectDecision(item)) return "blocked";
  if (isHighConfidence(item)) return "direct";
  if (item.llmReviewError) return "error";
  if (item.bestMatch.isLlmReviewed) {
    return isReviewPassed(item) ? "passed" : "rejected";
  }
  if (requiresReview(item) && item.llmReviewDraft !== undefined) return "streaming";
  if (props.llmStreaming && requiresReview(item)) return "waiting";
  if (requiresReview(item)) return "pending";
  if (isAutoApply(item)) return "manual";
  return "none";
};

const formatLlmScore = (score?: number) => {
  const normalized = normalizeReviewScore(score);
  if (normalized <= 0) return "-";
  return `${normalized.toFixed(0)}`;
};

const getReviewStatusText = (item: MatchPreviewItem) => {
  const status = getReviewStatus(item);
  switch (status) {
    case "direct":
      return "直接采用";
    case "manual":
      return "待人工确认";
    case "waiting":
      return "等待复核";
    case "pending":
      return "待复核";
    case "streaming":
      return "复核中...";
    case "passed":
      return `${formatLlmScore(item.bestMatch?.llmScore)}分通过`;
    case "rejected":
      return `${formatLlmScore(item.bestMatch?.llmScore)}分未通过`;
    case "blocked":
      return "硬冲突拦截";
    case "error":
      return "复核失败";
    default:
      return "-";
  }
};

const getReviewTagType = (item: MatchPreviewItem) => {
  switch (getReviewStatus(item)) {
    case "direct":
    case "passed":
      return "success";
    case "manual":
    case "pending":
    case "streaming":
      return "warning";
    case "waiting":
      return "info";
    case "blocked":
    case "rejected":
    case "error":
      return "danger";
    default:
      return "info";
  }
};

const stats = computed(() => {
  const total = props.items.length;
  const matched = props.items.filter(i => i.hasMatch).length;
  const perfect = props.items.filter(
    i => i.hasMatch && isHighConfidence(i)
  ).length;
  const imperfect = total - perfect;
  const selected = Array.from(selectedSpecs.value.values()).filter(v => v !== null)
    .length;
  const ambiguous = props.items.filter(i => i.bestMatch?.isAmbiguous).length;
  return { total, matched, perfect, imperfect, selected, ambiguous };
});

type ScoreFilter = "all" | "perfect" | "imperfect";
const scoreFilter = ref<ScoreFilter>("all");

const filteredItems = computed(() => {
  if (scoreFilter.value === "all") return props.items;
  if (scoreFilter.value === "perfect") {
    return props.items.filter(
      i => i.hasMatch && i.bestMatch && i.bestMatch.score >= 0.9995
    );
  }
  return props.items.filter(
    i => !i.hasMatch || !isHighConfidence(i)
  );
});

const hasReasonColumn = computed(() =>
  props.items.some(
    item =>
      !item.hasMatch ||
      !!item.noMatchReason ||
      !!item.bestMatch?.issues?.length ||
      !!item.bestMatch?.conflictSummary?.length ||
      !!item.bestMatch?.evidenceSummary?.length ||
      !!item.bestMatch?.llmReason ||
      !!item.llmReviewError
  )
);

defineExpose({
  getSelections: () => {
    const selections: Array<{
      rowIndex: number;
      specId?: number;
      matchScore?: number;
      llmReviewScore?: number;
      manualConfirmed?: boolean;
    }> = [];

    selectedSpecs.value.forEach((selection, rowIndex) => {
      if (!selection) return;

      const item = props.items.find(i => i.rowIndex === rowIndex);
      if (!item?.bestMatch) return;

      selections.push({
        rowIndex,
        specId: item.bestMatch.specId,
        matchScore: item.bestMatch.score,
        llmReviewScore: normalizeReviewScore(item.bestMatch.llmScore),
        manualConfirmed: selection.manualConfirmed
      });
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
        <span class="divider">|</span>
        <span class="ambiguous">高歧义 {{ stats.ambiguous }} 行</span>
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
          自动采用 ({{ stats.perfect }})
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

      <!-- 最终决策 -->
      <el-table-column label="决策" width="110" align="center">
        <template #default="{ row }">
          <el-tag
            v-if="row.bestMatch"
            size="small"
            :type="getDecisionTagType(row.bestMatch.decision)"
          >
            {{ getDecisionText(row.bestMatch.decision) }}
          </el-tag>
          <span v-else class="reason-none">-</span>
        </template>
      </el-table-column>

      <!-- 最佳匹配 -->
      <el-table-column label="最佳匹配" min-width="260">
        <template #default="{ row }">
          <div v-if="row.bestMatch" class="match-best">
            <div class="match-main">
              <div class="match-text">
                {{ row.bestMatch.project }} - {{ row.bestMatch.specification }}
              </div>
              <div class="match-meta">
                <el-tag
                  size="small"
                  type="info"
                  effect="plain"
                >
                  召回 {{ row.bestMatch.recalledCandidateCount }}
                </el-tag>
                <el-tag
                  v-if="row.bestMatch.isAmbiguous"
                  size="small"
                  type="warning"
                  effect="plain"
                >
                  高歧义
                </el-tag>
                <el-tag
                  v-if="row.bestMatch.hasHardConflict"
                  size="small"
                  type="danger"
                  effect="plain"
                >
                  硬冲突
                </el-tag>
              </div>
            </div>
            <div class="match-score">{{ formatScore(row.bestMatch.score) }}</div>
            <div
              v-if="getPrimaryIssue(row)"
              class="issue-summary"
            >
              <span class="issue-summary__title">问题：</span>
              <span>{{ getPrimaryIssue(row)?.message }}</span>
              <div
                v-if="formatIssueComparison(getPrimaryIssue(row))"
                class="issue-summary__meta"
              >
                {{ formatIssueComparison(getPrimaryIssue(row)) }}
              </div>
            </div>
            <div
              v-if="row.bestMatch.evidenceSummary?.length"
              class="evidence-summary"
            >
              {{ row.bestMatch.evidenceSummary.slice(0, 2).join("；") }}
            </div>
            <div
              v-if="row.bestMatch.conflictSummary?.length"
              class="conflict-summary"
            >
              {{ row.bestMatch.conflictSummary.join("；") }}
            </div>
          </div>
          <div v-else class="no-match">
            <el-tag type="info" size="small">无匹配</el-tag>
          </div>
        </template>
      </el-table-column>

      <!-- 复核状态 -->
      <el-table-column label="复核状态" width="130" align="center">
        <template #default="{ row }">
          <div class="ai-status-cell">
            <el-tag
              v-if="getReviewStatus(row) !== 'none'"
              size="small"
              :type="getReviewTagType(row)"
              :class="{ 'ai-streaming': getReviewStatus(row) === 'streaming' }"
            >
              {{ getReviewStatusText(row) }}
            </el-tag>
            <span v-else class="reason-none">-</span>
          </div>
        </template>
      </el-table-column>

      <!-- 验收标准预览 -->
      <el-table-column label="验收标准" min-width="180">
        <template #default="{ row }">
          <span class="acceptance-text">
            {{ row.bestMatch?.acceptance || "-" }}
          </span>
        </template>
      </el-table-column>

      <!-- 备注预览 -->
      <el-table-column label="备注" min-width="150">
        <template #default="{ row }">
          <span class="acceptance-text">
            {{ row.bestMatch?.remark || "-" }}
          </span>
        </template>
      </el-table-column>

      <!-- 不匹配原因 / 复核说明 -->
      <el-table-column v-if="hasReasonColumn" label="说明" min-width="220">
        <template #default="{ row }">
          <div
            v-if="!row.hasMatch || row.noMatchReason || row.bestMatch?.issues?.length || row.bestMatch?.conflictSummary?.length || row.bestMatch?.evidenceSummary?.length || row.bestMatch?.llmReason || row.llmReviewError"
            class="reason-cell"
          >
            <div v-if="!row.hasMatch && row.noMatchReason" class="reason-text">
              {{ row.noMatchReason }}
            </div>
            <div
              v-if="getPrimaryIssue(row)"
              class="reason-issue"
            >
              问题：{{ getPrimaryIssue(row)?.message }}
            </div>
            <div
              v-if="formatIssueComparison(getPrimaryIssue(row))"
              class="reason-issue-detail"
            >
              {{ formatIssueComparison(getPrimaryIssue(row)) }}
            </div>
            <div
              v-if="getPrimaryIssue(row)?.suggestedAction"
              class="reason-issue-detail"
            >
              建议：{{ getPrimaryIssue(row)?.suggestedAction }}
            </div>
            <div
              v-if="row.bestMatch?.conflictSummary?.length"
              class="reason-conflict"
            >
              冲突：{{ row.bestMatch.conflictSummary.join("；") }}
            </div>
            <div
              v-else-if="row.bestMatch?.evidenceSummary?.length"
              class="suggestion-reason"
            >
              证据：{{ row.bestMatch.evidenceSummary.join("；") }}
            </div>
            <div v-if="row.bestMatch?.llmReason" class="suggestion-reason">
              复核结论：{{ row.bestMatch.llmReason }}
            </div>
            <div v-if="row.llmReviewError" class="suggestion-reason">
              复核异常：{{ row.llmReviewError }}
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
              v-if="row.bestMatch && canUseBestMatch(row) && getSelection(row.rowIndex)?.type !== 'best'"
              size="small"
              @click="handleSelectBest(row)"
            >
              {{
                isHighConfidence(row)
                  ? "使用匹配"
                  : row.bestMatch?.decision === "manualReview"
                    ? "人工确认"
                    : "手动采用"
              }}
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

.ambiguous {
  color: #b45309;
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
  flex-direction: column;
  gap: 8px;
}

.match-main {
  flex: 1;
  min-width: 0;
}

.match-text {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: var(--color-text);
  font-weight: 500;
}

.match-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin-top: 6px;
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

.reason-conflict {
  color: #b42318;
  font-size: 12px;
}

.reason-issue {
  color: #b42318;
  font-size: 12px;
  font-weight: 600;
}

.reason-issue-detail {
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

.suggestion-reason {
  font-size: 12px;
  color: #9ca3af;
  font-style: italic;
}

.evidence-summary {
  font-size: 12px;
  color: #4b5563;
  line-height: 1.5;
}

.conflict-summary {
  font-size: 12px;
  color: #b42318;
  line-height: 1.5;
}

.issue-summary {
  padding: 8px 10px;
  border-radius: 10px;
  background: #fff7ed;
  color: #9a3412;
  font-size: 12px;
  line-height: 1.5;
}

.issue-summary__title {
  font-weight: 600;
}

.issue-summary__meta {
  margin-top: 4px;
  color: #7c2d12;
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
