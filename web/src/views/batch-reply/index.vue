<script setup lang="ts">
import { computed, onBeforeUnmount, ref } from "vue";
import { ElMessage } from "element-plus";
import { UploadFilled } from "@element-plus/icons-vue";
import type { UploadFile, UploadInstance, UploadRequestOptions } from "element-plus";
import BatchTableConfig from "@/views/smart-fill/components/BatchTableConfig.vue";
import type { BatchTableConfigItem } from "@/views/smart-fill/components/BatchTableConfig.vue";
import {
  decideTargetUpload,
  type TargetUploadItem
} from "./target-upload";
import {
  downloadBatchReplyResult,
  executeBatchReply,
  getBatchReplyTablePreview,
  getBatchReplyTables,
  previewBatchReply,
  uploadBatchReplySource,
  type BatchReplyExecuteResponse,
  type BatchReplyPreviewResponse,
  type BatchReplySourceUploadResponse
} from "@/api/matching";
import type { TableData, TableInfo } from "@/api/document";
import { hasPerms } from "@/utils/auth";
import { ensurePermission } from "@/utils/permission-guard";

defineOptions({ name: "BatchReplyPage" });

const steps = [
  { title: "上传来源", description: "上传一份人工已回复文档" },
  { title: "配置表格", description: "按来源文件配置项目、规格、验收和备注列" },
  { title: "上传目标", description: "选择待批量回复的同模板文件" },
  { title: "预检执行", description: "先严格预检，再执行下载" }
];

const sourceFile = ref<BatchReplySourceUploadResponse | null>(null);
const sourceTables = ref<TableInfo[]>([]);
const batchTableConfigs = ref<BatchTableConfigItem[]>([]);
const targetFiles = ref<TargetUploadItem[]>([]);
const previewResult = ref<BatchReplyPreviewResponse | null>(null);
const executeResult = ref<BatchReplyExecuteResponse | null>(null);
const sourceUploading = ref(false);
const previewing = ref(false);
const executing = ref(false);
const targetUploadKey = ref(0);
const previewAbortController = ref<AbortController | null>(null);
const targetUploadRef = ref<UploadInstance>();

const currentStep = computed(() => {
  if (!sourceFile.value) return 0;
  if (selectedTableCount.value === 0) return 1;
  if (targetFiles.value.length === 0) return 2;
  return 3;
});

const sourceSessionId = computed(() => sourceFile.value?.sessionId ?? "");
const sourceIsExcel = computed(() => sourceFile.value?.sourceFileType === 1);
const targetAccept = computed(() => (sourceIsExcel.value ? ".xlsx" : ".docx"));
const canUploadSourceFile = computed(() => hasPerms("api:batch-reply:upload-source"));
const canPreviewBatchReply = computed(() => hasPerms("btn:batch-reply:preview"));
const canExecuteBatchReply = computed(() => hasPerms("btn:batch-reply:execute"));
const canDownloadBatchReply = computed(() => hasPerms("api:batch-reply:download"));
const selectedTableCount = computed(
  () => batchTableConfigs.value.filter(item => item.selected).length
);
const selectedTableConfigs = computed(() =>
  batchTableConfigs.value.filter(item => item.selected)
);
const readyFiles = computed(
  () => previewResult.value?.files.filter(item => item.canApply) ?? []
);

const stopPreviewRequest = () => {
  const controller = previewAbortController.value;
  controller?.abort();
  if (previewAbortController.value === controller) {
    previewAbortController.value = null;
  }
};

onBeforeUnmount(() => {
  stopPreviewRequest();
});

const triggerBrowserDownload = (blob: Blob, fileName: string) => {
  const url = window.URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = fileName;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  window.URL.revokeObjectURL(url);
};

const formatFileSize = (size: number) => {
  if (size < 1024) return `${size} B`;
  if (size < 1024 * 1024) return `${(size / 1024).toFixed(1)} KB`;
  return `${(size / (1024 * 1024)).toFixed(1)} MB`;
};

const buildExcelTableConfig = (
  table: TableInfo,
  selected: boolean
): BatchTableConfigItem => {
  const usedStartRow = Math.max(1, table.usedRangeStartRow ?? 1);
  const totalColumns = Math.max(table.columnCount, table.headers.length, 1);
  const clampColumnIndex = (preferredIndex: number) =>
    Math.min(preferredIndex, totalColumns - 1);

  return {
    tableIndex: table.index,
    projectColumnIndex: clampColumnIndex(0),
    specificationColumnIndex: clampColumnIndex(1),
    acceptanceColumnIndex: clampColumnIndex(2),
    remarkColumnIndex: totalColumns > 3 ? 3 : undefined,
    headerRowStart: usedStartRow,
    headerRowCount: 1,
    dataStartRow: usedStartRow + 1,
    filterEmptySourceRows: true,
    selected,
    tableInfo: table
  };
};

const buildWordTableConfig = (
  table: TableInfo,
  selected: boolean
): BatchTableConfigItem => {
  const totalColumns = Math.max(table.columnCount, table.headers.length, 1);
  const clampColumnIndex = (preferredIndex: number) =>
    Math.min(preferredIndex, totalColumns - 1);

  return {
    tableIndex: table.index,
    projectColumnIndex: clampColumnIndex(0),
    specificationColumnIndex: clampColumnIndex(1),
    acceptanceColumnIndex: clampColumnIndex(2),
    remarkColumnIndex: totalColumns > 3 ? 3 : undefined,
    headerRowStart: 1,
    headerRowCount: 1,
    dataStartRow: 2,
    filterEmptySourceRows: true,
    selected,
    tableInfo: table
  };
};

const resetPreviewState = () => {
  stopPreviewRequest();
  previewResult.value = null;
  executeResult.value = null;
};

const resetSourceState = () => {
  sourceFile.value = null;
  sourceTables.value = [];
  batchTableConfigs.value = [];
  targetFiles.value = [];
  targetUploadKey.value++;
  resetPreviewState();
};

const loadSourceTables = async (sessionId: string, sourceFileType: number) => {
  const res = await getBatchReplyTables(sessionId);
  if (res.code !== 0) {
    throw new Error(res.message || "加载来源表格失败");
  }

  sourceTables.value = res.data;
  batchTableConfigs.value = res.data.map(table =>
    sourceFileType === 1
      ? buildExcelTableConfig(table, res.data.length === 1)
      : buildWordTableConfig(table, res.data.length === 1)
  );
};

const handleSourceUpload = async (options: UploadRequestOptions) => {
  if (!ensurePermission("api:batch-reply:upload-source", "权限不足，无法上传来源文件")) {
    return;
  }

  const file = options.file as File;
  const lowerName = file.name.toLowerCase();
  if (!lowerName.endsWith(".docx") && !lowerName.endsWith(".xlsx")) {
    ElMessage.error("仅支持 .docx / .xlsx 格式");
    return;
  }

  if (file.size > 50 * 1024 * 1024) {
    ElMessage.error("文件大小不能超过50MB");
    return;
  }

  sourceUploading.value = true;
  try {
    const res = await uploadBatchReplySource(file);
    if (res.code !== 0) {
      ElMessage.error(res.message || "来源文件上传失败");
      return;
    }

    resetSourceState();
    sourceFile.value = res.data;
    await loadSourceTables(res.data.sessionId, res.data.sourceFileType);
    ElMessage.success("来源文件上传成功");
  } catch {
    ElMessage.error("来源文件上传失败");
  } finally {
    sourceUploading.value = false;
  }
};

const handleTargetFileChange = (uploadFile: UploadFile) => {
  const rawFile = uploadFile.raw;
  if (!rawFile) {
    return;
  }

  const result = decideTargetUpload({
    hasSourceFile: !!sourceFile.value,
    accept: targetAccept.value,
    existingSignatures: targetFiles.value.map(item => item.id),
    file: rawFile
  });

  if (result.status === "accepted") {
    targetFiles.value = [...targetFiles.value, result.item as TargetUploadItem];
    resetPreviewState();
    targetUploadRef.value?.handleRemove(uploadFile);
    return;
  }

  if (result.level === "warning") {
    ElMessage.warning(result.message);
  } else {
    ElMessage.error(result.message);
  }
  targetUploadRef.value?.handleRemove(uploadFile);
};

const removeTargetFile = (id: string) => {
  targetFiles.value = targetFiles.value.filter(item => item.id !== id);
  resetPreviewState();
};

const buildPreviewRequestTables = () =>
  selectedTableConfigs.value.map(item => ({
    tableIndex: item.tableIndex,
    projectColumnIndex: item.projectColumnIndex,
    specificationColumnIndex: item.specificationColumnIndex,
    acceptanceColumnIndex: item.acceptanceColumnIndex,
    remarkColumnIndex: item.remarkColumnIndex,
    headerRowStart: item.headerRowStart,
    headerRowCount: item.headerRowCount,
    dataStartRow: item.dataStartRow,
    filterEmptySourceRows: item.filterEmptySourceRows
  }));

const previewLoader = async (
  tableIndex: number,
  options: {
    previewRows?: number;
    headerRowIndex?: number;
    headerRowCount?: number;
    dataStartRowIndex?: number;
  }
): Promise<TableData> => {
  if (!sourceSessionId.value) {
    throw new Error("来源会话不存在");
  }

  const res = await getBatchReplyTablePreview(sourceSessionId.value, tableIndex, options);
  if (res.code !== 0) {
    throw new Error(res.message || "加载来源表格预览失败");
  }

  return res.data;
};

const handlePreview = async () => {
  if (!ensurePermission("btn:batch-reply:preview", "权限不足，无法执行批量回复预检")) {
    return;
  }

  if (!sourceSessionId.value) {
    ElMessage.warning("请先上传来源文件");
    return;
  }

  if (selectedTableConfigs.value.length === 0) {
    ElMessage.warning("请至少选择一个来源表格");
    return;
  }

  if (targetFiles.value.length === 0) {
    ElMessage.warning("请至少添加一个目标文件");
    return;
  }

  stopPreviewRequest();
  const controller = new AbortController();
  previewAbortController.value = controller;
  previewing.value = true;
  try {
    const res = await previewBatchReply(
      sourceSessionId.value,
      buildPreviewRequestTables(),
      targetFiles.value.map(item => item.file),
      { signal: controller.signal }
    );

    if (previewAbortController.value !== controller) {
      return;
    }

    if (res.code !== 0) {
      ElMessage.error(res.message || "批量回复预检失败");
      return;
    }

    previewResult.value = res.data;
    executeResult.value = null;
    if (res.data.readyCount > 0) {
      ElMessage.success(`预检完成，可直接应用 ${res.data.readyCount} 份文件`);
    } else {
      ElMessage.warning("预检完成，但没有可直接应用的文件");
    }
  } catch (error: any) {
    if (error?.name === "CanceledError" || error?.name === "AbortError") {
      return;
    }

    ElMessage.error("批量回复预检失败");
  } finally {
    if (previewAbortController.value === controller) {
      previewAbortController.value = null;
      previewing.value = false;
    }
  }
};

const handleExecute = async () => {
  if (
    !ensurePermission("btn:batch-reply:execute", "权限不足，无法执行批量回复") ||
    !ensurePermission("api:batch-reply:download", "权限不足，无法下载批量回复结果")
  ) {
    return;
  }

  if (!sourceSessionId.value) {
    ElMessage.warning("请先上传来源文件");
    return;
  }

  if (readyFiles.value.length === 0) {
    ElMessage.warning("请先完成预检，并确保至少有一份文件可应用");
    return;
  }

  executing.value = true;
  try {
    const res = await executeBatchReply({
      sessionId: sourceSessionId.value
    });

    if (res.code !== 0) {
      ElMessage.error(res.message || "批量回复执行失败");
      return;
    }

    executeResult.value = res.data;
    const blob = await downloadBatchReplyResult(res.data.taskId);
    triggerBrowserDownload(blob, res.data.downloadFileName);
    ElMessage.success(
      res.data.failedCount > 0
        ? `批量回复完成，成功 ${res.data.successCount} 份，失败 ${res.data.failedCount} 份`
        : `批量回复完成，成功 ${res.data.successCount} 份`
    );
  } catch {
    ElMessage.error("批量回复执行失败");
  } finally {
    executing.value = false;
  }
};
</script>

<template>
  <div class="batch-reply-page">
    <div class="hero">
      <div class="hero-copy">
        <div class="eyebrow">严格复用</div>
        <h1>批量回复</h1>
        <p>
          上传一份人工已回复的同模板文档，将其中的验收与备注批量应用到本地目标文件。
          该能力不会重新匹配，不会调用 AI，只会在严格复用规则完全成立时写回结果。
        </p>
      </div>
      <el-alert
        class="hero-alert"
        title="仅支持同格式严格复用"
        type="info"
        :closable="false"
        show-icon
      >
        <template #default>
          仅支持 <strong>docx -&gt; docx</strong> 与 <strong>xlsx -&gt; xlsx</strong>；
          只有项目、规格、顺序和表格结构都完全一致时才允许批量回复。
        </template>
      </el-alert>
    </div>

    <el-steps class="workflow-steps" :active="currentStep" finish-status="success" align-center>
      <el-step
        v-for="step in steps"
        :key="step.title"
        :title="step.title"
        :description="step.description"
      />
    </el-steps>

    <div class="content-grid">
      <el-card class="section-card" shadow="hover">
        <template #header>
          <div class="section-header">
            <span>1. 来源文件</span>
            <el-button
              v-if="sourceFile"
              type="danger"
              link
              @click="resetSourceState"
            >
              重新选择
            </el-button>
          </div>
        </template>

        <el-upload
          v-if="canUploadSourceFile && !sourceFile"
          class="upload-area"
          drag
          :show-file-list="false"
          :http-request="handleSourceUpload"
          accept=".docx,.xlsx"
          :disabled="sourceUploading"
        >
          <el-icon class="el-icon--upload" :size="56">
            <UploadFilled />
          </el-icon>
          <div class="el-upload__text">
            将已回复文档拖到此处，或 <em>点击上传</em>
          </div>
          <template #tip>
            <div class="el-upload__tip">
              仅支持 .docx / .xlsx，文件大小不超过 50MB
            </div>
          </template>
        </el-upload>

        <el-alert
          v-else-if="!sourceFile"
          type="warning"
          :closable="false"
          show-icon
          title="当前账号没有来源文件上传权限"
        />

        <div v-else class="source-summary">
          <div class="source-file-name">{{ sourceFile.sourceFileName }}</div>
          <div class="source-meta">
            <el-tag size="small" type="primary">
              {{ sourceIsExcel ? "Excel" : "Word" }}
            </el-tag>
            <span>检测到 {{ sourceFile.tableCount }} 个{{ sourceIsExcel ? "工作表" : "表格" }}</span>
            <span v-if="!canUploadSourceFile" class="permission-tip">
              当前账号仅可查看，无法重新上传来源文件
            </span>
          </div>
        </div>
      </el-card>

      <el-card class="section-card" shadow="hover">
        <template #header>
          <div class="section-header">
            <span>2. 表格配置</span>
            <span class="section-subtitle">参考智能填充，逐表指定项目/规格/验收/备注列</span>
          </div>
        </template>

        <el-empty
          v-if="!sourceFile"
          description="请先上传来源文件"
        />
        <BatchTableConfig
          v-else
          v-model="batchTableConfigs"
          :tables="sourceTables"
          :is-excel="sourceIsExcel"
          :preview-loader="previewLoader"
        />
      </el-card>

      <el-card class="section-card" shadow="hover">
        <template #header>
          <div class="section-header">
            <span>3. 目标文件</span>
            <span class="section-subtitle">一次性添加多个同模板文件，预检时再统一上传</span>
          </div>
        </template>

        <el-upload
          ref="targetUploadRef"
          :key="targetUploadKey"
          class="upload-area"
          drag
          multiple
          :auto-upload="false"
          :show-file-list="false"
          :on-change="handleTargetFileChange"
          :accept="sourceFile ? targetAccept : '.docx,.xlsx'"
          :disabled="!sourceFile"
        >
          <el-icon class="el-icon--upload" :size="52">
            <UploadFilled />
          </el-icon>
          <div class="el-upload__text">
            <span v-if="!sourceFile">请先上传来源文件，再添加目标文件</span>
            <span v-else>
              将目标文件拖到此处，或 <em>点击添加</em>
            </span>
          </div>
          <template #tip>
            <div class="el-upload__tip">
              {{ sourceFile ? `当前仅接受 ${targetAccept} 格式` : "来源文件确认后自动限定同格式上传" }}
            </div>
          </template>
        </el-upload>

        <div v-if="targetFiles.length > 0" class="target-table">
          <el-table :data="targetFiles" border>
            <el-table-column label="文件名" min-width="320">
              <template #default="{ row }">
                {{ row.file.name }}
              </template>
            </el-table-column>
            <el-table-column label="大小" width="120">
              <template #default="{ row }">
                {{ formatFileSize(row.file.size) }}
              </template>
            </el-table-column>
            <el-table-column label="操作" width="100" align="center">
              <template #default="{ row }">
                <el-button type="danger" link @click="removeTargetFile(row.id)">
                  移除
                </el-button>
              </template>
            </el-table-column>
          </el-table>
        </div>
      </el-card>

      <el-card class="section-card result-card" shadow="hover">
        <template #header>
          <div class="section-header">
            <span>4. 预检与执行</span>
            <span class="section-subtitle">先逐文件核验，再批量写回并下载</span>
          </div>
        </template>

        <div class="action-row">
          <el-button
            type="primary"
            :loading="previewing"
            :disabled="!sourceFile || targetFiles.length === 0 || selectedTableCount === 0"
            @click="handlePreview"
          >
            预检批量回复
          </el-button>
          <el-button
            type="success"
            :loading="executing"
            :disabled="readyFiles.length === 0 || !canExecuteBatchReply || !canDownloadBatchReply"
            @click="handleExecute"
          >
            执行批量回复
          </el-button>
          <span class="action-tip">
            预检通过 {{ readyFiles.length }} / {{ previewResult?.files.length ?? targetFiles.length }} 份
          </span>
        </div>

        <el-empty
          v-if="!previewResult"
          description="完成来源配置并添加目标文件后，点击“预检批量回复”查看逐文件结果"
        />

        <template v-else>
          <el-alert
            :title="`预检完成：可应用 ${previewResult.readyCount} 份，共 ${previewResult.totalCount} 份`"
            :type="previewResult.readyCount > 0 ? 'success' : 'warning'"
            :closable="false"
            show-icon
          />

          <el-table class="preview-table" :data="previewResult.files" border>
            <el-table-column prop="fileName" label="文件名" min-width="280" />
            <el-table-column label="状态" width="120" align="center">
              <template #default="{ row }">
                <el-tag :type="row.canApply ? 'success' : 'danger'">
                  {{ row.canApply ? "可应用" : "不可应用" }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column label="失败原因" min-width="320">
              <template #default="{ row }">
                <span v-if="row.errors.length === 0">严格复用通过</span>
                <span v-else>{{ row.errors.join("；") }}</span>
              </template>
            </el-table-column>
          </el-table>

          <div v-if="executeResult" class="execute-summary">
            <el-alert
              :title="`执行完成：成功 ${executeResult.successCount} 份，失败 ${executeResult.failedCount} 份`"
              type="success"
              :closable="false"
              show-icon
            />
            <el-table class="preview-table" :data="executeResult.files" border>
              <el-table-column prop="fileName" label="文件名" min-width="280" />
              <el-table-column label="结果" width="120" align="center">
                <template #default="{ row }">
                  <el-tag :type="row.success ? 'success' : 'danger'">
                    {{ row.success ? "成功" : "失败" }}
                  </el-tag>
                </template>
              </el-table-column>
              <el-table-column prop="message" label="说明" min-width="320" />
            </el-table>
          </div>
        </template>
      </el-card>
    </div>
  </div>
</template>

<style scoped>
.batch-reply-page {
  display: flex;
  flex-direction: column;
  gap: 20px;
  padding: 4px;
}

.hero {
  display: grid;
  grid-template-columns: minmax(0, 1.5fr) minmax(320px, 0.9fr);
  gap: 20px;
  padding: 26px 28px;
  border-radius: 24px;
  background:
    radial-gradient(circle at top left, rgba(45, 123, 255, 0.12), transparent 42%),
    linear-gradient(135deg, #f7fbff 0%, #f5f7fb 55%, #eef4ff 100%);
  border: 1px solid rgba(30, 64, 175, 0.08);
}

.hero-copy h1 {
  margin: 6px 0 12px;
  font-size: 34px;
  line-height: 1.1;
  color: #10213a;
}

.hero-copy p {
  margin: 0;
  max-width: 700px;
  color: #4b5563;
  line-height: 1.75;
}

.eyebrow {
  font-size: 12px;
  letter-spacing: 0.2em;
  text-transform: uppercase;
  color: #1d4ed8;
  font-weight: 700;
}

.hero-alert {
  align-self: stretch;
}

.workflow-steps {
  padding: 0 8px;
}

.content-grid {
  display: grid;
  grid-template-columns: minmax(0, 1fr);
  gap: 20px;
}

.section-card {
  border-radius: 20px;
}

.section-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  font-weight: 600;
}

.section-subtitle {
  font-size: 12px;
  font-weight: 400;
  color: #6b7280;
}

.upload-area :deep(.el-upload-dragger) {
  width: 100%;
  min-height: 180px;
  border-radius: 18px;
  border-color: #d8e5ff;
  background: linear-gradient(180deg, #ffffff 0%, #f7fbff 100%);
}

.source-summary {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.source-file-name {
  font-size: 18px;
  font-weight: 600;
  color: #10213a;
  word-break: break-word;
}

.source-meta {
  display: flex;
  gap: 10px;
  flex-wrap: wrap;
  color: #6b7280;
  font-size: 13px;
}

.permission-tip {
  color: #b45309;
}

.target-table,
.preview-table,
.execute-summary {
  margin-top: 16px;
}

.result-card {
  margin-bottom: 8px;
}

.action-row {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
  margin-bottom: 16px;
}

.action-tip {
  color: #6b7280;
  font-size: 13px;
}

@media (max-width: 960px) {
  .hero {
    grid-template-columns: 1fr;
    padding: 22px 20px;
  }

  .hero-copy h1 {
    font-size: 28px;
  }

  .section-header {
    align-items: flex-start;
    flex-direction: column;
  }
}
</style>
