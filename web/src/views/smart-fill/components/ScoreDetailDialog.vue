<script setup lang="ts">
import { computed, onUnmounted, ref, watch } from "vue";
import type { MatchPreviewItem } from "@/api/matching";
import ScoreDetailBestMatchSection from "./ScoreDetailBestMatchSection.vue";
import ScoreDetailCandidateList from "./ScoreDetailCandidateList.vue";
import ScoreDetailDecisionSummarySection from "./ScoreDetailDecisionSummarySection.vue";
import ScoreDetailDiffSection from "./ScoreDetailDiffSection.vue";
import {
  formatScore,
  getConfidenceClass,
  getConfidenceText
} from "./scoreDetail.formatters";
import { useScoreDetailDiff } from "../composables/useScoreDetailDiff";

const props = defineProps<{
  visible: boolean;
  item: MatchPreviewItem | null;
  ambiguityMargin?: number;
  highConfidenceThreshold?: number;
}>();

const emit = defineEmits<{
  (e: "update:visible", value: boolean): void;
}>();

const dialogVisible = computed({
  get: () => props.visible,
  set: value => emit("update:visible", value)
});
const activeTab = ref("decision");
const activeTechnicalTag = ref("overview");
const technicalTags = [
  { key: "overview", label: "技术概览" },
  { key: "sourceDiff", label: "源项差异" },
  { key: "candidateDiff", label: "候选对比" },
  { key: "candidateList", label: "候选列表" }
] as const;

const topCandidates = computed(() => props.item?.bestMatch?.topCandidates ?? []);
const inlineDiffCache = new Map<
  string,
  { leftHtml: string; rightHtml: string; isSame: boolean }
>();

const clearInlineDiffCache = () => {
  inlineDiffCache.clear();
};

watch(
  () => props.visible,
  visible => {
    if (!visible) {
      clearInlineDiffCache();
      activeTab.value = "decision";
      activeTechnicalTag.value = "overview";
    }
  }
);

watch(
  () => props.item,
  () => {
    clearInlineDiffCache();
    activeTab.value = "decision";
    activeTechnicalTag.value = "overview";
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
    width="1200px"
    top="5vh"
    class="score-detail-dialog"
    destroy-on-close
  >
    <el-scrollbar class="dialog-scroll">
      <template v-if="item">
        <el-tabs v-model="activeTab" class="detail-tabs">
          <el-tab-pane label="匹配结论" name="decision">
            <ScoreDetailDecisionSummarySection
              :item="item"
              :source-best-rows="sourceBestRows"
            />
          </el-tab-pane>
          <el-tab-pane label="技术详情" name="technical">
            <div class="detail-layout detail-layout--technical">
              <div class="source-brief-card">
                <div class="source-brief-head">
                  <div>
                    <div class="source-brief-label">源数据</div>
                    <div class="source-brief-project">{{ item.sourceProject || "-" }}</div>
                  </div>
                  <el-tag :type="getConfidenceClass(item.confidenceLevel)" size="small">
                    置信度 {{ getConfidenceText(item.confidenceLevel) }}
                  </el-tag>
                </div>
                <div class="source-brief-spec">
                  {{ item.sourceSpecification || "-" }}
                </div>
                <div class="source-brief-meta">
                  <span>最佳得分 {{ item.bestMatch ? formatScore(item.bestMatch.score) : "-" }}</span>
                  <span v-if="item.bestMatch">规格 {{ item.bestMatch.specId }}</span>
                </div>
              </div>

              <div class="technical-tag-bar">
                <el-check-tag
                  v-for="tag in technicalTags"
                  :key="tag.key"
                  :checked="activeTechnicalTag === tag.key"
                  class="technical-tag"
                  @change="checked => checked && (activeTechnicalTag = tag.key)"
                >
                  {{ tag.label }}
                </el-check-tag>
              </div>

              <ScoreDetailBestMatchSection
                v-if="activeTechnicalTag === 'overview'"
                :item="item"
                :ambiguity-margin="ambiguityMargin"
                :high-confidence-threshold="highConfidenceThreshold"
              />

              <ScoreDetailDiffSection
                v-else-if="activeTechnicalTag === 'sourceDiff'"
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
                :show-source-diff="true"
                :show-candidate-compare="false"
                @update:comparison-rank="value => (comparisonRank = value)"
                @update:diff-view-mode="value => (diffViewMode = value)"
                @update:raw-only-diff="value => (rawOnlyDiff = value)"
              />

              <ScoreDetailDiffSection
                v-else-if="activeTechnicalTag === 'candidateDiff'"
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
                :show-source-diff="false"
                :show-candidate-compare="true"
                @update:comparison-rank="value => (comparisonRank = value)"
                @update:diff-view-mode="value => (diffViewMode = value)"
                @update:raw-only-diff="value => (rawOnlyDiff = value)"
              />

              <ScoreDetailCandidateList
                v-else
                :top-candidates="topCandidates"
                :is-compared-candidate="isComparedCandidate"
                :is-candidate-expanded="isCandidateExpanded"
                :handle-select-comparison-candidate="handleSelectComparisonCandidate"
              />
            </div>
          </el-tab-pane>
        </el-tabs>
      </template>
    </el-scrollbar>

    <template #footer>
      <el-button @click="dialogVisible = false">关闭</el-button>
    </template>
  </el-dialog>
</template>
<style scoped>
.dialog-scroll {
  height: 100%;
  min-height: 0;
  padding-right: 4px;
}

:deep(.score-detail-dialog) {
  display: flex;
  flex-direction: column;
  max-width: calc(100vw - 32px);
  max-height: 90vh;
  margin-bottom: 0;
}

:deep(.score-detail-dialog .el-dialog__body) {
  flex: 1;
  min-height: 0;
  overflow: hidden;
}

:deep(.score-detail-dialog .el-dialog__footer) {
  padding-top: 12px;
}

.detail-layout {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.detail-layout--technical {
  padding-bottom: 8px;
}

.detail-tabs {
  min-height: 100%;
}

.detail-tabs :deep(.el-tabs__header) {
  margin-bottom: 16px;
}

.detail-tabs :deep(.el-tabs__content) {
  padding-bottom: 8px;
}

.source-brief-card {
  padding: 14px 16px;
  border: 1px solid #e5e7eb;
  border-radius: 14px;
  background: linear-gradient(180deg, #fcfdff 0%, #f7f9fc 100%);
}

.source-brief-head,
.source-brief-meta,
.technical-tag-bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  flex-wrap: wrap;
}

.source-brief-label,
.source-brief-meta {
  font-size: 12px;
  color: #6b7280;
}

.source-brief-project {
  margin-top: 4px;
  font-size: 15px;
  font-weight: 700;
  color: #111827;
}

.source-brief-spec {
  margin-top: 8px;
  font-size: 13px;
  color: #374151;
  line-height: 1.7;
  word-break: break-word;
}

.source-brief-meta {
  margin-top: 10px;
  justify-content: flex-start;
}

.technical-tag {
  padding: 6px 12px;
  border-radius: 999px;
  font-size: 12px;
  line-height: 1.5;
}

@media (max-width: 900px) {
  .source-brief-head {
    flex-direction: column;
  }
}
</style>
