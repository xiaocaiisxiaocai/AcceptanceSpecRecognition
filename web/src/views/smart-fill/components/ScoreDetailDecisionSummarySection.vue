<script setup lang="ts">
import { computed } from "vue";
import type { MatchPreviewItem } from "@/api/matching";
import { getIssueFieldText } from "./scoreDetail.formatters";
import type { ScoreDetailDiffRow } from "../composables/useScoreDetailDiff";

const props = defineProps<{
  item: MatchPreviewItem;
  sourceBestRows: ScoreDetailDiffRow[];
}>();

const bestMatch = computed(() => props.item.bestMatch);

const hasCustomerVisibleDifference = computed(() => {
  if (!bestMatch.value) return false;

  const hasMediumOrHighIssues = (bestMatch.value.issues ?? []).some(issue =>
    ["high", "medium", "warning"].includes(issue.severity || "")
  );

  return (
    props.sourceBestRows.length > 0 ||
    props.item.confidenceLevel !== "high" ||
    !!bestMatch.value?.isAmbiguous ||
    hasMediumOrHighIssues
  );
});

const recommendation = computed(() => {
  if (!bestMatch.value) {
    return {
      title: "暂不填充",
      description: "暂无匹配结果。",
      type: "info" as const
    };
  }

  if (bestMatch.value.hasHardConflict || bestMatch.value.decision === "reject") {
    return {
      title: "不建议填充",
      description: "存在冲突，请先处理。",
      type: "error" as const
    };
  }

  if (hasCustomerVisibleDifference.value) {
    return {
      title: "先确认后填充",
      description: "存在差异，请先确认。",
      type: "warning" as const
    };
  }

  if (bestMatch.value.decision === "autoApply") {
    return {
      title: "可直接填充",
      description: "无明显差异。",
      type: "success" as const
    };
  }

  return {
    title: "需要确认",
    description: "有推荐结果，请先确认。",
    type: "warning" as const
  };
});

const actionSuggestion = computed(() => {
  if (!bestMatch.value) {
    return "人工补充";
  }

  if (bestMatch.value.hasHardConflict || bestMatch.value.decision === "reject") {
    return "先处理冲突";
  }

  if (hasCustomerVisibleDifference.value) {
    return "核对差异后再填充";
  }

  if (bestMatch.value.decision === "autoApply") {
    return "可直接填充";
  }

  return "确认后再填充";
});

const riskLevel = computed(() => {
  if (!bestMatch.value) {
    return { label: "中", type: "warning" as const, description: "需人工判断" };
  }

  if (bestMatch.value.hasHardConflict || bestMatch.value.decision === "reject") {
    return { label: "高", type: "danger" as const, description: "存在冲突" };
  }

  if (hasCustomerVisibleDifference.value) {
    return { label: "中", type: "warning" as const, description: "有差异" };
  }

  return { label: "低", type: "success" as const, description: "可直接处理" };
});

const riskItems = computed(() => {
  const conflicts =
    bestMatch.value?.conflictSummary?.map(item => ({
      text: item
    })) ?? [];

  const issues =
    bestMatch.value?.issues?.map(issue => ({
      text: `${issue.message}${issue.fieldName ? `（${getIssueFieldText(issue)}）` : ""}`
    })) ?? [];

  return [...conflicts, ...issues].slice(0, 4);
});

const focusChecklist = computed(() => {
  const checklist = [
    ...props.sourceBestRows.map(row => `${row.label}与推荐项不一致`),
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
        <div class="hero-card__title hero-card__title--normal">{{ actionSuggestion }}</div>
      </div>
      <div class="hero-card">
        <div class="hero-card__label">风险级别</div>
        <div class="hero-card__title">
          <el-tag :type="riskLevel.type" size="small">{{ riskLevel.label }}</el-tag>
        </div>
        <div class="hero-card__desc">{{ riskLevel.description }}</div>
      </div>
    </div>

    <div class="panel">
      <div class="panel__title">请重点确认</div>
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
            v-html="getComparisonHtml('specification', 'left', item.sourceSpecification)"
          />
        </el-descriptions-item>
        <el-descriptions-item label="推荐规格">
          <div
            class="comparison-rich-text"
            v-html="getComparisonHtml('specification', 'right', bestMatch?.specification)"
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
  border: 1px solid #e5e7eb;
  border-radius: 18px;
  background: linear-gradient(180deg, #ffffff 0%, #f8fafc 100%);
}

.hero-card--primary {
  background: linear-gradient(135deg, #eff6ff 0%, #f8fafc 100%);
  border-color: #bfdbfe;
}

.hero-card__label {
  font-size: 12px;
  color: #6b7280;
}

.hero-card__title {
  margin-top: 8px;
  font-size: 18px;
  font-weight: 700;
  line-height: 1.4;
  color: #111827;
}

.hero-card__title--normal {
  font-size: 15px;
  font-weight: 600;
}

.hero-card__desc {
  margin-top: 8px;
  font-size: 12px;
  line-height: 1.5;
  color: #4b5563;
}

.panel {
  padding: 14px 16px;
  border: 1px solid #e5e7eb;
  border-radius: 16px;
  background: #fff;
}

.panel__title {
  margin-bottom: 12px;
  font-size: 14px;
  font-weight: 600;
  color: #111827;
}

.plain-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.plain-item {
  padding: 8px 12px;
  border-radius: 12px;
  background: #f8fafc;
  font-size: 13px;
  line-height: 1.5;
  color: #374151;
}

.plain-item--focus {
  background: #fff7ed;
  color: #9a3412;
}

.comparison-rich-text {
  font-size: 13px;
  line-height: 1.8;
  color: #111827;
  word-break: break-word;
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
  .hero-grid {
    grid-template-columns: 1fr;
  }

  .risk-item {
    flex-direction: column;
  }
}
</style>
