<script setup lang="ts">
import TableSelector from "./TableSelector.vue";
import type { FileUploadResponse, TableInfo } from "@/api/document";

defineProps<{
  uploadedFile: FileUploadResponse | null;
  isExcelFile: boolean;
  modelValue: number[];
}>();

const emit = defineEmits<{
  (e: "update:modelValue", value: number[]): void;
  (e: "selectedMultiple", value: TableInfo[]): void;
}>();
</script>

<template>
  <div class="step-panel">
    <TableSelector
      v-if="uploadedFile"
      :file-id="uploadedFile.fileId"
      :item-label="isExcelFile ? '工作表' : '表格'"
      :model-value="modelValue"
      multiple
      @update:model-value="
        value =>
          emit('update:modelValue', Array.isArray(value) ? value : [value])
      "
      @selected-multiple="tables => emit('selectedMultiple', tables)"
    />
  </div>
</template>
