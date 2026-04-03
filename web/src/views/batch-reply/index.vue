<script setup lang="ts">
import { computed, ref } from "vue";
import { ElMessage } from "element-plus";
import { UploadFilled } from "@element-plus/icons-vue";
import type { UploadFile, UploadFiles, UploadInstance, UploadRequestOptions } from "element-plus";
import BatchTableConfigPanel from "@/views/smart-fill/components/BatchTableConfig.vue";
import type { BatchTableConfigItem } from "@/views/smart-fill/components/BatchTableConfig.vue";
import {
  downloadBatchReplyResult,
  executeBatchReply,
  getBatchReplyTablePreview,
  getBatchReplyTables,
  getBatchReplyTargetTablePreview,
  getBatchReplyTargetTables,
  previewBatchReplyTable,
  uploadBatchReplySource,
  uploadBatchReplyTargets,
  type BatchReplyDuplicateGroup,
  type BatchReplyDuplicateResolution,
  type BatchReplyDuplicateStrategy,
  type BatchReplyExecuteResponse,
  type BatchReplyTablePreviewResponse,
  type BatchReplyUploadedTargetFile,
  type BatchTableConfig as ApiBatchTableConfig
} from "@/api/matching";
import type { TableData, TableInfo } from "@/api/document";
import { createTargetFileSignature, decideTargetUpload } from "./target-upload";
import {
  createTargetPreviewLoaderResolver,
  prunePreviewResultsForConfigChange
} from "./batch-reply-preview-state";
import { hasPerms } from "@/utils/auth";
import { ensurePermission } from "@/utils/permission-guard";

defineOptions({ name: "BatchReplyPage" });

type BatchReplySourceFile = {
  sessionId: string;
  sourceFileName: string;
  sourceFileType: number;
  tableCount: number;
};

type BatchReplyTargetState = {
  targetId: string;
  fileName: string;
  fileType: number;
  tableCount: number;
  size: number;
  signature: string;
  tables: TableInfo[];
  configs: BatchTableConfigItem[];
  previewResults: Record<number, BatchReplyTablePreviewResponse | null>;
  previewLoadingTableIndex?: number;
};

type BatchReplyDuplicateDialogState = {
  targetId: string;
  tableIndex: number;
  sourceTableIndex: number;
  groups: BatchReplyDuplicateGroup[];
  strategies: Record<string, BatchReplyDuplicateStrategy>;
};

const activeRootTab = ref("source");
const activeSourceFileTab = ref("");
const sourceFile = ref<BatchReplySourceFile | null>(null);
const sourceTables = ref<TableInfo[]>([]);
const sourceConfigs = ref<BatchTableConfigItem[]>([]);
const targetFiles = ref<BatchReplyTargetState[]>([]);
const executeResult = ref<BatchReplyExecuteResponse | null>(null);
const sourceUploading = ref(false);
const targetUploading = ref(false);
const executing = ref(false);
const targetUploadKey = ref(0);
const targetUploadRef = ref<UploadInstance>();
const activeTargetFileId = ref("");
const duplicateDialog = ref<BatchReplyDuplicateDialogState | null>(null);
const pendingTargetUploadFiles = ref<File[]>([]);
const pendingTargetUploadSignatures = ref<string[]>([]);
let targetUploadFlushTimer: number | undefined;

const sourceSessionId = computed(() => sourceFile.value?.sessionId ?? "");
const sourceIsExcel = computed(() => sourceFile.value?.sourceFileType === 1);
const targetAccept = computed(() => (sourceIsExcel.value ? ".xlsx" : ".docx"));
const canUploadSourceFile = computed(() => hasPerms("api:batch-reply:upload-source"));
const canUploadTargetFile = computed(() => hasPerms("api:batch-reply:upload"));
const canPreviewBatchReply = computed(() => hasPerms("btn:batch-reply:preview"));
const canExecuteBatchReply = computed(() => hasPerms("btn:batch-reply:execute"));
const canDownloadBatchReply = computed(() => hasPerms("api:batch-reply:download"));
const selectedSourceConfigs = computed(() => sourceConfigs.value.filter(item => item.selected));
const selectedSourceTableOptions = computed(() =>
  selectedSourceConfigs.value.map(item => ({
    value: item.tableIndex,
    label: item.tableInfo.name || `来源表 ${item.tableIndex + 1}`
  }))
);
const executableTargets = computed(() => targetFiles.value.filter(isTargetExecutable));
const duplicateDialogVisible = computed(() => duplicateDialog.value !== null);

const formatFileSize = (size: number) => {
  if (size < 1024) return `${size} B`;
  if (size < 1024 * 1024) return `${(size / 1024).toFixed(1)} KB`;
  return `${(size / (1024 * 1024)).toFixed(1)} MB`;
};

const resolveDefaultSourceTableIndex = (tableIndex: number) => {
  const options = selectedSourceTableOptions.value;
  if (options.length === 0) {
    return undefined;
  }

  return options.some(option => option.value === tableIndex) ? tableIndex : options[0].value;
};

const buildExcelTableConfig = (
  table: TableInfo,
  selected: boolean,
  sourceTableIndex?: number
): BatchTableConfigItem => {
  const usedStartRow = Math.max(1, table.usedRangeStartRow ?? 1);
  const totalColumns = Math.max(table.columnCount, table.headers.length, 1);
  const clampColumnIndex = (preferredIndex: number) =>
    Math.min(preferredIndex, totalColumns - 1);

  return {
    tableIndex: table.index,
    sourceTableIndex,
    projectColumnIndex: clampColumnIndex(0),
    specificationColumnIndex: clampColumnIndex(1),
    acceptanceColumnIndex: clampColumnIndex(2),
    remarkColumnIndex: totalColumns > 3 ? 3 : undefined,
    headerRowStart: usedStartRow,
    headerRowCount: 1,
    dataStartRow: usedStartRow + 1,
    filterEmptySourceRows: true,
    duplicateResolutions: [],
    selected,
    tableInfo: table
  };
};

const buildWordTableConfig = (
  table: TableInfo,
  selected: boolean,
  sourceTableIndex?: number
): BatchTableConfigItem => {
  const totalColumns = Math.max(table.columnCount, table.headers.length, 1);
  const clampColumnIndex = (preferredIndex: number) =>
    Math.min(preferredIndex, totalColumns - 1);

  return {
    tableIndex: table.index,
    sourceTableIndex,
    projectColumnIndex: clampColumnIndex(0),
    specificationColumnIndex: clampColumnIndex(1),
    acceptanceColumnIndex: clampColumnIndex(2),
    remarkColumnIndex: totalColumns > 3 ? 3 : undefined,
    headerRowStart: 1,
    headerRowCount: 1,
    dataStartRow: 2,
    filterEmptySourceRows: true,
    duplicateResolutions: [],
    selected,
    tableInfo: table
  };
};

const buildTableConfig = (
  table: TableInfo,
  isExcel: boolean,
  selected: boolean,
  sourceTableIndex?: number
) =>
  isExcel
    ? buildExcelTableConfig(table, selected, sourceTableIndex)
    : buildWordTableConfig(table, selected, sourceTableIndex);

const toBatchTableConfig = (item: BatchTableConfigItem): ApiBatchTableConfig => ({
  tableIndex: item.tableIndex,
  sourceTableIndex: item.sourceTableIndex,
  projectColumnIndex: item.projectColumnIndex,
  specificationColumnIndex: item.specificationColumnIndex,
  acceptanceColumnIndex: item.acceptanceColumnIndex,
  remarkColumnIndex: item.remarkColumnIndex,
  headerRowStart: item.headerRowStart,
  headerRowCount: item.headerRowCount,
  dataStartRow: item.dataStartRow,
  filterEmptySourceRows: item.filterEmptySourceRows,
  duplicateResolutions: item.duplicateResolutions
});

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
      const defaultSourceTableIndex = resolveDefaultSourceTableIndex(config.tableIndex);
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
  activeTargetFileId.value = "";
  pendingTargetUploadFiles.value = [];
  pendingTargetUploadSignatures.value = [];
  if (targetUploadFlushTimer !== undefined) {
    window.clearTimeout(targetUploadFlushTimer);
    targetUploadFlushTimer = undefined;
  }
  targetUploadKey.value++;
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

const appendUploadedTarget = async (
  uploadedFile: BatchReplyUploadedTargetFile,
  rawFile: File
) => {
  const tablesResp = await getBatchReplyTargetTables(sourceSessionId.value, uploadedFile.targetId);
  if (tablesResp.code !== 0) {
    throw new Error(tablesResp.message || "加载目标表格失败");
  }

  const targetState: BatchReplyTargetState = {
    targetId: uploadedFile.targetId,
    fileName: uploadedFile.fileName,
    fileType: uploadedFile.fileType,
    tableCount: uploadedFile.tableCount,
    size: rawFile.size,
    signature: createTargetFileSignature(rawFile),
    tables: tablesResp.data,
    configs: tablesResp.data.map(table =>
      buildTableConfig(table, uploadedFile.fileType === 1, true, resolveDefaultSourceTableIndex(table.index))
    ),
    previewResults: {}
  };

  targetFiles.value = [...targetFiles.value, targetState];
  if (!activeTargetFileId.value) {
    activeTargetFileId.value = uploadedFile.targetId;
  }
};

const schedulePendingTargetUploadFlush = () => {
  if (targetUploadFlushTimer !== undefined) {
    return;
  }

  targetUploadFlushTimer = window.setTimeout(() => {
    targetUploadFlushTimer = undefined;
    void flushPendingTargetUploads();
  }, 0);
};

const flushPendingTargetUploads = async () => {
  if (targetUploading.value || pendingTargetUploadFiles.value.length === 0) {
    return;
  }

  const pendingFiles = [...pendingTargetUploadFiles.value];
  pendingTargetUploadFiles.value = [];
  pendingTargetUploadSignatures.value = [];

  targetUploading.value = true;
  try {
    const res = await uploadBatchReplyTargets(sourceSessionId.value, pendingFiles);
    if (res.code !== 0 || res.data.files.length === 0) {
      ElMessage.error(res.message || "目标文件上传失败");
      return;
    }

    for (let index = 0; index < res.data.files.length; index++) {
      const uploadedFile = res.data.files[index];
      const rawFile = pendingFiles[index];
      if (!uploadedFile || !rawFile) {
        continue;
      }

      await appendUploadedTarget(uploadedFile, rawFile);
    }

    activeRootTab.value = "target";
    ElMessage.success(
      pendingFiles.length === 1
        ? `${pendingFiles[0].name} 上传成功`
        : `已上传 ${pendingFiles.length} 个目标文件`
    );
  } catch {
    ElMessage.error("目标文件上传失败");
  } finally {
    targetUploading.value = false;
    if (pendingTargetUploadFiles.value.length > 0) {
      schedulePendingTargetUploadFlush();
    }
  }
};

const handleTargetFileChange = (uploadFile: UploadFile, _uploadFiles: UploadFiles) => {
  const rawFile = uploadFile.raw;
  if (!rawFile) {
    targetUploadRef.value?.handleRemove(uploadFile);
    return;
  }

  const decision = decideTargetUpload({
    hasSourceFile: !!sourceFile.value,
    accept: targetAccept.value,
    existingSignatures: [
      ...targetFiles.value.map(item => item.signature),
      ...pendingTargetUploadSignatures.value
    ],
    file: rawFile
  });

  if (decision.status === "rejected") {
    if (decision.level === "warning") {
      ElMessage.warning(decision.message);
    } else {
      ElMessage.error(decision.message);
    }
    targetUploadRef.value?.handleRemove(uploadFile);
    return;
  }

  if (!ensurePermission("api:batch-reply:upload", "权限不足，无法上传目标文件")) {
    targetUploadRef.value?.handleRemove(uploadFile);
    return;
  }

  pendingTargetUploadFiles.value = [...pendingTargetUploadFiles.value, rawFile];
  pendingTargetUploadSignatures.value = [
    ...pendingTargetUploadSignatures.value,
    createTargetFileSignature(rawFile)
  ];
  schedulePendingTargetUploadFlush();
  targetUploadRef.value?.handleRemove(uploadFile);
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

const getDuplicateResolutionFromConfig = (
  resolutions: BatchReplyDuplicateResolution[] | undefined,
  groupId: string
) => resolutions?.find(item => item.groupId === groupId)?.strategy;

const upsertDuplicateResolutions = (
  resolutions: BatchReplyDuplicateResolution[] | undefined,
  nextResolution: BatchReplyDuplicateResolution
) => {
  const nextList = [...(resolutions ?? [])];
  const existingIndex = nextList.findIndex(item => item.groupId === nextResolution.groupId);
  if (existingIndex >= 0) {
    nextList[existingIndex] = nextResolution;
  } else {
    nextList.push(nextResolution);
  }

  return nextList;
};

const getDuplicateSourceLabel = (duplicateSource: BatchReplyDuplicateGroup["duplicateSource"]) =>
  duplicateSource === "source" ? "来源表重复" : "目标表重复";

const openDuplicateDialog = (
  targetId: string,
  item: BatchTableConfigItem,
  groups: BatchReplyDuplicateGroup[]
) => {
  const targetFile = targetFiles.value.find(file => file.targetId === targetId);
  const strategies = Object.fromEntries(
    groups.map(group => {
      const config =
        group.duplicateSource === "source"
          ? sourceConfigs.value.find(source => source.tableIndex === group.tableIndex)
          : targetFile?.configs.find(configItem => configItem.tableIndex === group.tableIndex);
      return [
        group.groupId,
        getDuplicateResolutionFromConfig(config?.duplicateResolutions, group.groupId) ?? "keepFirst"
      ];
    })
  ) as Record<string, BatchReplyDuplicateStrategy>;

  duplicateDialog.value = {
    targetId,
    tableIndex: item.tableIndex,
    sourceTableIndex: item.sourceTableIndex ?? item.tableIndex,
    groups,
    strategies
  };
};

const closeDuplicateDialog = () => {
  duplicateDialog.value = null;
};

const updateDuplicateDialogStrategy = (
  groupId: string,
  strategy: BatchReplyDuplicateStrategy
) => {
  if (!duplicateDialog.value) {
    return;
  }

  duplicateDialog.value = {
    ...duplicateDialog.value,
    strategies: {
      ...duplicateDialog.value.strategies,
      [groupId]: strategy
    }
  };
};

const applyDuplicateResolutions = (dialog: BatchReplyDuplicateDialogState) => {
  const groupedResolutions = dialog.groups.reduce(
    (acc, group) => {
      const resolution: BatchReplyDuplicateResolution = {
        groupId: group.groupId,
        strategy: dialog.strategies[group.groupId] ?? "keepFirst"
      };
      if (group.duplicateSource === "source") {
        acc.source[group.tableIndex] = upsertDuplicateResolutions(acc.source[group.tableIndex], resolution);
      } else {
        acc.target[group.tableIndex] = upsertDuplicateResolutions(acc.target[group.tableIndex], resolution);
      }
      return acc;
    },
    {
      source: {} as Record<number, BatchReplyDuplicateResolution[]>,
      target: {} as Record<number, BatchReplyDuplicateResolution[]>
    }
  );

  sourceConfigs.value = sourceConfigs.value.map(config =>
    groupedResolutions.source[config.tableIndex]
      ? {
          ...config,
          duplicateResolutions: groupedResolutions.source[config.tableIndex]
        }
      : config
  );

  targetFiles.value = targetFiles.value.map(file =>
    file.targetId !== dialog.targetId
      ? file
      : {
          ...file,
          configs: file.configs.map(config =>
            groupedResolutions.target[config.tableIndex]
              ? {
                  ...config,
                  duplicateResolutions: groupedResolutions.target[config.tableIndex]
                }
              : config
          )
        }
  );
};

const confirmDuplicateDialog = async () => {
  const dialog = duplicateDialog.value;
  if (!dialog) {
    return;
  }

  applyDuplicateResolutions(dialog);
  closeDuplicateDialog();

  const targetFile = targetFiles.value.find(file => file.targetId === dialog.targetId);
  const targetConfig = targetFile?.configs.find(config => config.tableIndex === dialog.tableIndex);
  if (!targetConfig) {
    ElMessage.error("未找到当前目标表配置，无法重新预览");
    return;
  }

  await handleTargetTablePreview(dialog.targetId, targetConfig);
};

const createSourcePreviewLoader = async (
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

const createTargetPreviewLoader = (targetId: string) => {
  return async (
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

    const res = await getBatchReplyTargetTablePreview(sourceSessionId.value, targetId, tableIndex, options);
    if (res.code !== 0) {
      throw new Error(res.message || "加载目标表格预览失败");
    }

    return res.data;
  };
};

const getTargetPreviewLoader = createTargetPreviewLoaderResolver(createTargetPreviewLoader);

const handleTargetTablePreview = async (targetId: string, item: BatchTableConfigItem) => {
  if (!ensurePermission("btn:batch-reply:preview", "权限不足，无法预览当前目标表")) {
    return;
  }

  if (!sourceSessionId.value) {
    ElMessage.warning("请先上传来源文件");
    return;
  }

  if (selectedSourceConfigs.value.length === 0) {
    ElMessage.warning("请至少选择一个来源表");
    return;
  }

  if (item.sourceTableIndex === undefined) {
    ElMessage.warning("请先为当前目标表选择来源表");
    return;
  }

  targetFiles.value = targetFiles.value.map(file =>
    file.targetId === targetId
      ? { ...file, previewLoadingTableIndex: item.tableIndex }
      : file
  );

  try {
    const res = await previewBatchReplyTable({
      sessionId: sourceSessionId.value,
      sourceTables: selectedSourceConfigs.value.map(toBatchTableConfig),
      targetId,
      targetTable: toBatchTableConfig(item)
    });

    if (res.code !== 0) {
      ElMessage.error(res.message || "目标表预览失败");
      return;
    }

    targetFiles.value = targetFiles.value.map(file =>
      file.targetId === targetId
        ? {
            ...file,
            previewLoadingTableIndex: undefined,
            previewResults: {
              ...file.previewResults,
              [item.tableIndex]: res.data
            }
          }
        : file
    );

    if ((res.data.duplicateGroups?.length ?? 0) > 0) {
      openDuplicateDialog(targetId, item, res.data.duplicateGroups);
      ElMessage.warning("当前目标表存在重复项，请先确认处理方式");
    } else if (res.data.canApply) {
      ElMessage.success("当前 Sheet/表格预览通过");
    } else {
      ElMessage.warning("当前目标表仍存在需要处理的问题");
    }
  } catch {
    ElMessage.error("目标表预览失败");
    targetFiles.value = targetFiles.value.map(file =>
      file.targetId === targetId
        ? { ...file, previewLoadingTableIndex: undefined }
        : file
    );
  }
};

function isTargetExecutable(targetFile: BatchReplyTargetState) {
  const selectedTables = targetFile.configs.filter(item => item.selected);
  if (selectedTables.length === 0) {
    return false;
  }

  return selectedTables.every(item => targetFile.previewResults[item.tableIndex]?.canApply === true);
}

const executeReadyTargets = async () => {
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

  if (selectedSourceConfigs.value.length === 0) {
    ElMessage.warning("请至少选择一个来源表");
    return;
  }

  if (executableTargets.value.length === 0) {
    ElMessage.warning("请至少完成一个目标文件的逐表预览");
    return;
  }

  executing.value = true;
  try {
    const res = await executeBatchReply({
      sessionId: sourceSessionId.value,
      sourceTables: selectedSourceConfigs.value.map(toBatchTableConfig),
      targets: executableTargets.value.map(target => ({
        targetId: target.targetId,
        tables: target.configs.filter(item => item.selected).map(toBatchTableConfig)
      }))
    });

    if (res.code !== 0) {
      ElMessage.error(res.message || "批量回复执行失败");
      return;
    }

    executeResult.value = res.data;
    activeRootTab.value = "result";
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
          <el-card class="section-card file-stage-panel" shadow="never">
            <template #header>
              <div class="section-header">
                <span>来源文件</span>
                <el-button
                  v-if="sourceFile"
                  type="danger"
                  link
                  @click="resetAllState"
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
                <span>默认会把每张表都带出来，你可以逐表调整或取消参与。</span>
              </div>
            </div>
          </el-card>

          <el-card class="section-card file-stage-panel" shadow="never">
            <template #header>
              <div class="section-header">
                <span>来源文件工作区</span>
                <span class="section-subtitle">按文件和 Sheet/表格逐层配置行设置与列映射</span>
              </div>
            </template>

            <el-empty
              v-if="!sourceFile"
              description="请先上传来源文件"
            />
            <el-tabs
              v-else
              v-model="activeSourceFileTab"
              class="source-file-tabs"
            >
              <el-tab-pane
                :label="sourceFile.sourceFileName"
                :name="sourceFile.sessionId"
              >
                <div class="source-file-summary">
                  <div class="source-file-name">{{ sourceFile.sourceFileName }}</div>
                  <div class="source-meta">
                    <el-tag size="small" type="primary">
                      {{ sourceIsExcel ? "Excel" : "Word" }}
                    </el-tag>
                    <span>共 {{ sourceFile.tableCount }} 个{{ sourceIsExcel ? "工作表" : "表格" }}</span>
                    <span>请在对应 Sheet/表格里直接设置行配置、项目列、规格列、验收列和备注列。</span>
                  </div>
                </div>
                <BatchTableConfigPanel
                  :model-value="sourceConfigs"
                  :tables="sourceTables"
                  :is-excel="sourceIsExcel"
                  :preview-loader="createSourcePreviewLoader"
                  @update:model-value="handleSourceConfigChange"
                />
              </el-tab-pane>
            </el-tabs>
          </el-card>
        </div>
      </el-tab-pane>

      <el-tab-pane label="目标文件" name="target" :disabled="!sourceFile">
        <div class="content-grid">
          <el-card class="section-card file-stage-panel" shadow="never">
            <template #header>
              <div class="section-header">
                <span>目标文件</span>
                <span class="section-subtitle">上传后按目标文件和 Sheet/表格逐层配置，并为每个目标表选择来源表</span>
              </div>
            </template>

            <el-alert
              v-if="selectedSourceTableOptions.length === 0"
              type="warning"
              :closable="false"
              show-icon
              title="请先在“来源文件”步骤里至少保留一个来源表"
            />

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
              :disabled="!sourceFile || !canUploadTargetFile || targetUploading"
            >
              <el-icon class="el-icon--upload" :size="52">
                <UploadFilled />
              </el-icon>
              <div class="el-upload__text">
                <span v-if="!sourceFile">请先上传来源文件</span>
                <span v-else>将目标文件拖到此处，或 <em>点击添加</em></span>
              </div>
              <template #tip>
                <div class="el-upload__tip">
                  {{ sourceFile ? `当前仅接受 ${targetAccept} 格式` : "来源文件确认后自动限定同格式上传" }}
                </div>
              </template>
            </el-upload>

            <el-empty
              v-if="targetFiles.length === 0"
              description="上传目标文件后，会在下方按文件分 Tab 展开配置"
            />
            <el-tabs
              v-else
              v-model="activeTargetFileId"
              class="target-file-tabs"
            >
              <el-tab-pane
                v-for="targetFile in targetFiles"
                :key="targetFile.targetId"
                :label="targetFile.fileName"
                :name="targetFile.targetId"
              >
                <div class="target-file-summary">
                  <div>
                    <div class="target-file-name">{{ targetFile.fileName }}</div>
                    <div class="target-file-meta">
                      <span>{{ formatFileSize(targetFile.size) }}</span>
                      <span>共 {{ targetFile.tableCount }} 个{{ targetFile.fileType === 1 ? "工作表" : "表格" }}</span>
                      <el-tag
                        size="small"
                        :type="isTargetExecutable(targetFile) ? 'success' : 'warning'"
                      >
                        {{ isTargetExecutable(targetFile) ? "可执行" : "待预览" }}
                      </el-tag>
                    </div>
                  </div>
                  <el-button type="danger" link @click="removeTargetFile(targetFile.targetId)">
                    移除
                  </el-button>
                </div>

                <BatchTableConfigPanel
                  :model-value="targetFile.configs"
                  :tables="targetFile.tables"
                  :is-excel="targetFile.fileType === 1"
                  :preview-loader="getTargetPreviewLoader(targetFile.targetId)"
                  :source-table-options="selectedSourceTableOptions"
                  source-table-label="来源表"
                  :mapping-previewable="canPreviewBatchReply"
                  :mapping-preview-loading-table-index="targetFile.previewLoadingTableIndex"
                  :mapping-preview-results="targetFile.previewResults"
                  @update:model-value="(value) => handleTargetConfigChange(targetFile.targetId, value)"
                  @mapping-preview="(item) => handleTargetTablePreview(targetFile.targetId, item)"
                />
              </el-tab-pane>
            </el-tabs>
          </el-card>
        </div>
      </el-tab-pane>

      <el-tab-pane label="执行结果" name="result">
        <el-card class="section-card file-stage-panel" shadow="never">
          <template #header>
            <div class="section-header">
              <span>执行结果</span>
              <span class="section-subtitle">确认可执行文件后，在这里统一执行并查看结果</span>
            </div>
          </template>

          <div class="action-row">
            <el-button
              type="success"
              :loading="executing"
              :disabled="executableTargets.length === 0 || !canExecuteBatchReply || !canDownloadBatchReply"
              @click="executeReadyTargets"
            >
              执行已完成目标文件
            </el-button>
            <span class="action-tip">
              当前可执行 {{ executableTargets.length }} / {{ targetFiles.length }} 份目标文件
            </span>
          </div>

          <el-empty
            v-if="!executeResult"
            description="完成目标文件配置并执行后，结果会展示在这里"
          />
          <template v-else>
            <el-alert
              :title="`执行完成：成功 ${executeResult.successCount} 份，失败 ${executeResult.failedCount} 份`"
              :type="executeResult.failedCount > 0 ? 'warning' : 'success'"
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
          </template>
        </el-card>
      </el-tab-pane>
    </el-tabs>
    </div>

    <el-dialog
      :model-value="duplicateDialogVisible"
      title="重复项处理"
      width="960px"
      destroy-on-close
      @close="closeDuplicateDialog"
    >
      <template v-if="duplicateDialog">
        <div class="duplicate-dialog__intro">
          当前 Sheet/表格存在重复的“项目 + 规格”组合。请为每个冲突组选择处理策略，确认后系统会自动重新预览这张表。
        </div>

        <div class="duplicate-group-list">
          <div
            v-for="group in duplicateDialog.groups"
            :key="group.groupId"
            class="duplicate-group-card"
          >
            <div class="duplicate-group-card__header">
              <div class="duplicate-group-card__meta">
                <el-tag
                  size="small"
                  :type="group.duplicateSource === 'source' ? 'warning' : 'danger'"
                  effect="plain"
                >
                  {{ getDuplicateSourceLabel(group.duplicateSource) }}
                </el-tag>
                <strong>{{ group.project || "空项目" }}</strong>
                <span>/</span>
                <span>{{ group.specification || "空规格" }}</span>
              </div>
              <el-select
                :model-value="duplicateDialog.strategies[group.groupId]"
                class="duplicate-group-card__select"
                @update:model-value="updateDuplicateDialogStrategy(group.groupId, $event)"
              >
                <el-option label="保留首条" value="keepFirst" />
                <el-option label="保留末条" value="keepLast" />
                <el-option label="跳过该组" value="skip" />
              </el-select>
            </div>

            <el-table :data="group.rows" border size="small" class="duplicate-group-card__table">
              <el-table-column prop="rowIndex" label="行号" width="90" />
              <el-table-column prop="project" label="项目" min-width="150" />
              <el-table-column prop="specification" label="规格" min-width="180" />
              <el-table-column prop="acceptance" label="验收" min-width="180" />
              <el-table-column prop="remark" label="备注" min-width="180" />
            </el-table>
          </div>
        </div>
      </template>

      <template #footer>
        <div class="duplicate-dialog__footer">
          <el-button @click="closeDuplicateDialog">取消</el-button>
          <el-button type="primary" @click="confirmDuplicateDialog">
            确认处理
          </el-button>
        </div>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.batch-reply-page {
  display: flex;
  flex-direction: column;
  gap: 16px;
  padding: 8px;
}

.page-shell {
  background: #f4f6f8;
}

.page-header {
  display: grid;
  grid-template-columns: minmax(0, 1.8fr) minmax(280px, 0.9fr);
  gap: 16px;
  padding: 22px 24px;
  border-radius: 18px;
  background: #fff;
  border: 1px solid #d9e1ea;
  box-shadow: 0 8px 24px rgba(15, 23, 42, 0.04);
}

.page-header h1 {
  margin: 6px 0 12px;
  font-size: 30px;
  line-height: 1.1;
  color: #152334;
}

.page-header p {
  margin: 0;
  max-width: 720px;
  color: #526172;
  line-height: 1.7;
}

.page-header__eyebrow {
  font-size: 12px;
  letter-spacing: 0.16em;
  text-transform: uppercase;
  color: #2158a8;
  font-weight: 700;
}

.page-header__stats {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 12px;
  align-self: stretch;
}

.header-stat {
  display: flex;
  flex-direction: column;
  justify-content: center;
  gap: 6px;
  min-height: 90px;
  padding: 16px;
  border-radius: 14px;
  background: linear-gradient(180deg, #f9fbfd 0%, #f1f5f9 100%);
  border: 1px solid #d8e1eb;
}

.header-stat strong {
  font-size: 26px;
  color: #152334;
}

.header-stat__label {
  font-size: 12px;
  color: #607083;
}

.rule-strip {
  display: flex;
  align-items: flex-start;
  gap: 14px;
  padding: 14px 18px;
  border-radius: 14px;
  border: 1px solid #d7dee8;
  background: #fff;
}

.rule-strip__title {
  flex-shrink: 0;
  min-width: 72px;
  font-size: 13px;
  font-weight: 700;
  color: #1f3d6b;
}

.rule-strip__content {
  color: #556476;
  line-height: 1.6;
}

.workflow-panel {
  padding: 18px;
  border-radius: 18px;
  background: #fff;
  border: 1px solid #d9e1ea;
  box-shadow: 0 8px 24px rgba(15, 23, 42, 0.03);
}

.root-tabs :deep(.el-tabs__header) {
  margin-bottom: 20px;
}

.workstep-tabs :deep(.el-tabs__nav-wrap::after) {
  height: 1px;
  background: #d9e1ea;
}

.workstep-tabs :deep(.el-tabs__item) {
  height: 42px;
  padding: 0 18px;
  color: #617284;
  font-weight: 600;
}

.workstep-tabs :deep(.el-tabs__item.is-active) {
  color: #173d73;
}

.workstep-tabs :deep(.el-tabs__active-bar) {
  height: 3px;
  border-radius: 999px;
  background: #2f6bb2;
}

.content-grid {
  display: grid;
  grid-template-columns: minmax(0, 1fr);
  gap: 16px;
}

.section-card {
  border-radius: 16px;
}

.file-stage-panel {
  border: 1px solid #d8e1eb;
  box-shadow: none;
  background: #fcfdff;
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
  color: #6d7b8a;
}

.upload-area :deep(.el-upload-dragger) {
  width: 100%;
  min-height: 180px;
  border-radius: 14px;
  border-color: #d4dde8;
  background: #f8fafc;
}

.source-summary,
.target-file-summary {
  display: flex;
  justify-content: space-between;
  gap: 16px;
  align-items: flex-start;
}

.source-file-name,
.target-file-name {
  font-size: 18px;
  font-weight: 600;
  color: #162536;
  word-break: break-word;
}

.source-meta,
.target-file-meta {
  display: flex;
  gap: 10px;
  flex-wrap: wrap;
  color: #617284;
  font-size: 13px;
  margin-top: 8px;
}

.source-file-summary {
  padding: 14px 16px;
  border-radius: 12px;
  background: #f7fafc;
  border: 1px solid #dde5ee;
}

.source-file-tabs :deep(.el-tabs__item),
.target-file-tabs {
  margin-top: 12px;
}

.source-file-tabs :deep(.el-tabs__item),
.target-file-tabs :deep(.el-tabs__item) {
  height: 38px;
  color: #5f6e7c;
}

.source-file-tabs :deep(.el-tabs__item.is-active),
.target-file-tabs :deep(.el-tabs__item.is-active) {
  color: #153a70;
  font-weight: 600;
}

.source-file-tabs :deep(.el-tabs__active-bar),
.target-file-tabs :deep(.el-tabs__active-bar) {
  background: #2f6bb2;
}

.action-row {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
  margin-top: 18px;
}

.action-tip {
  color: #617284;
  font-size: 13px;
}

.duplicate-dialog__intro {
  margin-bottom: 16px;
  color: #556476;
  line-height: 1.7;
}

.duplicate-group-list {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.duplicate-group-card {
  padding: 16px;
  border-radius: 14px;
  border: 1px solid #dde5ee;
  background: #fbfcfd;
}

.duplicate-group-card__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 12px;
}

.duplicate-group-card__meta {
  display: flex;
  align-items: center;
  gap: 8px;
  color: #1f3349;
  flex-wrap: wrap;
}

.duplicate-group-card__select {
  width: 150px;
}

.duplicate-group-card__table {
  width: 100%;
}

.duplicate-dialog__footer {
  display: flex;
  justify-content: flex-end;
  gap: 12px;
}

.source-file-tabs {
  margin-top: 8px;
}

@media (max-width: 960px) {
  .page-header {
    grid-template-columns: 1fr;
    padding: 20px 18px;
  }

  .page-header h1 {
    font-size: 28px;
  }

  .section-header,
  .source-summary,
  .target-file-summary,
  .page-header__stats,
  .duplicate-group-card__header {
    align-items: flex-start;
    flex-direction: column;
  }

  .page-header__stats {
    grid-template-columns: 1fr;
  }
}
</style>
