<script setup lang="ts">
import { computed, ref, watch } from "vue";
import MatchPreviewTable from "./MatchPreviewTable.vue";
import type { EditedBackfillItem } from "./MatchPreviewTable.vue";
import type { BatchTablePreviewResult, MatchPreviewItem } from "@/api/matching";

const props = defineProps<{
  /** 各表格预览结果 */
  results: BatchTablePreviewResult[];
  /** 加载状态 */
  loading?: boolean;
  /** 高置信结果分层阈值 */
  highConfidenceThreshold?: number;
  /** 高歧义分差阈值 */
  ambiguityMargin?: number;
  /** LLM 流式处理是否进行中 */
  llmStreaming?: boolean;
}>();

const emit = defineEmits<{
  (
    e: "select",
    tableIndex: number,
    rowIndex: number,
    spec: MatchPreviewItem["bestMatch"] | null
  ): void;
  (e: "showDetail", item: MatchPreviewItem): void;
}>();

/** 活跃的 Tab */
const activeTab = ref("0");
const activeTableRef = ref<InstanceType<typeof MatchPreviewTable> | null>(null);
const selectionCache = new Map<
  number,
  Array<{
    rowIndex: number;
    selected?: boolean;
    specId?: number;
    manualConfirmed?: boolean;
    manualFill?: boolean;
    reviewApprovalToken?: string;
    overrideAcceptance?: string;
    overrideRemark?: string;
  }>
>();

const activeTableResult = computed(() =>
  props.results.find(result => String(result.tableIndex) === activeTab.value) ?? null
);

const isNoAnswerPlaceholderRow = (item: MatchPreviewItem) => {
  const project = (item.sourceProject || "").trim().toLowerCase();
  const specification = (item.sourceSpecification || "").trim();
  if (specification) {
    return false;
  }

  return new Set(["其他", "-", "/", "无", "n/a", "na"]).has(project);
};

const getDecision = (item: MatchPreviewItem) =>
  item.bestMatch?.decision ?? "manualReview";

const canUseBestMatch = (item: MatchPreviewItem) => {
  if (!item.bestMatch || isNoAnswerPlaceholderRow(item)) {
    return false;
  }

  if (getDecision(item) === "reject") {
    return false;
  }

  if (item.llmReviewStage === "streaming") {
    return false;
  }

  return true;
};

const isHighConfidence = (item: MatchPreviewItem) =>
  getDecision(item) === "autoApply" && item.confidenceLevel === "high";

const buildDefaultSelections = (tableResult?: BatchTablePreviewResult | null) => {
  if (!tableResult) {
    return [];
  }

  return tableResult.items
    .filter(item => {
      if (!canUseBestMatch(item)) {
        return false;
      }

      return !!item.bestMatch?.reviewApprovalToken || isHighConfidence(item);
    })
    .map(item => ({
      rowIndex: item.rowIndex,
      selected: true,
      specId: item.bestMatch?.specId,
      manualConfirmed: false,
      manualFill: false,
      reviewApprovalToken: item.bestMatch?.reviewApprovalToken,
      overrideAcceptance: undefined,
      overrideRemark: undefined
    }));
};

watch(
  () => props.results,
  (results) => {
    selectionCache.clear();
    if (results.length === 0) {
      activeTab.value = "0";
      return;
    }

    const hasActiveTab = results.some(
      result => String(result.tableIndex) === activeTab.value
    );
    if (!hasActiveTab) {
      activeTab.value = String(results[0].tableIndex);
    }
  },
  { immediate: true }
);

watch(activeTab, (_newTab, oldTab) => {
  const previousTableIndex = Number(oldTab);
  if (!Number.isNaN(previousTableIndex)) {
    syncTableSelections(previousTableIndex);
  }
});

const getPersistedSelections = (tableIndex: number) =>
  selectionCache.get(tableIndex)?.map(item => ({ ...item })) ??
  buildDefaultSelections(
    props.results.find(result => result.tableIndex === tableIndex)
  );

const syncTableSelections = (tableIndex?: number) => {
  const targetTableIndex = tableIndex ?? activeTableResult.value?.tableIndex;
  if (targetTableIndex === undefined || targetTableIndex === null) {
    return;
  }
  if (!activeTableRef.value) {
    return;
  }

  selectionCache.set(
    targetTableIndex,
    activeTableRef.value.getSelections().map(item => ({ ...item }))
  );
};

const handleSelect = (
  rowIndex: number,
  spec: MatchPreviewItem["bestMatch"] | null
) => {
  const currentTable = activeTableResult.value;
  if (!currentTable) {
    return;
  }

  syncTableSelections(currentTable.tableIndex);
  emit("select", currentTable.tableIndex, rowIndex, spec);
};

/** 获取所有表格的选择结果（按表格分组） */
const getAllSelections = () => {
  syncTableSelections();

  const result: Map<
    number,
    Array<{
      rowIndex: number;
      specId?: number;
      manualConfirmed?: boolean;
      manualFill?: boolean;
      reviewApprovalToken?: string;
      overrideAcceptance?: string;
      overrideRemark?: string;
    }>
  > = new Map();

  for (const tableResult of props.results) {
    result.set(
      tableResult.tableIndex,
      getPersistedSelections(tableResult.tableIndex)
        .filter(item => item.selected)
        .map(({ selected: _selected, ...item }) => ({ ...item }))
    );
  }

  return result;
};

const getAllEditedBackfillItems = () => {
  syncTableSelections();

  const activeTableIndex = activeTableResult.value?.tableIndex;
  const activeItems = activeTableRef.value?.getEditedBackfillItems() ?? [];
  const result: Array<EditedBackfillItem & { tableIndex: number }> = [];

  for (const tableResult of props.results) {
    const items = tableResult.tableIndex === activeTableIndex
      ? activeItems
      : getPersistedSelections(tableResult.tableIndex)
        .filter(item => item.overrideAcceptance !== undefined || item.overrideRemark !== undefined)
        .map((item): EditedBackfillItem | null => {
          const previewItem = tableResult.items.find(row => row.rowIndex === item.rowIndex);
          if (!previewItem) return null;
          return {
            rowIndex: item.rowIndex,
            specId: item.specId,
            sourceProject: previewItem.sourceProject,
            sourceSpecification: previewItem.sourceSpecification,
            originalAcceptance: previewItem.bestMatch?.acceptance,
            originalRemark: previewItem.bestMatch?.remark,
            overrideAcceptance: item.overrideAcceptance,
            overrideRemark: item.overrideRemark,
            actionType: previewItem.bestMatch ? "update" : "create"
          } satisfies EditedBackfillItem;
        })
        .filter((item): item is EditedBackfillItem => !!item);

    result.push(...items.map(item => ({ ...item, tableIndex: tableResult.tableIndex })));
  }

  return result;
};

defineExpose({ getAllSelections, getAllEditedBackfillItems });
</script>

<template>
  <div class="batch-preview-tabs">
    <el-tabs v-model="activeTab" type="border-card">
      <el-tab-pane
        v-for="tableResult in results"
        :key="tableResult.tableIndex"
        :label="`表格 ${tableResult.tableIndex + 1} (${tableResult.totalMatched}/${tableResult.items.length})`"
        :name="String(tableResult.tableIndex)"
      />
    </el-tabs>

    <div v-if="activeTableResult" class="active-preview-table">
      <div class="table-stats">
        <el-tag type="success" size="small">
          高 {{ activeTableResult.highConfidenceCount }}
        </el-tag>
        <el-tag type="warning" size="small">
          中 {{ activeTableResult.mediumConfidenceCount }}
        </el-tag>
        <el-tag type="danger" size="small">
          低 {{ activeTableResult.lowConfidenceCount }}
        </el-tag>
        <el-tag
          v-if="activeTableResult.ambiguousCount > 0"
          type="warning"
          size="small"
          effect="plain"
        >
          歧义 {{ activeTableResult.ambiguousCount }}
        </el-tag>
      </div>

      <MatchPreviewTable
        v-if="activeTableResult"
        ref="activeTableRef"
        :key="activeTableResult.tableIndex"
        :items="activeTableResult.items"
        :loading="loading"
        :high-confidence-threshold="highConfidenceThreshold"
        :ambiguity-margin="ambiguityMargin"
        :llm-streaming="llmStreaming"
        :persisted-selections="getPersistedSelections(activeTableResult.tableIndex)"
        @select="handleSelect"
        @show-detail="(item) => emit('showDetail', item)"
      />
    </div>
  </div>
</template>

<style scoped>
.batch-preview-tabs {
  margin-top: 12px;
}

.table-stats {
  margin-bottom: 12px;
  display: flex;
  gap: 8px;
}

.active-preview-table {
  margin-top: 12px;
}
</style>
