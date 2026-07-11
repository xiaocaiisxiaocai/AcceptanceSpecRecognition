<script setup lang="ts">
import { computed, ref, watch } from "vue";
import type { TableData } from "@/api/document";
import {
  applyExcelMappingRowFieldChange,
  buildExcelColumnOptions,
  toExcelColumnLetter,
  normalizeExcelMappingByTable
} from "../dataImport.helpers";

export type ExcelSheetMapping = {
  projectColumn?: number;
  specificationColumn?: number;
  acceptanceColumn?: number;
  remarkColumn?: number;
  headerRowStart: number;
  headerRowCount: number;
  dataStartRow: number;
  dataEndRow: number;
};

const props = defineProps<{
  modelValue?: ExcelSheetMapping;
  detectedMapping?: ExcelSheetMapping;
  previewData?: TableData | null;
  usedRangeStartRow?: number;
  usedRangeEndRow?: number;
  usedRangeStartColumn?: number;
}>();

const emit = defineEmits<{
  (e: "update:modelValue", value: ExcelSheetMapping): void;
}>();

const defaultMapping: ExcelSheetMapping = {
  projectColumn: undefined,
  specificationColumn: undefined,
  acceptanceColumn: undefined,
  remarkColumn: undefined,
  headerRowStart: 1,
  headerRowCount: 1,
  dataStartRow: 2,
  dataEndRow: 2
};

const mapping = ref<ExcelSheetMapping>({ ...defaultMapping });

function getTableInfoForNormalization() {
  const usedStartRow = Math.max(1, props.usedRangeStartRow || 1);
  const usedEndRow = Math.max(
    usedStartRow,
    props.usedRangeEndRow || usedStartRow
  );

  return {
    index: 0,
    name: "Excel",
    rowCount: usedEndRow - usedStartRow + 1,
    columnCount: 0,
    isNested: false,
    previewText: "",
    headers: [],
    hasMergedCells: false,
    usedRangeStartRow: usedStartRow,
    usedRangeStartColumn: Math.max(1, props.usedRangeStartColumn || 1)
  };
}

function buildNormalizedMapping(
  source?: Partial<ExcelSheetMapping>
): ExcelSheetMapping {
  return normalizeExcelMappingByTable(getTableInfoForNormalization(), {
    ...defaultMapping,
    ...(source || {})
  });
}

function isSameMappingValue(
  a?: Partial<ExcelSheetMapping> | null,
  b?: Partial<ExcelSheetMapping> | null
) {
  return (
    a?.projectColumn === b?.projectColumn &&
    a?.specificationColumn === b?.specificationColumn &&
    a?.acceptanceColumn === b?.acceptanceColumn &&
    a?.remarkColumn === b?.remarkColumn &&
    a?.headerRowStart === b?.headerRowStart &&
    a?.headerRowCount === b?.headerRowCount &&
    a?.dataStartRow === b?.dataStartRow &&
    a?.dataEndRow === b?.dataEndRow
  );
}

function normalize() {
  const normalized = buildNormalizedMapping(mapping.value);
  if (!isSameMappingValue(mapping.value, normalized)) {
    mapping.value = normalized;
  }
}

function handleRowFieldChange(
  field: "headerRowStart" | "headerRowCount" | "dataStartRow" | "dataEndRow",
  value: number | undefined
) {
  if (value === undefined || value === null || Number.isNaN(Number(value))) {
    return;
  }

  mapping.value = applyExcelMappingRowFieldChange(
    getTableInfoForNormalization(),
    mapping.value,
    field,
    value
  );
}

watch(
  () => props.modelValue,
  val => {
    const normalized = buildNormalizedMapping(val || {});
    if (!isSameMappingValue(mapping.value, normalized)) {
      mapping.value = normalized;
    }
  },
  { immediate: true, deep: true }
);

watch(
  () => mapping.value,
  val => {
    const normalized = buildNormalizedMapping(val || {});
    if (!isSameMappingValue(val, normalized)) {
      mapping.value = normalized;
      return;
    }

    if (!isSameMappingValue(normalized, props.modelValue || {})) {
      emit("update:modelValue", { ...normalized });
    }
  },
  { deep: true }
);

watch(
  () => props.usedRangeStartRow,
  () => {
    normalize();
  }
);

watch(
  () => props.usedRangeEndRow,
  () => {
    normalize();
  }
);

const columnOptions = computed(() =>
  buildExcelColumnOptions(getTableInfoForNormalization(), props.previewData)
);

const columnHint = computed(() => ({
  project: toExcelColumnLetter(mapping.value.projectColumn),
  spec: toExcelColumnLetter(mapping.value.specificationColumn),
  acceptance: toExcelColumnLetter(mapping.value.acceptanceColumn),
  remark: toExcelColumnLetter(mapping.value.remarkColumn)
}));

function applyDetectedFieldMapping() {
  if (!props.detectedMapping) {
    return;
  }

  mapping.value = buildNormalizedMapping(props.detectedMapping);
}
</script>

<template>
  <div class="excel-mapping">
    <el-alert
      type="info"
      :closable="false"
      title="Excel 导入按列序号配置（1-based）"
      :description="`第 1 列 = A；表头可跨多行；合并单元格会在预览与导入时展开。${props.usedRangeStartRow || props.usedRangeStartColumn ? ` 已用区域起点：行 ${props.usedRangeStartRow ?? '-'}，列 ${props.usedRangeStartColumn ?? '-'}（${toExcelColumnLetter(props.usedRangeStartColumn)}）` : ''}`"
      show-icon
    />

    <div class="grid">
      <div class="group">
        <div class="group-title-row">
          <div class="group-title">行范围</div>
          <el-button
            size="small"
            type="primary"
            plain
            :disabled="!props.detectedMapping"
            @click="applyDetectedFieldMapping"
          >
            重置为识别结果
          </el-button>
        </div>
        <el-form label-width="110px">
          <el-form-item label="表头起始行">
            <el-input-number
              :model-value="mapping.headerRowStart"
              :min="Math.max(1, props.usedRangeStartRow || 1)"
              :step="1"
              @update:model-value="
                (v: number | undefined) =>
                  handleRowFieldChange('headerRowStart', v)
              "
            />
          </el-form-item>
          <el-form-item label="表头行数">
            <el-input-number
              :model-value="mapping.headerRowCount"
              :min="0"
              :step="1"
              @update:model-value="
                (v: number | undefined) =>
                  handleRowFieldChange('headerRowCount', v)
              "
            />
          </el-form-item>
          <el-form-item label="数据起始行">
            <el-input-number
              :model-value="mapping.dataStartRow"
              :min="Math.max(1, props.usedRangeStartRow || 1)"
              :step="1"
              @update:model-value="
                (v: number | undefined) =>
                  handleRowFieldChange('dataStartRow', v)
              "
            />
          </el-form-item>
          <el-form-item label="数据结束行">
            <el-input-number
              :model-value="mapping.dataEndRow"
              :min="
                Math.max(mapping.dataStartRow, props.usedRangeStartRow || 1)
              "
              :max="props.usedRangeEndRow"
              :step="1"
              @update:model-value="
                (v: number | undefined) => handleRowFieldChange('dataEndRow', v)
              "
            />
          </el-form-item>
        </el-form>
      </div>

      <div class="group">
        <div class="group-title">列映射</div>
        <el-form label-width="110px">
          <el-form-item label="项目列（必填）">
            <div class="col-select-row">
              <el-select
                v-model="mapping.projectColumn"
                class="column-select"
                placeholder="请选择项目列"
                clearable
                filterable
              >
                <el-option
                  v-for="opt in columnOptions"
                  :key="opt.value"
                  :label="opt.label"
                  :value="opt.value"
                />
              </el-select>
              <span class="col-letter">{{ columnHint.project }}</span>
            </div>
          </el-form-item>
          <el-form-item label="规格列（必填）">
            <div class="col-select-row">
              <el-select
                v-model="mapping.specificationColumn"
                class="column-select"
                placeholder="请选择规格列"
                clearable
                filterable
              >
                <el-option
                  v-for="opt in columnOptions"
                  :key="opt.value"
                  :label="opt.label"
                  :value="opt.value"
                />
              </el-select>
              <span class="col-letter">{{ columnHint.spec }}</span>
            </div>
          </el-form-item>
          <el-form-item label="验收列（可选）">
            <div class="col-select-row">
              <el-select
                v-model="mapping.acceptanceColumn"
                class="column-select"
                placeholder="请选择验收列"
                clearable
                filterable
              >
                <el-option
                  v-for="opt in columnOptions"
                  :key="opt.value"
                  :label="opt.label"
                  :value="opt.value"
                />
              </el-select>
              <span class="col-letter">{{ columnHint.acceptance }}</span>
            </div>
          </el-form-item>
          <el-form-item label="备注列（可选）">
            <div class="col-select-row">
              <el-select
                v-model="mapping.remarkColumn"
                class="column-select"
                placeholder="请选择备注列"
                clearable
                filterable
              >
                <el-option
                  v-for="opt in columnOptions"
                  :key="opt.value"
                  :label="opt.label"
                  :value="opt.value"
                />
              </el-select>
              <span class="col-letter">{{ columnHint.remark }}</span>
            </div>
          </el-form-item>
        </el-form>
      </div>
    </div>
  </div>
</template>

<style scoped>
.excel-mapping {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
}

.group {
  padding: 12px;
  background: var(--app-bg-card);
  border: 1px solid var(--app-border);
  border-radius: 12px;
}

.group-title {
  margin-bottom: 8px;
  font-size: 14px;
  font-weight: 600;
  color: var(--color-text);
}

.group-title-row {
  display: flex;
  gap: 8px;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 8px;
}

.group-title-row .group-title {
  margin-bottom: 0;
}

.col-select-row {
  display: flex;
  gap: 8px;
  align-items: center;
}

.column-select {
  width: 260px;
}

.col-letter {
  font-family:
    ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, "Liberation Mono",
    "Courier New", monospace;
  color: var(--app-text-secondary);
}

@media (width <= 960px) {
  .grid {
    grid-template-columns: 1fr;
  }
}
</style>
