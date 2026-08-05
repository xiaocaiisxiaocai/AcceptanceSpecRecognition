<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, useId, watch } from "vue";
import type { ImportPreviewGroup, ImportPreviewRow } from "../dataImport.types";
import { isAcceptanceAndRemarkBlank } from "../composables/useDataImportPreviewSelection";

const props = withDefaults(
  defineProps<{
    previewDataCount: number;
    previewLoadState: {
      loadedRows: number;
      totalRows: number;
      hasPartialPreview: boolean;
      hasPendingInitialPreview: boolean;
    };
    removedPreviewRowCount: number;
    selectedImportPreviewRowKeys: string[];
    selectedImportPreviewRowsCount: number;
    irrelevantPreviewRowCount: number;
    allIrrelevantPreviewRowsSelected: boolean;
    someIrrelevantPreviewRowsSelected: boolean;
    importPreviewGroups: ImportPreviewGroup[];
    hasPendingDifferenceConfirmation: boolean;
    activeTableIndex?: number;
    showHeading?: boolean;
    autoLoadFull?: boolean;
    tabbedGroups?: boolean;
  }>(),
  {
    showHeading: false,
    autoLoadFull: false,
    tabbedGroups: false
  }
);

const emit = defineEmits<{
  "update:activeTableIndex": [tableIndex: number];
  removeSelectedPreviewRows: [];
  restoreRemovedPreviewRows: [];
  selectIrrelevantRowsChange: [selected: boolean];
  importPreviewSelectionChange: [
    tableIndex: number,
    rows: ImportPreviewRow[],
    visibleRows: ImportPreviewRow[]
  ];
  removeSinglePreviewRow: [row: ImportPreviewRow];
  loadFullPreview: [];
}>();

type PreviewSelectionTable = {
  clearSelection: () => void;
  toggleRowSelection: (row: ImportPreviewRow, selected?: boolean) => void;
};

const previewTabNamePrefix = `${useId()}-import-preview`;
const getPreviewTabName = (groupKey: string) =>
  `${previewTabNamePrefix}-${groupKey}`;
const getPreviewGroupKey = (name: string | number) => {
  const normalizedName = String(name);
  const prefix = `${previewTabNamePrefix}-`;
  if (!normalizedName.startsWith(prefix)) return "";
  return normalizedName.slice(prefix.length);
};
const activePreviewTabName = ref("");
const selectedPreviewRowKeySet = computed(
  () => new Set(props.selectedImportPreviewRowKeys)
);
const visibleImportPreviewGroups = computed(() => {
  if (!props.tabbedGroups || props.activeTableIndex == null) {
    return props.importPreviewGroups;
  }
  return props.importPreviewGroups.filter(
    group => group.tableIndex === props.activeTableIndex
  );
});

watch(
  [
    () => props.activeTableIndex,
    () => visibleImportPreviewGroups.value.map(group => group.key).join("|")
  ],
  () => {
    const currentGroupKey = getPreviewGroupKey(activePreviewTabName.value);
    const currentGroup = visibleImportPreviewGroups.value.find(
      group => group.key === currentGroupKey
    );
    const nextGroup = currentGroup ?? visibleImportPreviewGroups.value[0];

    activePreviewTabName.value =
      nextGroup == null ? "" : getPreviewTabName(nextGroup.key);
  },
  { immediate: true }
);

const importPreviewTableRefs = new Map<string, PreviewSelectionTable>();
const syncVisibleSelection = async (groupKey: string) => {
  await nextTick();
  const table = importPreviewTableRefs.get(groupKey);
  const group = visibleImportPreviewGroups.value.find(
    item => item.key === groupKey
  );
  if (!table || !group) return;

  table.clearSelection();
  group.rows
    .filter(row => selectedPreviewRowKeySet.value.has(row.key))
    .forEach(row => table.toggleRowSelection(row, true));
};

const setImportPreviewTableRef = (groupKey: string, instance: unknown) => {
  if (!instance) {
    importPreviewTableRefs.delete(groupKey);
    return;
  }
  if (importPreviewTableRefs.get(groupKey) === instance) return;
  importPreviewTableRefs.set(groupKey, instance as PreviewSelectionTable);
  void syncVisibleSelection(groupKey);
};

const handlePreviewTabChange = (name: string | number) => {
  if (!props.tabbedGroups) return;
  const groupKey = getPreviewGroupKey(name);
  const group = visibleImportPreviewGroups.value.find(
    item => item.key === groupKey
  );
  if (!group) return;
  emit("update:activeTableIndex", group.tableIndex);
  void syncVisibleSelection(group.key);
};

const handlePreviewSelectionChange = (
  tableIndex: number,
  visibleRows: ImportPreviewRow[],
  selectedRows: ImportPreviewRow[]
) => {
  const visibleRowKeys = new Set(visibleRows.map(row => row.key));
  emit(
    "importPreviewSelectionChange",
    tableIndex,
    selectedRows.filter(row => visibleRowKeys.has(row.key)),
    visibleRows
  );
};

const handleSelectIrrelevantRowsChange = (value: boolean | string | number) => {
  const selected = value === true;
  emit("selectIrrelevantRowsChange", selected);
  props.importPreviewGroups.forEach(group => {
    const table = importPreviewTableRefs.get(group.key);
    if (!table) return;
    group.rows
      .filter(isAcceptanceAndRemarkBlank)
      .forEach(row => table.toggleRowSelection(row, selected));
  });
};

let autoLoadFullTimer: ReturnType<typeof setTimeout> | null = null;
watch(
  () =>
    [
      props.autoLoadFull,
      props.previewLoadState.hasPartialPreview,
      props.previewLoadState.hasPendingInitialPreview,
      props.previewLoadState.loadedRows,
      props.previewLoadState.totalRows,
      props.importPreviewGroups
    ] as const,
  ([
    autoLoadFull,
    hasPartialPreview,
    hasPendingInitialPreview,
    _loadedRows,
    _totalRows,
    _importPreviewGroups
  ]) => {
    if (autoLoadFullTimer) {
      clearTimeout(autoLoadFullTimer);
      autoLoadFullTimer = null;
    }
    if (!autoLoadFull || !hasPartialPreview || hasPendingInitialPreview) {
      return;
    }

    autoLoadFullTimer = setTimeout(() => {
      autoLoadFullTimer = null;
      emit("loadFullPreview");
    }, 800);
  },
  { flush: "post", immediate: true }
);

onBeforeUnmount(() => {
  if (autoLoadFullTimer) clearTimeout(autoLoadFullTimer);
});
</script>

<template>
  <section class="data-import-preview-panel">
    <header class="preview-topbar">
      <div v-if="showHeading" class="preview-heading">
        <strong>待导入清单</strong>
        <span>实时预览</span>
      </div>

      <div class="import-preview-toolbar">
        <div class="import-preview-summary">
          <div class="preview-metric primary">
            <strong>{{ previewDataCount }}</strong>
            <span>待导入</span>
          </div>
          <div class="preview-metric">
            <strong>{{ previewLoadState.loadedRows }}</strong>
            <span>
              {{ previewLoadState.hasPartialPreview ? "正在加载" : "当前显示" }}
            </span>
          </div>
          <div v-if="removedPreviewRowCount > 0" class="preview-metric warning">
            <strong>{{ removedPreviewRowCount }}</strong>
            <span>已移出</span>
          </div>
        </div>
        <div class="import-preview-actions">
          <el-tooltip content="验收列和备注列同时为空" placement="top">
            <el-checkbox
              :model-value="allIrrelevantPreviewRowsSelected"
              :indeterminate="someIrrelevantPreviewRowsSelected"
              :disabled="
                previewLoadState.hasPartialPreview ||
                hasPendingDifferenceConfirmation ||
                irrelevantPreviewRowCount === 0
              "
              @change="handleSelectIrrelevantRowsChange"
            >
              选中无关项
              <span v-if="irrelevantPreviewRowCount > 0">
                （{{ irrelevantPreviewRowCount }}）
              </span>
            </el-checkbox>
          </el-tooltip>
          <el-button
            size="small"
            type="danger"
            plain
            :disabled="
              previewLoadState.hasPartialPreview ||
              hasPendingDifferenceConfirmation ||
              selectedImportPreviewRowsCount === 0
            "
            @click="emit('removeSelectedPreviewRows')"
          >
            移出所选（{{ selectedImportPreviewRowsCount }}）
          </el-button>
          <el-button
            size="small"
            :disabled="
              hasPendingDifferenceConfirmation || removedPreviewRowCount === 0
            "
            @click="emit('restoreRemovedPreviewRows')"
          >
            恢复移出项
          </el-button>
        </div>
      </div>
    </header>

    <el-tabs
      v-if="visibleImportPreviewGroups.length > 0"
      v-model="activePreviewTabName"
      class="import-preview-tabs"
      :class="{ 'import-preview-tabs--stacked': !tabbedGroups }"
      @tab-change="handlePreviewTabChange"
    >
      <el-tab-pane
        v-for="group in visibleImportPreviewGroups"
        :key="`import-preview-${group.key}`"
        :name="getPreviewTabName(group.key)"
      >
        <template #label>
          <span class="preview-tab-label">
            <span>{{ group.label }}</span>
            <span>{{ group.rows.length }} 条</span>
          </span>
        </template>

        <div class="import-preview-group">
          <div v-if="!tabbedGroups" class="import-preview-group__header">
            <span>{{ group.label }}</span>
            <span class="group-count">保留 {{ group.rows.length }} 条</span>
          </div>
          <div class="import-preview-table">
            <el-table
              :ref="instance => setImportPreviewTableRef(group.key, instance)"
              :data="group.rows"
              height="100%"
              border
              size="small"
              row-key="key"
              reserve-selection
              @selection-change="
                rows =>
                  handlePreviewSelectionChange(
                    group.tableIndex,
                    group.rows,
                    rows
                  )
              "
            >
              <el-table-column type="selection" width="48" />
              <el-table-column
                prop="displayRowNumber"
                label="行号"
                width="72"
              />
              <el-table-column
                prop="project"
                label="项目"
                min-width="120"
                show-overflow-tooltip
              >
                <template #default="{ row }">
                  {{ row.project || "-" }}
                </template>
              </el-table-column>
              <el-table-column
                prop="specification"
                label="规格"
                min-width="220"
                show-overflow-tooltip
              >
                <template #default="{ row }">
                  {{ row.specification || "-" }}
                </template>
              </el-table-column>
              <el-table-column
                prop="acceptance"
                label="验收"
                min-width="140"
                show-overflow-tooltip
              >
                <template #default="{ row }">
                  {{ row.acceptance || "-" }}
                </template>
              </el-table-column>
              <el-table-column
                prop="remark"
                label="备注"
                min-width="140"
                show-overflow-tooltip
              >
                <template #default="{ row }">
                  {{ row.remark || "-" }}
                </template>
              </el-table-column>
              <el-table-column label="操作" width="72" fixed="right">
                <template #default="{ row }">
                  <el-button
                    type="danger"
                    link
                    :disabled="
                      previewLoadState.hasPartialPreview ||
                      hasPendingDifferenceConfirmation
                    "
                    @click="emit('removeSinglePreviewRow', row)"
                  >
                    移出
                  </el-button>
                </template>
              </el-table-column>
            </el-table>
          </div>
        </div>
      </el-tab-pane>
    </el-tabs>
    <el-empty
      v-else
      description="当前没有待导入数据，可恢复已移出数据或返回左侧调整配置。"
    />
  </section>
</template>

<style scoped>
.data-import-preview-panel {
  display: flex;
  flex: 1;
  flex-direction: column;
  gap: 10px;
  min-width: 0;
  height: 100%;
  min-height: 0;
  overflow: hidden;
}

.preview-topbar {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  align-items: center;
  justify-content: space-between;
  padding: 10px 12px;
  background: var(--el-fill-color-extra-light);
  border: 1px solid var(--app-border);
  border-radius: 8px;
}

.preview-heading {
  display: flex;
  flex: 0 0 auto;
  gap: 8px;
  align-items: center;
}

.preview-heading strong {
  font-size: 15px;
  color: var(--app-text-primary);
}

.preview-heading span {
  padding: 2px 7px;
  font-size: 11px;
  color: var(--app-primary);
  background: var(--app-primary-light);
  border-radius: 999px;
}

.import-preview-toolbar {
  display: flex;
  flex: 1;
  flex-wrap: wrap;
  gap: 12px;
  align-items: center;
  justify-content: space-between;
  min-width: 0;
}

.import-preview-summary,
.import-preview-actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
}

.preview-metric {
  display: flex;
  gap: 5px;
  align-items: baseline;
  padding: 0 11px;
  color: var(--app-text-secondary);
  border-left: 1px solid var(--app-border);
}

.preview-metric:first-child {
  padding-left: 0;
  border-left: 0;
}

.preview-metric strong {
  font-size: 17px;
  font-weight: 700;
  color: var(--app-text-primary);
  letter-spacing: -0.03em;
}

.preview-metric span {
  font-size: 12px;
}

.preview-metric.primary strong {
  color: var(--app-primary);
}

.preview-metric.warning strong {
  color: var(--app-warning);
}

.import-preview-actions {
  gap: 7px;
}

.import-preview-actions :deep(.el-checkbox) {
  margin-right: 4px;
}

.import-preview-tabs {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
  overflow: hidden;
}

.import-preview-tabs :deep(.el-tabs__header) {
  flex: 0 0 auto;
  margin: 0;
}

.import-preview-tabs :deep(.el-tabs__nav-wrap) {
  padding: 0 10px;
}

.import-preview-tabs :deep(.el-tabs__content) {
  flex: 1;
  min-height: 0;
}

.import-preview-tabs :deep(.el-tab-pane) {
  height: 100%;
}

.preview-tab-label {
  display: inline-flex;
  gap: 8px;
  align-items: center;
}

.preview-tab-label > span:last-child {
  padding: 1px 6px;
  font-size: 11px;
  color: var(--app-text-secondary);
  background: var(--el-fill-color);
  border-radius: 999px;
}

.import-preview-tabs--stacked {
  overflow: auto;
  scrollbar-gutter: stable;
  overscroll-behavior: contain;
}

.import-preview-tabs--stacked :deep(.el-tabs__header) {
  display: none;
}

.import-preview-tabs--stacked :deep(.el-tabs__content) {
  display: flex;
  flex: none;
  flex-direction: column;
  gap: 12px;
  overflow: visible;
}

.import-preview-tabs--stacked :deep(.el-tab-pane) {
  display: block !important;
  height: min(620px, calc(100dvh - 360px));
}

.import-preview-group {
  display: flex;
  flex-direction: column;
  min-width: 0;
  height: 100%;
  min-height: 0;
  overflow: clip;
  background: var(--app-bg-card);
  border: 1px solid var(--app-border);
  border-radius: 8px;
}

.import-preview-group__header {
  display: flex;
  gap: 12px;
  align-items: center;
  justify-content: space-between;
  padding: 8px 11px;
  font-size: 13px;
  font-weight: 600;
  color: var(--app-text-primary);
  background: var(--el-fill-color-extra-light);
  border-bottom: 1px solid var(--app-border);
}

.group-count {
  font-size: 12px;
  font-weight: 500;
  color: var(--app-text-secondary);
}

.import-preview-group :deep(.el-table) {
  --el-table-border-color: var(--app-border);

  border: 0;
}

.import-preview-group :deep(.el-table__inner-wrapper::before) {
  display: none;
}

.import-preview-table {
  flex: 1;
  min-height: 0;
}

@media (width <= 1280px) {
  .data-import-preview-panel {
    height: auto;
    overflow: visible;
  }

  .import-preview-tabs {
    overflow: visible;
  }

  .import-preview-tabs :deep(.el-tabs__content) {
    min-height: 560px;
  }

  .import-preview-tabs--stacked :deep(.el-tabs__content) {
    min-height: 0;
  }
}

@media (width <= 900px) {
  .import-preview-toolbar {
    align-items: stretch;
  }

  .import-preview-summary,
  .import-preview-actions {
    width: 100%;
  }

  .import-preview-actions :deep(.el-button) {
    flex: 1;
    min-height: 40px;
    margin-left: 0;
  }
}

@media (width <= 560px) {
  .preview-topbar {
    flex-direction: column;
    align-items: flex-start;
  }

  .preview-metric {
    padding: 0 9px;
  }
}
</style>
