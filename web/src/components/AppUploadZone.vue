<script setup lang="ts">
import { computed } from "vue";
import { UploadFilled } from "@element-plus/icons-vue";
import type { UploadRequestOptions } from "element-plus";

const props = withDefaults(
  defineProps<{
    uploading?: boolean;
    uploadHint?: string;
    accept?: string;
    size?: "small" | "normal" | "large";
    headerTitle?: string;
    dragText?: string;
    tipText?: string;
    showHeader?: boolean;
  }>(),
  {
    uploading: false,
    accept: ".docx,.xlsx",
    size: "normal",
    dragText: "将文件拖到此处，或 ",
    tipText: "仅支持 .docx / .xlsx，文件大小不超过 50MB",
    showHeader: false
  }
);

const emit = defineEmits<{
  upload: [options: UploadRequestOptions];
}>();

const minHeightClass = computed(() => {
  const map = { small: "120px", normal: "200px", large: "280px" };
  return map[props.size];
});

const iconSizeValue = computed(() => {
  const map = { small: 32, normal: 48, large: 64 };
  return map[props.size];
});

const handleUpload = (options: UploadRequestOptions) => {
  emit("upload", options);
  return Promise.resolve();
};
</script>

<template>
  <div class="app-upload-zone">
    <div v-if="showHeader" class="upload-zone-header">
      <span>{{ headerTitle }}</span>
    </div>
    <el-upload
      class="app-upload-area"
      drag
      :show-file-list="false"
      :http-request="handleUpload"
      :accept="accept"
      :disabled="uploading"
    >
      <el-icon class="el-icon--upload" :size="iconSizeValue">
        <UploadFilled />
      </el-icon>
      <div class="el-upload__text">
        <span v-if="uploading">上传中...</span>
        <span v-else>
          {{ dragText }}<em>点击上传</em>
        </span>
      </div>
      <template #tip>
        <div class="el-upload__tip">{{ uploadHint || tipText }}</div>
      </template>
    </el-upload>
  </div>
</template>

<style scoped>
.app-upload-zone {
  width: 100%;
}

.upload-zone-header {
  margin-bottom: 12px;
  font-size: 14px;
  font-weight: 500;
  color: var(--color-text);
}

.app-upload-area {
  width: 100%;
}

.app-upload-area :deep(.el-upload-dragger) {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  width: 100%;
  min-height: v-bind(minHeightClass);
  background: var(--app-bg-card);
  border-color: var(--app-border);
  border-radius: 12px;
  transition:
    border-color 0.2s ease,
    box-shadow 0.2s ease;
}

.app-upload-area :deep(.el-upload-dragger:hover) {
  border-color: var(--app-primary);
  box-shadow: var(--shadow-sm);
}
</style>
