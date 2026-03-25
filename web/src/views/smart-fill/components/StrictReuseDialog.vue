<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { ElMessage } from "element-plus";
import { UploadFilled } from "@element-plus/icons-vue";
import type { UploadRequestOptions } from "element-plus";
import { uploadFile, type FileUploadResponse } from "@/api/document";
import {
  downloadFillResult,
  strictReuseExecute,
  strictReusePreview,
  type StrictReuseExecuteResponse,
  type StrictReusePreviewResponse
} from "@/api/matching";

const props = withDefaults(
  defineProps<{
    visible: boolean;
    sourceTaskId: string;
    sourceFileName: string;
    isExcel?: boolean;
    canPreview?: boolean;
    canExecute?: boolean;
    canDownload?: boolean;
  }>(),
  {
    isExcel: false,
    canPreview: false,
    canExecute: false,
    canDownload: false
  }
);

const emit = defineEmits<{
  (e: "update:visible", value: boolean): void;
}>();

const dialogVisible = computed({
  get: () => props.visible,
  set: value => emit("update:visible", value)
});

const uploadedFiles = ref<FileUploadResponse[]>([]);
const previewResult = ref<StrictReusePreviewResponse | null>(null);
const executeResult = ref<StrictReuseExecuteResponse | null>(null);
const uploadPendingCount = ref(0);
const previewing = ref(false);
const executing = ref(false);

const accept = computed(() => (props.isExcel ? ".xlsx" : ".docx"));
const uploadHint = computed(
  () => `仅支持 ${accept.value} 格式，文件大小不超过 50MB`
);
const uploading = computed(() => uploadPendingCount.value > 0);
const uploadedFileIds = computed(() => uploadedFiles.value.map(file => file.fileId));
const readyFiles = computed(() => previewResult.value?.files.filter(file => file.canApply) ?? []);
const readyFileIds = computed(() => readyFiles.value.map(file => file.fileId));
const canRunPreview = computed(
  () =>
    !!props.sourceTaskId &&
    props.canPreview &&
    uploadedFiles.value.length > 0 &&
    !uploading.value &&
    !previewing.value &&
    !executing.value
);
const canRunExecute = computed(
  () =>
    !!props.sourceTaskId &&
    props.canExecute &&
    props.canDownload &&
    readyFileIds.value.length > 0 &&
    !uploading.value &&
    !previewing.value &&
    !executing.value
);

watch(
  () => dialogVisible.value,
  visible => {
    if (!visible) {
      resetState();
    }
  }
);

watch(
  uploadedFiles,
  () => {
    previewResult.value = null;
    executeResult.value = null;
  },
  { deep: true }
);

const resetState = () => {
  uploadedFiles.value = [];
  previewResult.value = null;
  executeResult.value = null;
  uploadPendingCount.value = 0;
  previewing.value = false;
  executing.value = false;
};

const handleUpload = async (options: UploadRequestOptions) => {
  const file = options.file;
  const lowerName = file.name.toLowerCase();
  if (!lowerName.endsWith(accept.value)) {
    ElMessage.error(uploadHint.value);
    return;
  }

  if (file.size > 50 * 1024 * 1024) {
    ElMessage.error("文件大小不能超过50MB");
    return;
  }

  uploadPendingCount.value++;
  try {
    const res = await uploadFile(file);
    if (res.code !== 0) {
      ElMessage.error(res.message || "上传失败");
      return;
    }

    uploadedFiles.value = [...uploadedFiles.value, res.data];
    ElMessage.success(`${res.data.fileName} 上传成功`);
  } catch {
    ElMessage.error("上传失败，请重试");
  } finally {
    uploadPendingCount.value = Math.max(0, uploadPendingCount.value - 1);
  }
};

const removeUploadedFile = (fileId: number) => {
  uploadedFiles.value = uploadedFiles.value.filter(file => file.fileId !== fileId);
};

const handlePreview = async () => {
  if (!props.canPreview) {
    ElMessage.warning("权限不足，无法执行严格复用预检");
    return;
  }

  if (!props.sourceTaskId) {
    ElMessage.warning("当前填充任务不存在，请重新执行智能填充");
    return;
  }

  if (uploadedFileIds.value.length === 0) {
    ElMessage.warning("请先上传目标文件");
    return;
  }

  previewing.value = true;
  try {
    const res = await strictReusePreview({
      sourceTaskId: props.sourceTaskId,
      targetFileIds: uploadedFileIds.value
    });

    if (res.code !== 0) {
      ElMessage.error(res.message || "严格复用预检失败");
      return;
    }

    previewResult.value = res.data;
    executeResult.value = null;

    if (res.data.readyCount > 0) {
      ElMessage.success(`预检完成，可直接应用 ${res.data.readyCount} 份文件`);
    } else {
      ElMessage.warning("预检完成，但没有可直接应用的文件");
    }
  } catch {
    ElMessage.error("严格复用预检失败");
  } finally {
    previewing.value = false;
  }
};

const downloadResult = async (taskId: string, fileName: string) => {
  const blob = await downloadFillResult(taskId);
  const url = window.URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName;
  link.click();
  window.URL.revokeObjectURL(url);
};

const handleExecute = async () => {
  if (!props.canExecute || !props.canDownload) {
    ElMessage.warning("权限不足，无法执行严格复用");
    return;
  }

  if (!props.sourceTaskId) {
    ElMessage.warning("当前填充任务不存在，请重新执行智能填充");
    return;
  }

  if (readyFileIds.value.length === 0) {
    ElMessage.warning("请先完成预检，并确保至少有一份文件可应用");
    return;
  }

  executing.value = true;
  try {
    const res = await strictReuseExecute({
      sourceTaskId: props.sourceTaskId,
      targetFileIds: readyFileIds.value
    });

    if (res.code !== 0) {
      ElMessage.error(res.message || "严格复用执行失败");
      return;
    }

    executeResult.value = res.data;
    await downloadResult(res.data.taskId, res.data.downloadFileName);
    ElMessage.success(
      res.data.failedCount > 0
        ? `严格复用完成，成功 ${res.data.successCount} 份，失败 ${res.data.failedCount} 份`
        : `严格复用完成，成功 ${res.data.successCount} 份`
    );
  } catch {
    ElMessage.error("严格复用执行失败");
  } finally {
    executing.value = false;
  }
};
</script>

<template>
  <el-dialog
    v-model="dialogVisible"
    title="应用到相同验规"
    width="960px"
    destroy-on-close
  >
    <div class="strict-reuse-dialog">
      <el-alert
        title="严格模式一次性复用"
        type="info"
        :closable="false"
        show-icon
      >
        <template #default>
          <div class="intro-text">
            当前将基于 <strong>{{ sourceFileName }}</strong> 的已确认填充结果执行复用。
            该流程不会重新匹配、不会调用 AI、不会保存长期模板，只会把来源任务中最终确认的验收和备注写入相同模板文件。
          </div>
        </template>
      </el-alert>

      <div class="section">
        <div class="section-title">上传相同模板文件</div>
        <el-upload
          class="upload-area"
          drag
          multiple
          :show-file-list="false"
          :http-request="handleUpload"
          :accept="accept"
          :disabled="uploading || previewing || executing"
        >
          <el-icon class="el-icon--upload" :size="52">
            <UploadFilled />
          </el-icon>
          <div class="el-upload__text">
            将{{ isExcel ? " Excel " : " Word " }}文件拖到此处，或
            <em>点击上传</em>
          </div>
          <template #tip>
            <div class="el-upload__tip">{{ uploadHint }}</div>
          </template>
        </el-upload>
      </div>

      <div v-if="uploadedFiles.length > 0" class="section">
        <div class="section-title">待复用文件</div>
        <el-table :data="uploadedFiles" border>
          <el-table-column prop="fileName" label="文件名" min-width="300" />
          <el-table-column label="类型" width="100">
            <template #default>
              <el-tag size="small">{{ isExcel ? "Excel" : "Word" }}</el-tag>
            </template>
          </el-table-column>
          <el-table-column label="操作" width="100" align="center">
            <template #default="{ row }">
              <el-button
                link
                type="danger"
                :disabled="previewing || executing"
                @click="removeUploadedFile(row.fileId)"
              >
                移除
              </el-button>
            </template>
          </el-table-column>
        </el-table>
      </div>

      <div v-if="previewResult" class="section">
        <div class="section-header">
          <div class="section-title">预检结果</div>
          <div class="summary-tags">
            <el-tag type="success">可应用 {{ previewResult.readyCount }}</el-tag>
            <el-tag type="info">总计 {{ previewResult.totalCount }}</el-tag>
            <el-tag type="warning">AI {{ previewResult.usesAi ? "开启" : "关闭" }}</el-tag>
          </div>
        </div>
        <el-table :data="previewResult.files" border>
          <el-table-column prop="fileName" label="文件名" min-width="260" />
          <el-table-column label="状态" width="100" align="center">
            <template #default="{ row }">
              <el-tag :type="row.canApply ? 'success' : 'danger'">
                {{ row.canApply ? "可应用" : "不可应用" }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column label="失败原因" min-width="360">
            <template #default="{ row }">
              <span v-if="row.errors.length === 0" class="empty-text">通过</span>
              <div v-else class="error-list">
                <div v-for="error in row.errors" :key="error">{{ error }}</div>
              </div>
            </template>
          </el-table-column>
        </el-table>
      </div>

      <div v-if="executeResult" class="section">
        <div class="section-header">
          <div class="section-title">执行结果</div>
          <div class="summary-tags">
            <el-tag type="success">成功 {{ executeResult.successCount }}</el-tag>
            <el-tag type="danger">失败 {{ executeResult.failedCount }}</el-tag>
          </div>
        </div>
        <el-table :data="executeResult.files" border>
          <el-table-column prop="fileName" label="文件名" min-width="260" />
          <el-table-column label="结果" width="100" align="center">
            <template #default="{ row }">
              <el-tag :type="row.success ? 'success' : 'danger'">
                {{ row.success ? "成功" : "失败" }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column prop="message" label="说明" min-width="360" />
        </el-table>
      </div>
    </div>

    <template #footer>
      <div class="dialog-actions">
        <el-button @click="dialogVisible = false">关闭</el-button>
        <el-button
          type="primary"
          plain
          :loading="previewing"
          :disabled="!canRunPreview"
          @click="handlePreview"
        >
          开始校验
        </el-button>
        <el-button
          type="primary"
          :loading="executing"
          :disabled="!canRunExecute"
          @click="handleExecute"
        >
          确认批量填充
        </el-button>
      </div>
    </template>
  </el-dialog>
</template>

<style scoped>
.strict-reuse-dialog {
  display: flex;
  flex-direction: column;
  gap: 18px;
}

.intro-text {
  line-height: 1.7;
}

.section {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.section-title {
  font-size: 15px;
  font-weight: 600;
  color: var(--color-text);
}

.section-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  flex-wrap: wrap;
}

.summary-tags {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.upload-area {
  width: 100%;
}

.upload-area :deep(.el-upload-dragger) {
  width: 100%;
  min-height: 168px;
  border-radius: 12px;
}

.error-list {
  display: flex;
  flex-direction: column;
  gap: 4px;
  color: #b42318;
}

.empty-text {
  color: #909399;
}

.dialog-actions {
  display: flex;
  justify-content: flex-end;
  gap: 12px;
}
</style>
