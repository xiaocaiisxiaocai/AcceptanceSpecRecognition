<script setup lang="ts">
import { computed } from "vue";
import type { MatchPreviewItem } from "@/api/matching";
import type {
  ScoreDetailDiffRow,
  ScoreDetailDiffViewMode
} from "../composables/useScoreDetailDiff";
import {
  getLlmEquivalenceDifferenceTone,
  getLlmEquivalenceDifferenceToneDescription,
  getLlmEquivalenceDifferenceToneTagType,
  getLlmEquivalenceDifferenceToneText,
  getLlmEquivalenceReasonTagType,
  getLlmEquivalenceReasonTypeText,
  getLlmEquivalenceSummaryText,
  getLlmEquivalenceVerdictTagType,
  getLlmEquivalenceVerdictText
} from "./scoreDetail.llmEquivalence";

const props = defineProps<{
  item: MatchPreviewItem;
  topCandidates: any[];
  comparisonCandidate: any | null;
  comparisonOptions: Array<{ label: string; value: number }>;
  comparisonRank: number | null;
  diffViewMode: ScoreDetailDiffViewMode;
  rawOnlyDiff: boolean;
  sourceBestRows: ScoreDetailDiffRow[];
  comparisonRows: ScoreDetailDiffRow[];
  rawComparisonRows: ScoreDetailDiffRow[];
  showSourceDiff?: boolean;
  showCandidateCompare?: boolean;
}>();

const emit = defineEmits<{
  (e: "update:comparisonRank", value: number | null): void;
  (e: "update:diffViewMode", value: ScoreDetailDiffViewMode): void;
  (e: "update:rawOnlyDiff", value: boolean): void;
}>();

const comparisonRankModel = computed({
  get: () => props.comparisonRank,
  set: value => emit("update:comparisonRank", value)
});

const diffViewModeModel = computed({
  get: () => props.diffViewMode,
  set: value => emit("update:diffViewMode", value)
});

const rawOnlyDiffModel = computed({
  get: () => props.rawOnlyDiff,
  set: value => emit("update:rawOnlyDiff", value)
});

const shouldShowSourceDiff = computed(() => props.showSourceDiff !== false);
const shouldShowCandidateCompare = computed(
  () => props.showCandidateCompare !== false
);
const sourceDiffEquivalence = computed(() => props.item.bestMatch?.llmEquivalence);
const sourceDiffTone = computed(() =>
  getLlmEquivalenceDifferenceTone(sourceDiffEquivalence.value)
);
</script>

<template>
  <div
    v-if="shouldShowSourceDiff"
    class="best-section"
  >
    <h4>源项与最佳匹配差异</h4>
    <div
      v-if="item.bestMatch?.llmEquivalence"
      class="source-diff-callout"
      :class="`source-diff-callout--${sourceDiffTone}`"
    >
      <div class="source-diff-callout__head">
        <span>AI 等价裁决</span>
        <div class="source-diff-callout__tags">
          <el-tag
            size="small"
            :type="getLlmEquivalenceVerdictTagType(item.bestMatch.llmEquivalence.verdict)"
          >
            {{ getLlmEquivalenceVerdictText(item.bestMatch.llmEquivalence.verdict) }}
          </el-tag>
          <el-tag
            size="small"
            effect="plain"
            :type="getLlmEquivalenceReasonTagType(item.bestMatch.llmEquivalence.reasonType)"
          >
            {{ getLlmEquivalenceReasonTypeText(item.bestMatch.llmEquivalence.reasonType) }}
          </el-tag>
          <el-tag
            size="small"
            effect="plain"
            :type="getLlmEquivalenceDifferenceToneTagType(sourceDiffTone)"
          >
            {{ getLlmEquivalenceDifferenceToneText(sourceDiffTone) }}
          </el-tag>
        </div>
      </div>
      <div class="source-diff-callout__text">
        {{ getLlmEquivalenceSummaryText(item.bestMatch.llmEquivalence) }}
      </div>
      <div class="source-diff-callout__hint">
        {{ getLlmEquivalenceDifferenceToneDescription(sourceDiffTone) }}
      </div>
    </div>
    <div v-if="item.bestMatch && sourceBestRows.length > 0" class="diff-section">
      <div class="diff-columns">
        <div class="diff-column">
          <div class="diff-column-title">差异字段</div>
        </div>
        <div class="diff-column">
          <div class="diff-column-title">源项</div>
        </div>
        <div class="diff-column">
          <div class="diff-column-title">
            最佳匹配 · 规格 {{ item.bestMatch.specId }}
          </div>
        </div>
      </div>

      <div class="diff-rows">
        <div
          v-for="row in sourceBestRows"
          :key="`source-best-${row.key}`"
          class="diff-row"
        >
          <div class="diff-label">
            <div class="diff-label__content">
              <span>{{ row.label }}</span>
              <el-tag
                v-if="item.bestMatch?.llmEquivalence"
                size="small"
                effect="plain"
                :type="getLlmEquivalenceDifferenceToneTagType(sourceDiffTone)"
              >
                {{ getLlmEquivalenceDifferenceToneText(sourceDiffTone) }}
              </el-tag>
            </div>
          </div>
          <div class="diff-cell">
            <div class="diff-content" v-html="row.leftHtml" />
          </div>
          <div class="diff-cell">
            <div class="diff-content" v-html="row.rightHtml" />
          </div>
        </div>
      </div>
    </div>
    <el-empty
      v-else
      description="当前源项与最佳匹配无可展示差异"
      :image-size="60"
    />
  </div>

  <div v-if="shouldShowCandidateCompare" class="candidate-section">
    <div class="candidate-header">
      <h4>候选对比</h4>
      <span>Top1 对 Top2/Top3</span>
    </div>

    <div v-if="topCandidates.length === 0" class="diff-section diff-section--empty">
      <el-empty description="当前没有候选可对比" :image-size="60" />
    </div>
    <div v-else-if="comparisonCandidate" class="diff-section">
      <div class="diff-header">
        <div>
          <h5>Top1 差异高亮</h5>
          <p>切换右侧候选与视图</p>
        </div>
        <div class="diff-toolbar">
          <el-radio-group
            v-if="comparisonOptions.length > 1"
            v-model="comparisonRankModel"
            size="small"
          >
            <el-radio-button
              v-for="option in comparisonOptions"
              :key="option.value"
              :label="option.value"
            >
              {{ option.label }}
            </el-radio-button>
          </el-radio-group>
          <el-tag v-else type="info" effect="plain">
            对比 Top{{ comparisonCandidate.rank }}
          </el-tag>
          <el-radio-group v-model="diffViewModeModel" size="small">
            <el-radio-button label="raw">原文对照</el-radio-button>
            <el-radio-button label="field">字段差异</el-radio-button>
          </el-radio-group>
        </div>
      </div>

      <div
        v-if="diffViewMode === 'raw'"
        class="raw-diff-shell"
      >
        <div class="raw-diff-meta">
          <div class="raw-diff-desc">
            红=Top1独有，绿=候选新增
          </div>
          <el-switch
            v-model="rawOnlyDiffModel"
            inline-prompt
            active-text="仅差异"
            inactive-text="全部字段"
          />
        </div>

        <div class="raw-diff-header">
          <div class="raw-diff-header-spacer" />
          <div class="raw-diff-header-title">
            Top1 · 规格 {{ topCandidates[0]?.specId }}
          </div>
          <div class="raw-diff-header-title">
            Top{{ comparisonCandidate.rank }} · 规格 {{ comparisonCandidate.specId }}
          </div>
        </div>

        <div
          v-if="rawComparisonRows.length > 0"
          class="raw-diff-rows"
        >
          <div
            v-for="(row, index) in rawComparisonRows"
            :key="`raw-${row.key}`"
            class="raw-diff-row"
            :class="{ 'diff-row-same': row.isSame }"
          >
            <div class="raw-line-cell">
              <div class="raw-line-no">{{ index + 1 }}</div>
              <div class="raw-line-label">{{ row.label }}</div>
            </div>
            <div class="raw-pane-cell">
              <div class="raw-pane-inner">
                <div class="raw-pane-label">{{ row.label }}</div>
                <div class="raw-pane-content" v-html="row.leftHtml" />
              </div>
            </div>
            <div class="raw-pane-cell">
              <div class="raw-pane-inner">
                <div class="raw-pane-label">{{ row.label }}</div>
                <div class="raw-pane-content" v-html="row.rightHtml" />
              </div>
            </div>
          </div>
        </div>
        <el-empty
          v-else
          description="当前 Top1 与该候选无字段差异"
          :image-size="60"
        />
      </div>

      <div v-else class="diff-columns">
        <div class="diff-column">
          <div class="diff-column-title">
            Top1 · 规格 {{ topCandidates[0]?.specId }}
          </div>
        </div>
        <div class="diff-column">
          <div class="diff-column-title">
            Top{{ comparisonCandidate.rank }} · 规格 {{ comparisonCandidate.specId }}
          </div>
        </div>
      </div>

      <div v-if="diffViewMode === 'field'" class="diff-rows">
        <div
          v-for="row in comparisonRows"
          :key="row.key"
          class="diff-row"
          :class="{ 'diff-row-same': row.isSame }"
        >
          <div class="diff-label">{{ row.label }}</div>
          <div class="diff-cell">
            <div class="diff-content" v-html="row.leftHtml" />
          </div>
          <div class="diff-cell">
            <div class="diff-content" v-html="row.rightHtml" />
          </div>
        </div>
      </div>
    </div>
    <div v-else class="diff-section diff-section--empty">
      <el-empty description="请选择候选后查看差异" :image-size="60" />
    </div>
  </div>
</template>

<style scoped>
.best-section,
.candidate-section {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.best-section h4,
.candidate-header h4 {
  margin: 0;
  font-size: 14px;
  font-weight: 600;
  color: #111827;
}

.candidate-header {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 12px;
}

.candidate-header span {
  font-size: 12px;
  color: #6b7280;
}

.diff-section {
  padding: 14px;
  border: 1px solid #e5e7eb;
  border-radius: 14px;
  background: linear-gradient(180deg, #fcfdff 0%, #f7f9fc 100%);
}

.diff-section--empty {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 180px;
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

.diff-toolbar,
.raw-diff-meta {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 10px;
}

.diff-columns {
  display: grid;
  grid-template-columns: 120px minmax(0, 1fr) minmax(0, 1fr);
  gap: 10px;
  margin-bottom: 8px;
}

.diff-column:first-child {
  visibility: hidden;
}

.diff-column-title,
.raw-diff-header-title {
  padding: 8px 10px;
  border-radius: 10px;
  background: #eef4ff;
  color: #1f2937;
  font-size: 12px;
  font-weight: 600;
}

.diff-rows,
.raw-diff-rows,
.raw-diff-shell {
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
  padding-top: 10px;
  font-size: 12px;
  color: #6b7280;
}

.diff-label__content {
  display: flex;
  flex-direction: column;
  gap: 6px;
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

.raw-diff-desc {
  font-size: 12px;
  color: #6b7280;
}

.raw-diff-header,
.raw-diff-row {
  display: grid;
  grid-template-columns: 90px minmax(0, 1fr) minmax(0, 1fr);
  gap: 10px;
}

.raw-diff-header-spacer {
  min-height: 1px;
}

.raw-line-cell {
  display: flex;
  flex-direction: column;
  gap: 4px;
  align-items: flex-start;
  padding: 10px 8px;
  border-radius: 12px;
  background: #f3f4f6;
}

.raw-line-no {
  font-size: 12px;
  font-weight: 700;
  color: #374151;
}

.raw-line-label,
.raw-pane-label {
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

.source-diff-callout {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 12px 14px;
  border-radius: 14px;
  border: 1px solid #dbeafe;
  background: #f8fbff;
}

.source-diff-callout--decision {
  border-color: #fed7aa;
  background: #fff7ed;
}

.source-diff-callout__head,
.source-diff-callout__tags {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  flex-wrap: wrap;
}

.source-diff-callout__head {
  font-size: 13px;
  font-weight: 600;
  color: #0f172a;
}

.source-diff-callout__text {
  font-size: 13px;
  line-height: 1.6;
  color: #1e3a8a;
}

.source-diff-callout--decision .source-diff-callout__text {
  color: #9a3412;
}

.source-diff-callout__hint {
  font-size: 12px;
  line-height: 1.6;
  color: #475569;
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
  .diff-header,
  .raw-diff-meta {
    flex-direction: column;
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
