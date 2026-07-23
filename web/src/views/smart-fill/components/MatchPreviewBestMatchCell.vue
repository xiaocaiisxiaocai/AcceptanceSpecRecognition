<script setup lang="ts">
import { computed } from "vue";
import type { MatchPreviewItem } from "@/api/matching";
import {
  getLlmEquivalenceDifferenceTone,
  getLlmEquivalenceDifferenceToneTagType,
  getLlmEquivalenceDifferenceToneText,
  getLlmEquivalenceSummaryText,
  getLlmEquivalenceVerdictTagType,
  shouldHideInlineLlmEquivalenceSummary
} from "./scoreDetail.llmEquivalence";
import {
  formatIssueComparison,
  formatPreviewScore,
  getAmbiguityHint,
  getMatchBasisText,
  getPrimaryIssue
} from "./matchPreviewTable.formatters";

const props = defineProps<{
  item: MatchPreviewItem;
  ambiguityMargin: number;
}>();

const primaryIssue = computed(() => getPrimaryIssue(props.item));
const issueComparison = computed(() =>
  formatIssueComparison(primaryIssue.value)
);
const ambiguityHint = computed(() =>
  getAmbiguityHint(props.item, props.ambiguityMargin)
);
const llmEquivalence = computed(() => props.item.bestMatch?.llmEquivalence);
const isAiConfirmedEquivalent = computed(
  () =>
    props.item.bestMatch?.decision === "autoApply" &&
    props.item.bestMatch?.selectionMode !== "exactShortcut" &&
    llmEquivalence.value?.verdict === "equivalent"
);
const primaryScore = computed(() =>
  isAiConfirmedEquivalent.value
    ? (llmEquivalence.value?.confidence ?? props.item.bestMatch?.score ?? 0)
    : (props.item.bestMatch?.score ?? 0)
);
const primaryScoreLabel = computed(() =>
  isAiConfirmedEquivalent.value ? "AI确认等价" : "规则基础分"
);
const shouldShowLlmEquivalence = computed(
  () =>
    !!llmEquivalence.value &&
    !shouldHideInlineLlmEquivalenceSummary(
      llmEquivalence.value,
      props.item.bestMatch?.score
    )
);
const llmDifferenceTone = computed(() =>
  llmEquivalence.value
    ? getLlmEquivalenceDifferenceTone(llmEquivalence.value)
    : "neutral"
);
const basisLabel = computed(() => {
  const m = props.item.bestMatch;
  if (!m) return "";
  if (m.selectionMode === "exactShortcut") return "精确直达";
  if (m.decision === "autoApply") return "AI语义命中";
  return "需人工确认";
});
const basisTagType = computed(() =>
  basisLabel.value === "精确直达"
    ? "success"
    : basisLabel.value === "AI语义命中"
      ? "primary"
      : "warning"
);
</script>

<template>
  <div v-if="item.bestMatch" class="match-best">
    <div class="match-main">
      <div class="match-text">
        {{ item.bestMatch.project }} - {{ item.bestMatch.specification }}
      </div>
      <div class="match-meta">
        <el-tag size="small" :type="basisTagType" effect="dark">
          {{ basisLabel }}
        </el-tag>
        <el-tag size="small" type="info" effect="plain">
          Emb {{ formatPreviewScore(item.bestMatch.embeddingScore) }}
        </el-tag>
        <el-tag
          v-if="isAiConfirmedEquivalent"
          size="small"
          type="info"
          effect="plain"
        >
          规则基础 {{ formatPreviewScore(item.bestMatch.score) }}
        </el-tag>
        <el-tag size="small" type="info" effect="plain">
          召回 {{ item.bestMatch.recalledCandidateCount }}
        </el-tag>
        <el-tag
          v-if="item.bestMatch.isAmbiguous"
          size="small"
          type="warning"
          effect="plain"
        >
          高歧义
        </el-tag>
        <el-tag
          v-if="item.bestMatch.matchBasis"
          size="small"
          type="info"
          effect="plain"
        >
          匹配依据：{{ getMatchBasisText(item.bestMatch.matchBasis) }}
        </el-tag>
      </div>
    </div>
    <div class="match-score">
      <span class="match-score__label">{{ primaryScoreLabel }}</span>
      <span>{{ formatPreviewScore(primaryScore) }}</span>
    </div>
    <div v-if="item.bestMatch.isAmbiguous" class="ambiguity-reason">
      {{ ambiguityHint }}
    </div>
    <div v-if="primaryIssue" class="issue-summary">
      <span class="issue-summary__title">问题：</span>
      <span>{{ primaryIssue.message }}</span>
      <div v-if="issueComparison" class="issue-summary__meta">
        {{ issueComparison }}
      </div>
    </div>
    <div v-if="item.bestMatch.evidenceSummary?.length" class="evidence-summary">
      {{ item.bestMatch.evidenceSummary.slice(0, 2).join("；") }}
    </div>
    <div
      v-if="shouldShowLlmEquivalence && llmEquivalence"
      class="equivalence-summary"
    >
      <div class="equivalence-summary__tags">
        <el-tag
          size="small"
          effect="plain"
          :type="getLlmEquivalenceVerdictTagType(llmEquivalence.verdict)"
        >
          AI 等价裁决
        </el-tag>
        <el-tag
          size="small"
          effect="plain"
          :type="getLlmEquivalenceDifferenceToneTagType(llmDifferenceTone)"
        >
          {{ getLlmEquivalenceDifferenceToneText(llmDifferenceTone) }}
        </el-tag>
      </div>
      <div class="equivalence-summary__text">
        {{ getLlmEquivalenceSummaryText(llmEquivalence) }}
      </div>
    </div>
    <div v-if="item.bestMatch.conflictSummary?.length" class="conflict-summary">
      {{ item.bestMatch.conflictSummary.join("；") }}
    </div>
  </div>
  <div v-else class="no-match">
    <el-tag type="info" size="small">无匹配</el-tag>
  </div>
</template>
