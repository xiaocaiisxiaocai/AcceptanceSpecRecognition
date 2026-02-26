<script setup lang="ts">
import { computed } from "vue";
import type { TableInfo } from "@/api/document";
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
  /** 当前配置（v-model） */
  modelValue: BatchTableConfigItem[];
}>();

const emit = defineEmits<{
  (e: "update:modelValue", value: BatchTableConfigItem[]): void;
}>();

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
  value: number | undefined
) => {
  const updated = [...items.value];
  updated[index] = { ...updated[index], [field]: value };
  items.value = updated;
};

/** 已选中的表格数量 */
const selectedCount = computed(() => items.value.filter((i) => i.selected).length);
</script>

<template>
  <div class="batch-table-config">
    <div class="summary">
      已选择 <strong>{{ selectedCount }}</strong> / {{ tables.length }} 个表格
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
            <el-tag size="small" type="info">
              {{ item.tableInfo.rowCount }} 行 x {{ item.tableInfo.columnCount }} 列
            </el-tag>
          </div>
        </template>

        <div v-if="item.tableInfo.headers.length > 0" class="headers-preview">
          <span class="label">表头：</span>
          <el-tag
            v-for="(h, hi) in item.tableInfo.headers.slice(0, 8)"
            :key="hi"
            size="small"
            type="info"
            class="header-tag"
          >
            [{{ hi }}] {{ h }}
          </el-tag>
          <span v-if="item.tableInfo.headers.length > 8" class="more">...</span>
        </div>

        <el-form
          v-if="item.selected"
          label-width="110px"
          class="column-config"
          size="small"
        >
          <el-form-item label="项目列索引">
            <el-input-number
              :model-value="item.projectColumnIndex"
              :min="0"
              :max="50"
              @change="(v: number | undefined) => updateField(idx, 'projectColumnIndex', v)"
            />
          </el-form-item>
          <el-form-item label="规格列索引">
            <el-input-number
              :model-value="item.specificationColumnIndex"
              :min="0"
              :max="50"
              @change="(v: number | undefined) => updateField(idx, 'specificationColumnIndex', v)"
            />
          </el-form-item>
          <el-form-item label="验收列索引">
            <el-input-number
              :model-value="item.acceptanceColumnIndex"
              :min="0"
              :max="50"
              @change="(v: number | undefined) => updateField(idx, 'acceptanceColumnIndex', v)"
            />
          </el-form-item>
          <el-form-item label="备注列索引">
            <el-input-number
              :model-value="item.remarkColumnIndex"
              :min="0"
              :max="50"
              @change="(v: number | undefined) => updateField(idx, 'remarkColumnIndex', v)"
            />
          </el-form-item>
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
  max-width: 400px;
}
</style>
