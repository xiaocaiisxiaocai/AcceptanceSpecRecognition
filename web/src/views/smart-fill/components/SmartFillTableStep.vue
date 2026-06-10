<script setup lang="ts">
import BatchTableConfig from "./BatchTableConfig.vue";
import type { BatchTableConfigItem } from "./batchTableConfig.types";
import type { TableInfo } from "@/api/document";

defineProps<{
  batchTableConfigs: BatchTableConfigItem[];
  uploadedFileId?: number;
  isExcelFile: boolean;
  allTables: TableInfo[];
  hasUploadedFile: boolean;
}>();

const emit = defineEmits<{
  (e: "update:batchTableConfigs", value: BatchTableConfigItem[]): void;
}>();
</script>

<template>
  <div class="step-panel">
    <h3 class="step-title">选择表格并配置列索引</h3>
    <p class="step-desc">
      勾选需要填充的表格，并为每个表格指定各列索引（从0开始）。 Word
      会按列映射规则自动预填，Excel 仍需按实际内容手工调整并刷新表头。
    </p>

    <BatchTableConfig
      v-if="batchTableConfigs.length > 0"
      :model-value="batchTableConfigs"
      :file-id="uploadedFileId"
      :is-excel="isExcelFile"
      :tables="allTables"
      @update:model-value="emit('update:batchTableConfigs', $event)"
    />

    <el-empty
      v-else-if="hasUploadedFile"
      description="未检测到表格，请确认文档格式"
    />
  </div>
</template>
