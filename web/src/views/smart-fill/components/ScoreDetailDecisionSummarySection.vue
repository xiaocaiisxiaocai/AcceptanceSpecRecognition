<script setup lang="ts">
import { computed } from "vue";
import type { MatchPreviewItem } from "@/api/matching";
import {
  getIssueFieldText,
  getSmartFillDecisionSummaryState
} from "./scoreDetail.formatters";
import type { ScoreDetailDiffRow } from "../composables/useScoreDetailDiff";
import {
  getLlmEquivalenceDifferenceTone,
  getLlmEquivalenceDifferenceToneDescription,
  getLlmEquivalenceDifferenceToneTagType,
  getLlmEquivalenceDifferenceToneText,
  getLlmEquivalenceReasonTagType,
  getLlmEquivalenceReasonTypeText,
  getLlmEquivalenceSummaryText,
  getLlmEquivalenceVerdictTagType,
  getLlmEquivalenceVerdictText,
  isLlmEquivalenceDecisionRisk,
  isLlmEquivalenceHintOnly
} from "./scoreDetail.llmEquivalence";

const props = defineProps<{
  item: MatchPreviewItem;
  sourceBestRows: ScoreDetailDiffRow[];
}>();

const bestMatch = computed(() => props.item.bestMatch);
const llmEquivalence = computed(() => bestMatch.value?.llmEquivalence);
const llmEquivalenceTone = computed(() =>
  getLlmEquivalenceDifferenceTone(llmEquivalence.value)
);
const hasHintOnlyEquivalence = computed(() =>
  isLlmEquivalenceHintOnly(llmEquivalence.value)
);
const hasDecisionEquivalenceRisk = computed(() =>
  isLlmEquivalenceDecisionRisk(llmEquivalence.value)
);
const riskRelevantSourceRows = computed(() =>
  props.sourceBestRows.filter(row => row.isRiskRelevant)
);
const decisionSummaryState = computed(() =>
  getSmartFillDecisionSummaryState(props.item, {
    sourceBestRowCount: riskRelevantSourceRows.value.length
  })
);

const riskItems = computed(() => {
  const llmRiskItems =
    hasDecisionEquivalenceRisk.value && llmEquivalence.value
      ? [{ text: getLlmEquivalenceSummaryText(llmEquivalence.value) }]
      : [];

  const conflicts =
    bestMatch.value?.conflictSummary?.map(item => ({
      text: item
    })) ?? [];

  const issues =
    bestMatch.value?.issues?.map(issue => ({
      text: `${issue.message}${issue.fieldName ? `（${getIssueFieldText(issue)}）` : ""}`
    })) ?? [];

  return [...llmRiskItems, ...conflicts, ...issues].slice(0, 4);
});

const recommendation = computed(
  () => decisionSummaryState.value.recommendation
);
const actionSuggestion = computed(
  () => decisionSummaryState.value.actionSuggestion
);

const focusChecklist = computed(() => {
  const checklist = [
    ...(props.sourceBestRows.length > 0
      ? ["存在格式、符号或原文差异，详情中已保留高亮供复核"]
      : []),
    ...(llmEquivalence.value
      ? [`AI 裁决提示：${getLlmEquivalenceSummaryText(llmEquivalence.value)}`]
      : []),
    ...riskRelevantSourceRows.value.map(row => `${row.label}与推荐项不一致`),
    ...riskItems.value.slice(0, 2).map(item => item.text)
  ];

  if (checklist.length > 0) {
    return checklist.slice(0, 3);
  }

  return ["确认验收标准是否符合现场要求"];
});

const sourceBestRowMap = computed(() => {
  return new Map(props.sourceBestRows.map(row => [row.key, row]));
});

const escapeHtml = (text?: string) =>
  (text ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;")
    .replaceAll("\n", "<br />");

const getComparisonHtml = (
  key: string,
  side: "left" | "right",
  fallback?: string
) => {
  const row = sourceBestRowMap.value.get(key);
  if (row) {
    return side === "left" ? row.leftHtml : row.rightHtml;
  }

  return fallback ? escapeHtml(fallback) : "-";
};
</script>

<template>
  <div class="decision-layout">
    <div class="hero-grid">
      <div class="hero-card hero-card--primary">
        <div class="hero-card__label">一句话结论</div>
        <div class="hero-card__title">{{ recommendation.title }}</div>
        <div class="hero-card__desc">{{ recommendation.description }}</div>
      </div>
      <div class="hero-card">
        <div class="hero-card__label">建议动作</div>
        <div class="hero-card__title hero-card__title--normal">
          {{ actionSuggestion }}
        </div>
      </div>
      <div class="hero-card">
        <div class="hero-card__label">风险级别</div>
        <div class="hero-card__title">
          <el-tag :type="decisionSummaryState.riskLevel.type" size="small">
            {{ decisionSummaryState.riskLevel.label }}
          </el-tag>
        </div>
        <div class="hero-card__desc">
          {{ decisionSummaryState.riskLevel.description }}
        </div>
      </div>
    </div>

    <div class="panel">
      <div class="panel__title">AI 等价裁决</div>
      <div v-if="bestMatch?.llmEquivalence" class="equivalence-card">
        <div class="equivalence-card__tags">
          <el-tag
            size="small"
            :type="
              getLlmEquivalenceVerdictTagType(bestMatch.llmEquivalence.verdict)
            "
          >
            {{ getLlmEquivalenceVerdictText(bestMatch.llmEquivalence.verdict) }}
          </el-tag>
          <el-tag
            size="small"
            effect="plain"
            :type="
              getLlmEquivalenceReasonTagType(
                bestMatch.llmEquivalence.reasonType
              )
            "
          >
            {{
              getLlmEquivalenceReasonTypeText(
                bestMatch.llmEquivalence.reasonType
              )
            }}
          </el-tag>
          <el-tag
            size="small"
            effect="plain"
            :type="getLlmEquivalenceDifferenceToneTagType(llmEquivalenceTone)"
          >
            {{ getLlmEquivalenceDifferenceToneText(llmEquivalenceTone) }}
          </el-tag>
        </div>
        <div class="equivalence-card__text">
          {{ getLlmEquivalenceSummaryText(bestMatch.llmEquivalence) }}
        </div>
        <div class="equivalence-card__hint">
          {{ getLlmEquivalenceDifferenceToneDescription(llmEquivalenceTone) }}
        </div>
        <div class="equivalence-card__hint">
          AI 裁决结果已纳入当前推荐，请以页面结论为准。
        </div>
      </div>
      <div v-else class="plain-item">当前最佳匹配未触发 AI 等价裁决。</div>
    </div>

    <div class="panel">
      <div class="panel__title">请重点确认</div>
      <div class="plain-item plain-item--hint">
        请结合详情表格核对源项与推荐项差异
      </div>
      <div class="plain-list">
        <div
          v-for="(checkItem, index) in focusChecklist"
          :key="`check-${index}`"
          class="plain-item plain-item--focus"
        >
          {{ checkItem }}
        </div>
      </div>
    </div>

    <div class="panel">
      <div class="panel__title">源项与推荐项</div>
      <el-descriptions :column="2" border size="small">
        <el-descriptions-item label="源项目">
          <div
            class="comparison-rich-text"
            v-html="getComparisonHtml('project', 'left', item.sourceProject)"
          />
        </el-descriptions-item>
        <el-descriptions-item label="推荐项目">
          <div
            class="comparison-rich-text"
            v-html="getComparisonHtml('project', 'right', bestMatch?.project)"
          />
        </el-descriptions-item>
        <el-descriptions-item label="源规格">
          <div
            class="comparison-rich-text"
            v-html="
              getComparisonHtml(
                'specification',
                'left',
                item.sourceSpecification
              )
            "
          />
        </el-descriptions-item>
        <el-descriptions-item label="推荐规格">
          <div
            class="comparison-rich-text"
            v-html="
              getComparisonHtml(
                'specification',
                'right',
                bestMatch?.specification
              )
            "
          />
        </el-descriptions-item>
        <el-descriptions-item label="推荐验收标准">
          {{ bestMatch?.acceptance || "-" }}
        </el-descriptions-item>
        <el-descriptions-item label="推荐备注">
          {{ bestMatch?.remark || "-" }}
        </el-descriptions-item>
      </el-descriptions>
    </div>
  </div>
</template>

<style scoped>
.decision-layout {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.hero-grid {
  display: grid;
  grid-template-columns: 1.3fr 1fr 0.9fr;
  gap: 12px;
}

.hero-card {
  padding: 16px;
  background: linear-gradient(180deg, #fff 0%, var(--app-info-bg) 100%);
  border: 1px solid var(--app-border);
  border-radius: 18px;
}

.hero-card--primary {
  background: linear-gradient(
    135deg,
    var(--app-info-bg) 0%,
    var(--app-info-bg) 100%
  );
  border-color: var(--app-info-bg);
}

.hero-card__label {
  font-size: 12px;
  color: var(--app-text-secondary);
}

.hero-card__title {
  margin-top: 8px;
  font-size: 18px;
  font-weight: 700;
  line-height: 1.4;
  color: var(--app-text-primary);
}

.hero-card__title--normal {
  font-size: 15px;
  font-weight: 600;
}

.hero-card__desc {
  margin-top: 8px;
  font-size: 12px;
  line-height: 1.5;
  color: var(--app-text-secondary);
}

.panel {
  padding: 14px 16px;
  background: #fff;
  border: 1px solid var(--app-border);
  border-radius: 16px;
}

.panel__title {
  margin-bottom: 12px;
  font-size: 14px;
  font-weight: 600;
  color: var(--app-text-primary);
}

.plain-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.plain-item {
  padding: 8px 12px;
  font-size: 13px;
  line-height: 1.5;
  color: var(--app-text-secondary);
  background: var(--app-info-bg);
  border-radius: 12px;
}

.plain-item--focus {
  color: var(--app-danger);
  background: var(--app-warning-bg);
}

.plain-item--hint {
  padding: 8px 12px;
  margin-bottom: 8px;
  font-size: 13px;
  line-height: 1.5;
  color: var(--app-primary);
  background: var(--app-primary-light);
  border-radius: 12px;
}

.comparison-rich-text {
  font-size: 13px;
  line-height: 1.8;
  color: var(--app-text-primary);
  word-break: break-word;
}

.equivalence-card {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding: 12px;
  background: linear-gradient(
    180deg,
    var(--app-info-bg) 0%,
    var(--app-info-bg) 100%
  );
  border: 1px solid var(--app-info-bg);
  border-radius: 14px;
}

.equivalence-card__tags {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.equivalence-card__text {
  font-size: 13px;
  line-height: 1.6;
  color: var(--app-primary);
}

.equivalence-card__hint {
  font-size: 12px;
  line-height: 1.6;
  color: var(--app-text-secondary);
}

:deep(.inline-mark) {
  padding: 0 2px;
  border-radius: 4px;
}

:deep(.inline-mark-old) {
  color: var(--app-danger);
  background: rgb(245 108 108 / 18%);
}

:deep(.inline-mark-new) {
  color: var(--app-success);
  background: rgb(103 194 58 / 18%);
}

:deep(.placeholder-text) {
  font-style: italic;
  color: var(--app-text-disabled);
}

@media (width <= 900px) {
  .hero-grid {
    grid-template-columns: 1fr;
  }

  .risk-item {
    flex-direction: column;
  }
}
</style>
