<script setup lang="ts">
import { computed, onBeforeUnmount, ref } from "vue";
import {
  CircleCheckFilled,
  CircleCloseFilled,
  Loading,
  UploadFilled
} from "@element-plus/icons-vue";
import type { UploadInstance, UploadRequestOptions } from "element-plus";
import { useAppUploadTask, type AppUploadRequest } from "./useAppUploadTask";

const props = withDefaults(
  defineProps<{
    request: AppUploadRequest;
    disabled?: boolean;
    uploadHint?: string;
    accept?: string;
    size?: "small" | "normal" | "large";
    headerTitle?: string;
    dragText?: string;
    tipText?: string;
    showHeader?: boolean;
    resetAfterSuccess?: boolean;
  }>(),
  {
    disabled: false,
    accept: ".docx,.xlsx",
    size: "normal",
    dragText: "将文件拖到此处，或 ",
    tipText: "仅支持 .docx / .xlsx，文件大小不超过 50MB",
    showHeader: false,
    resetAfterSuccess: false
  }
);

const emit = defineEmits<{
  cancel: [];
}>();

type UploadInstanceWithElement = UploadInstance & {
  $el?: HTMLElement;
};

const uploadRef = ref<UploadInstanceWithElement | null>(null);

const {
  phase,
  active,
  progressPercent,
  progressText,
  errorMessage,
  execute,
  cancel,
  reset
} = useAppUploadTask((options, context) => props.request(options, context));

const minHeightClass = computed(() => {
  const map = { small: "120px", normal: "200px", large: "280px" };
  return map[props.size];
});

const iconSizeValue = computed(() => {
  const map = { small: 32, normal: 48, large: 64 };
  return map[props.size];
});

const handleUpload = async (options: UploadRequestOptions) => {
  await execute(options);
  if (props.resetAfterSuccess) reset();
};

const handleCancel = () => {
  cancel();
  emit("cancel");
};

const openFileDialog = () => {
  const root = uploadRef.value?.$el as HTMLElement | undefined;
  const input = root?.querySelector<HTMLInputElement>('input[type="file"]');
  if (!input || input.disabled) return;

  // 允许用户连续选择同一个文件；否则浏览器不会再次触发 change。
  input.value = "";
  input.click();
};

const statusTitle = computed(() => {
  if (phase.value === "uploading") return "正在上传文件";
  if (phase.value === "processing") return "文件已上传，正在解析结构";
  if (phase.value === "success") return "上传和处理完成";
  if (phase.value === "failure") return "上传失败，可以重新选择文件";
  return "";
});

onBeforeUnmount(cancel);
</script>

<template>
  <div class="app-upload-zone">
    <div v-if="showHeader" class="upload-zone-header">
      <span>{{ headerTitle }}</span>
    </div>
    <el-upload
      ref="uploadRef"
      class="app-upload-area"
      drag
      :show-file-list="false"
      :http-request="handleUpload"
      :accept="accept"
      :disabled="disabled || active"
    >
      <el-icon
        class="el-icon--upload"
        :class="{ 'is-processing': phase === 'processing' }"
        :size="iconSizeValue"
      >
        <Loading v-if="phase === 'uploading' || phase === 'processing'" />
        <CircleCheckFilled v-else-if="phase === 'success'" />
        <CircleCloseFilled v-else-if="phase === 'failure'" />
        <UploadFilled v-else />
      </el-icon>
      <div v-if="phase === 'idle'" class="el-upload__text">
        <span>{{ dragText }}</span>
        <el-button
          class="upload-select-button"
          type="primary"
          link
          aria-label="选择文件"
          @click.stop="openFileDialog"
        >
          选择文件
        </el-button>
      </div>
      <div v-else class="upload-status" aria-live="polite">
        <strong>{{ statusTitle }}</strong>
        <span v-if="phase === 'uploading'" class="upload-status__detail">
          {{ progressText }}
        </span>
        <span v-else-if="phase === 'processing'" class="upload-status__detail">
          服务端正在读取工作表或表格，请稍候
        </span>
        <span v-else-if="phase === 'failure'" class="upload-status__error">
          {{ errorMessage }}
        </span>
      </div>
      <div v-if="active" class="upload-progress" @click.stop>
        <el-progress
          v-if="progressPercent !== null && phase === 'uploading'"
          :percentage="progressPercent"
          :stroke-width="8"
        />
        <el-progress
          v-else
          :percentage="100"
          :show-text="false"
          :indeterminate="true"
          :duration="1.8"
          :stroke-width="8"
        />
        <el-button type="primary" link @click.stop.prevent="handleCancel">
          {{ phase === "processing" ? "停止等待" : "取消上传" }}
        </el-button>
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
  cursor: pointer;
  user-select: none;
  background: var(--app-bg-card);
  border-color: var(--app-border);
  border-radius: 12px;
  transition:
    border-color 0.2s ease,
    box-shadow 0.2s ease;
}

.el-upload__text {
  display: inline-flex;
  gap: 4px;
  align-items: center;
}

.upload-select-button {
  padding: 0;
  font: inherit;
  vertical-align: baseline;
}

.app-upload-area :deep(.el-upload-dragger:hover) {
  border-color: var(--app-primary);
  box-shadow: var(--shadow-sm);
}

.el-icon--upload.is-processing {
  animation: upload-spin 1.2s linear infinite;
}

.upload-status {
  display: flex;
  flex-direction: column;
  gap: 6px;
  align-items: center;
  color: var(--app-text-primary);
}

.upload-status__detail {
  font-size: 13px;
  color: var(--app-text-secondary);
}

.upload-status__error {
  max-width: 520px;
  font-size: 13px;
  color: var(--el-color-danger);
}

.upload-progress {
  display: flex;
  gap: 12px;
  align-items: center;
  width: min(520px, 80%);
  margin-top: 16px;
}

.upload-progress :deep(.el-progress) {
  flex: 1;
}

@keyframes upload-spin {
  to {
    transform: rotate(360deg);
  }
}
</style>
