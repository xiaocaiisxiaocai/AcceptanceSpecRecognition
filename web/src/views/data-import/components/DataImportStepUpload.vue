<script setup lang="ts">
import FileUpload from "./FileUpload.vue";
import SmartStructureSummaryBanner from "@/views/shared/SmartStructureSummaryBanner.vue";
import type { FileUploadResponse } from "@/api/document";

defineProps<{
  canUploadSourceFile: boolean;
  canImportAny: boolean;
  uploadAccept: string;
  uploadBlockedMessage: string;
  modelValue: FileUploadResponse | null;
  smartRecognitionError?: string;
  smartRecognizing?: boolean;
}>();

const emit = defineEmits<{
  (e: "update:modelValue", value: FileUploadResponse | null): void;
  (e: "uploaded", value: FileUploadResponse): void;
  (e: "retry"): void;
  (e: "retryMetadata"): void;
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
      @retry-metadata="emit('retryMetadata')"
    />
    <el-alert
      v-else
      type="warning"
      :closable="false"
      show-icon
      :title="uploadBlockedMessage"
    />
    <SmartStructureSummaryBanner
      v-if="smartRecognitionError"
      class="upload-recognition-error"
      :tables="[]"
      :loading="smartRecognizing"
      :error="smartRecognitionError"
      @retry="emit('retry')"
    />
    <slot name="extra" />
  </div>
</template>

<style scoped>
.upload-recognition-error {
  margin-top: 12px;
}
</style>
