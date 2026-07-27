<script setup lang="ts">
import { computed, ref } from "vue";
import { ElMessage } from "element-plus";
import type { UploadRequestOptions } from "element-plus";
import type { BatchTableConfigItem } from "@/views/smart-fill/components/batchTableConfig.types";
import BatchReplyResultPanel from "./components/BatchReplyResultPanel.vue";
import DuplicateResolutionDialog from "./components/DuplicateResolutionDialog.vue";
import SourceConfigPanel from "./components/SourceConfigPanel.vue";
import SourceUploadPanel from "./components/SourceUploadPanel.vue";
import TargetFilesPanel from "./components/TargetFilesPanel.vue";
import {
  getBatchReplyTables,
  uploadBatchReplySource,
  uploadBatchReplyTargets,
  previewBatchReply,
  executeBatchReply,
  downloadBatchReplyResult
} from "@/api/matching";
import type { TableInfo } from "@/api/document";
import { validateBatchReplySourceFile } from "./target-upload";
import {
  buildBatchReplyPreviewFingerprint,
  prunePreviewResultsForConfigChange
} from "./batch-reply-preview-state";
import {
  buildTableConfig,
  resolveDefaultSourceTableIndex
} from "./batch-reply-table-config";
import { getDuplicateSourceLabel } from "./batch-reply-duplicates";
import {
  buildBatchReplyDerivedState,
  buildBatchReplyPermissionState,
  getBatchReplyResultFiles,
  type BatchReplySourceFileState,
  type BatchReplyTargetState
} from "./batch-reply-state";
import { useBatchReplyExecution } from "./composables/useBatchReplyExecution";
import { useBatchReplyPreview } from "./composables/useBatchReplyPreview";
import { useBatchReplyTargetUploads } from "./composables/useBatchReplyTargetUploads";
import { hasPerms } from "@/utils/auth";
import { ensurePermission } from "@/utils/permission-guard";
import type { AppUploadRequestContext } from "@/components/useAppUploadTask";
import {
  isUploadRequestCancelled,
  throwIfUploadCancelled
} from "@/utils/upload-request";

defineOptions({ name: "BatchReplyPage" });

const activeRootTab = ref("source");
const activeSourceFileTab = ref("");
const sourceFile = ref<BatchReplySourceFileState | null>(null);
const sourceTables = ref<TableInfo[]>([]);
const sourceConfigs = ref<BatchTableConfigItem[]>([]);
const targetFiles = ref<BatchReplyTargetState[]>([]);

const permissionState = computed(() =>
  buildBatchReplyPermissionState(hasPerms)
);
const batchReplyState = computed(() =>
  buildBatchReplyDerivedState({
    sourceFile: sourceFile.value,
    sourceConfigs: sourceConfigs.value,
    targetFiles: targetFiles.value,
    duplicateDialogVisible: duplicateDialog.value !== null,
    permissions: permissionState.value
  })
);
const sourceSessionId = computed(() => batchReplyState.value.sourceSessionId);
const sourceIsExcel = computed(() => batchReplyState.value.sourceIsExcel);
const targetAccept = computed(() => batchReplyState.value.targetAccept);
const canUploadSourceFile = computed(
  () => permissionState.value.canUploadSourceFile
);
const canUploadTargetFile = computed(
  () => permissionState.value.canUploadTargetFile
);
const canPreviewBatchReply = computed(
  () => permissionState.value.canPreviewBatchReply
);
const selectedSourceConfigs = computed(
  () => batchReplyState.value.selectedSourceConfigs
);
const selectedSourceTableOptions = computed(
  () => batchReplyState.value.selectedSourceTableOptions
);
const executableTargets = computed(
  () => batchReplyState.value.executableTargets
);
const duplicateDialogVisible = computed(
  () => batchReplyState.value.duplicateDialogVisible
);
const executeDisabled = computed(() => batchReplyState.value.executeDisabled);

const {
  activeTargetFileId,
  targetUploadKey,
  handleTargetFileChange,
  resetTargetUploadState
} = useBatchReplyTargetUploads({
  sourceFile,
  targetFiles,
  sourceSessionId,
  targetAccept,
  selectedSourceTableOptions,
  activeRootTab,
  onUploadTargets: (sessionId, pendingFiles, options) =>
    uploadBatchReplyTargets(sessionId, pendingFiles, options)
});

const {
  cancelTargetPreviews,
  duplicateDialog,
  closeDuplicateDialog,
  confirmDuplicateDialog,
  createSourcePreviewLoader,
  getTargetPreviewLoader,
  handleTargetTablePreview,
  updateDuplicateDialogStrategy
} = useBatchReplyPreview({
  sourceSessionId,
  selectedSourceConfigs,
  sourceConfigs,
  targetFiles
});

const {
  downloadError,
  downloadLoading,
  executeResult,
  executing,
  executeReadyTargets,
  retryDownload
} = useBatchReplyExecution({
  sourceSessionId,
  selectedSourceConfigs,
  executableTargets,
  activeRootTab
});
const resultFiles = computed(() =>
  getBatchReplyResultFiles(executeResult.value)
);

const formatFileSize = (size: number) => {
  if (size < 1024) return `${size} B`;
  if (size < 1024 * 1024) return `${(size / 1024).toFixed(1)} KB`;
  return `${(size / (1024 * 1024)).toFixed(1)} MB`;
};

const syncTargetSourceDefaults = () => {
  targetFiles.value = targetFiles.value.map(file => ({
    ...file,
    configs: file.configs.map(config => {
      const defaultSourceTableIndex = resolveDefaultSourceTableIndex(
        config.tableIndex,
        selectedSourceTableOptions.value
      );
      if (selectedSourceTableOptions.value.length === 0) {
        return {
          ...config,
          sourceTableIndex: undefined
        };
      }

      if (
        config.sourceTableIndex !== undefined &&
        selectedSourceTableOptions.value.some(
          option => option.value === config.sourceTableIndex
        )
      ) {
        return config;
      }

      return {
        ...config,
        sourceTableIndex: defaultSourceTableIndex
      };
    }),
    previewResults: {}
  }));
};

const resetTargetState = () => {
  cancelTargetPreviews();
  targetFiles.value = [];
  resetTargetUploadState();
};

const resetAllState = () => {
  sourceFile.value = null;
  sourceTables.value = [];
  sourceConfigs.value = [];
  executeResult.value = null;
  downloadError.value = "";
  resetTargetState();
};

const fetchSourceTableState = async (sessionId: string, fileType: number) => {
  const res = await getBatchReplyTables(sessionId);
  if (res.code !== 0) {
    throw new Error(res.message || "加载来源表格失败");
  }

  return {
    tables: res.data,
    configs: res.data.map(table =>
      buildTableConfig(table, fileType === 1, true)
    )
  };
};

const handleSourceConfigChange = (value: BatchTableConfigItem[]) => {
  sourceConfigs.value = value;
  cancelTargetPreviews();
  syncTargetSourceDefaults();
};

const handleTargetConfigChange = (
  targetId: string,
  value: BatchTableConfigItem[]
) => {
  const targetFile = targetFiles.value.find(file => file.targetId === targetId);
  if (targetFile) {
    const nextConfigMap = new Map(
      value.map(config => [config.tableIndex, config])
    );
    targetFile.configs.forEach(config => {
      const nextConfig = nextConfigMap.get(config.tableIndex);
      if (
        buildBatchReplyPreviewFingerprint(
          sourceSessionId.value,
          targetId,
          config
        ) !==
        buildBatchReplyPreviewFingerprint(
          sourceSessionId.value,
          targetId,
          nextConfig
        )
      ) {
        cancelTargetPreviews(targetId, config.tableIndex);
      }
    });
  }

  targetFiles.value = targetFiles.value.map(file =>
    file.targetId === targetId
      ? {
          ...file,
          configs: value,
          previewResults: prunePreviewResultsForConfigChange(
            file.previewResults,
            file.configs,
            value
          )
        }
      : file
  );
};

const handleSourceUpload = async (
  options: UploadRequestOptions,
  context: AppUploadRequestContext
) => {
  if (
    !ensurePermission(
      "api:batch-reply:upload-source",
      "权限不足，无法上传来源文件"
    )
  ) {
    throw new Error("权限不足，无法上传来源文件");
  }

  const file = options.file as File;
  const sourceFileError = validateBatchReplySourceFile(file);
  if (sourceFileError) {
    ElMessage.error(sourceFileError);
    throw new Error(sourceFileError);
  }

  try {
    const res = await uploadBatchReplySource(file, context);
    throwIfUploadCancelled(context.signal);
    if (res.code !== 0) throw new Error(res.message || "来源文件上传失败");

    const sourceTableState = await fetchSourceTableState(
      res.data.sessionId,
      res.data.sourceFileType
    );
    throwIfUploadCancelled(context.signal);

    resetAllState();
    sourceFile.value = res.data;
    activeSourceFileTab.value = res.data.sessionId;
    sourceTables.value = sourceTableState.tables;
    sourceConfigs.value = sourceTableState.configs;
    activeRootTab.value = "source";
    ElMessage.success("来源文件上传成功");
  } catch (error) {
    if (isUploadRequestCancelled(error)) throw error;
    ElMessage.error("来源文件上传失败");
    throw error;
  }
};

const removeTargetFile = (targetId: string) => {
  cancelTargetPreviews(targetId);
  targetFiles.value = targetFiles.value.filter(
    item => item.targetId !== targetId
  );
  if (activeTargetFileId.value === targetId) {
    activeTargetFileId.value = targetFiles.value[0]?.targetId ?? "";
  }

  if (duplicateDialog.value?.targetId === targetId) {
    duplicateDialog.value = null;
  }
};
</script>

<template>
  <div class="page page--fill page-shell batch-reply-page">
    <div class="page-header">
      <div class="page-header__stats">
        <div class="header-stat">
          <span class="header-stat__label">来源表</span>
          <strong>{{ sourceTables.length }}</strong>
        </div>
        <div class="header-stat">
          <span class="header-stat__label">目标文件</span>
          <strong>{{ targetFiles.length }}</strong>
        </div>
        <div class="header-stat">
          <span class="header-stat__label">可执行</span>
          <strong>{{ executableTargets.length }}</strong>
        </div>
      </div>
    </div>

    <div class="rule-strip">
      <div class="rule-strip__title">执行规则</div>
      <div class="rule-strip__content">
        仅支持 <strong>docx -&gt; docx</strong> 与
        <strong>xlsx -&gt; xlsx</strong>； 匹配键为项目 +
        规格；允许行顺序不同；写回仅更新验收列和备注列。
      </div>
    </div>

    <div class="workflow-panel">
      <el-tabs v-model="activeRootTab" class="root-tabs workstep-tabs">
        <el-tab-pane label="来源文件" name="source">
          <div class="content-grid">
            <SourceUploadPanel
              v-if="canUploadSourceFile && !sourceFile"
              :source-file="sourceFile"
              :source-is-excel="sourceIsExcel"
              :can-upload-source-file="canUploadSourceFile"
              :upload-request="handleSourceUpload"
              @reset="resetAllState"
            />
            <el-empty
              v-else-if="!canUploadSourceFile && !sourceFile"
              description="当前账号没有来源文件上传权限"
            />
            <SourceUploadPanel
              v-else
              :source-file="sourceFile"
              :source-is-excel="sourceIsExcel"
              :can-upload-source-file="canUploadSourceFile"
              :upload-request="handleSourceUpload"
              @reset="resetAllState"
            />
            <!--
              页面级权限常量（定义于 batch-reply-state.ts）：
                btn:batch-reply:preview — 控制预览操作
                btn:batch-reply:execute — 控制执行操作
            -->
            <SourceConfigPanel
              v-model:active-tab="activeSourceFileTab"
              :source-file="sourceFile"
              :source-tables="sourceTables"
              :source-configs="sourceConfigs"
              :source-is-excel="sourceIsExcel"
              :preview-loader="createSourcePreviewLoader"
              @config-change="handleSourceConfigChange"
            />
          </div>
        </el-tab-pane>

        <el-tab-pane label="目标文件" name="target" :disabled="!sourceFile">
          <div class="content-grid">
            <TargetFilesPanel
              v-model:active-target-file-id="activeTargetFileId"
              :can-preview-batch-reply="canPreviewBatchReply"
              :can-upload-target-file="canUploadTargetFile"
              :format-file-size="formatFileSize"
              :get-target-preview-loader="getTargetPreviewLoader"
              :selected-source-table-options="selectedSourceTableOptions"
              :source-file="sourceFile"
              :target-accept="targetAccept"
              :target-files="targetFiles"
              :target-upload-key="targetUploadKey"
              :upload-request="handleTargetFileChange"
              @config-change="handleTargetConfigChange"
              @preview="handleTargetTablePreview"
              @remove="removeTargetFile"
            />
          </div>
        </el-tab-pane>

        <el-tab-pane label="执行结果" name="result">
          <BatchReplyResultPanel
            :download-error="downloadError"
            :download-loading="downloadLoading"
            :execute-disabled="executeDisabled"
            :execute-result="executeResult"
            :executable-target-count="executableTargets.length"
            :executing="executing"
            :result-files="resultFiles"
            :target-file-count="targetFiles.length"
            @execute="executeReadyTargets"
            @retry-download="retryDownload"
          />
        </el-tab-pane>
      </el-tabs>
    </div>

    <!-- 重复项处理：当来源/目标文件中存在重复项目+规格组合时，弹出以下对话框由用户选择处理策略
         支持的策略：保留首条 / 保留末条 / 跳过该组
         后端返回的结构化冲突通过 duplicateGroups 字段传入 -->
    <!-- 目标文件工作区以 file-stage-panel 为容器类，保持与来源文件区域一致的企业面板风格 -->
    <DuplicateResolutionDialog
      :dialog="duplicateDialog"
      :model-value="duplicateDialogVisible"
      :get-source-label="getDuplicateSourceLabel"
      @update:strategy="updateDuplicateDialogStrategy"
      @close="closeDuplicateDialog"
      @confirm="confirmDuplicateDialog"
    />
  </div>
</template>

<style scoped src="./index.styles.css"></style>
