<script setup lang="ts">
import { computed, ref, watch } from "vue";

export type ExcelSheetMapping = {
  projectColumn?: number;
  specificationColumn?: number;
  acceptanceColumn?: number;
  remarkColumn?: number;
  headerRowStart: number;
  headerRowCount: number;
  dataStartRow: number;
};

const props = defineProps<{
  modelValue?: ExcelSheetMapping;
  usedRangeStartRow?: number;
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
  dataStartRow: 2
};

const mapping = ref<ExcelSheetMapping>({ ...defaultMapping });
function buildNormalizedMapping(source?: Partial<ExcelSheetMapping>): ExcelSheetMapping {
  const merged: ExcelSheetMapping = {
    ...defaultMapping,
    ...(source || {})
  };

  const baseRow = Math.max(1, props.usedRangeStartRow || 1);
  const headerRowStart = Math.max(
    baseRow,
    merged.headerRowStart || baseRow
  );
  const headerRowCount = Math.max(0, merged.headerRowCount ?? 1);
  const dataStartRow = Math.max(baseRow, merged.dataStartRow || baseRow);

  return {
    ...merged,
    headerRowStart,
    headerRowCount,
    dataStartRow:
    dataStartRow < headerRowStart + headerRowCount
      ? headerRowStart + headerRowCount
      : dataStartRow
  };
}

function isSameMapping(
  a?: Partial<ExcelSheetMapping> | null,
  b?: Partial<ExcelSheetMapping> | null
) {
  const aa = buildNormalizedMapping(a || {});
  const bb = buildNormalizedMapping(b || {});
  return (
    aa.projectColumn === bb.projectColumn &&
    aa.specificationColumn === bb.specificationColumn &&
    aa.acceptanceColumn === bb.acceptanceColumn &&
    aa.remarkColumn === bb.remarkColumn &&
    aa.headerRowStart === bb.headerRowStart &&
    aa.headerRowCount === bb.headerRowCount &&
    aa.dataStartRow === bb.dataStartRow
  );
}

function normalize() {
  const normalized = buildNormalizedMapping(mapping.value);
  if (!isSameMapping(mapping.value, normalized)) {
    mapping.value = normalized;
  }
}

watch(
  () => props.modelValue,
  (val) => {
    const normalized = buildNormalizedMapping(val || {});
    if (!isSameMapping(mapping.value, normalized)) {
      mapping.value = normalized;
    }
  },
  { immediate: true, deep: true }
);

watch(
  () => mapping.value,
  (val) => {
    const normalized = buildNormalizedMapping(val || {});
    if (!isSameMapping(val, normalized)) {
      mapping.value = normalized;
      return;
    }

    if (!isSameMapping(normalized, props.modelValue || {})) {
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

const colLetter = (n?: number) => {
  if (!n || n <= 0) return "";
  let x = n;
  let s = "";
  while (x > 0) {
    x -= 1;
    s = String.fromCharCode(65 + (x % 26)) + s;
    x = Math.floor(x / 26);
  }
  return s;
};

const columnHint = computed(() => ({
  project: colLetter(mapping.value.projectColumn),
  spec: colLetter(mapping.value.specificationColumn),
  acceptance: colLetter(mapping.value.acceptanceColumn),
  remark: colLetter(mapping.value.remarkColumn)
}));
</script>

<template>
  <div class="excel-mapping">
    <el-alert
      type="info"
      :closable="false"
      title="Excel 导入按列序号配置（1-based）"
      :description="`第 1 列 = A；表头可跨多行；合并单元格会在预览与导入时展开。${props.usedRangeStartRow || props.usedRangeStartColumn ? ` 已用区域起点：行 ${props.usedRangeStartRow ?? '-'}，列 ${props.usedRangeStartColumn ?? '-'}（${colLetter(props.usedRangeStartColumn)}）` : ''}`"
      show-icon
    />

    <div class="grid">
      <div class="group">
        <div class="group-title">行范围</div>
        <el-form label-width="110px">
          <el-form-item label="表头起始行">
            <el-input-number
              v-model="mapping.headerRowStart"
              :min="Math.max(1, props.usedRangeStartRow || 1)"
              :step="1"
              @change="normalize"
            />
          </el-form-item>
          <el-form-item label="表头行数">
            <el-input-number
              v-model="mapping.headerRowCount"
              :min="0"
              :step="1"
              @change="normalize"
            />
          </el-form-item>
          <el-form-item label="数据起始行">
            <el-input-number
              v-model="mapping.dataStartRow"
              :min="Math.max(1, props.usedRangeStartRow || 1)"
              :step="1"
              @change="normalize"
            />
          </el-form-item>
        </el-form>
      </div>

      <div class="group">
        <div class="group-title">列映射</div>
        <el-form label-width="110px">
          <el-form-item label="项目列（必填）">
            <div class="col-input">
              <el-input-number v-model="mapping.projectColumn" :min="1" :step="1" />
              <span class="col-letter">{{ columnHint.project }}</span>
            </div>
          </el-form-item>
          <el-form-item label="规格列（必填）">
            <div class="col-input">
              <el-input-number
                v-model="mapping.specificationColumn"
                :min="1"
                :step="1"
              />
              <span class="col-letter">{{ columnHint.spec }}</span>
            </div>
          </el-form-item>
          <el-form-item label="验收列（可选）">
            <div class="col-input">
              <el-input-number v-model="mapping.acceptanceColumn" :min="1" :step="1" />
              <span class="col-letter">{{ columnHint.acceptance }}</span>
            </div>
          </el-form-item>
          <el-form-item label="备注列（可选）">
            <div class="col-input">
              <el-input-number v-model="mapping.remarkColumn" :min="1" :step="1" />
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
  border: 1px solid #ede7f6;
  border-radius: 12px;
  padding: 12px;
  background: #ffffff;
}

.group-title {
  font-size: 14px;
  font-weight: 600;
  margin-bottom: 8px;
  color: var(--color-text);
}

.col-input {
  display: flex;
  align-items: center;
  gap: 8px;
}

.col-letter {
  font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas,
    "Liberation Mono", "Courier New", monospace;
  color: #6b7280;
}

@media (max-width: 960px) {
  .grid {
    grid-template-columns: 1fr;
  }
}
</style>
