<script setup lang="ts">
import FileUpload from "./FileUpload.vue";
import type { FileUploadResponse } from "@/api/document";

defineProps<{
  canUploadSourceFile: boolean;
  canImportAny: boolean;
  uploadAccept: string;
  uploadBlockedMessage: string;
  modelValue: FileUploadResponse | null;
}>();

const emit = defineEmits<{
  (e: "update:modelValue", value: FileUploadResponse | null): void;
  (e: "uploaded", value: FileUploadResponse): void;
}>();
</script>

<template>
  <div class="step-panel">
    <h3 class="step-title">上传文件</h3>
    <p class="step-desc">
      请选择包含验收规格数据的 Word（.docx）或 Excel（.xlsx）文件
    </p>
    <FileUpload
      v-if="canUploadSourceFile && canImportAny"
      :model-value="modelValue"
      :accept="uploadAccept"
      @update:model-value="value => emit('update:modelValue', value)"
      @uploaded="value => emit('uploaded', value)"
    />
    <el-alert
      v-else
      type="warning"
      :closable="false"
      show-icon
      :title="uploadBlockedMessage"
    />
  </div>
</template>
