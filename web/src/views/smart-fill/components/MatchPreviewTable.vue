<script setup lang="ts">
import { computed, ref, watch } from "vue";
import {
  DEFAULT_AMBIGUITY_MARGIN,
  type MatchPreviewItem
} from "@/api/matching";
import {
  getSmartFillTableState,
  type SmartFillFillRecommendation,
  type SmartFillReviewStatus
} from "./scoreDetail.formatters";
import {
  canEditMatchPreviewRow,
  canManuallyAcceptMatchPreviewBestMatch,
  canUseMatchPreviewBestMatch,
  filterMatchPreviewItems,
  formatOptionalPercent,
  getAmbiguityHint,
  getConfirmBestMatchButtonText,
  getFillRecommendationTagType as getPreviewFillRecommendationTagType,
  getFillRecommendationText as getPreviewFillRecommendationText,
  getMatchPreviewStats,
  getPagedMatchPreviewItems,
  getPreviewConfidenceClass,
  getPreviewConfidenceText,
  getReviewStatusText as getPreviewReviewStatusText,
  getReviewTagType as getPreviewReviewTagType,
  type MatchPreviewScoreFilter,
  isHighConfidenceMatchPreview,
  isNoAnswerPlaceholderRow,
  shouldShowFillRecommendation,
  shouldShowReasonColumn
} from "./matchPreviewTable.formatters";
import MatchPreviewDataTable from "./MatchPreviewDataTable.vue";
import MatchPreviewEditDialog from "./MatchPreviewEditDialog.vue";
import MatchPreviewStatsBar from "./MatchPreviewStatsBar.vue";
import {
  cloneMatchPreviewOverride,
  collectMatchPreviewSelections,
  discardCommittedMatchPreviewOverride,
  hasManualFillOverrideValue,
  hasMatchPreviewOverrideValue
} from "./matchPreviewTable.selection";
import type {
  MatchPreviewEditOverride,
  MatchPreviewSelection,
  PersistedSelection
} from "./matchPreviewTable.types";

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
  (
    e: "select",
    rowIndex: number,
    spec: MatchPreviewItem["bestMatch"] | null
  ): void;
  (e: "showDetail", item: MatchPreviewItem): void;
  (e: "selectionChange", selections: PersistedSelection[]): void;
}>();

const selectedSpecs = ref<Map<number, MatchPreviewSelection | null>>(new Map());
const editedOverrides = ref<Map<number, MatchPreviewEditOverride>>(new Map());
const manualClearedRows = ref<Set<number>>(new Set());
const editDialogVisible = ref(false);
const editingItem = ref<MatchPreviewItem | null>(null);
const currentPage = ref(1);
const pageSize = ref(50);
const pageSizeOptions = [20, 50, 100, 200];
const editForm = ref({
  overrideAcceptance: "",
  overrideRemark: ""
});

const getTableState = (item: MatchPreviewItem) =>
  getSmartFillTableState(item, { llmStreaming: props.llmStreaming });

const getReviewStatus = (item: MatchPreviewItem): SmartFillReviewStatus =>
  getTableState(item).reviewStatus;

const effectiveAmbiguityMargin = computed(
  () => props.ambiguityMargin ?? DEFAULT_AMBIGUITY_MARGIN
);

const isHighConfidence = isHighConfidenceMatchPreview;

const canUseBestMatch = (item: MatchPreviewItem) =>
  canUseMatchPreviewBestMatch(item, getReviewStatus(item));

const canManuallyAcceptBestMatch = (item: MatchPreviewItem) =>
  canManuallyAcceptMatchPreviewBestMatch(item, getReviewStatus(item));

const canEditRow = (item: MatchPreviewItem) =>
  canEditMatchPreviewRow(item, getReviewStatus(item));

const canShowClearSelection = (item: MatchPreviewItem) =>
  !!getSelection(item.rowIndex) || canManuallyAcceptBestMatch(item);

const initSelections = () => {
  selectedSpecs.value.clear();
  editedOverrides.value.clear();
  manualClearedRows.value.clear();
  props.items.forEach(item => {
    if (
      item.bestMatch &&
      isHighConfidence(item) &&
      !isNoAnswerPlaceholderRow(item)
    ) {
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

const hasOverrideValue = hasMatchPreviewOverrideValue;

const getCurrentSelections = (): PersistedSelection[] =>
  collectMatchPreviewSelections(
    props.items,
    selectedSpecs.value,
    editedOverrides.value,
    manualClearedRows.value
  );

const emitSelectionChange = () => {
  emit("selectionChange", getCurrentSelections());
};

const persistedStateMap = computed(
  () =>
    new Map(
      (props.persistedSelections ?? []).map(item => [item.rowIndex, item])
    )
);

const getPersistedState = (rowIndex: number) =>
  persistedStateMap.value.get(rowIndex);

const getPersistedSelection = (
  rowIndex: number
): MatchPreviewSelection | null => {
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
  cloneMatchPreviewOverride(getPersistedState(rowIndex));

const getExistingSelection = (
  rowIndex: number
): MatchPreviewSelection | null => {
  if (selectedSpecs.value.has(rowIndex)) {
    return selectedSpecs.value.get(rowIndex) ?? null;
  }

  return getPersistedSelection(rowIndex);
};

const getExistingManualCleared = (rowIndex: number) => {
  if (selectedSpecs.value.has(rowIndex)) {
    return manualClearedRows.value.has(rowIndex);
  }

  return getPersistedState(rowIndex)?.manualCleared === true;
};

const getOverride = (rowIndex: number) => {
  if (editedOverrides.value.has(rowIndex)) {
    return cloneMatchPreviewOverride(editedOverrides.value.get(rowIndex));
  }

  return getPersistedOverride(rowIndex);
};

const getRawAcceptanceText = (item: MatchPreviewItem) =>
  getOverride(item.rowIndex)?.overrideAcceptance ??
  item.bestMatch?.acceptance ??
  "";

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
  const nextOverride: MatchPreviewEditOverride = {
    overrideAcceptance:
      item.bestMatch && editForm.value.overrideAcceptance === baseAcceptance
        ? undefined
        : editForm.value.overrideAcceptance,
    overrideRemark:
      item.bestMatch && editForm.value.overrideRemark === baseRemark
        ? undefined
        : editForm.value.overrideRemark
  };

  if (!item.bestMatch && !hasManualFillOverrideValue(nextOverride)) {
    return;
  }

  if (hasOverrideValue(nextOverride)) {
    editedOverrides.value.set(item.rowIndex, nextOverride);
  } else {
    editedOverrides.value.delete(item.rowIndex);
  }

  selectedSpecs.value.set(item.rowIndex, {
    type: item.bestMatch ? "best" : "manual",
    manualConfirmed: item.bestMatch
      ? !item.bestMatch.reviewApprovalToken
      : true,
    reviewApprovalToken: item.bestMatch?.reviewApprovalToken
  });
  manualClearedRows.value.delete(item.rowIndex);
  emitSelectionChange();
  emit("select", item.rowIndex, item.bestMatch ?? null);
  closeEditDialog();
};

const selectionSyncKey = computed(() =>
  props.items
    .map(item =>
      [
        item.rowIndex,
        item.bestMatch?.specId,
        item.bestMatch?.decision,
        item.bestMatch?.reviewApprovalToken,
        item.llmReviewStage,
        item.bestMatch?.acceptance,
        item.bestMatch?.remark
      ].join(":")
    )
    .join("|")
);

const syncSelectionsWithItems = () => {
  const nextSelections = new Map<number, MatchPreviewSelection | null>();
  const nextOverrides = new Map<number, MatchPreviewEditOverride>();
  const nextManualClearedRows = new Set<number>();

  props.items.forEach(item => {
    const existing = getExistingSelection(item.rowIndex);
    const existingManualCleared = getExistingManualCleared(item.rowIndex);
    const existingOverride = discardCommittedMatchPreviewOverride(
      item,
      getOverride(item.rowIndex)
    );

    if (existingOverride) {
      nextOverrides.set(item.rowIndex, existingOverride);
    }

    if (!item.bestMatch) {
      nextSelections.set(
        item.rowIndex,
        existing?.type === "manual" &&
          hasManualFillOverrideValue(existingOverride)
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
      if (existingManualCleared) {
        nextManualClearedRows.add(item.rowIndex);
      }
      return;
    }

    if (existingManualCleared) {
      nextSelections.set(item.rowIndex, null);
      nextManualClearedRows.add(item.rowIndex);
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
  manualClearedRows.value = nextManualClearedRows;
};

watch(selectionSyncKey, () => syncSelectionsWithItems(), { immediate: true });

const getSelection = (rowIndex: number) => selectedSpecs.value.get(rowIndex);

const handleSelectBest = (item: MatchPreviewItem) => {
  if (!canManuallyAcceptBestMatch(item)) {
    return;
  }

  selectedSpecs.value.set(item.rowIndex, {
    type: "best",
    manualConfirmed: !item.bestMatch?.reviewApprovalToken,
    reviewApprovalToken: item.bestMatch?.reviewApprovalToken
  });
  manualClearedRows.value.delete(item.rowIndex);
  emitSelectionChange();
  emit("select", item.rowIndex, item.bestMatch ?? null);
};

const handleClearSelection = (item: MatchPreviewItem) => {
  selectedSpecs.value.set(item.rowIndex, null);
  manualClearedRows.value.add(item.rowIndex);
  emitSelectionChange();
  emit("select", item.rowIndex, null);
};

const clearSelectionByRow = (rowIndex: number) => {
  selectedSpecs.value.set(rowIndex, null);
  manualClearedRows.value.add(rowIndex);
  emitSelectionChange();
};

const getConfidenceClass = getPreviewConfidenceClass;
const getConfidenceText = getPreviewConfidenceText;
const getReviewStatusText = (item: MatchPreviewItem) => {
  return getPreviewReviewStatusText(
    item,
    getReviewStatus(item),
    isHighConfidence(item)
  );
};

const getReviewTagType = (item: MatchPreviewItem) => {
  return getPreviewReviewTagType(getReviewStatus(item));
};

const getFillRecommendation = (
  item: MatchPreviewItem
): SmartFillFillRecommendation => {
  const tableState = getTableState(item);
  return tableState.fillRecommendation;
};

const getFillRecommendationText = (item: MatchPreviewItem) => {
  return getPreviewFillRecommendationText(getFillRecommendation(item));
};

const getFillRecommendationTagType = (
  item: MatchPreviewItem
): "success" | "warning" | "danger" | "info" => {
  return getPreviewFillRecommendationTagType(getFillRecommendation(item));
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
  return getMatchPreviewStats(
    props.items,
    selectedCount.value,
    getFillRecommendation
  );
});

const scoreFilter = ref<MatchPreviewScoreFilter>("all");

const filteredItems = computed(() => {
  return filterMatchPreviewItems(
    props.items,
    scoreFilter.value,
    getFillRecommendation
  );
});

const pagedFilteredItems = computed(() => {
  return getPagedMatchPreviewItems(
    filteredItems.value,
    currentPage.value,
    pageSize.value
  );
});

const hasReasonColumn = computed(() => shouldShowReasonColumn(props.items));

watch(scoreFilter, () => {
  currentPage.value = 1;
});

watch(
  () => props.items,
  () => {
    currentPage.value = 1;
  }
);

watch(
  filteredItems,
  items => {
    const maxPage = Math.max(1, Math.ceil(items.length / pageSize.value));
    if (currentPage.value > maxPage) {
      currentPage.value = maxPage;
    }
  },
  { immediate: true }
);

const handlePageSizeChange = (size: number) => {
  pageSize.value = size;
  currentPage.value = 1;
};

defineExpose({
  getSelections: getCurrentSelections,
  initSelections,
  clearSelectionByRow
});
</script>

<template>
  <div class="match-preview-table">
    <!--
      统计筛选栏（由 MatchPreviewStatsBar 渲染）分组说明：
        100%精确直达 (exactFillable) — selectionMode=exactShortcut 的完全命中行
        AI/普通可填充 (partialFillable) — 非精确但 fillRecommendation=fillable 的行
        需要确认 (review) — 需人工复核的行
        不建议填充 (blocked) — 冲突/拒绝行
        无匹配 (unmatched) — 无任何候选的行

      复核状态文案（由 getReviewStatusText 渲染）：
        AI判定可采用 — LLM 复核完成且置信度达标
        复核后待确认 — LLM 复核完成但仍需人工确认

      歧义说明（由 getAmbiguityHint / formatOptionalPercent 渲染）：
        Top1/Top2分差 与 歧义阈值 数值由 formatOptionalPercent 格式化后展示
    -->
    <MatchPreviewStatsBar v-model:score-filter="scoreFilter" :stats="stats" />

    <MatchPreviewDataTable
      v-model:current-page="currentPage"
      v-model:page-size="pageSize"
      :items="pagedFilteredItems"
      :loading="loading"
      :has-reason-column="hasReasonColumn"
      :page-size-options="pageSizeOptions"
      :total="filteredItems.length"
      :ambiguity-margin="effectiveAmbiguityMargin"
      :get-confidence-class="getConfidenceClass"
      :get-confidence-text="getConfidenceText"
      :should-show-fill-recommendation="shouldShowFillRecommendation"
      :get-fill-recommendation-tag-type="getFillRecommendationTagType"
      :get-fill-recommendation-text="getFillRecommendationText"
      :get-review-status="getReviewStatus"
      :get-review-tag-type="getReviewTagType"
      :get-review-status-text="getReviewStatusText"
      :get-display-acceptance-text="getDisplayAcceptanceText"
      :get-display-remark-text="getDisplayRemarkText"
      :has-acceptance-override="hasAcceptanceOverride"
      :has-remark-override="hasRemarkOverride"
      :get-selection="getSelection"
      :can-edit-row="canEditRow"
      :can-use-best-match="canUseBestMatch"
      :can-manually-accept-best-match="canManuallyAcceptBestMatch"
      :can-show-clear-selection="canShowClearSelection"
      :get-confirm-best-match-button-text="getConfirmBestMatchButtonText"
      @page-size-change="handlePageSizeChange"
      @show-detail="emit('showDetail', $event)"
      @edit="openEditDialog"
      @select-best="handleSelectBest"
      @clear-selection="handleClearSelection"
    >
      <template #pagination-actions>
        <slot name="pagination-actions" />
      </template>
    </MatchPreviewDataTable>

    <MatchPreviewEditDialog
      v-model:visible="editDialogVisible"
      :item="editingItem"
      :form="editForm"
      @closed="closeEditDialog"
      @save="handleSaveEditedSelection"
    />
  </div>
</template>

<style scoped src="./MatchPreviewTable.styles.css"></style>
