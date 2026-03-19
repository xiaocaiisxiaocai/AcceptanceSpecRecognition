<script setup lang="ts">
import { computed, ref } from "vue";
import { ElMessage } from "element-plus";
import type { TableData, TableInfo } from "@/api/document";
import TablePreview from "@/views/data-import/components/TablePreview.vue";
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
  /** 是否为 Excel 文件 */
  isExcel?: boolean;
  /** 当前配置（v-model） */
  modelValue: BatchTableConfigItem[];
}>();

const emit = defineEmits<{
  (e: "update:modelValue", value: BatchTableConfigItem[]): void;
}>();

type TablePreviewRef = {
  refresh?: () => Promise<void> | void;
} | null;

type ConfigClipboard = {
  sourceTableIndex: number;
  payload: Pick<
    BatchTableConfig,
    | "projectColumnIndex"
    | "specificationColumnIndex"
    | "acceptanceColumnIndex"
    | "remarkColumnIndex"
    | "headerRowStart"
    | "headerRowCount"
    | "dataStartRow"
    | "filterEmptySourceRows"
  >;
};

const items = computed({
  get: () => props.modelValue,
  set: (val) => emit("update:modelValue", val)
});

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
  const updated = [...items.value];
  updated[index] = { ...updated[index], [field]: value };
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
  items.value = updated;
  previewHeadersMap.value = {
    ...previewHeadersMap.value,
    [draft.tableIndex]: []
  };
};

const previewRefs = ref<Record<number, TablePreviewRef>>({});
const previewHeadersMap = ref<Record<number, string[]>>({});
const configClipboard = ref<ConfigClipboard | null>(null);

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

/** 复制某个表格配置 */
const copyTableConfig = (index: number) => {
  const item = items.value[index];
  if (!item) return;

  configClipboard.value = {
    sourceTableIndex: item.tableIndex,
    payload: {
      projectColumnIndex: item.projectColumnIndex,
      specificationColumnIndex: item.specificationColumnIndex,
      acceptanceColumnIndex: item.acceptanceColumnIndex,
      remarkColumnIndex: item.remarkColumnIndex,
      headerRowStart: item.headerRowStart,
      headerRowCount: item.headerRowCount,
      dataStartRow: item.dataStartRow,
      filterEmptySourceRows: item.filterEmptySourceRows
    }
  };

  ElMessage.success(`已复制表格 ${item.tableIndex + 1} 的配置`);
};

/** 粘贴配置到其他表格 */
const pasteConfigToOthers = () => {
  if (!configClipboard.value) {
    ElMessage.warning("请先复制一个表格配置");
    return;
  }

  const { sourceTableIndex, payload } = configClipboard.value;
  const updated = [...items.value];
  let pastedCount = 0;

  for (let i = 0; i < updated.length; i++) {
    const item = updated[i];
    if (!item || item.tableIndex === sourceTableIndex) continue;

    const next: BatchTableConfigItem = {
      ...item,
      projectColumnIndex: payload.projectColumnIndex,
      specificationColumnIndex: payload.specificationColumnIndex,
      acceptanceColumnIndex: payload.acceptanceColumnIndex,
      remarkColumnIndex: payload.remarkColumnIndex,
      filterEmptySourceRows: payload.filterEmptySourceRows
    };

    if (props.isExcel) {
      const normalized = normalizeExcelRows({
        ...next,
        headerRowStart: payload.headerRowStart,
        headerRowCount: payload.headerRowCount,
        dataStartRow: payload.dataStartRow
      });
      next.headerRowStart = normalized.headerRowStart;
      next.headerRowCount = normalized.headerRowCount;
      next.dataStartRow = normalized.dataStartRow;
    }

    updated[i] = next;
    previewHeadersMap.value = {
      ...previewHeadersMap.value,
      [item.tableIndex]: []
    };
    pastedCount++;
  }

  if (pastedCount === 0) {
    ElMessage.warning("没有可粘贴的其他表格");
    return;
  }

  items.value = updated;
  ElMessage.success(`已粘贴到 ${pastedCount} 个其他表格`);
};

/** 已选中的表格数量 */
const selectedCount = computed(() => items.value.filter((i) => i.selected).length);
const hasClipboard = computed(() => configClipboard.value !== null);
const clipboardSourceText = computed(() => {
  if (!configClipboard.value) return "";
  return `已复制表格 ${configClipboard.value.sourceTableIndex + 1} 配置`;
});

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
      <div class="summary-actions">
        <el-button
          size="small"
          type="primary"
          plain
          :disabled="!hasClipboard || items.length < 2"
          @click="pasteConfigToOthers"
        >
          粘贴到其他表格
        </el-button>
        <span v-if="hasClipboard" class="clipboard-tip">
          {{ clipboardSourceText }}
        </span>
      </div>
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
              <el-button
                size="small"
                text
                type="primary"
                @click.stop="copyTableConfig(idx)"
              >
                复制此表配置
              </el-button>
              <el-tag size="small" type="info">
                {{ item.tableInfo.rowCount }} 行 x {{ item.tableInfo.columnCount }} 列
              </el-tag>
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
          v-if="item.selected"
          label-width="90px"
          class="column-config"
          size="small"
        >
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

          <div v-if="props.fileId" class="table-preview-wrap">
            <div class="preview-title">数据预览</div>
            <TablePreview
              :key="getPreviewKey(item)"
              :ref="(el) => setPreviewRef(item.tableIndex, el as TablePreviewRef)"
              :file-id="props.fileId"
              :table-index="item.tableIndex"
              :header-row-index="props.isExcel ? getPreviewOptions(item).headerRowIndex : 0"
              :header-row-count="props.isExcel ? getPreviewOptions(item).headerRowCount : 1"
              :data-start-row-index="props.isExcel ? getPreviewOptions(item).dataStartRowIndex : 1"
              @loaded="(data) => handlePreviewLoaded(item.tableIndex, data)"
            />
          </div>

          <div class="config-narrow">
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

.summary-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}

.clipboard-tip {
  font-size: 12px;
  color: #6b7280;
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
