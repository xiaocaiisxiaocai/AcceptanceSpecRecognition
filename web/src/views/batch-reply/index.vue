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
  uploadBatchReplySource
} from "@/api/matching";
import type { TableInfo } from "@/api/document";
import { validateBatchReplySourceFile } from "./target-upload";
import { prunePreviewResultsForConfigChange } from "./batch-reply-preview-state";
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

defineOptions({ name: "BatchReplyPage" });

const activeRootTab = ref("source");
const activeSourceFileTab = ref("");
const sourceFile = ref<BatchReplySourceFileState | null>(null);
const sourceTables = ref<TableInfo[]>([]);
const sourceConfigs = ref<BatchTableConfigItem[]>([]);
const targetFiles = ref<BatchReplyTargetState[]>([]);
const sourceUploading = ref(false);

const permissionState = computed(() => buildBatchReplyPermissionState(hasPerms));
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
const canUploadSourceFile = computed(() => permissionState.value.canUploadSourceFile);
const canUploadTargetFile = computed(() => permissionState.value.canUploadTargetFile);
const canPreviewBatchReply = computed(() => permissionState.value.canPreviewBatchReply);
const selectedSourceConfigs = computed(() => batchReplyState.value.selectedSourceConfigs);
const selectedSourceTableOptions = computed(() => batchReplyState.value.selectedSourceTableOptions);
const executableTargets = computed(() => batchReplyState.value.executableTargets);
const duplicateDialogVisible = computed(() => batchReplyState.value.duplicateDialogVisible);
const executeDisabled = computed(() => batchReplyState.value.executeDisabled);

const {
  activeTargetFileId,
  targetUploading,
  targetUploadKey,
  targetUploadRef,
  handleTargetFileChange,
  resetTargetUploadState
} = useBatchReplyTargetUploads({
  sourceFile,
  targetFiles,
  sourceSessionId,
  targetAccept,
  selectedSourceTableOptions,
  activeRootTab
});

const {
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

const { executeResult, executing, executeReadyTargets } = useBatchReplyExecution({
  sourceSessionId,
  selectedSourceConfigs,
  executableTargets,
  activeRootTab
});
const resultFiles = computed(() => getBatchReplyResultFiles(executeResult.value));

const formatFileSize = (size: number) => {
  if (size < 1024) return `${size} B`;
  if (size < 1024 * 1024) return `${(size / 1024).toFixed(1)} KB`;
  return `${(size / (1024 * 1024)).toFixed(1)} MB`;
};

const clearTargetPreviews = () => {
  targetFiles.value = targetFiles.value.map(file => ({
    ...file,
    previewResults: {},
    previewLoadingTableIndex: undefined
  }));
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
        selectedSourceTableOptions.value.some(option => option.value === config.sourceTableIndex)
      ) {
        return config;
      }

      return {
        ...config,
        sourceTableIndex: defaultSourceTableIndex
      };
    }),
    previewResults: {},
  }));
};

const resetTargetState = () => {
  targetFiles.value = [];
  resetTargetUploadState();
};

const resetAllState = () => {
  sourceFile.value = null;
  sourceTables.value = [];
  sourceConfigs.value = [];
  executeResult.value = null;
  resetTargetState();
};

const loadSourceTables = async (sessionId: string, fileType: number) => {
  const res = await getBatchReplyTables(sessionId);
  if (res.code !== 0) {
    throw new Error(res.message || "加载来源表格失败");
  }

  sourceTables.value = res.data;
  sourceConfigs.value = res.data.map(table => buildTableConfig(table, fileType === 1, true));
};

const handleSourceConfigChange = (value: BatchTableConfigItem[]) => {
  sourceConfigs.value = value;
  clearTargetPreviews();
  syncTargetSourceDefaults();
};

const handleTargetConfigChange = (targetId: string, value: BatchTableConfigItem[]) => {
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

const handleSourceUpload = async (options: UploadRequestOptions) => {
  if (!ensurePermission("api:batch-reply:upload-source", "权限不足，无法上传来源文件")) {
    return;
  }

  const file = options.file as File;
  const sourceFileError = validateBatchReplySourceFile(file);
  if (sourceFileError) {
    ElMessage.error(sourceFileError);
    return;
  }

  sourceUploading.value = true;
  try {
    const res = await uploadBatchReplySource(file);
    if (res.code !== 0) {
      ElMessage.error(res.message || "来源文件上传失败");
      return;
    }

    resetAllState();
    sourceFile.value = res.data;
    activeSourceFileTab.value = res.data.sessionId;
    await loadSourceTables(res.data.sessionId, res.data.sourceFileType);
    activeRootTab.value = "source";
    ElMessage.success("来源文件上传成功");
  } catch {
    ElMessage.error("来源文件上传失败");
  } finally {
    sourceUploading.value = false;
  }
};

const removeTargetFile = (targetId: string) => {
  targetFiles.value = targetFiles.value.filter(item => item.targetId !== targetId);
  if (activeTargetFileId.value === targetId) {
    activeTargetFileId.value = targetFiles.value[0]?.targetId ?? "";
  }

  if (duplicateDialog.value?.targetId === targetId) {
    duplicateDialog.value = null;
  }
};
</script>

<template>
  <div class="batch-reply-page page-shell">
    <div class="page-header">
      <div class="page-header__main">
        <div class="page-header__eyebrow">批量工作台</div>
        <h1>批量回复</h1>
        <p>
          以来源文件为基准，按文件和 Sheet/表格配置映射关系，统一完成同模板文档的回复回写。
        </p>
      </div>
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
        仅支持 <strong>docx -&gt; docx</strong> 与 <strong>xlsx -&gt; xlsx</strong>；
        匹配键为项目 + 规格；允许行顺序不同；写回仅更新验收列和备注列。
      </div>
    </div>

    <div class="workflow-panel">
      <el-tabs v-model="activeRootTab" class="root-tabs workstep-tabs">
        <el-tab-pane label="来源文件" name="source">
          <div class="content-grid">
            <SourceUploadPanel
              :source-file="sourceFile"
              :source-is-excel="sourceIsExcel"
              :can-upload-source-file="canUploadSourceFile"
              :source-uploading="sourceUploading"
              :upload-request="handleSourceUpload"
              @reset="resetAllState"
            />
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
              v-model:target-upload-ref="targetUploadRef"
              v-model:active-target-file-id="activeTargetFileId"
              :can-preview-batch-reply="canPreviewBatchReply"
              :can-upload-target-file="canUploadTargetFile"
              :format-file-size="formatFileSize"
              :get-target-preview-loader="getTargetPreviewLoader"
              :selected-source-table-options="selectedSourceTableOptions"
              :source-file="sourceFile"
              :target-accept="targetAccept"
              :target-files="targetFiles"
              :target-uploading="targetUploading"
              :target-upload-key="targetUploadKey"
              @config-change="handleTargetConfigChange"
              @file-change="handleTargetFileChange"
              @preview="handleTargetTablePreview"
              @remove="removeTargetFile"
            />
          </div>
        </el-tab-pane>

        <el-tab-pane label="执行结果" name="result">
          <BatchReplyResultPanel
            :execute-disabled="executeDisabled"
            :execute-result="executeResult"
            :executable-target-count="executableTargets.length"
            :executing="executing"
            :result-files="resultFiles"
            :target-file-count="targetFiles.length"
            @execute="executeReadyTargets"
          />
        </el-tab-pane>
      </el-tabs>
    </div>

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
