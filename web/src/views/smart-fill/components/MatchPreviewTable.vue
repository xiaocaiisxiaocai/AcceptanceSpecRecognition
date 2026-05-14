<script setup lang="ts">
import { computed, ref, watch } from "vue";
import {
  DEFAULT_AMBIGUITY_MARGIN,
  type MatchIssue,
  type MatchPreviewItem
} from "@/api/matching";
import {
  getLlmEquivalenceDifferenceTone,
  getLlmEquivalenceDifferenceToneTagType,
  getLlmEquivalenceDifferenceToneText,
  getLlmEquivalenceSummaryText,
  shouldHideInlineLlmEquivalenceSummary,
  getLlmEquivalenceVerdictTagType
} from "./scoreDetail.llmEquivalence";
import {
  getSmartFillTableState,
  type SmartFillFillRecommendation,
  type SmartFillReviewStatus
} from "./scoreDetail.formatters";

const props = defineProps<{
  items: MatchPreviewItem[];
  loading?: boolean;
  highConfidenceThreshold?: number;
  ambiguityMargin?: number;
  /** LLM 流式处理是否进行中 */
  llmStreaming?: boolean;
  /** 父组件缓存的已选中行，用于切换 Tab 后恢复 */
  persistedSelections?: PersistedSelection[];
}>();

const emit = defineEmits<{
  (e: "select", rowIndex: number, spec: MatchPreviewItem["bestMatch"] | null): void;
  (e: "showDetail", item: MatchPreviewItem): void;
}>();

type Selection = {
  type: "best" | "manual";
  manualConfirmed: boolean;
  reviewApprovalToken?: string;
};

type EditOverride = {
  overrideAcceptance?: string;
  overrideRemark?: string;
};

type PersistedSelection = {
  rowIndex: number;
  selected?: boolean;
  specId?: number;
  manualConfirmed?: boolean;
  manualFill?: boolean;
  reviewApprovalToken?: string;
  overrideAcceptance?: string;
  overrideRemark?: string;
};

export type EditedBackfillItem = {
  rowIndex: number;
  specId?: number;
  sourceProject: string;
  sourceSpecification: string;
  originalAcceptance?: string;
  originalRemark?: string;
  overrideAcceptance?: string;
  overrideRemark?: string;
  actionType: "update" | "create";
};

const selectedSpecs = ref<Map<number, Selection | null>>(new Map());
const editedOverrides = ref<Map<number, EditOverride>>(new Map());
const editDialogVisible = ref(false);
const editingItem = ref<MatchPreviewItem | null>(null);
const currentPage = ref(1);
const pageSize = ref(100);
const pageSizeOptions = [50, 100, 200, 500];
const editForm = ref({
  overrideAcceptance: "",
  overrideRemark: ""
});

const isNoAnswerPlaceholderRow = (item: MatchPreviewItem) => {
  const project = (item.sourceProject || "").trim();
  const specification = (item.sourceSpecification || "").trim();
  if (specification) return false;

  const placeholderProjects = new Set(["其他", "-", "/", "无", "n/a", "na"]);
  return placeholderProjects.has(project.toLowerCase());
};

const getTableState = (item: MatchPreviewItem) =>
  getSmartFillTableState(item, { llmStreaming: props.llmStreaming });

const getReviewStatus = (item: MatchPreviewItem): SmartFillReviewStatus =>
  getTableState(item).reviewStatus;

const effectiveAmbiguityMargin = computed(() => props.ambiguityMargin ?? DEFAULT_AMBIGUITY_MARGIN);

const getDecision = (item: MatchPreviewItem) =>
  item.bestMatch?.decision ?? "manualReview";

const isAutoApply = (item: MatchPreviewItem) => getDecision(item) === "autoApply";

const isRejectDecision = (item: MatchPreviewItem) => getDecision(item) === "reject";

const isHighConfidence = (item: MatchPreviewItem) =>
  isAutoApply(item) &&
  item.confidenceLevel === "high";

const isReviewInFlight = (item: MatchPreviewItem) =>
  getReviewStatus(item) === "streaming";

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

const canEditRow = (item: MatchPreviewItem) => {
  if (isNoAnswerPlaceholderRow(item) || isReviewInFlight(item)) {
    return false;
  }

  return item.bestMatch ? canUseBestMatch(item) : true;
};

const initSelections = () => {
  selectedSpecs.value.clear();
  editedOverrides.value.clear();
  props.items.forEach(item => {
    if (item.bestMatch && isHighConfidence(item) && !isNoAnswerPlaceholderRow(item)) {
      selectedSpecs.value.set(item.rowIndex, {
        type: "best",
        manualConfirmed: false,
        reviewApprovalToken: item.bestMatch?.reviewApprovalToken
      });
    } else {
      selectedSpecs.value.set(item.rowIndex, null);
    }
  });
};

const hasOverrideValue = (value?: EditOverride | null) =>
  !!value &&
  (value.overrideAcceptance !== undefined || value.overrideRemark !== undefined);

const cloneOverride = (value?: EditOverride | null): EditOverride | undefined => {
  if (!hasOverrideValue(value)) {
    return undefined;
  }

  return {
    overrideAcceptance: value.overrideAcceptance,
    overrideRemark: value.overrideRemark
  };
};

const persistedStateMap = computed(() =>
  new Map((props.persistedSelections ?? []).map(item => [item.rowIndex, item]))
);

const getPersistedState = (rowIndex: number) =>
  persistedStateMap.value.get(rowIndex);

const getPersistedSelection = (rowIndex: number): Selection | null => {
  const persisted = getPersistedState(rowIndex);
  if (!persisted?.selected) {
    return null;
  }

  return {
    type: persisted.manualFill ? "manual" : "best",
    manualConfirmed: !!persisted.manualConfirmed,
    reviewApprovalToken: persisted.reviewApprovalToken
  };
};

const getPersistedOverride = (rowIndex: number) =>
  cloneOverride(getPersistedState(rowIndex));

const getExistingSelection = (rowIndex: number): Selection | null => {
  if (selectedSpecs.value.has(rowIndex)) {
    return selectedSpecs.value.get(rowIndex) ?? null;
  }

  return getPersistedSelection(rowIndex);
};

const getOverride = (rowIndex: number) => {
  if (editedOverrides.value.has(rowIndex)) {
    return cloneOverride(editedOverrides.value.get(rowIndex));
  }

  return getPersistedOverride(rowIndex);
};

const getRawAcceptanceText = (item: MatchPreviewItem) =>
  getOverride(item.rowIndex)?.overrideAcceptance ?? item.bestMatch?.acceptance ?? "";

const getRawRemarkText = (item: MatchPreviewItem) =>
  getOverride(item.rowIndex)?.overrideRemark ?? item.bestMatch?.remark ?? "";

const getDisplayAcceptanceText = (item: MatchPreviewItem) =>
  getRawAcceptanceText(item) || "-";

const getDisplayRemarkText = (item: MatchPreviewItem) =>
  getRawRemarkText(item) || "-";

const hasAcceptanceOverride = (item: MatchPreviewItem) =>
  getOverride(item.rowIndex)?.overrideAcceptance !== undefined;

const hasRemarkOverride = (item: MatchPreviewItem) =>
  getOverride(item.rowIndex)?.overrideRemark !== undefined;

const closeEditDialog = () => {
  editDialogVisible.value = false;
  editingItem.value = null;
  editForm.value = {
    overrideAcceptance: "",
    overrideRemark: ""
  };
};

const openEditDialog = (item: MatchPreviewItem) => {
  if (!canEditRow(item)) {
    return;
  }

  editingItem.value = item;
  editForm.value = {
    overrideAcceptance: getRawAcceptanceText(item),
    overrideRemark: getRawRemarkText(item)
  };
  editDialogVisible.value = true;
};

const handleSaveEditedSelection = () => {
  const item = editingItem.value;
  if (!item || !canEditRow(item)) {
    return;
  }

  const baseAcceptance = item.bestMatch?.acceptance ?? "";
  const baseRemark = item.bestMatch?.remark ?? "";
  const nextOverride: EditOverride = {
    overrideAcceptance:
      item.bestMatch && editForm.value.overrideAcceptance === baseAcceptance
        ? undefined
        : editForm.value.overrideAcceptance,
    overrideRemark:
      item.bestMatch && editForm.value.overrideRemark === baseRemark
        ? undefined
        : editForm.value.overrideRemark
  };

  if (!item.bestMatch && !hasOverrideValue(nextOverride)) {
    return;
  }

  if (hasOverrideValue(nextOverride)) {
    editedOverrides.value.set(item.rowIndex, nextOverride);
  } else {
    editedOverrides.value.delete(item.rowIndex);
  }

  selectedSpecs.value.set(item.rowIndex, {
    type: item.bestMatch ? "best" : "manual",
    manualConfirmed: item.bestMatch ? !item.bestMatch.reviewApprovalToken : true,
    reviewApprovalToken: item.bestMatch?.reviewApprovalToken
  });
  emit("select", item.rowIndex, item.bestMatch ?? null);
  closeEditDialog();
};

const selectionSyncKey = computed(() =>
  props.items
    .map(item =>
      [
        item.rowIndex,
        item.bestMatch?.decision,
        item.bestMatch?.reviewApprovalToken,
        item.llmReviewStage
      ].join(":")
    )
    .join("|")
);

const syncSelectionsWithItems = () => {
  const nextSelections = new Map<number, Selection | null>();
  const nextOverrides = new Map<number, EditOverride>();

  props.items.forEach(item => {
    const existing = getExistingSelection(item.rowIndex);
    const existingOverride = getOverride(item.rowIndex);

    if (existingOverride) {
      nextOverrides.set(item.rowIndex, existingOverride);
    }

    if (!item.bestMatch) {
      nextSelections.set(
        item.rowIndex,
        existing?.type === "manual" && hasOverrideValue(existingOverride)
          ? {
              type: "manual",
              manualConfirmed: true,
              reviewApprovalToken: undefined
            }
          : null
      );
      return;
    }

    if (isNoAnswerPlaceholderRow(item) || !canUseBestMatch(item)) {
      nextSelections.set(item.rowIndex, null);
      return;
    }

    if (item.bestMatch?.reviewApprovalToken) {
      nextSelections.set(item.rowIndex, {
        type: "best",
        manualConfirmed: false,
        reviewApprovalToken: item.bestMatch.reviewApprovalToken
      });
      return;
    }

    if (isHighConfidence(item)) {
      nextSelections.set(item.rowIndex, {
        type: "best",
        manualConfirmed: false,
        reviewApprovalToken: undefined
      });
      return;
    }

    if (existing?.manualConfirmed) {
      nextSelections.set(item.rowIndex, {
        type: "best",
        manualConfirmed: true,
        reviewApprovalToken: undefined
      });
      return;
    }

    nextSelections.set(item.rowIndex, null);
  });

  selectedSpecs.value = nextSelections;
  editedOverrides.value = nextOverrides;
};

watch(selectionSyncKey, () => syncSelectionsWithItems(), { immediate: true });

const getSelection = (rowIndex: number) => selectedSpecs.value.get(rowIndex);

const handleSelectBest = (item: MatchPreviewItem) => {
  if (!canUseBestMatch(item)) {
    return;
  }

  selectedSpecs.value.set(item.rowIndex, {
    type: "best",
    manualConfirmed: !item.bestMatch?.reviewApprovalToken,
    reviewApprovalToken: item.bestMatch?.reviewApprovalToken
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
const formatOptionalPercent = (value?: number) =>
  value === undefined || value === null ? "-" : `${(value * 100).toFixed(1)}%`;

const getAmbiguityHint = (item: MatchPreviewItem) => {
  if (!item.bestMatch?.isAmbiguous) return "";

  return `Top1/Top2分差 ${formatOptionalPercent(item.bestMatch.scoreGap)}，歧义阈值 ${formatOptionalPercent(effectiveAmbiguityMargin.value)}`;
};

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

const getReviewStatusText = (item: MatchPreviewItem) => {
  const status = getReviewStatus(item);
  switch (status) {
    case "direct":
      return "无需复核";
    case "completed":
      return isHighConfidence(item) ? "无需复核" : "AI判定可采用";
    case "manual":
      return item.llmReviewStage === "done" ? "复核后待确认" : "待确认";
    case "waiting":
      return "等待复核";
    case "pending":
      return "待复核";
    case "streaming":
      return "复核中...";
    case "blocked":
      return "暂不采用";
    case "error":
      return "已转人工确认";
    default:
      return "-";
  }
};

const getReviewTagType = (item: MatchPreviewItem) => {
  switch (getReviewStatus(item)) {
    case "direct":
      return "success";
    case "completed":
      return "success";
    case "manual":
    case "pending":
    case "streaming":
    case "error":
      return "warning";
    case "waiting":
      return "info";
    case "blocked":
      return "danger";
    default:
      return "info";
  }
};

const getFillRecommendation = (
  item: MatchPreviewItem
): SmartFillFillRecommendation => {
  const tableState = getTableState(item);
  return tableState.fillRecommendation;
};

const isExactFillable = (item: MatchPreviewItem) =>
  getFillRecommendation(item) === "fillable" &&
  item.bestMatch?.selectionMode === "exactShortcut";

const isPartialFillable = (item: MatchPreviewItem) =>
  getFillRecommendation(item) === "fillable" &&
  !!item.bestMatch &&
  item.bestMatch.selectionMode !== "exactShortcut";

const getFillRecommendationText = (item: MatchPreviewItem) => {
  switch (getFillRecommendation(item)) {
    case "fillable":
      return "可直接填充";
    case "blocked":
      return "不建议填充";
    case "unmatched":
      return "无匹配";
    case "review":
    default:
      return "需要确认";
  }
};

const getFillRecommendationTagType = (
  item: MatchPreviewItem
): "success" | "warning" | "danger" | "info" => {
  switch (getFillRecommendation(item)) {
    case "fillable":
      return "success";
    case "blocked":
      return "danger";
    case "unmatched":
      return "info";
    case "review":
    default:
      return "warning";
  }
};

const selectedCount = computed(() => {
  let count = 0;
  selectedSpecs.value.forEach(value => {
    if (value !== null) {
      count += 1;
    }
  });
  return count;
});

const stats = computed(() => {
  const total = props.items.length;
  const matched = props.items.filter(i => i.hasMatch).length;
  const exactFillable = props.items.filter(item => isExactFillable(item)).length;
  const partialFillable = props.items.filter(item => isPartialFillable(item)).length;
  const review = props.items.filter(
    item => getFillRecommendation(item) === "review"
  ).length;
  const blocked = props.items.filter(
    item => getFillRecommendation(item) === "blocked"
  ).length;
  const unmatched = props.items.filter(
    item => getFillRecommendation(item) === "unmatched"
  ).length;
  const ambiguous = props.items.filter(i => i.bestMatch?.isAmbiguous).length;
  return {
    total,
    matched,
    exactFillable,
    partialFillable,
    review,
    blocked,
    unmatched,
    selected: selectedCount.value,
    ambiguous
  };
});

type ScoreFilter =
  | "all"
  | "exactFillable"
  | "partialFillable"
  | "review"
  | "blocked"
  | "unmatched";
const scoreFilter = ref<ScoreFilter>("all");

const filteredItems = computed(() => {
  if (scoreFilter.value === "all") return props.items;

  return props.items.filter(item => {
    switch (scoreFilter.value) {
      case "exactFillable":
        return isExactFillable(item);
      case "partialFillable":
        return isPartialFillable(item);
      default:
        return getFillRecommendation(item) === scoreFilter.value;
    }
  });
});

const pagedFilteredItems = computed(() => {
  const start = (currentPage.value - 1) * pageSize.value;
  return filteredItems.value.slice(start, start + pageSize.value);
});

const hasReasonColumn = computed(() =>
  props.items.some(
    item =>
      !item.hasMatch ||
      !!item.noMatchReason ||
      !!item.bestMatch?.conflictSummary?.length ||
      !!item.llmReviewError
    )
);

watch(scoreFilter, () => {
  currentPage.value = 1;
});

watch(
  () => props.items,
  () => {
    currentPage.value = 1;
  }
);

watch(filteredItems, items => {
  const maxPage = Math.max(1, Math.ceil(items.length / pageSize.value));
  if (currentPage.value > maxPage) {
    currentPage.value = maxPage;
  }
}, { immediate: true });

const handlePageSizeChange = (size: number) => {
  pageSize.value = size;
  currentPage.value = 1;
};

defineExpose({
  getSelections: () => {
        const selections: Array<{
          rowIndex: number;
          selected?: boolean;
          specId?: number;
          manualConfirmed?: boolean;
          manualFill?: boolean;
          reviewApprovalToken?: string;
          overrideAcceptance?: string;
          overrideRemark?: string;
        }> = [];

    const rowIndexes = new Set<number>([
      ...selectedSpecs.value.keys(),
      ...editedOverrides.value.keys()
    ]);

    rowIndexes.forEach(rowIndex => {
      const selection = selectedSpecs.value.get(rowIndex) ?? null;
      const override = editedOverrides.value.get(rowIndex);
      if (!selection && !hasOverrideValue(override)) return;

      const item = props.items.find(i => i.rowIndex === rowIndex);
      if (!item) return;

      selections.push({
          rowIndex,
          selected: !!selection,
          specId: selection?.type === "best" ? item.bestMatch?.specId : undefined,
          manualConfirmed: selection?.manualConfirmed,
          manualFill: selection?.type === "manual",
          reviewApprovalToken: selection?.reviewApprovalToken,
          overrideAcceptance: override?.overrideAcceptance,
          overrideRemark: override?.overrideRemark
        });
    });

    return selections;
  },
  getEditedBackfillItems: (): EditedBackfillItem[] => {
    return [...editedOverrides.value.entries()]
      .map((entry): EditedBackfillItem | null => {
        const [rowIndex, override] = entry;
        if (!hasOverrideValue(override)) return null;
        const item = props.items.find(i => i.rowIndex === rowIndex);
        if (!item) return null;

        return {
          rowIndex,
          specId: item.bestMatch?.specId,
          sourceProject: item.sourceProject,
          sourceSpecification: item.sourceSpecification,
          originalAcceptance: item.bestMatch?.acceptance,
          originalRemark: item.bestMatch?.remark,
          overrideAcceptance: override.overrideAcceptance,
          overrideRemark: override.overrideRemark,
          actionType: item.bestMatch ? "update" : "create"
        } satisfies EditedBackfillItem;
      })
      .filter((item): item is EditedBackfillItem => !!item);
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
        <el-radio-button value="exactFillable">
          100%精确直达 ({{ stats.exactFillable }})
        </el-radio-button>
        <el-radio-button value="partialFillable">
          AI/普通可填充 ({{ stats.partialFillable }})
        </el-radio-button>
        <el-radio-button value="review">
          需要确认 ({{ stats.review }})
        </el-radio-button>
        <el-radio-button value="blocked">
          不建议填充 ({{ stats.blocked }})
        </el-radio-button>
        <el-radio-button value="unmatched">
          无匹配 ({{ stats.unmatched }})
        </el-radio-button>
      </el-radio-group>
    </div>

    <!-- 表格 -->
    <el-table
      :data="pagedFilteredItems"
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

      <!-- 填充建议 -->
      <el-table-column label="填充建议" width="120" align="center">
        <template #default="{ row }">
          <el-tag
            v-if="row.bestMatch || !row.hasMatch"
            size="small"
            :type="getFillRecommendationTagType(row)"
          >
            {{ getFillRecommendationText(row) }}
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
              </div>
            </div>
            <div class="match-score">{{ formatScore(row.bestMatch.score) }}</div>
            <div
              v-if="row.bestMatch.isAmbiguous"
              class="ambiguity-reason"
            >
              {{ getAmbiguityHint(row) }}
            </div>
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
              v-if="row.bestMatch.llmEquivalence && !shouldHideInlineLlmEquivalenceSummary(row.bestMatch.llmEquivalence, row.bestMatch.score)"
              class="equivalence-summary"
            >
              <div class="equivalence-summary__tags">
                <el-tag
                  size="small"
                  effect="plain"
                  :type="getLlmEquivalenceVerdictTagType(row.bestMatch.llmEquivalence.verdict)"
                >
                  AI 等价裁决
                </el-tag>
                <el-tag
                  size="small"
                  effect="plain"
                  :type="
                    getLlmEquivalenceDifferenceToneTagType(
                      getLlmEquivalenceDifferenceTone(row.bestMatch.llmEquivalence)
                    )
                  "
                >
                  {{
                    getLlmEquivalenceDifferenceToneText(
                      getLlmEquivalenceDifferenceTone(row.bestMatch.llmEquivalence)
                    )
                  }}
                </el-tag>
              </div>
              <div class="equivalence-summary__text">
                {{ getLlmEquivalenceSummaryText(row.bestMatch.llmEquivalence) }}
              </div>
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
          <div class="preview-cell">
            <span class="acceptance-text">
              {{ getDisplayAcceptanceText(row) }}
            </span>
            <el-tag
              v-if="hasAcceptanceOverride(row)"
              size="small"
              type="warning"
              effect="plain"
            >
              已编辑
            </el-tag>
            <el-tag
              v-if="getSelection(row.rowIndex)?.type === 'manual'"
              size="small"
              type="success"
              effect="plain"
            >
              已手工填写
            </el-tag>
          </div>
        </template>
      </el-table-column>

      <!-- 备注预览 -->
      <el-table-column label="备注" min-width="150">
        <template #default="{ row }">
          <div class="preview-cell">
            <span class="acceptance-text">
              {{ getDisplayRemarkText(row) }}
            </span>
            <el-tag
              v-if="hasRemarkOverride(row)"
              size="small"
              type="warning"
              effect="plain"
            >
              已编辑
            </el-tag>
            <el-tag
              v-if="getSelection(row.rowIndex)?.type === 'manual'"
              size="small"
              type="success"
              effect="plain"
            >
              已手工填写
            </el-tag>
          </div>
        </template>
      </el-table-column>

      <!-- 不匹配原因 / 复核说明 -->
      <el-table-column v-if="hasReasonColumn" label="异常/原因" min-width="220">
        <template #default="{ row }">
          <div
            v-if="!row.hasMatch || row.noMatchReason || row.bestMatch?.conflictSummary?.length || row.llmReviewError"
            class="reason-cell"
          >
            <div v-if="!row.hasMatch" class="reason-text">
              {{ row.noMatchReason || "未找到可匹配数据" }}
            </div>
            <div
              v-if="row.bestMatch?.conflictSummary?.length"
              class="reason-conflict"
            >
              冲突：{{ row.bestMatch.conflictSummary.join("；") }}
            </div>
            <div v-if="row.llmReviewError" class="reason-text">
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
              v-if="canEditRow(row)"
              link
              size="small"
              @click="openEditDialog(row)"
            >
              编辑
            </el-button>
            <el-button
              v-if="row.bestMatch && canUseBestMatch(row) && getSelection(row.rowIndex)?.type !== 'best'"
              size="small"
              @click="handleSelectBest(row)"
            >
              {{
                isHighConfidence(row)
                  ? "使用匹配"
                  : "确认采用"
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

    <div class="table-pagination">
      <el-pagination
        v-model:current-page="currentPage"
        v-model:page-size="pageSize"
        background
        small
        :page-sizes="pageSizeOptions"
        :total="filteredItems.length"
        layout="total, sizes, prev, pager, next"
        @size-change="handlePageSizeChange"
      />
    </div>

    <el-dialog
      v-model="editDialogVisible"
      title="编辑本次导出内容"
      width="640px"
      @closed="closeEditDialog"
    >
      <div v-if="editingItem" class="edit-dialog">
        <div class="edit-dialog__hint">
          修改将用于本次导出，执行填充前可选择是否回填到验收规格。
        </div>
        <el-form label-position="top">
          <el-form-item label="项目">
            <el-input :model-value="editingItem.sourceProject" readonly />
          </el-form-item>
          <el-form-item label="规格">
            <el-input
              :model-value="editingItem.sourceSpecification"
              readonly
              type="textarea"
              :rows="2"
            />
          </el-form-item>
          <el-form-item label="验收标准">
            <el-input
              v-model="editForm.overrideAcceptance"
              type="textarea"
              :rows="3"
              placeholder="请输入本次导出的验收标准"
            />
          </el-form-item>
          <el-form-item label="备注">
            <el-input
              v-model="editForm.overrideRemark"
              type="textarea"
              :rows="3"
              placeholder="请输入本次导出的备注"
            />
          </el-form-item>
        </el-form>
      </div>
      <template #footer>
        <div class="edit-dialog__footer">
          <el-button @click="closeEditDialog">取消</el-button>
          <el-button type="primary" @click="handleSaveEditedSelection">
            保存并采用
          </el-button>
        </div>
      </template>
    </el-dialog>
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

.table-pagination {
  display: flex;
  justify-content: flex-end;
  margin-top: 12px;
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

.preview-cell {
  display: flex;
  flex-direction: column;
  gap: 6px;
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

.equivalence-summary {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: 8px 10px;
  border-radius: 10px;
  border: 1px solid #bfdbfe;
  background: #eff6ff;
}

.equivalence-summary__tags {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.equivalence-summary__text {
  font-size: 12px;
  line-height: 1.5;
  color: #1d4ed8;
}

.issue-summary {
  padding: 8px 10px;
  border-radius: 10px;
  background: #fff7ed;
  color: #9a3412;
  font-size: 12px;
  line-height: 1.5;
}

.ambiguity-reason {
  font-size: 12px;
  color: #b45309;
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

.edit-dialog {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.edit-dialog__hint {
  margin-bottom: 8px;
  padding: 10px 12px;
  border-radius: 8px;
  background: #f5f7fa;
  color: #606266;
  font-size: 13px;
}

.edit-dialog__footer {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
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
