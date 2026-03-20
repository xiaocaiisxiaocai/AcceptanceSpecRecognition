<script setup lang="ts">
import { computed } from "vue";
import MatchPreviewTable from "./MatchPreviewTable.vue";
import type { MatchPreviewItem } from "@/api/matching";
import type { BatchTablePreviewResult } from "@/api/matching";

const props = defineProps<{
  /** 各表格预览结果 */
  results: BatchTablePreviewResult[];
  /** 加载状态 */
  loading?: boolean;
  /** 高置信自动采用阈值 */
  highConfidenceThreshold?: number;
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
const activeTab = computed(() =>
  props.results.length > 0 ? String(props.results[0].tableIndex) : "0"
);

/** 每个表格的 MatchPreviewTable ref */
const tableRefs = new Map<number, InstanceType<typeof MatchPreviewTable>>();

const setTableRef = (tableIndex: number, el: any) => {
  if (el) {
    tableRefs.set(tableIndex, el);
  }
};

/** 获取所有表格的选择结果（按表格分组） */
const getAllSelections = () => {
  const result: Map<
    number,
    Array<{
      rowIndex: number;
      specId?: number;
      matchScore?: number;
      llmReviewScore?: number;
    }>
  > = new Map();

  for (const tableResult of props.results) {
    const ref = tableRefs.get(tableResult.tableIndex);
    if (ref) {
      result.set(tableResult.tableIndex, ref.getSelections());
    }
  }

  return result;
};

defineExpose({ getAllSelections });
</script>

<template>
  <div class="batch-preview-tabs">
    <el-tabs v-model="activeTab" type="border-card">
      <el-tab-pane
        v-for="tableResult in results"
        :key="tableResult.tableIndex"
        :label="`表格 ${tableResult.tableIndex + 1} (${tableResult.totalMatched}/${tableResult.items.length})`"
        :name="String(tableResult.tableIndex)"
      >
        <div class="table-stats">
          <el-tag type="success" size="small">
            高 {{ tableResult.highConfidenceCount }}
          </el-tag>
          <el-tag type="warning" size="small">
            中 {{ tableResult.mediumConfidenceCount }}
          </el-tag>
          <el-tag type="danger" size="small">
            低 {{ tableResult.lowConfidenceCount }}
          </el-tag>
          <el-tag
            v-if="tableResult.ambiguousCount > 0"
            type="warning"
            size="small"
            effect="plain"
          >
            歧义 {{ tableResult.ambiguousCount }}
          </el-tag>
        </div>

        <MatchPreviewTable
          :ref="(el: any) => setTableRef(tableResult.tableIndex, el)"
          :items="tableResult.items"
          :loading="loading"
          :high-confidence-threshold="highConfidenceThreshold"
          :llm-streaming="llmStreaming"
          @select="
            (rowIndex, spec) => emit('select', tableResult.tableIndex, rowIndex, spec)
          "
          @show-detail="(item) => emit('showDetail', item)"
        />
      </el-tab-pane>
    </el-tabs>
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
</style>
