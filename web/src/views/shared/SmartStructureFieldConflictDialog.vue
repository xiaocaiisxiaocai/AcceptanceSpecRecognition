<script setup lang="ts">
import { computed, reactive, ref, watch } from "vue";
import type { TableInfo } from "@/api/document";
import type {
  SmartStructureFieldConflictItem,
  SmartStructureFieldConflictSelection
} from "./smart-structure-field-conflicts";
import { createRecommendedSmartStructureFieldSelections } from "./smart-structure-field-conflicts";

const props = withDefaults(
  defineProps<{
    visible: boolean;
    conflicts: SmartStructureFieldConflictItem[];
    tableInfos?: TableInfo[];
    isExcelFile?: boolean;
  }>(),
  {
    tableInfos: () => [],
    isExcelFile: true
  }
);

const emit = defineEmits<{
  "update:visible": [value: boolean];
  confirm: [selections: SmartStructureFieldConflictSelection[]];
  cancel: [];
}>();

const selectedColumns = reactive<Record<string, number | undefined>>({});
const hasAdjustedSelection = ref(false);

watch(
  () => [props.visible, props.conflicts] as const,
  ([visible, conflicts]) => {
    if (!visible) return;
    hasAdjustedSelection.value = false;
    Object.keys(selectedColumns).forEach(key => delete selectedColumns[key]);
    Object.assign(
      selectedColumns,
      createRecommendedSmartStructureFieldSelections(conflicts)
    );
  },
  { deep: true }
);

const resolvedCount = computed(
  () =>
    props.conflicts.filter(conflict => selectedColumns[conflict.key] != null)
      .length
);
const canConfirm = computed(
  () =>
    props.conflicts.length > 0 && resolvedCount.value === props.conflicts.length
);

const toExcelColumnLabel = (columnNumber: number) => {
  let value = Math.max(1, columnNumber);
  let label = "";
  while (value > 0) {
    value -= 1;
    label = String.fromCharCode(65 + (value % 26)) + label;
    value = Math.floor(value / 26);
  }
  return label;
};

const getTableInfo = (tableIndex: number) =>
  props.tableInfos.find(table => table.index === tableIndex);

const formatColumnLabel = (
  conflict: SmartStructureFieldConflictItem,
  columnIndex: number
) => {
  const info = getTableInfo(conflict.tableIndex);
  return props.isExcelFile
    ? toExcelColumnLabel((info?.usedRangeStartColumn ?? 1) + columnIndex)
    : `第 ${columnIndex + 1} 列`;
};

const formatRange = (
  conflict: SmartStructureFieldConflictItem,
  columnIndex: number
) => {
  const info = getTableInfo(conflict.tableIndex);
  const startRow = (info?.usedRangeStartRow ?? 1) + conflict.dataStartRowIndex;
  const endRow =
    (info?.usedRangeStartRow ?? 1) +
    (conflict.dataEndRowIndex ?? conflict.dataStartRowIndex);
  const column = formatColumnLabel(conflict, columnIndex);
  return props.isExcelFile
    ? `${column}${startRow}:${column}${endRow}`
    : `${column}（第 ${startRow}–${endRow} 行）`;
};

const handleCancel = () => {
  emit("update:visible", false);
  emit("cancel");
};

const handleConfirm = () => {
  if (!canConfirm.value) return;
  emit(
    "confirm",
    props.conflicts.map(conflict => ({
      key: conflict.key,
      tableIndex: conflict.tableIndex,
      regionId: conflict.regionId,
      regionIndex: conflict.regionIndex,
      field: conflict.field,
      columnIndex: selectedColumns[conflict.key]!
    }))
  );
};
</script>

<template>
  <el-dialog
    :model-value="visible"
    width="760px"
    class="smart-field-conflict-dialog"
    append-to-body
    :show-close="false"
    :close-on-click-modal="false"
    :close-on-press-escape="false"
  >
    <template #header>
      <div class="dialog-heading">
        <div class="dialog-heading__copy">
          <h3>确认数据列</h3>
          <p>系统已预选推荐项，可直接确认或调整。</p>
        </div>
        <span
          class="selection-status"
          :class="{ 'is-adjusted': hasAdjustedSelection }"
        >
          {{ hasAdjustedSelection ? "已选择" : "已预选" }}
          {{ resolvedCount }}/{{ conflicts.length }}
        </span>
      </div>
    </template>

    <div class="conflict-list">
      <section
        v-for="conflict in conflicts"
        :key="conflict.key"
        class="conflict-section"
      >
        <header>
          <div>
            <span class="sheet-name">{{ conflict.tableName }}</span>
            <span class="region-name">区域 {{ conflict.regionIndex + 1 }}</span>
          </div>
          <strong>{{ conflict.fieldLabel }}</strong>
        </header>

        <el-radio-group
          v-model="selectedColumns[conflict.key]"
          class="candidate-grid"
          @change="hasAdjustedSelection = true"
        >
          <el-radio
            v-for="candidate in conflict.candidates"
            :key="candidate.columnIndex"
            :value="candidate.columnIndex"
            class="candidate-card"
            border
          >
            <span class="candidate-main">
              <span class="column-mark">
                {{ formatColumnLabel(conflict, candidate.columnIndex) }}
              </span>
              <span class="candidate-title">{{ candidate.header }}</span>
              <el-tag
                v-if="candidate.isRecommended"
                size="small"
                type="success"
                effect="plain"
              >
                系统推荐
              </el-tag>
            </span>
            <span class="candidate-meta">
              <span>{{ formatRange(conflict, candidate.columnIndex) }}</span>
              <span>置信度 {{ Math.round(candidate.confidence * 100) }}%</span>
            </span>
            <span v-if="candidate.samples.length" class="candidate-samples">
              示例：{{ candidate.samples.join(" · ") }}
            </span>
            <span v-else class="candidate-samples is-empty">
              当前范围暂无非空样例
            </span>
          </el-radio>
        </el-radio-group>
      </section>
    </div>

    <template #footer>
      <div class="dialog-footer">
        <span>预选结果不会自动生效，确认后才会学习配置并进入下一步</span>
        <div>
          <el-button @click="handleCancel">暂不处理</el-button>
          <el-button
            type="primary"
            :disabled="!canConfirm"
            @click="handleConfirm"
          >
            确认选择并继续
          </el-button>
        </div>
      </div>
    </template>
  </el-dialog>
</template>

<style scoped>
.dialog-heading {
  display: flex;
  gap: 20px;
  align-items: center;
  justify-content: space-between;
}

.dialog-heading__copy {
  display: grid;
  gap: 3px;
  min-width: 0;
}

.dialog-heading h3 {
  margin: 0;
  font-size: 18px;
  line-height: 1.35;
  color: var(--app-text-primary);
}

.dialog-heading p {
  margin: 0;
  font-size: 13px;
  color: var(--app-text-secondary);
}

.selection-status {
  flex: 0 0 auto;
  padding: 4px 9px;
  font-size: 12px;
  font-weight: 600;
  color: var(--el-color-primary);
  background: var(--el-color-primary-light-9);
  border: 1px solid var(--el-color-primary-light-7);
  border-radius: 999px;
}

.selection-status.is-adjusted {
  color: var(--el-color-success);
  background: var(--el-color-success-light-9);
  border-color: var(--el-color-success-light-7);
}

.conflict-list {
  display: grid;
  gap: 14px;
  max-height: min(56vh, 520px);
  padding-right: 4px;
  overflow-y: auto;
}

.conflict-section {
  padding: 14px;
  background: var(--el-fill-color-extra-light);
  border: 1px solid var(--el-border-color-light);
  border-radius: 8px;
}

.conflict-section header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 10px;
}

.sheet-name {
  font-weight: 700;
  color: var(--app-text-primary);
}

.region-name {
  margin-left: 8px;
  font-size: 12px;
  color: var(--app-text-secondary);
}

.candidate-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px;
  width: 100%;
}

.candidate-card {
  width: 100%;
  height: auto;
  min-height: 112px;
  padding: 12px;
  margin: 0;
  background: var(--el-bg-color);
}

.candidate-card :deep(.el-radio__label) {
  display: grid;
  gap: 8px;
  width: 100%;
  min-width: 0;
  padding-left: 10px;
  white-space: normal;
}

.candidate-main,
.candidate-meta {
  display: flex;
  gap: 8px;
  align-items: center;
}

.candidate-meta {
  justify-content: space-between;
  font-size: 12px;
  color: var(--app-text-secondary);
}

.column-mark {
  display: inline-grid;
  place-items: center;
  min-width: 30px;
  height: 26px;
  font-weight: 800;
  color: var(--el-color-primary);
  background: var(--el-color-primary-light-9);
  border-radius: 5px;
}

.candidate-title {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  font-weight: 700;
  color: var(--app-text-primary);
  white-space: nowrap;
}

.candidate-samples {
  overflow: hidden;
  text-overflow: ellipsis;
  font-size: 12px;
  line-height: 1.55;
  color: var(--app-text-regular);
  white-space: nowrap;
}

.candidate-samples.is-empty {
  color: var(--app-text-placeholder);
}

.dialog-footer {
  display: flex;
  gap: 16px;
  align-items: center;
  justify-content: space-between;
  font-size: 12px;
  color: var(--app-text-secondary);
}

@media (width <= 760px) {
  .dialog-heading {
    align-items: flex-start;
  }

  .candidate-grid {
    grid-template-columns: 1fr;
  }

  .dialog-footer {
    flex-direction: column;
    align-items: flex-start;
  }
}
</style>
