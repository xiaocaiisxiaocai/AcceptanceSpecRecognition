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

const PREVIEW_PAGE_SIZE = 50;
const previewTabNamePrefix = `${useId()}-import-preview`;
const getPreviewTabName = (tableIndex: number) =>
  `${previewTabNamePrefix}-${tableIndex}`;
const getPreviewTableIndex = (name: string | number) => {
  const normalizedName = String(name);
  const prefix = `${previewTabNamePrefix}-`;
  if (!normalizedName.startsWith(prefix)) return Number.NaN;
  return Number(normalizedName.slice(prefix.length));
};
const previewPageMap = ref<Record<number, number>>({});
const activePreviewTabName = ref("");
const selectedPreviewRowKeySet = computed(
  () => new Set(props.selectedImportPreviewRowKeys)
);
const pagedImportPreviewGroups = computed(() =>
  props.importPreviewGroups.map(group => {
    const pageCount = Math.max(
      1,
      Math.ceil(group.rows.length / PREVIEW_PAGE_SIZE)
    );
    const currentPage = Math.min(
      previewPageMap.value[group.tableIndex] ?? 1,
      pageCount
    );
    const start = (currentPage - 1) * PREVIEW_PAGE_SIZE;
    return {
      ...group,
      currentPage,
      totalRows: group.rows.length,
      rows: group.rows.slice(start, start + PREVIEW_PAGE_SIZE)
    };
  })
);

watch(
  [
    () => props.activeTableIndex,
    () => props.importPreviewGroups.map(group => group.tableIndex).join("|")
  ],
  ([requestedTableIndex]) => {
    const availableTableIndexes = props.importPreviewGroups.map(
      group => group.tableIndex
    );
    const currentTableIndex = getPreviewTableIndex(activePreviewTabName.value);
    const nextTableIndex =
      requestedTableIndex != null &&
      availableTableIndexes.includes(requestedTableIndex)
        ? requestedTableIndex
        : availableTableIndexes.includes(currentTableIndex)
          ? currentTableIndex
          : availableTableIndexes[0];

    activePreviewTabName.value =
      nextTableIndex == null ? "" : getPreviewTabName(nextTableIndex);
  },
  { immediate: true }
);

const importPreviewTableRefs = new Map<number, PreviewSelectionTable>();
const syncVisibleSelection = async (tableIndex: number) => {
  await nextTick();
  const table = importPreviewTableRefs.get(tableIndex);
  const group = pagedImportPreviewGroups.value.find(
    item => item.tableIndex === tableIndex
  );
  if (!table || !group) return;

  table.clearSelection();
  group.rows
    .filter(row => selectedPreviewRowKeySet.value.has(row.key))
    .forEach(row => table.toggleRowSelection(row, true));
};

const setImportPreviewTableRef = (tableIndex: number, instance: unknown) => {
  if (!instance) {
    importPreviewTableRefs.delete(tableIndex);
    return;
  }
  if (importPreviewTableRefs.get(tableIndex) === instance) return;
  importPreviewTableRefs.set(tableIndex, instance as PreviewSelectionTable);
  void syncVisibleSelection(tableIndex);
};

const handlePreviewPageChange = (tableIndex: number, page: number) => {
  previewPageMap.value = {
    ...previewPageMap.value,
    [tableIndex]: page
  };
  void syncVisibleSelection(tableIndex);
};

const handlePreviewTabChange = (name: string | number) => {
  if (!props.tabbedGroups) return;
  const tableIndex = getPreviewTableIndex(name);
  if (!Number.isInteger(tableIndex)) return;
  emit("update:activeTableIndex", tableIndex);
  void syncVisibleSelection(tableIndex);
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
  pagedImportPreviewGroups.value.forEach(group => {
    const table = importPreviewTableRefs.get(group.tableIndex);
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
      v-if="previewDataCount > 0"
      v-model="activePreviewTabName"
      class="import-preview-tabs"
      :class="{ 'import-preview-tabs--stacked': !tabbedGroups }"
      @tab-change="handlePreviewTabChange"
    >
      <el-tab-pane
        v-for="group in pagedImportPreviewGroups"
        :key="`import-preview-${group.tableIndex}`"
        :name="getPreviewTabName(group.tableIndex)"
      >
        <template #label>
          <span class="preview-tab-label">
            <span>{{ group.label }}</span>
            <span>{{ group.totalRows }} 条</span>
          </span>
        </template>

        <div class="import-preview-group">
          <div v-if="!tabbedGroups" class="import-preview-group__header">
            <span>{{ group.label }}</span>
            <span class="group-count">保留 {{ group.totalRows }} 条</span>
          </div>
          <div class="import-preview-table">
            <el-table
              :ref="
                instance => setImportPreviewTableRef(group.tableIndex, instance)
              "
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
          <el-pagination
            v-if="group.totalRows > PREVIEW_PAGE_SIZE"
            class="import-preview-pagination"
            small
            background
            layout="prev, pager, next"
            :current-page="group.currentPage"
            :page-size="PREVIEW_PAGE_SIZE"
            :total="group.totalRows"
            @current-change="
              page => handlePreviewPageChange(group.tableIndex, page)
            "
          />
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

.import-preview-pagination {
  justify-content: flex-end;
  padding: 10px 12px;
  border-top: 1px solid var(--app-border);
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
