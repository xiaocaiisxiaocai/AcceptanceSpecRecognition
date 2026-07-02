<script setup lang="ts">
import FileUpload from "@/views/data-import/components/FileUpload.vue";
import type { FileUploadResponse } from "@/api/document";

defineProps<{
  uploadedFile: FileUploadResponse | null;
  loadingUploadedFileTables: boolean;
  canUploadSourceFile: boolean;
}>();

const emit = defineEmits<{
  (e: "update:uploadedFile", value: FileUploadResponse | null): void;
  (e: "uploaded", value: FileUploadResponse): void;
}>();
</script>

<template>
  <div class="step-panel">
    <h3 class="step-title">上传目标文档</h3>
    <p class="step-desc">请选择需要填充验收标准的 Word/Excel 文档</p>
    <FileUpload
      v-if="canUploadSourceFile"
      :model-value="uploadedFile"
      @update:model-value="emit('update:uploadedFile', $event)"
      @uploaded="emit('uploaded', $event)"
    />
    <el-alert
      v-if="canUploadSourceFile && uploadedFile && loadingUploadedFileTables"
      type="info"
      :closable="false"
      show-icon
      title="正在读取表格结构，请稍候"
      class="upload-meta-alert"
    />
    <el-alert
      v-if="!canUploadSourceFile"
      type="warning"
      :closable="false"
      show-icon
      title="当前账号没有文档上传权限"
    />
    <slot name="extra" />
  </div>
</template>
