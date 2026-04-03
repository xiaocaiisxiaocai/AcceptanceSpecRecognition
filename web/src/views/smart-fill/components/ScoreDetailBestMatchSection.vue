<script setup lang="ts">
import { computed } from "vue";
import {
  DEFAULT_AMBIGUITY_MARGIN,
  DEFAULT_HIGH_CONFIDENCE_THRESHOLD,
  type MatchPreviewItem
} from "@/api/matching";
import {
  formatLlmScore,
  formatScore,
  getDecisionTagType,
  getDecisionText,
  getEntityRelationTagType,
  getEntityRelationText,
  getConfidenceText,
  getIssueFieldText,
  getIssueSeverityText,
  getIssueTagType
} from "./scoreDetail.formatters";

const props = defineProps<{
  item: MatchPreviewItem;
  ambiguityMargin?: number;
  highConfidenceThreshold?: number;
}>();

const bestMatch = computed(() => props.item.bestMatch);
const bestMatchIssues = computed(() => bestMatch.value?.issues ?? []);
const bestMatchEntities = computed(() => bestMatch.value?.entities ?? []);
const effectiveAmbiguityMargin = computed(() => props.ambiguityMargin ?? DEFAULT_AMBIGUITY_MARGIN);
const effectiveHighConfidenceThreshold = computed(
  () => props.highConfidenceThreshold ?? DEFAULT_HIGH_CONFIDENCE_THRESHOLD
);
const formatOptionalPercent = (value?: number) => {
  if (value === undefined || value === null) return "-";
  return `${(value * 100).toFixed(1)}%`;
};
const normalizeComparableText = (value?: string) =>
  (value ?? "")
    .trim()
    .toLowerCase()
    .replace(/\s+/g, " ")
    .replaceAll("（", "(")
    .replaceAll("）", ")");

const metricCards = computed(() => {
  if (!bestMatch.value) return [];

  return [
    {
      label: "最终得分",
      value: formatScore(bestMatch.value.score)
    },
    {
      label: "Embedding",
      value: formatScore(bestMatch.value.embeddingScore)
    },
    {
      label: "模型复核分",
      value: formatLlmScore(bestMatch.value.llmScore)
    },
    {
      label: "Top1/Top2分差",
      value: formatOptionalPercent(bestMatch.value.scoreGap)
    },
    {
      label: "歧义阈值",
      value: formatOptionalPercent(effectiveAmbiguityMargin.value)
    },
    {
      label: "召回候选",
      value: bestMatch.value.recalledCandidateCount?.toString() || "-"
    }
  ];
});

const explanationRows = computed(() => {
  if (!bestMatch.value) return [];

  const rows = [];
  const thresholdReached =
    bestMatch.value.score >= effectiveHighConfidenceThreshold.value;

  rows.push({
    label: "置信阈值",
    value: thresholdReached
      ? `当前高置信阈值为 ${formatScore(effectiveHighConfidenceThreshold.value)}，当前得分 ${formatScore(bestMatch.value.score)}，已达到高置信门槛。`
      : `当前高置信阈值为 ${formatScore(effectiveHighConfidenceThreshold.value)}，当前得分 ${formatScore(bestMatch.value.score)}，因此当前显示为${getConfidenceText(props.item.confidenceLevel)}置信度。`
  });

  const keywordScore = bestMatch.value.scoreDetails?.KeywordOverlap;
  if (keywordScore === 0.5) {
    const sameComparableSpecification =
      normalizeComparableText(props.item.sourceSpecification) ===
      normalizeComparableText(bestMatch.value.specification);

    rows.push({
      label: "关键词项",
      value: sameComparableSpecification
        ? "规格文本可比后是一致的，但未提取到有效关键词 token，关键词项按保守 50% 计分。"
        : "双方都未提取到有效关键词 token，关键词项按保守 50% 计分。短文本、符号或数字占比较高时容易出现这种情况。"
    });
  }

  return rows;
});

const metaTags = computed(() => {
  if (!bestMatch.value) return [];

  return [
    {
      text: getDecisionText(bestMatch.value.decision),
      type: getDecisionTagType(bestMatch.value.decision)
    },
    {
      text: bestMatch.value.hasHardConflict ? "硬冲突" : "无硬冲突",
      type: bestMatch.value.hasHardConflict ? "danger" : "success"
    },
    {
      text: bestMatch.value.isAmbiguous ? "高歧义" : "歧义低",
      type: bestMatch.value.isAmbiguous ? "warning" : "success"
    },
    {
      text: `问题 ${bestMatchIssues.value.length}`,
      type: bestMatchIssues.value.length > 0 ? "warning" : "info"
    },
    {
      text: `实体 ${bestMatchEntities.value.length}`,
      type: bestMatchEntities.value.length > 0 ? "primary" : "info"
    }
  ];
});

const summaryRows = computed(() => {
  if (!bestMatch.value) return [];

  return [
    {
      label: "证据",
      value: bestMatch.value.evidenceSummary?.join("；") || "",
      tone: "normal"
    },
    {
      label: "冲突",
      value: bestMatch.value.conflictSummary?.join("；") || "",
      tone: "danger"
    },
    {
      label: "重排",
      value: bestMatch.value.rerankSummary || "",
      tone: "normal"
    },
    {
      label: "复核结论",
      value: bestMatch.value.llmReason || props.item.llmReviewDraft || "",
      tone: "normal"
    },
    {
      label: "复核说明",
      value: bestMatch.value.llmCommentary || "",
      tone: "muted"
    },
    {
      label: "复核异常",
      value: props.item.llmReviewError || "",
      tone: "danger"
    }
  ].filter(row => !!row.value);
});
</script>

<template>
  <div class="best-section">
    <h4>技术概览</h4>
    <template v-if="bestMatch">
      <div class="overview-card">
        <div class="overview-head">
          <div class="overview-main">
            <div class="overview-caption">最佳匹配 · 规格 {{ bestMatch.specId }}</div>
            <div class="overview-title">{{ bestMatch.project }}</div>
            <div class="overview-spec">{{ bestMatch.specification }}</div>
          </div>
          <div class="overview-score">
            <strong>{{ formatScore(bestMatch.score) }}</strong>
            <span>当前最佳</span>
          </div>
        </div>

        <div class="meta-tag-list">
          <el-tag
            v-for="tag in metaTags"
            :key="tag.text"
            size="small"
            effect="plain"
            :type="tag.type as any"
          >
            {{ tag.text }}
          </el-tag>
        </div>

        <div class="metric-grid">
          <div
            v-for="metric in metricCards"
            :key="metric.label"
            class="metric-card"
          >
            <span>{{ metric.label }}</span>
            <strong>{{ metric.value }}</strong>
          </div>
        </div>

        <div class="reference-grid">
          <div class="reference-row">
            <span>验收标准</span>
            <strong>{{ bestMatch.acceptance || "-" }}</strong>
          </div>
          <div class="reference-row">
            <span>备注</span>
            <strong>{{ bestMatch.remark || "-" }}</strong>
          </div>
        </div>
      </div>

      <div
        v-if="explanationRows.length > 0"
        class="info-block"
      >
        <div class="info-label">判定解释</div>
        <div class="summary-list">
          <div
            v-for="row in explanationRows"
            :key="row.label"
            class="summary-row"
          >
            <div class="summary-row__label">{{ row.label }}</div>
            <div class="summary-row__value">{{ row.value }}</div>
          </div>
        </div>
      </div>

      <div
        v-if="bestMatchIssues.length > 0"
        class="info-block info-block--issue"
      >
        <div class="info-label">结构化问题</div>
        <div class="compact-list">
          <div
            v-for="(issue, index) in bestMatchIssues"
            :key="`best-issue-${index}-${issue.code}`"
            class="compact-row"
          >
            <div class="compact-row__head">
              <span class="compact-row__title">{{ issue.message }}</span>
              <el-tag
                size="small"
                effect="plain"
                :type="getIssueTagType(issue.severity)"
              >
                {{ getIssueSeverityText(issue.severity) }}
              </el-tag>
            </div>
            <div class="compact-row__meta">字段：{{ getIssueFieldText(issue) }}</div>
            <div v-if="issue.suggestedAction" class="compact-row__meta">
              建议：{{ issue.suggestedAction }}
            </div>
          </div>
        </div>
      </div>

      <div
        v-if="bestMatchEntities.length > 0"
        class="info-block"
      >
        <div class="info-label">实体证据</div>
        <div class="compact-list">
          <div
            v-for="(entity, index) in bestMatchEntities"
            :key="`best-entity-${index}-${entity.sourceValue}-${entity.candidateValue}`"
            class="compact-row compact-row--entity"
          >
            <div class="compact-row__head">
              <span class="compact-row__title">
                {{ entity.entityType || "实体" }}：{{ entity.sourceValue }} -> {{ entity.candidateValue }}
              </span>
              <el-tag
                size="small"
                effect="plain"
                :type="getEntityRelationTagType(entity.relation)"
              >
                {{ getEntityRelationText(entity.relation) }}
              </el-tag>
            </div>
            <div class="compact-row__meta">
              归一化：{{ entity.normalizedSourceValue || "-" }} / {{ entity.normalizedCandidateValue || "-" }}
            </div>
          </div>
        </div>
      </div>

      <div v-if="summaryRows.length > 0" class="info-block summary-panel">
        <div class="info-label">分析摘要</div>
        <div class="summary-list">
          <div
            v-for="row in summaryRows"
            :key="row.label"
            class="summary-row"
            :class="`summary-row--${row.tone}`"
          >
            <div class="summary-row__label">{{ row.label }}</div>
            <div class="summary-row__value">{{ row.value }}</div>
          </div>
        </div>
      </div>
    </template>
    <el-empty v-else description="无匹配结果" :image-size="60" />
  </div>
</template>

<style scoped>
.best-section {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.best-section h4 {
  margin: 0;
  font-size: 14px;
  font-weight: 600;
  color: #111827;
}

.overview-card {
  padding: 16px;
  border: 1px solid #e5e7eb;
  border-radius: 14px;
  background: linear-gradient(180deg, #fcfdff 0%, #f7f9fc 100%);
}

.overview-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
}

.overview-main {
  min-width: 0;
}

.overview-caption {
  font-size: 12px;
  color: #6b7280;
}

.overview-title {
  margin-top: 4px;
  font-size: 15px;
  font-weight: 700;
  color: #111827;
  line-height: 1.6;
}

.overview-spec {
  margin-top: 6px;
  font-size: 13px;
  color: #374151;
  line-height: 1.7;
  word-break: break-word;
}

.overview-score {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  min-width: 100px;
}

.overview-score strong {
  font-size: 24px;
  color: #111827;
}

.overview-score span {
  margin-top: 4px;
  font-size: 12px;
  color: #6b7280;
}

.meta-tag-list {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 14px;
}

.metric-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(120px, 1fr));
  gap: 10px;
  margin-top: 14px;
}

.metric-card {
  padding: 10px 12px;
  border-radius: 12px;
  background: #fff;
  border: 1px solid #e5e7eb;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.metric-card span {
  font-size: 12px;
  color: #6b7280;
}

.metric-card strong {
  font-size: 15px;
  color: #111827;
}

.reference-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(280px, 1fr));
  gap: 10px;
  margin-top: 14px;
}

.reference-row {
  padding: 10px 12px;
  border-radius: 12px;
  background: rgba(255, 255, 255, 0.86);
  border: 1px solid #eef2f7;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.reference-row span,
.info-label {
  font-size: 12px;
  color: #6b7280;
}

.reference-row strong {
  font-size: 13px;
  color: #374151;
  line-height: 1.6;
  word-break: break-word;
}

.info-block {
  padding: 12px 14px;
  border-radius: 12px;
  background: #f8fafc;
}

.info-block--issue {
  background: #fff9f5;
}

.compact-list,
.summary-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin-top: 8px;
}

.compact-row {
  padding: 10px 12px;
  border-radius: 10px;
  background: rgba(255, 255, 255, 0.92);
  border: 1px solid #eef2f7;
}

.compact-row--entity {
  border-color: #dbeafe;
}

.compact-row__head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
}

.compact-row__title {
  font-size: 13px;
  font-weight: 600;
  color: #111827;
  line-height: 1.6;
}

.compact-row__meta {
  margin-top: 6px;
  font-size: 12px;
  color: #6b7280;
  line-height: 1.6;
}

.summary-panel {
  background: #f8fafc;
}

.summary-row {
  display: grid;
  grid-template-columns: 84px minmax(0, 1fr);
  gap: 12px;
  padding: 8px 10px;
  border-radius: 10px;
  background: rgba(255, 255, 255, 0.9);
}

.summary-row--danger {
  background: #fff4f4;
}

.summary-row--muted {
  background: #f3f4f6;
}

.summary-row__label {
  font-size: 12px;
  font-weight: 600;
  color: #6b7280;
}

.summary-row__value {
  min-width: 0;
  font-size: 13px;
  line-height: 1.6;
  color: #374151;
  white-space: pre-wrap;
  word-break: break-word;
}

@media (max-width: 900px) {
  .overview-head,
  .compact-row__head {
    flex-direction: column;
  }

  .overview-score {
    align-items: flex-start;
  }
}
</style>
