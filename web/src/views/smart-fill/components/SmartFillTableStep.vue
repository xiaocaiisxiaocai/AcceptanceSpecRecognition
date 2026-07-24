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
    <BatchTableConfig
      v-if="batchTableConfigs.length > 0"
      :model-value="batchTableConfigs"
      :file-id="uploadedFileId"
      :is-excel="isExcelFile"
      :tables="allTables"
      :show-filter-empty-source-rows="false"
      @update:model-value="emit('update:batchTableConfigs', $event)"
    />

    <el-empty
      v-else-if="hasUploadedFile"
      description="未检测到表格，请确认文档格式"
    />
  </div>
</template>
