<script setup lang="ts">
import { ref, computed } from "vue";
import { ElMessage } from "element-plus";
import { UploadFilled } from "@element-plus/icons-vue";
import { uploadFile, type FileUploadResponse } from "@/api/document";
import type { UploadRequestOptions } from "element-plus";

const props = defineProps<{
  modelValue?: FileUploadResponse | null;
  accept?: string;
}>();

const emit = defineEmits<{
  (e: "update:modelValue", value: FileUploadResponse | null): void;
  (e: "uploaded", value: FileUploadResponse): void;
}>();

const uploading = ref(false);
const uploadedFile = computed({
  get: () => props.modelValue,
  set: (val) => emit("update:modelValue", val)
});

const isExcel = computed(() => uploadedFile.value?.fileType === 1);
const resolvedAccept = computed(() => (props.accept?.trim() || ".docx,.xlsx").toLowerCase());
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

// 自定义上传
const handleUpload = async (options: UploadRequestOptions) => {
  const file = options.file;
  const extensions = allowedExtensions.value;

  // 检查文件类型
  const lower = file.name.toLowerCase();
  if (
    extensions.length === 0 ||
    !extensions.some(extension => lower.endsWith(extension))
  ) {
    ElMessage.error(uploadHint.value);
    return;
  }

  // 检查文件大小（最大50MB）
  if (file.size > 50 * 1024 * 1024) {
    ElMessage.error("文件大小不能超过50MB");
    return;
  }

  uploading.value = true;
  try {
    const res = await uploadFile(file);
    if (res.code === 0) {
      uploadedFile.value = res.data;
      emit("uploaded", res.data);
      ElMessage.success("文件上传成功");
    } else {
      ElMessage.error(res.message || "上传失败");
    }
  } catch (error) {
    ElMessage.error("上传失败，请重试");
  } finally {
    uploading.value = false;
  }
};

// 清除文件
const clearFile = () => {
  uploadedFile.value = null;
};
</script>

<template>
  <div class="file-upload">
    <el-upload
      v-if="!uploadedFile"
      class="upload-area"
      drag
      :show-file-list="false"
      :http-request="handleUpload"
      :accept="resolvedAccept"
      :disabled="uploading"
    >
      <el-icon class="el-icon--upload" :size="60">
        <UploadFilled />
      </el-icon>
      <div class="el-upload__text">
        <span v-if="uploading">上传中...</span>
        <span v-else>
          将 Word/Excel 文件拖到此处，或
          <em>点击上传</em>
        </span>
      </div>
      <template #tip>
        <div class="el-upload__tip">{{ uploadHint }}</div>
      </template>
    </el-upload>

    <!-- 已上传文件信息 -->
    <el-card v-else class="uploaded-info">
      <div class="file-info">
        <div class="file-icon">
          <el-icon :size="48" color="#409EFF">
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
            <span>
              包含 {{ uploadedFile.tableCount }} 个{{ isExcel ? "工作表" : "表格" }}
            </span>
          </div>
        </div>
        <div class="file-actions">
          <el-button type="danger" link @click="clearFile">删除</el-button>
        </div>
      </div>
    </el-card>
  </div>
</template>

<style scoped>
.file-upload {
  width: 100%;
}

.upload-area {
  width: 100%;
}

.upload-area :deep(.el-upload-dragger) {
  width: 100%;
  min-height: 200px;
  display: flex;
  flex-direction: column;
  justify-content: center;
  align-items: center;
  border-radius: 12px;
  border-color: #e4d7fb;
  background: #ffffff;
  transition:
    border-color 0.2s ease,
    box-shadow 0.2s ease;
}

.upload-area :deep(.el-upload-dragger:hover) {
  border-color: var(--color-primary);
  box-shadow: var(--shadow-sm);
}

.uploaded-info {
  width: 100%;
}

.file-info {
  display: flex;
  align-items: center;
  gap: 16px;
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
  margin-top: 4px;
  font-size: 14px;
  color: #6b7280;
  display: flex;
  align-items: center;
  gap: 8px;
}

.file-actions {
  flex-shrink: 0;
}
</style>
