<script setup lang="ts">
import { computed, ref, watch } from "vue";
import type { TableData, TableInfo } from "@/api/document";
import TablePreview from "@/views/data-import/components/TablePreview.vue";
import type { TablePreviewLoader } from "@/views/data-import/components/TablePreview.vue";
import { normalizePreviewHeaders } from "@/views/data-import/components/table-preview-columns";
import type {
  BatchReplyTablePreviewResponse,
  BatchTableConfig
} from "@/api/matching";
import {
  applyExcelBatchTableRowFieldChange,
  normalizeExcelBatchTableRows
} from "./batchTableConfig.helpers";
import type { BatchTableConfigItem } from "./batchTableConfig.types";

const props = defineProps<{
  /** 所有可选表格 */
  tables: TableInfo[];
  /** 当前文件ID（用于刷新表头预览） */
  fileId?: number;
  /** 自定义表格预览加载器（用于非 documents 场景） */
  previewLoader?: TablePreviewLoader;
  /** 是否为 Excel 文件 */
  isExcel?: boolean;
  /** 目标表可选的来源表列表（批量回复目标配置使用） */
  sourceTableOptions?: Array<{
    value: number;
    label: string;
  }>;
  /** 来源表字段标签 */
  sourceTableLabel?: string;
  /** 是否显示映射预览操作 */
  mappingPreviewable?: boolean;
  /** 当前正在预览的表格索引 */
  mappingPreviewLoadingTableIndex?: number;
  /** 当前表格对应的预览结果 */
  mappingPreviewResults?: Record<number, BatchReplyTablePreviewResponse | null>;
  /** 当前配置（v-model） */
  modelValue: BatchTableConfigItem[];
}>();

const emit = defineEmits<{
  (e: "update:modelValue", value: BatchTableConfigItem[]): void;
  (e: "mapping-preview", value: BatchTableConfigItem): void;
}>();

type TablePreviewRef = {
  refresh?: (forceRefresh?: boolean) => Promise<void> | void;
} | null;

const items = computed({
  get: () => props.modelValue,
  set: val => emit("update:modelValue", val)
});

const activeSheetTab = ref("");

watch(
  items,
  value => {
    if (value.length === 0) {
      activeSheetTab.value = "";
      return;
    }

    const currentExists = value.some(
      item => String(item.tableIndex) === activeSheetTab.value
    );
    if (!currentExists) {
      activeSheetTab.value = String(value[0].tableIndex);
    }
  },
  { immediate: true, deep: true }
);

const isPrimaryExcelTable = (index: number) => !!props.isExcel && index === 0;
const isSecondaryExcelTable = (index: number) => !!props.isExcel && index > 0;
const customizedExcelTableMap = ref<Record<number, boolean>>({});

const applyPrimaryExcelConfigToOthers = (
  sourceItems: BatchTableConfigItem[]
) => {
  if (!props.isExcel || sourceItems.length < 2) {
    return sourceItems;
  }

  const primary = sourceItems[0];
  if (!primary) {
    return sourceItems;
  }

  return sourceItems.map((item, index) => {
    if (index === 0) {
      return item;
    }

    if (customizedExcelTableMap.value[item.tableIndex]) {
      return item;
    }

    const next: BatchTableConfigItem = {
      ...item,
      projectColumnIndex: primary.projectColumnIndex,
      specificationColumnIndex: primary.specificationColumnIndex,
      acceptanceColumnIndex: primary.acceptanceColumnIndex,
      remarkColumnIndex: primary.remarkColumnIndex,
      filterEmptySourceRows: primary.filterEmptySourceRows
    };

    const normalized = normalizeExcelBatchTableRows({
      ...next,
      headerRowStart: primary.headerRowStart,
      headerRowCount: primary.headerRowCount,
      dataStartRow: primary.dataStartRow
    });

    return {
      ...next,
      headerRowStart: normalized.headerRowStart,
      headerRowCount: normalized.headerRowCount,
      dataStartRow: normalized.dataStartRow
    };
  });
};

const markExcelTableAsCustomized = (index: number) => {
  if (!props.isExcel || index <= 0) {
    return;
  }

  const item = items.value[index];
  if (!item || customizedExcelTableMap.value[item.tableIndex]) {
    return;
  }

  customizedExcelTableMap.value = {
    ...customizedExcelTableMap.value,
    [item.tableIndex]: true
  };
};

/** 切换表格选中状态 */
const toggleSelect = (index: number) => {
  const updated = [...items.value];
  updated[index] = { ...updated[index], selected: !updated[index].selected };
  items.value = updated;
};

/** 更新某个表格的列索引 */
const updateField = (
  index: number,
  field: keyof BatchTableConfig,
  value: BatchTableConfig[keyof BatchTableConfig]
) => {
  markExcelTableAsCustomized(index);

  const updated = [...items.value];
  const mappingFields: Array<keyof BatchTableConfig> = [
    "projectColumnIndex",
    "specificationColumnIndex",
    "acceptanceColumnIndex",
    "remarkColumnIndex"
  ];
  updated[index] = {
    ...updated[index],
    [field]: value,
    ...(mappingFields.includes(field) ? { mappingAutoDetected: false } : {})
  };

  if (props.isExcel && index === 0) {
    items.value = applyPrimaryExcelConfigToOthers(updated);
    return;
  }

  items.value = updated;
};

/** 更新 Excel 行配置并做联动校正 */
const updateExcelRowField = (
  index: number,
  field: "headerRowStart" | "headerRowCount" | "dataStartRow",
  value: number | undefined
) => {
  const old = items.value[index];
  if (!old) return;
  if (value === undefined || value === null || Number.isNaN(Number(value)))
    return;

  markExcelTableAsCustomized(index);

  const oldNormalized = normalizeExcelBatchTableRows(old);
  const normalized = applyExcelBatchTableRowFieldChange(old, field, value);

  const changed =
    oldNormalized.headerRowStart !== normalized.headerRowStart ||
    oldNormalized.headerRowCount !== normalized.headerRowCount ||
    oldNormalized.dataStartRow !== normalized.dataStartRow;

  if (!changed) return;

  const updated = [...items.value];
  updated[index] = {
    ...old,
    headerRowStart: normalized.headerRowStart,
    headerRowCount: normalized.headerRowCount,
    dataStartRow: normalized.dataStartRow
  };

  const nextItems =
    props.isExcel && index === 0
      ? applyPrimaryExcelConfigToOthers(updated)
      : updated;

  items.value = nextItems;

  const clearedHeaders: Record<number, string[]> = {};
  nextItems.forEach(config => {
    clearedHeaders[config.tableIndex] = [];
  });
  previewHeadersMap.value = {
    ...previewHeadersMap.value,
    ...clearedHeaders
  };
};

const previewRefs = ref<Record<number, TablePreviewRef>>({});
const previewHeadersMap = ref<Record<number, string[]>>({});

const setPreviewRef = (tableIndex: number, el: TablePreviewRef) => {
  previewRefs.value[tableIndex] = el;
};

/** 按当前配置刷新预览，并由预览回传 headers 同步下拉 */
const refreshHeaders = async (index: number) => {
  const item = items.value[index];
  if (!item) return;

  previewHeadersMap.value = {
    ...previewHeadersMap.value,
    [item.tableIndex]: []
  };

  await previewRefs.value[item.tableIndex]?.refresh?.(true);
};

const getDisplayHeaders = (item: BatchTableConfigItem) => {
  const resolved = previewHeadersMap.value[item.tableIndex];
  if (resolved && resolved.length > 0) {
    return normalizePreviewHeaders({
      headers: resolved,
      columnCount: item.tableInfo.columnCount
    });
  }

  return normalizePreviewHeaders({
    headers: item.tableInfo.headers ?? [],
    columnCount: item.tableInfo.columnCount
  });
};

const getPreviewOptions = (item: BatchTableConfigItem) => {
  const normalized = normalizeExcelBatchTableRows(item);
  const usedStartRow = Math.max(1, item.tableInfo.usedRangeStartRow ?? 1);
  return {
    headerRowIndex: Math.max(0, normalized.headerRowStart - usedStartRow),
    headerRowCount: Math.max(
      1,
      normalized.headerRowCount === 0 ? 1 : normalized.headerRowCount
    ),
    dataStartRowIndex: Math.max(0, normalized.dataStartRow - usedStartRow)
  };
};

const getPreviewKey = (item: BatchTableConfigItem) => {
  const normalized = normalizeExcelBatchTableRows(item);
  return `${item.tableIndex}-${normalized.headerRowStart}-${normalized.headerRowCount}-${normalized.dataStartRow}`;
};

/** 预览加载完成后按 tableIndex 同步下拉表头，避免跨卡片错位 */
const handlePreviewLoaded = (tableIndex: number, data: TableData) => {
  previewHeadersMap.value = {
    ...previewHeadersMap.value,
    [tableIndex]: normalizePreviewHeaders({
      headers: data.headers || [],
      rows: data.rows,
      columnCount: data.columnCount
    })
  };
};

/** 构建表头下拉选项（索引 + 名称） */
const toExcelColumnLetter = (columnNumber: number) => {
  let current = Math.max(1, Math.floor(columnNumber));
  let result = "";
  while (current > 0) {
    current--;
    result = String.fromCharCode(65 + (current % 26)) + result;
    current = Math.floor(current / 26);
  }
  return result;
};

const getHeaderOptions = (item: BatchTableConfigItem) => {
  const headers = getDisplayHeaders(item);
  const usedStartColumn = Math.max(1, item.tableInfo.usedRangeStartColumn ?? 1);
  return headers.map((header, i) => ({
    value: i,
    label: `[${i}] 第 ${i + 1} 列 (${toExcelColumnLetter(usedStartColumn + i)}) ${header || `列${i + 1}`}`
  }));
};

/** 已选中的表格数量 */
const selectedCount = computed(
  () => items.value.filter(i => i.selected).length
);

/** 是否全选 */
const allSelected = computed(() => {
  return items.value.length > 0 && selectedCount.value === items.value.length;
});

/** 全选/取消全选 */
const toggleSelectAll = (val: boolean) => {
  items.value = items.value.map(item => ({ ...item, selected: val }));
};

const getPreviewResult = (tableIndex: number) => {
  return props.mappingPreviewResults?.[tableIndex] ?? null;
};
</script>

<template>
  <div class="batch-table-config workspace-shell">
    <div class="summary">
      <el-checkbox
        :model-value="allSelected"
        :indeterminate="selectedCount > 0 && !allSelected"
        @change="(v: string | number | boolean) => toggleSelectAll(Boolean(v))"
      >
        全选
      </el-checkbox>
      <span class="summary-text">
        已选择 <strong>{{ selectedCount }}</strong> / {{ tables.length }} 个表格
      </span>
      <span v-if="props.isExcel && items.length > 1" class="sync-tip">
        首表配置会作为默认值带出其他表，其他表仍可单独调整
      </span>
    </div>

    <el-tabs v-model="activeSheetTab" class="sheet-tabs">
      <el-tab-pane
        v-for="(item, idx) in items"
        :key="item.tableIndex"
        :name="String(item.tableIndex)"
        lazy
      >
        <template #label>
          <div class="sheet-tab-label">
            <span>{{
              item.tableInfo.name || `表格 ${item.tableIndex + 1}`
            }}</span>
            <el-tag
              size="small"
              :type="item.selected ? 'success' : 'info'"
              effect="plain"
            >
              {{ item.selected ? "参与" : "跳过" }}
            </el-tag>
          </div>
        </template>

        <div :class="['sheet-card', { selected: item.selected }]">
          <div class="card-header">
            <el-checkbox
              :model-value="item.selected"
              @change="toggleSelect(idx)"
            >
              <span class="table-name">
                {{ item.tableInfo.name || `表格 ${item.tableIndex + 1}` }}
              </span>
            </el-checkbox>
            <div class="card-actions">
              <el-tag
                v-if="isPrimaryExcelTable(idx)"
                size="small"
                type="primary"
              >
                主配置表
              </el-tag>
              <el-tag
                v-else-if="isSecondaryExcelTable(idx)"
                size="small"
                type="warning"
                effect="plain"
              >
                默认参考首表
              </el-tag>
              <el-tag size="small" type="info">
                {{ item.tableInfo.rowCount }} 行 x
                {{ item.tableInfo.columnCount }} 列
              </el-tag>
              <el-button
                v-if="props.mappingPreviewable && item.selected"
                type="primary"
                link
                size="small"
                :loading="
                  props.mappingPreviewLoadingTableIndex === item.tableIndex
                "
                @click="emit('mapping-preview', item)"
              >
                预览回写
              </el-button>
            </div>
          </div>

          <div
            v-if="getDisplayHeaders(item).length > 0"
            class="headers-preview"
          >
            <span class="label">表头：</span>
            <el-tag
              v-for="(h, hi) in getDisplayHeaders(item).slice(0, 8)"
              :key="hi"
              size="small"
              type="info"
              class="header-tag"
            >
              [{{ hi }}] {{ h || `列${hi + 1}` }}
            </el-tag>
            <span v-if="getDisplayHeaders(item).length > 8" class="more"
              >...</span
            >
          </div>

          <el-form label-width="90px" class="column-config" size="small">
            <div class="config-section">
              <div class="config-section__title">基础状态</div>
              <div v-if="!item.selected" class="row-config-hint">
                当前 Sheet/表格已取消参与，仍可继续调整配置后重新启用。
              </div>
              <div
                v-if="isSecondaryExcelTable(idx)"
                class="row-config-hint row-config-hint--sync"
              >
                当前表格已按首表带出默认配置，可单独调整且不会影响首表。
              </div>
            </div>

            <div
              v-if="props.fileId || props.previewLoader"
              class="table-preview-wrap"
            >
              <div class="preview-title">数据预览</div>
              <TablePreview
                :key="getPreviewKey(item)"
                :ref="
                  el => setPreviewRef(item.tableIndex, el as TablePreviewRef)
                "
                :file-id="props.fileId"
                :preview-loader="props.previewLoader"
                :table-index="item.tableIndex"
                :header-row-index="
                  props.isExcel ? getPreviewOptions(item).headerRowIndex : 0
                "
                :header-row-count="
                  props.isExcel ? getPreviewOptions(item).headerRowCount : 1
                "
                :data-start-row-index="
                  props.isExcel ? getPreviewOptions(item).dataStartRowIndex : 1
                "
                @loaded="data => handlePreviewLoaded(item.tableIndex, data)"
              />
            </div>

            <div v-if="props.isExcel" class="config-section config-narrow">
              <div class="config-section__title">行设置</div>
              <div class="row-config-title">行配置（1-based）</div>
              <div class="row-config-hint">
                已用区域首行：第
                {{ Math.max(1, item.tableInfo.usedRangeStartRow ?? 1) }} 行
              </div>
              <el-form-item label="表头起始行">
                <el-input-number
                  :model-value="
                    normalizeExcelBatchTableRows(item).headerRowStart
                  "
                  :min="1"
                  @update:model-value="
                    (v: number | undefined) =>
                      updateExcelRowField(idx, 'headerRowStart', v)
                  "
                />
              </el-form-item>
              <el-form-item label="表头行数">
                <el-input-number
                  :model-value="
                    normalizeExcelBatchTableRows(item).headerRowCount
                  "
                  :min="0"
                  @update:model-value="
                    (v: number | undefined) =>
                      updateExcelRowField(idx, 'headerRowCount', v)
                  "
                />
              </el-form-item>
              <el-form-item label="数据起始行">
                <el-input-number
                  :model-value="normalizeExcelBatchTableRows(item).dataStartRow"
                  :min="1"
                  @update:model-value="
                    (v: number | undefined) =>
                      updateExcelRowField(idx, 'dataStartRow', v)
                  "
                />
              </el-form-item>
              <el-form-item label="表头预览">
                <el-button size="small" @click="refreshHeaders(idx)">
                  按行配置刷新表头
                </el-button>
              </el-form-item>
            </div>

            <div class="config-section config-narrow">
              <div class="config-section__title">字段映射</div>
              <el-form-item
                v-if="
                  props.sourceTableOptions &&
                  props.sourceTableOptions.length > 0
                "
                :label="props.sourceTableLabel || '来源表'"
              >
                <el-select
                  :model-value="
                    item.sourceTableIndex ?? props.sourceTableOptions[0]?.value
                  "
                  placeholder="请选择来源表"
                  @change="
                    (v: number) => updateField(idx, 'sourceTableIndex', v)
                  "
                >
                  <el-option
                    v-for="opt in props.sourceTableOptions"
                    :key="opt.value"
                    :label="opt.label"
                    :value="opt.value"
                  />
                </el-select>
              </el-form-item>
              <el-form-item label="过滤空行">
                <el-switch
                  :model-value="item.filterEmptySourceRows ?? true"
                  active-text="开启"
                  inactive-text="关闭"
                  @change="
                    (v: string | number | boolean) =>
                      updateField(idx, 'filterEmptySourceRows', Boolean(v))
                  "
                />
              </el-form-item>
              <el-form-item label="项目列">
                <el-select
                  :model-value="item.projectColumnIndex"
                  placeholder="请选择项目列"
                  @change="
                    (v: number) => updateField(idx, 'projectColumnIndex', v)
                  "
                >
                  <el-option
                    v-for="opt in getHeaderOptions(item)"
                    :key="opt.value"
                    :label="opt.label"
                    :value="opt.value"
                  />
                </el-select>
              </el-form-item>
              <el-form-item label="规格列">
                <el-select
                  :model-value="item.specificationColumnIndex"
                  placeholder="请选择规格列"
                  @change="
                    (v: number) =>
                      updateField(idx, 'specificationColumnIndex', v)
                  "
                >
                  <el-option
                    v-for="opt in getHeaderOptions(item)"
                    :key="opt.value"
                    :label="opt.label"
                    :value="opt.value"
                  />
                </el-select>
              </el-form-item>
              <el-form-item label="验收列">
                <el-select
                  :model-value="item.acceptanceColumnIndex"
                  placeholder="请选择验收列"
                  @change="
                    (v: number) => updateField(idx, 'acceptanceColumnIndex', v)
                  "
                >
                  <el-option
                    v-for="opt in getHeaderOptions(item)"
                    :key="opt.value"
                    :label="opt.label"
                    :value="opt.value"
                  />
                </el-select>
              </el-form-item>
              <el-form-item label="备注列">
                <el-select
                  :model-value="item.remarkColumnIndex"
                  placeholder="请选择备注列（可选）"
                  clearable
                  @change="
                    (v: number) => updateField(idx, 'remarkColumnIndex', v)
                  "
                >
                  <el-option
                    v-for="opt in getHeaderOptions(item)"
                    :key="opt.value"
                    :label="opt.label"
                    :value="opt.value"
                  />
                </el-select>
              </el-form-item>
            </div>
          </el-form>

          <div v-if="getPreviewResult(item.tableIndex)" class="inline-preview">
            <el-alert
              :title="
                getPreviewResult(item.tableIndex)?.canApply
                  ? '当前 Sheet/表格可直接写回'
                  : '当前 Sheet/表格仍有问题待处理'
              "
              :type="
                getPreviewResult(item.tableIndex)?.canApply
                  ? 'success'
                  : 'warning'
              "
              :closable="false"
              show-icon
            />

            <div
              v-if="
                (getPreviewResult(item.tableIndex)?.errors?.length ?? 0) > 0
              "
              class="preview-errors"
            >
              <el-tag
                v-for="error in getPreviewResult(item.tableIndex)?.errors ?? []"
                :key="error"
                type="danger"
                effect="plain"
              >
                {{ error }}
              </el-tag>
            </div>

            <el-table
              v-if="(getPreviewResult(item.tableIndex)?.rows?.length ?? 0) > 0"
              :data="getPreviewResult(item.tableIndex)?.rows ?? []"
              border
              class="preview-table"
              max-height="420"
            >
              <el-table-column prop="rowIndex" label="目标行号" width="100" />
              <el-table-column prop="project" label="项目" min-width="160" />
              <el-table-column
                prop="specification"
                label="规格"
                min-width="180"
              />
              <el-table-column
                prop="acceptance"
                label="预览验收"
                min-width="180"
              />
              <el-table-column prop="remark" label="预览备注" min-width="180" />
            </el-table>
          </div>
        </div>
      </el-tab-pane>
    </el-tabs>
  </div>
</template>

<style scoped>
.batch-table-config {
  padding: 8px 0;
}

.workspace-shell {
  border-radius: 14px;
}

.summary {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  align-items: center;
  padding: 12px 14px;
  margin-bottom: 16px;
  font-size: 14px;
  color: #5d6d7e;
  background: #f8fafc;
  border: 1px solid #dbe3ec;
  border-radius: 12px;
}

.sync-tip {
  font-size: 12px;
  color: #8a6408;
}

.sheet-tabs {
  margin-top: 12px;
}

.sheet-tabs :deep(.el-tabs__nav-wrap::after) {
  height: 1px;
  background: #d9e1ea;
}

.sheet-tabs :deep(.el-tabs__item) {
  height: 38px;
  color: #60707f;
}

.sheet-tabs :deep(.el-tabs__item.is-active) {
  font-weight: 600;
  color: #173d73;
}

.sheet-tabs :deep(.el-tabs__active-bar) {
  background: #2f6bb2;
}

.sheet-tab-label {
  display: inline-flex;
  gap: 8px;
  align-items: center;
}

.sheet-card {
  padding: 18px;
  background: #fff;
  border: 1px solid #d9e2eb;
  border-radius: 16px;
  box-shadow: 0 6px 18px rgb(15 23 42 / 3%);
  transition:
    border-color 0.2s,
    box-shadow 0.2s;
}

.sheet-card.selected {
  border-color: #9fb7d6;
  box-shadow: 0 8px 22px rgb(47 107 178 / 8%);
}

.card-header {
  display: flex;
  gap: 12px;
  align-items: center;
  justify-content: space-between;
}

.card-actions {
  display: flex;
  gap: 8px;
  align-items: center;
}

.table-name {
  font-weight: 600;
}

.headers-preview {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
  align-items: center;
  margin-bottom: 12px;
}

.headers-preview .label {
  font-size: 12px;
  color: #909399;
}

.header-tag {
  font-family: monospace;
}

.more {
  font-size: 12px;
  color: #909399;
}

.column-config {
  display: grid;
  gap: 14px;
  width: 100%;
  margin-top: 12px;
}

.config-section {
  padding: 14px 16px;
  background: #fbfcfd;
  border: 1px solid #e0e7ef;
  border-radius: 12px;
}

.config-section__title {
  margin-bottom: 10px;
  font-size: 13px;
  font-weight: 700;
  color: #1f3349;
}

.row-config-title {
  margin-bottom: 8px;
  font-size: 12px;
  color: #606266;
}

.row-config-hint {
  margin-bottom: 10px;
  font-size: 12px;
  color: #909399;
}

.row-config-hint--sync {
  margin-bottom: 12px;
  color: #b45309;
}

.table-preview-wrap {
  box-sizing: border-box;
  width: 100%;
  min-width: 0;
  max-width: 100%;
  padding: 14px 16px;
  margin: 0;
  background: #fbfcfd;
  border: 1px solid #e0e7ef;
  border-radius: 12px;
}

.inline-preview {
  padding-top: 18px;
  margin-top: 20px;
  border-top: 1px solid #e2e8f0;
}

.preview-errors {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 16px;
}

.preview-table {
  margin-top: 16px;
}

.preview-title {
  margin-bottom: 8px;
  font-size: 13px;
  color: #606266;
}

.config-narrow {
  max-width: 460px;
}
</style>
