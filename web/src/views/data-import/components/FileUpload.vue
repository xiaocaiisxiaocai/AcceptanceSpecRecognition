<script setup lang="ts">
import { computed, onActivated, onDeactivated, ref } from "vue";
import { ElMessage } from "element-plus";
import { uploadFile, type FileUploadResponse } from "@/api/document";
import type { UploadRequestOptions } from "element-plus";
import { getRequestErrorMessage } from "@/utils/error-message";
import AppUploadZone from "@/components/AppUploadZone.vue";
import type { AppUploadRequestContext } from "@/components/useAppUploadTask";
import {
  isUploadRequestCancelled,
  throwIfUploadCancelled
} from "@/utils/upload-request";

const props = defineProps<{
  modelValue?: FileUploadResponse | null;
  accept?: string;
}>();

const emit = defineEmits<{
  (e: "update:modelValue", value: FileUploadResponse | null): void;
  (e: "uploaded", value: FileUploadResponse): void;
  (e: "retryMetadata"): void;
}>();

const uploadedFile = computed({
  get: () => props.modelValue ?? null,
  set: val => emit("update:modelValue", val)
});
const uploadZoneMounted = ref(true);

const isExcel = computed(() => uploadedFile.value?.fileType === 1);
const isTableCountPending = computed(
  () =>
    uploadedFile.value?.tableMetadataStatus === "loading" ||
    (uploadedFile.value?.tableMetadataStatus == null &&
      uploadedFile.value?.tableCountReady === false)
);
const isTableMetadataError = computed(
  () => uploadedFile.value?.tableMetadataStatus === "error"
);
const resolvedAccept = computed(() =>
  (props.accept?.trim() || ".docx,.xlsx").toLowerCase()
);
const allowedExtensions = computed(() =>
  resolvedAccept.value
    .split(",")
    .map(item => item.trim().toLowerCase())
    .filter(Boolean)
);
const uploadHint = computed(() => {
  const extText = allowedExtensions.value.join(" / ");
  return `仅支持 ${extText} 格式，文件大小不超过 50MB`;
});

const handleUpload = async (
  options: UploadRequestOptions,
  context: AppUploadRequestContext
) => {
  const file = options.file;
  try {
    const extensions = allowedExtensions.value;
    const lower = file.name.toLowerCase();
    if (
      extensions.length === 0 ||
      !extensions.some(extension => lower.endsWith(extension))
    ) {
      throw new Error(uploadHint.value);
    }

    if (file.size > 50 * 1024 * 1024) throw new Error("文件大小不能超过50MB");

    const res = await uploadFile(file, context);
    throwIfUploadCancelled(context.signal);
    if (res.code !== 0) throw new Error(res.message || "上传失败");

    const uploaded: FileUploadResponse = {
      ...res.data,
      tableMetadataStatus: res.data.tableCountReady ? "ready" : "loading"
    };
    uploadedFile.value = uploaded;
    emit("uploaded", uploaded);
    ElMessage.success("文件上传成功");
  } catch (error) {
    if (isUploadRequestCancelled(error)) throw error;
    ElMessage.error(getRequestErrorMessage(error, "上传失败，请重试"));
    throw error;
  }
};

const removeFromCurrentFlow = () => {
  uploadedFile.value = null;
};

onDeactivated(() => {
  // AppUploadZone 卸载时会调用既有 cancel，真正中止当前上传请求。
  uploadZoneMounted.value = false;
});

onActivated(() => {
  uploadZoneMounted.value = true;
});
</script>

<template>
  <div class="file-upload">
    <template v-if="!uploadedFile">
      <AppUploadZone
        v-if="uploadZoneMounted"
        :request="handleUpload"
        :upload-hint="uploadHint"
        :accept="resolvedAccept"
        size="normal"
        drag-text="将 Word/Excel 文件拖到此处或"
      />
    </template>

    <!-- 已上传文件信息 -->
    <el-card v-else class="uploaded-info">
      <div class="file-info">
        <div class="file-icon">
          <el-icon :size="48" color="var(--el-color-primary)">
            <svg viewBox="0 0 24 24" fill="currentColor">
              <path
                d="M14 2H6c-1.1 0-2 .9-2 2v16c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V8l-6-6zm-1 2l5 5h-5V4zM6 20V4h6v6h6v10H6z"
              />
            </svg>
          </el-icon>
        </div>
        <div class="file-details">
          <div class="file-name">{{ uploadedFile.fileName }}</div>
          <div class="file-meta">
            <span v-if="isTableCountPending">
              文件已上传，正在读取{{ isExcel ? "工作表" : "表格" }}结构...
            </span>
            <span v-else-if="isTableMetadataError" class="metadata-error">
              {{ uploadedFile.tableMetadataError || "表结构读取失败" }}
              <el-button type="primary" link @click="emit('retryMetadata')">
                重试
              </el-button>
            </span>
            <span v-else-if="uploadedFile.tableCount > 0">
              包含 {{ uploadedFile.tableCount }} 个{{
                isExcel ? "工作表" : "表格"
              }}
            </span>
            <span v-else> 未检测到{{ isExcel ? "工作表" : "表格" }} </span>
          </div>
        </div>
        <div class="file-actions">
          <el-button type="danger" link @click="removeFromCurrentFlow">
            移出当前流程
          </el-button>
        </div>
      </div>
    </el-card>
  </div>
</template>

<style scoped>
.file-upload {
  width: 100%;
}

.uploaded-info {
  width: 100%;
}

.file-info {
  display: flex;
  gap: 16px;
  align-items: center;
}

.file-icon {
  flex-shrink: 0;
}

.file-details {
  flex: 1;
}

.file-name {
  font-size: 16px;
  font-weight: 500;
  color: var(--color-text);
}

.file-meta {
  display: flex;
  gap: 8px;
  align-items: center;
  margin-top: 4px;
  font-size: 14px;
  color: var(--app-text-secondary);
}

.file-actions {
  flex-shrink: 0;
}

.metadata-error {
  color: var(--el-color-danger);
}
</style>
