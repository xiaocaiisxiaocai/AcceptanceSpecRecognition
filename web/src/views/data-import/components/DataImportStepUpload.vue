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
    <slot name="extra" />
  </div>
</template>
