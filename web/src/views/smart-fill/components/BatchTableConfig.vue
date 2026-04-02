<script setup lang="ts">
import { computed, ref } from "vue";
import type { TableData, TableInfo } from "@/api/document";
import TablePreview from "@/views/data-import/components/TablePreview.vue";
import type { TablePreviewLoader } from "@/views/data-import/components/TablePreview.vue";
import type { BatchTableConfig } from "@/api/matching";

/** 带勾选状态的表格配置项 */
export interface BatchTableConfigItem extends BatchTableConfig {
  /** 是否被选中 */
  selected: boolean;
  /** 表格信息（用于展示） */
  tableInfo: TableInfo;
}

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
  /** 当前配置（v-model） */
  modelValue: BatchTableConfigItem[];
}>();

const emit = defineEmits<{
  (e: "update:modelValue", value: BatchTableConfigItem[]): void;
  (e: "mapping-preview", value: BatchTableConfigItem): void;
}>();

type TablePreviewRef = {
  refresh?: () => Promise<void> | void;
} | null;

const items = computed({
  get: () => props.modelValue,
  set: (val) => emit("update:modelValue", val)
});

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

    const normalized = normalizeExcelRows({
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
  updated[index] = { ...updated[index], [field]: value };

  if (props.isExcel && index === 0) {
    items.value = applyPrimaryExcelConfigToOthers(updated);
    return;
  }

  items.value = updated;
};

/** 归一化 Excel 行配置 */
const normalizeExcelRows = (item: BatchTableConfigItem) => {
  const usedStartRow = Math.max(1, item.tableInfo.usedRangeStartRow ?? 1);
  const headerRowStart = Math.max(usedStartRow, item.headerRowStart ?? usedStartRow);
  const headerRowCount = Math.max(0, item.headerRowCount ?? 1);
  const minDataStartRow = headerRowStart + headerRowCount;
  const dataStartRow = Math.max(minDataStartRow, item.dataStartRow ?? minDataStartRow);

  return {
    usedStartRow,
    headerRowStart,
    headerRowCount,
    dataStartRow
  };
};

/** 更新 Excel 行配置并做联动校正 */
const updateExcelRowField = (
  index: number,
  field: "headerRowStart" | "headerRowCount" | "dataStartRow",
  value: number | undefined
) => {
  const old = items.value[index];
  if (!old) return;
  if (value === undefined || value === null || Number.isNaN(Number(value))) return;

  markExcelTableAsCustomized(index);

  const oldNormalized = normalizeExcelRows(old);
  const draft: BatchTableConfigItem = { ...old, [field]: value };
  const normalized = normalizeExcelRows(draft);

  const changed =
    oldNormalized.headerRowStart !== normalized.headerRowStart ||
    oldNormalized.headerRowCount !== normalized.headerRowCount ||
    oldNormalized.dataStartRow !== normalized.dataStartRow;

  if (!changed) return;

  const updated = [...items.value];
  updated[index] = {
    ...draft,
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
  nextItems.forEach((config) => {
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

  await previewRefs.value[item.tableIndex]?.refresh?.();
};

const getDisplayHeaders = (item: BatchTableConfigItem) => {
  const resolved = previewHeadersMap.value[item.tableIndex];
  if (resolved && resolved.length > 0) return resolved;
  return item.tableInfo.headers ?? [];
};

const getPreviewOptions = (item: BatchTableConfigItem) => {
  const normalized = normalizeExcelRows(item);
  return {
    headerRowIndex: Math.max(0, normalized.headerRowStart - normalized.usedStartRow),
    headerRowCount: Math.max(1, normalized.headerRowCount === 0 ? 1 : normalized.headerRowCount),
    dataStartRowIndex: Math.max(0, normalized.dataStartRow - normalized.usedStartRow)
  };
};

const getPreviewKey = (item: BatchTableConfigItem) => {
  const normalized = normalizeExcelRows(item);
  return `${item.tableIndex}-${normalized.headerRowStart}-${normalized.headerRowCount}-${normalized.dataStartRow}`;
};

/** 预览加载完成后按 tableIndex 同步下拉表头，避免跨卡片错位 */
const handlePreviewLoaded = (tableIndex: number, data: TableData) => {
  previewHeadersMap.value = {
    ...previewHeadersMap.value,
    [tableIndex]: data.headers || []
  };
};

/** 构建表头下拉选项（索引 + 名称） */
const getHeaderOptions = (headers: string[]) => {
  return headers.map((header, i) => ({
    value: i,
    label: `[${i}] ${header || `列${i + 1}`}`
  }));
};

/** 已选中的表格数量 */
const selectedCount = computed(() => items.value.filter((i) => i.selected).length);

/** 是否全选 */
const allSelected = computed(() => {
  return items.value.length > 0 && selectedCount.value === items.value.length;
});

/** 全选/取消全选 */
const toggleSelectAll = (val: boolean) => {
  items.value = items.value.map((item) => ({ ...item, selected: val }));
};

</script>

<template>
  <div class="batch-table-config">
    <div class="summary">
      <el-checkbox
        :model-value="allSelected"
        :indeterminate="selectedCount > 0 && !allSelected"
        @change="(v: any) => toggleSelectAll(!!v)"
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

    <div class="table-cards">
      <el-card
        v-for="(item, idx) in items"
        :key="item.tableIndex"
        :class="['table-card', { selected: item.selected }]"
        shadow="hover"
      >
        <template #header>
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
              <el-tag v-if="isPrimaryExcelTable(idx)" size="small" type="primary">
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
                {{ item.tableInfo.rowCount }} 行 x {{ item.tableInfo.columnCount }} 列
              </el-tag>
              <el-button
                v-if="props.mappingPreviewable && item.selected"
                type="primary"
                link
                size="small"
                :loading="props.mappingPreviewLoadingTableIndex === item.tableIndex"
                @click="emit('mapping-preview', item)"
              >
                预览回写
              </el-button>
            </div>
          </div>
        </template>

        <div v-if="getDisplayHeaders(item).length > 0" class="headers-preview">
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
          <span v-if="getDisplayHeaders(item).length > 8" class="more">...</span>
        </div>

        <el-form
          v-if="item.selected || isPrimaryExcelTable(idx)"
          label-width="90px"
          class="column-config"
          size="small"
        >
          <div v-if="isSecondaryExcelTable(idx)" class="row-config-hint row-config-hint--sync">
            当前表格已按首表带出默认配置，可单独调整且不会影响首表。
          </div>
          <div v-if="props.isExcel" class="config-narrow">
            <div class="row-config-title">行配置（1-based）</div>
            <div class="row-config-hint">
              已用区域首行：第 {{ normalizeExcelRows(item).usedStartRow }} 行
            </div>
            <el-form-item label="表头起始行">
              <el-input-number
                :model-value="normalizeExcelRows(item).headerRowStart"
                :min="1"
                controls-position="right"
                @update:model-value="(v: number | undefined) => updateExcelRowField(idx, 'headerRowStart', v)"
              />
            </el-form-item>
            <el-form-item label="表头行数">
              <el-input-number
                :model-value="normalizeExcelRows(item).headerRowCount"
                :min="0"
                controls-position="right"
                @update:model-value="(v: number | undefined) => updateExcelRowField(idx, 'headerRowCount', v)"
              />
            </el-form-item>
            <el-form-item label="数据起始行">
              <el-input-number
                :model-value="normalizeExcelRows(item).dataStartRow"
                :min="1"
                controls-position="right"
                @update:model-value="(v: number | undefined) => updateExcelRowField(idx, 'dataStartRow', v)"
              />
            </el-form-item>
            <el-form-item label="表头预览">
              <el-button
                size="small"
                @click="refreshHeaders(idx)"
              >
                按行配置刷新表头
              </el-button>
            </el-form-item>
          </div>

          <div v-if="props.fileId || props.previewLoader" class="table-preview-wrap">
            <div class="preview-title">数据预览</div>
            <TablePreview
              :key="getPreviewKey(item)"
              :ref="(el) => setPreviewRef(item.tableIndex, el as TablePreviewRef)"
              :file-id="props.fileId"
              :preview-loader="props.previewLoader"
              :table-index="item.tableIndex"
              :header-row-index="props.isExcel ? getPreviewOptions(item).headerRowIndex : 0"
              :header-row-count="props.isExcel ? getPreviewOptions(item).headerRowCount : 1"
              :data-start-row-index="props.isExcel ? getPreviewOptions(item).dataStartRowIndex : 1"
              @loaded="(data) => handlePreviewLoaded(item.tableIndex, data)"
            />
          </div>

          <div class="config-narrow">
            <el-form-item
              v-if="props.sourceTableOptions && props.sourceTableOptions.length > 0"
              :label="props.sourceTableLabel || '来源表'"
            >
              <el-select
                :model-value="item.sourceTableIndex ?? props.sourceTableOptions[0]?.value"
                placeholder="请选择来源表"
                @change="(v: number) => updateField(idx, 'sourceTableIndex', v)"
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
                @change="(v: boolean) => updateField(idx, 'filterEmptySourceRows', v)"
              />
            </el-form-item>
            <el-form-item label="项目列">
              <el-select
                :model-value="item.projectColumnIndex"
                placeholder="请选择项目列"
                @change="(v: number) => updateField(idx, 'projectColumnIndex', v)"
              >
                <el-option
                  v-for="opt in getHeaderOptions(getDisplayHeaders(item))"
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
                @change="(v: number) => updateField(idx, 'specificationColumnIndex', v)"
              >
                <el-option
                  v-for="opt in getHeaderOptions(getDisplayHeaders(item))"
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
                @change="(v: number) => updateField(idx, 'acceptanceColumnIndex', v)"
              >
                <el-option
                  v-for="opt in getHeaderOptions(getDisplayHeaders(item))"
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
                @change="(v: number) => updateField(idx, 'remarkColumnIndex', v)"
              >
                <el-option
                  v-for="opt in getHeaderOptions(getDisplayHeaders(item))"
                  :key="opt.value"
                  :label="opt.label"
                  :value="opt.value"
                />
              </el-select>
            </el-form-item>
          </div>
        </el-form>
      </el-card>
    </div>
  </div>
</template>

<style scoped>
.batch-table-config {
  padding: 8px 0;
}

.summary {
  margin-bottom: 16px;
  font-size: 14px;
  color: #606266;
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
}

.sync-tip {
  font-size: 12px;
  color: #7c3aed;
}

.table-cards {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.table-card {
  border: 2px solid transparent;
  transition: border-color 0.2s;
}

.table-card.selected {
  border-color: var(--el-color-primary);
}

.card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.card-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}

.table-name {
  font-weight: 600;
}

.headers-preview {
  margin-bottom: 12px;
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 4px;
}

.headers-preview .label {
  font-size: 12px;
  color: #909399;
}

.header-tag {
  font-family: monospace;
}

.more {
  color: #909399;
  font-size: 12px;
}

.column-config {
  margin-top: 12px;
  width: 100%;
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
  margin: 12px 0 8px;
  width: 100%;
}

.preview-title {
  margin-bottom: 8px;
  font-size: 13px;
  color: #606266;
}

.config-narrow {
  max-width: 420px;
}
</style>
