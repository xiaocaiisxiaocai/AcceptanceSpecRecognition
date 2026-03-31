<script setup lang="ts">
import { computed, onUnmounted, watch } from "vue";
import type { MatchPreviewItem } from "@/api/matching";
import ScoreDetailBestMatchSection from "./ScoreDetailBestMatchSection.vue";
import ScoreDetailCandidateList from "./ScoreDetailCandidateList.vue";
import ScoreDetailDiffSection from "./ScoreDetailDiffSection.vue";
import { formatScore } from "./scoreDetail.formatters";
import { useScoreDetailDiff } from "../composables/useScoreDetailDiff";

const props = defineProps<{
  visible: boolean;
  item: MatchPreviewItem | null;
}>();

const emit = defineEmits<{
  (e: "update:visible", value: boolean): void;
}>();

const dialogVisible = computed({
  get: () => props.visible,
  set: value => emit("update:visible", value)
});

const topCandidates = computed(() => props.item?.bestMatch?.topCandidates ?? []);
const inlineDiffCache = new Map<
  string,
  { leftHtml: string; rightHtml: string; isSame: boolean }
>();

const clearInlineDiffCache = () => {
  inlineDiffCache.clear();
};

const getConfidenceClass = (
  level: string
): "success" | "warning" | "danger" | "info" => {
  const map: Record<string, "success" | "warning" | "danger" | "info"> = {
    high: "success",
    medium: "warning",
    low: "danger"
  };
  return map[level] || "info";
};

const getConfidenceText = (level: string) => {
  const map: Record<string, string> = {
    high: "高",
    medium: "中",
    low: "低"
  };
  return map[level] || "无";
};

watch(
  () => props.visible,
  visible => {
    if (!visible) {
      clearInlineDiffCache();
    }
  }
);

watch(
  () => props.item,
  () => {
    clearInlineDiffCache();
  }
);

onUnmounted(() => {
  clearInlineDiffCache();
});

const {
  comparisonRank,
  diffViewMode,
  rawOnlyDiff,
  comparisonOptions,
  comparisonCandidate,
  comparisonRows,
  rawComparisonRows,
  sourceBestRows,
  isComparedCandidate,
  handleSelectComparisonCandidate,
  isCandidateExpanded
} = useScoreDetailDiff({
  item: computed(() => props.item),
  topCandidates,
  inlineDiffCache
});
</script>

<template>
  <el-dialog
    v-model="dialogVisible"
    title="匹配详情"
    width="920px"
    top="5vh"
    destroy-on-close
  >
    <el-scrollbar class="dialog-scroll">
      <template v-if="item">
        <div class="detail-layout">
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

          <ScoreDetailBestMatchSection :item="item" />

          <ScoreDetailDiffSection
            :item="item"
            :top-candidates="topCandidates"
            :comparison-candidate="comparisonCandidate"
            :comparison-options="comparisonOptions"
            :comparison-rank="comparisonRank"
            :diff-view-mode="diffViewMode"
            :raw-only-diff="rawOnlyDiff"
            :source-best-rows="sourceBestRows"
            :comparison-rows="comparisonRows"
            :raw-comparison-rows="rawComparisonRows"
            @update:comparison-rank="value => (comparisonRank = value)"
            @update:diff-view-mode="value => (diffViewMode = value)"
            @update:raw-only-diff="value => (rawOnlyDiff = value)"
          />

          <ScoreDetailCandidateList
            :top-candidates="topCandidates"
            :is-compared-candidate="isComparedCandidate"
            :is-candidate-expanded="isCandidateExpanded"
            :handle-select-comparison-candidate="handleSelectComparisonCandidate"
          />
        </div>
      </template>
    </el-scrollbar>

    <template #footer>
      <el-button @click="dialogVisible = false">关闭</el-button>
    </template>
  </el-dialog>
</template>
<style scoped>
.dialog-scroll {
  max-height: 72vh;
  padding-right: 4px;
}

.detail-layout {
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.source-info h4,
.best-section h4,
.candidate-header h4 {
  margin: 0 0 12px;
  font-size: 14px;
  font-weight: 600;
  color: var(--color-text);
}

.candidate-header {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 12px;
}

.candidate-header span {
  font-size: 12px;
  color: #6b7280;
}

.diff-section {
  margin-bottom: 14px;
  padding: 14px;
  border: 1px solid #e5e7eb;
  border-radius: 14px;
  background: linear-gradient(180deg, #fcfdff 0%, #f7f9fc 100%);
}

.diff-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 12px;
}

.diff-header h5 {
  margin: 0;
  font-size: 14px;
  color: #111827;
}

.diff-header p {
  margin: 4px 0 0;
  font-size: 12px;
  color: #6b7280;
}

.diff-toolbar {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 10px;
}

.diff-columns {
  display: grid;
  grid-template-columns: 120px minmax(0, 1fr) minmax(0, 1fr);
  gap: 10px;
  margin-bottom: 8px;
}

.diff-column {
  min-width: 0;
}

.diff-column:first-child {
  visibility: hidden;
}

.diff-column-title {
  padding: 8px 10px;
  border-radius: 10px;
  background: #eef4ff;
  color: #1f2937;
  font-size: 12px;
  font-weight: 600;
}

.diff-rows {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.diff-row {
  display: grid;
  grid-template-columns: 120px minmax(0, 1fr) minmax(0, 1fr);
  gap: 10px;
  align-items: stretch;
}

.diff-row-same .diff-cell {
  background: #f9fafb;
}

.diff-label {
  display: flex;
  align-items: flex-start;
  justify-content: flex-start;
  padding-top: 10px;
  font-size: 12px;
  color: #6b7280;
}

.diff-cell {
  min-width: 0;
  padding: 10px 12px;
  border-radius: 12px;
  border: 1px solid #e5e7eb;
  background: #fff;
}

.diff-content {
  font-size: 13px;
  color: #111827;
  line-height: 1.7;
  word-break: break-word;
}

.raw-diff-shell {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.raw-diff-meta {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.raw-diff-desc {
  font-size: 12px;
  color: #6b7280;
}

.raw-diff-header {
  display: grid;
  grid-template-columns: 90px minmax(0, 1fr) minmax(0, 1fr);
  gap: 10px;
}

.raw-diff-header-spacer {
  min-height: 1px;
}

.raw-diff-header-title {
  padding: 8px 10px;
  border-radius: 10px;
  background: #eef4ff;
  color: #1f2937;
  font-size: 12px;
  font-weight: 600;
}

.raw-diff-rows {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.raw-diff-row {
  display: grid;
  grid-template-columns: 90px minmax(0, 1fr) minmax(0, 1fr);
  gap: 10px;
  align-items: stretch;
}

.raw-line-cell {
  display: flex;
  flex-direction: column;
  gap: 4px;
  align-items: flex-start;
  justify-content: flex-start;
  padding: 10px 8px;
  border-radius: 12px;
  background: #f3f4f6;
}

.raw-line-no {
  font-size: 12px;
  font-weight: 700;
  color: #374151;
}

.raw-line-label {
  font-size: 12px;
  color: #6b7280;
}

.raw-pane-cell {
  min-width: 0;
  border: 1px solid #e5e7eb;
  border-radius: 12px;
  background: #fff;
  overflow: hidden;
}

.raw-pane-inner {
  display: flex;
  flex-direction: column;
  min-height: 100%;
}

.raw-pane-label {
  padding: 8px 10px;
  border-bottom: 1px solid #eef2f7;
  background: #f8fafc;
  font-size: 12px;
  color: #6b7280;
}

.raw-pane-content {
  padding: 12px;
  font-family: Consolas, "Courier New", monospace;
  font-size: 13px;
  color: #111827;
  line-height: 1.75;
  white-space: normal;
  word-break: break-word;
}

.info-block {
  margin-top: 12px;
  padding: 12px 14px;
  border-radius: 10px;
  background: #f8fafc;
}

.info-block--danger {
  background: #fff4f4;
}

.info-block--issue {
  background: #fff9f5;
}

.info-block.compact {
  margin-top: 10px;
}

.info-label {
  font-size: 12px;
  color: #6b7280;
}

.info-text {
  margin-top: 4px;
  font-size: 13px;
  color: #374151;
  white-space: pre-wrap;
  line-height: 1.6;
}

.info-error {
  margin-top: 8px;
  font-size: 12px;
  color: #f56c6c;
}

.issue-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
  margin-top: 8px;
}

.entity-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
  margin-top: 8px;
}

.entity-card {
  padding: 12px;
  border: 1px solid #dbeafe;
  border-radius: 12px;
  background: rgba(255, 255, 255, 0.82);
}

.entity-card__header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
}

.entity-card__title {
  font-size: 13px;
  font-weight: 600;
  color: #1d4ed8;
  line-height: 1.6;
}

.entity-card__meta {
  margin-top: 6px;
  font-size: 12px;
  color: #1e3a8a;
  line-height: 1.6;
}

.issue-card {
  padding: 12px;
  border: 1px solid #fed7aa;
  border-radius: 12px;
  background: rgba(255, 255, 255, 0.78);
}

.issue-card__header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
}

.issue-card__title {
  font-size: 13px;
  font-weight: 600;
  color: #9a3412;
  line-height: 1.6;
}

.issue-card__meta {
  margin-top: 6px;
  font-size: 12px;
  color: #7c2d12;
  line-height: 1.6;
}

.issue-card__action {
  margin-top: 8px;
  font-size: 12px;
  color: #b45309;
  line-height: 1.6;
}

.candidate-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.candidate-card {
  border-radius: 14px;
  transition:
    border-color 0.2s ease,
    background 0.2s ease,
    box-shadow 0.2s ease,
    transform 0.2s ease;
}

.candidate-card.is-top1 {
  border-color: #409eff;
  background: linear-gradient(180deg, #f8fbff 0%, #ffffff 100%);
}

.candidate-card.is-compared {
  border-color: #e6a23c;
  background: linear-gradient(180deg, #fffaf2 0%, #ffffff 100%);
}

.candidate-card.is-clickable {
  cursor: pointer;
}

.candidate-card.is-clickable:hover {
  border-color: #cbd5e1;
  box-shadow: 0 10px 24px rgba(15, 23, 42, 0.06);
  transform: translateY(-1px);
}

.candidate-card.is-compared.is-clickable:hover {
  border-color: #e6a23c;
}

.candidate-detail {
  display: flex;
  flex-direction: column;
}

.candidate-collapsed {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 12px 14px;
  border-radius: 12px;
  background: #f8fafc;
}

.candidate-collapsed-meta {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 12px;
  font-size: 12px;
  color: #4b5563;
}

.candidate-collapsed-summary {
  font-size: 13px;
  color: #374151;
  line-height: 1.6;
}

.candidate-collapsed-tip {
  font-size: 12px;
  color: #b45309;
}

.candidate-top {
  display: flex;
  justify-content: space-between;
  gap: 16px;
  margin-bottom: 12px;
}

.candidate-rank {
  font-size: 12px;
  color: #6b7280;
  margin-bottom: 6px;
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
}

.candidate-status {
  display: inline-flex;
  align-items: center;
  padding: 2px 8px;
  border-radius: 999px;
  font-size: 11px;
  line-height: 1.4;
}

.candidate-status-top1 {
  background: rgba(64, 158, 255, 0.12);
  color: #1d4ed8;
}

.candidate-status-compare {
  background: rgba(230, 162, 60, 0.14);
  color: #b45309;
}

.candidate-title {
  line-height: 1.6;
  color: #111827;
}

.candidate-score {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  min-width: 110px;
}

.candidate-score strong {
  font-size: 18px;
  color: #111827;
}

.candidate-score span {
  font-size: 12px;
  color: #6b7280;
}

.score-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(130px, 1fr));
  gap: 8px;
  margin-top: 12px;
}

.score-chip {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  padding: 8px 10px;
  border-radius: 10px;
  background: #f8fafc;
  font-size: 12px;
  color: #6b7280;
}

.score-chip strong {
  color: #111827;
  font-size: 13px;
}

:deep(.inline-mark) {
  padding: 0 2px;
  border-radius: 4px;
}

:deep(.inline-mark-old) {
  background: rgba(245, 108, 108, 0.18);
  color: #b42318;
}

:deep(.inline-mark-new) {
  background: rgba(103, 194, 58, 0.18);
  color: #166534;
}

:deep(.placeholder-text) {
  color: #9ca3af;
  font-style: italic;
}

@media (max-width: 900px) {
  .candidate-header,
  .candidate-top,
  .diff-header,
  .raw-diff-meta {
    flex-direction: column;
  }

  .candidate-score {
    align-items: flex-start;
  }

  .diff-columns,
  .diff-row,
  .raw-diff-header,
  .raw-diff-row {
    grid-template-columns: 1fr;
  }

  .diff-column:first-child,
  .raw-diff-header-spacer {
    display: none;
  }

  .diff-label,
  .raw-line-cell {
    padding-top: 0;
    font-weight: 600;
  }
}
</style>
