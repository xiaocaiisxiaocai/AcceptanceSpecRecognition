<script setup lang="ts">
import { ref, computed, watch } from "vue";
import type { ColumnMapping, TableData } from "@/api/document";

const props = defineProps<{
  tableData: TableData | null;
  modelValue?: ColumnMapping;
}>();

const emit = defineEmits<{
  (e: "update:modelValue", value: ColumnMapping): void;
}>();

// 默认映射配置
const defaultMapping: ColumnMapping = {
  projectColumn: undefined,
  specificationColumn: undefined,
  acceptanceColumn: undefined,
  remarkColumn: undefined,
  headerRowIndex: 0,
  dataStartRowIndex: 1
};

const mapping = ref<ColumnMapping>({ ...defaultMapping });

// 列选项
const columnOptions = computed(() => {
  if (!props.tableData) return [];
  return props.tableData.headers.map((header, index) => ({
    value: index,
    label: header || `列${index + 1}`
  }));
});

// 同步modelValue
watch(
  () => props.modelValue,
  (val) => {
    if (val) {
      // 合并默认值，避免缺失字段导致UI异常
      mapping.value = { ...defaultMapping, ...val };
    }
  },
  { immediate: true }
);

// 触发更新
const updateMapping = () => {
  emit("update:modelValue", { ...mapping.value });
};

// 重置映射
const resetMapping = () => {
  mapping.value = { ...defaultMapping };
  updateMapping();
};

// 暴露方法
defineExpose({
  resetMapping
});
</script>

<template>
  <div class="column-mapping">
    <div class="mapping-header">
      <span class="title">列映射配置</span>
      <div class="actions">
        <el-button size="small" @click="resetMapping">重置</el-button>
      </div>
    </div>

    <el-form label-width="120px" class="mapping-form">
      <el-row :gutter="20">
        <el-col :span="12">
          <el-form-item label="项目名称列" required>
            <el-select
              v-model="mapping.projectColumn"
              placeholder="请选择"
              clearable
              class="w-full"
              @change="updateMapping"
            >
              <el-option
                v-for="opt in columnOptions"
                :key="opt.value"
                :label="opt.label"
                :value="opt.value"
              />
            </el-select>
          </el-form-item>
        </el-col>
        <el-col :span="12">
          <el-form-item label="规格内容列" required>
            <el-select
              v-model="mapping.specificationColumn"
              placeholder="请选择"
              clearable
              class="w-full"
              @change="updateMapping"
            >
              <el-option
                v-for="opt in columnOptions"
                :key="opt.value"
                :label="opt.label"
                :value="opt.value"
              />
            </el-select>
          </el-form-item>
        </el-col>
      </el-row>

      <el-row :gutter="20">
        <el-col :span="12">
          <el-form-item label="验收标准列" required>
            <el-select
              v-model="mapping.acceptanceColumn"
              placeholder="请选择"
              clearable
              class="w-full"
              @change="updateMapping"
            >
              <el-option
                v-for="opt in columnOptions"
                :key="opt.value"
                :label="opt.label"
                :value="opt.value"
              />
            </el-select>
          </el-form-item>
        </el-col>
        <el-col :span="12">
          <el-form-item label="备注列" required>
            <el-select
              v-model="mapping.remarkColumn"
              placeholder="请选择"
              clearable
              class="w-full"
              @change="updateMapping"
            >
              <el-option
                v-for="opt in columnOptions"
                :key="opt.value"
                :label="opt.label"
                :value="opt.value"
              />
            </el-select>
          </el-form-item>
        </el-col>
      </el-row>

      <el-row :gutter="20">
        <el-col :span="12">
          <el-form-item label="表头行号">
            <el-input-number
              v-model="mapping.headerRowIndex"
              :min="0"
              :max="10"
              class="w-full"
              @change="updateMapping"
            />
            <div class="form-tip">从0开始计数，默认第一行为表头</div>
          </el-form-item>
        </el-col>
        <el-col :span="12">
          <el-form-item label="数据起始行号">
            <el-input-number
              v-model="mapping.dataStartRowIndex"
              :min="0"
              :max="100"
              class="w-full"
              @change="updateMapping"
            />
            <div class="form-tip">从0开始计数，默认第二行开始为数据</div>
          </el-form-item>
        </el-col>
      </el-row>
    </el-form>
  </div>
</template>

<style scoped>
.column-mapping {
  width: 100%;
}

.mapping-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 20px;
}

.title {
  font-size: 16px;
  font-weight: 600;
  color: #303133;
}

.actions {
  display: flex;
  gap: 8px;
}

.mapping-form {
  margin-bottom: 20px;
}

.w-full {
  width: 100%;
}

.form-tip {
  font-size: 12px;
  color: #909399;
  margin-top: 4px;
}

</style>
