<script setup lang="ts">
import { ref, computed, onBeforeUnmount, watch } from "vue";
import { useEventListener } from "@vueuse/core";
import { ElMessage } from "element-plus";
import { Loading } from "@element-plus/icons-vue";
import FileUpload from "@/views/data-import/components/FileUpload.vue";
import MatchConfig from "./components/MatchConfig.vue";
import BatchTableConfig from "./components/BatchTableConfig.vue";
import BatchPreviewTabs from "./components/BatchPreviewTabs.vue";
import ScoreDetailDialog from "./components/ScoreDetailDialog.vue";
import {
  applyMatchLlmStreamDisconnectToPreviewItem,
  applyMatchLlmStreamEventToPreviewItem,
  shouldStreamMatchReview
} from "./components/scoreDetail.formatters";
import type { BatchTableConfigItem } from "./components/BatchTableConfig.vue";
import {
  batchPreviewMatch,
  batchExecuteFill,
  downloadFillResult,
  requestMatchLlmStream,
  createMatchLlmStreamRequest,
  getBatchPreviewProgress,
  type MatchPreviewItem,
  type MatchConfig as MatchConfigType,
  type MatchResult,
  type MatchLlmStreamEvent,
  type MatchLlmStreamEventData,
  type BatchPreviewProgressResponse,
  type BatchTablePreviewResult,
  DEFAULT_AMBIGUITY_MARGIN,
  DEFAULT_HIGH_CONFIDENCE_THRESHOLD,
  defaultMatchConfig
} from "@/api/matching";
import type { FileUploadResponse, TableInfo } from "@/api/document";
import { getFileTables } from "@/api/document";
import {
  getEffectiveColumnMappingRules,
  type ColumnMappingRule
} from "@/api/column-mapping-rules";
import { matchWordTableColumnsByRules } from "@/views/shared/word-column-mapping-rules";
import { hasPerms } from "@/utils/auth";
import { ensurePermission } from "@/utils/permission-guard";

defineOptions({ name: "SmartFill" });

// 步骤
const currentStep = ref(0);
const steps = [
  { title: "上传文件", description: "选择目标文档" },
  { title: "选择表格", description: "选择要填充的表格并配置列索引" },
  { title: "配置匹配", description: "设置匹配参数" },
  { title: "预览确认", description: "确认匹配结果" }
];

// 文件上传
const uploadedFile = ref<FileUploadResponse | null>(null);
const isExcelFile = computed(() => uploadedFile.value?.fileType === 1);
const canUploadSourceFile = computed(() => hasPerms("btn:document:upload"));
const canPreviewMatching = computed(() => hasPerms("btn:matching:preview-batch"));
const canLlmStream = computed(() => hasPerms("btn:matching-fill:llm-stream"));
const canExecuteFill = computed(() => hasPerms("btn:matching-fill:execute-batch"));
const canDownloadFillResult = computed(() => hasPerms("btn:matching:download"));

// 所有表格信息
const allTables = ref<TableInfo[]>([]);
// 批量表格配置
const batchTableConfigs = ref<BatchTableConfigItem[]>([]);
const wordColumnMappingRules = ref<ColumnMappingRule[]>([]);

// 匹配配置
const matchConfig = ref<MatchConfigType>({ ...defaultMatchConfig });
const matchConfigRef = ref<InstanceType<typeof MatchConfig> | null>(null);

// 批量预览结果
const batchPreviewResults = ref<BatchTablePreviewResult[]>([]);
const batchPreviewTabsRef = ref<InstanceType<typeof BatchPreviewTabs> | null>(
  null
);
const loadingUploadedFileTables = ref(false);
const loading = ref(false);
const previewProgress = ref<BatchPreviewProgressResponse | null>(null);
const previewElapsedSeconds = ref(0);
const previewProgressPollTimer = ref<number | null>(null);
const previewElapsedTimer = ref<number | null>(null);
const currentPreviewRequestId = ref<string | null>(null);
const llmStreaming = ref(false);
const llmStreamController = ref<AbortController | null>(null);
const previewAbortController = ref<AbortController | null>(null);
let previewRequestVersion = 0;

const stopPreviewRequest = () => {
  const controller = previewAbortController.value;
  controller?.abort();
  if (previewAbortController.value === controller) {
    previewAbortController.value = null;
  }
};

const clearPreviewProgressTimers = () => {
  if (previewProgressPollTimer.value !== null && typeof window !== "undefined") {
    window.clearInterval(previewProgressPollTimer.value);
  }
  if (previewElapsedTimer.value !== null && typeof window !== "undefined") {
    window.clearInterval(previewElapsedTimer.value);
  }
  previewProgressPollTimer.value = null;
  previewElapsedTimer.value = null;
};

const stopPreviewProgressPolling = () => {
  clearPreviewProgressTimers();
  currentPreviewRequestId.value = null;
};

const resetPreviewProgress = () => {
  previewProgress.value = null;
  previewElapsedSeconds.value = 0;
};

const invalidatePendingPreview = () => {
  stopPreviewRequest();
  stopPreviewProgressPolling();
  resetPreviewProgress();
  previewRequestVersion++;
  loading.value = false;
};

const createPreviewRequestId = () => {
  if (typeof crypto !== "undefined" && typeof crypto.randomUUID === "function") {
    return crypto.randomUUID();
  }

  return `preview-${Date.now()}-${Math.random().toString(16).slice(2, 10)}`;
};

const fetchBatchPreviewProgress = async (requestId: string) => {
  try {
    const res = await getBatchPreviewProgress(requestId);
    if (currentPreviewRequestId.value !== requestId || res.code !== 0) {
      return;
    }

    previewProgress.value = res.data;
    if (res.data.status !== "running") {
      stopPreviewProgressPolling();
    }
  } catch (error: any) {
    if (currentPreviewRequestId.value !== requestId) {
      return;
    }

    if (error?.response?.status === 404) {
      if (!loading.value) {
        stopPreviewProgressPolling();
      }
      return;
    }
  }
};

const startPreviewProgressPolling = (requestId: string) => {
  clearPreviewProgressTimers();
  previewProgress.value = {
    requestId,
    status: "running",
    stage: "preparing",
    stageText: "正在准备匹配任务",
    detailText: "正在等待后端返回真实进度",
    completedItems: 0,
    totalItems: 0,
    progressPercent: 1,
    startedAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    elapsedMs: 0
  };
  previewElapsedSeconds.value = 0;
  currentPreviewRequestId.value = requestId;
  void fetchBatchPreviewProgress(requestId);

  if (typeof window === "undefined") {
    return;
  }

  previewElapsedTimer.value = window.setInterval(() => {
    previewElapsedSeconds.value += 1;
  }, 1000);

  previewProgressPollTimer.value = window.setInterval(() => {
    void fetchBatchPreviewProgress(requestId);
  }, 900);
};

const markPreviewProgressCompleted = () => {
  const requestId = currentPreviewRequestId.value;
  if (!requestId) {
    return;
  }

  const now = new Date().toISOString();
  previewProgress.value = {
    requestId,
    status: "completed",
    stage: "completed",
    stageText: "匹配预览已完成",
    detailText:
      previewProgress.value?.detailText ||
      `已完成 ${previewProgress.value?.completedItems ?? 0}/${
        previewProgress.value?.totalItems ?? 0
      } 行`,
    completedItems:
      previewProgress.value?.totalItems ?? previewProgress.value?.completedItems ?? 0,
    totalItems:
      previewProgress.value?.totalItems ?? previewProgress.value?.completedItems ?? 0,
    progressPercent: 100,
    startedAt: previewProgress.value?.startedAt ?? now,
    updatedAt: now,
    elapsedMs: previewElapsedSeconds.value * 1000
  };

  stopPreviewProgressPolling();
};

const getEffectiveFilterEmptySourceRows = (tableConfig: {
  filterEmptySourceRows?: boolean;
}) => tableConfig.filterEmptySourceRows ?? matchConfig.value.filterEmptySourceRows ?? true;

const finalizeInterruptedLlmStreamRows = (
  message = "LLM流式输出中断，已转为人工确认"
) => {
  batchPreviewResults.value.forEach((tableResult) => {
    tableResult.items.forEach((item) => {
      applyMatchLlmStreamDisconnectToPreviewItem(item, message);
    });
  });
};

const handleWindowOffline = () => {
  if (!llmStreaming.value) {
    return;
  }

  const message = "浏览器网络已断开，LLM 复核已转为人工确认";
  finalizeInterruptedLlmStreamRows(message);
  stopLlmStream();
  ElMessage.warning(message);
};

if (typeof window !== "undefined") {
  useEventListener(window, "offline", handleWindowOffline);
}

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

// 页面卸载时清理进行中的预览/流式请求，防止离页后继续占用资源
onBeforeUnmount(() => {
  invalidatePendingPreview();
  stopLlmStream();
});

watch(currentStep, (step) => {
  if (step !== 3) {
    invalidatePendingPreview();
    stopLlmStream();
  }
});

// 详情弹窗
const detailVisible = ref(false);
const detailItem = ref<MatchPreviewItem | null>(null);

// 执行状态
const executing = ref(false);
const downloadingResult = ref(false);
const taskId = ref<string | null>(null);
const lastDownloadFailed = ref(false);
const previewState = ref<"none" | "noScopeCandidates" | "embeddingUnavailable" | "emptyResults">("none");
const previewFailureDetail = ref("");

// 选中的表格数量
const selectedTableCount = computed(
  () => batchTableConfigs.value.filter((t) => t.selected).length
);

// 所有预览项（扁平化）
const allPreviewItems = computed(() =>
  batchPreviewResults.value.flatMap((t) => t.items)
);

const previewProgressStageText = computed(
  () => previewProgress.value?.stageText || "正在准备匹配任务"
);
const previewProgressDetailText = computed(() => {
  if (previewProgress.value?.detailText) {
    return previewProgress.value.detailText;
  }

  if (selectedTableCount.value > 0) {
    return `已选择 ${selectedTableCount.value} 个表格，正在等待真实进度`;
  }

  return "正在等待真实进度";
});
const previewProgressPercent = computed(() =>
  Math.min(Math.max(Math.round(previewProgress.value?.progressPercent ?? 0), 0), 100)
);
const previewProgressCounterText = computed(() => {
  if (!previewProgress.value?.totalItems) {
    return "";
  }

  return `${previewProgress.value.completedItems}/${previewProgress.value.totalItems} 行`;
});

const getMatchConfigServiceStatus = () =>
  matchConfigRef.value?.getServiceStatus?.() ?? {
    hasAvailableEmbeddingService: true,
    hasAvailableLlmService: true
  };

const getPrePreviewBlockingMessage = () => {
  const { hasAvailableEmbeddingService } = getMatchConfigServiceStatus();
  if (!hasAvailableEmbeddingService) {
    return "请先配置可用的 Embedding 服务";
  }

  return "";
};

const previewBlockingMessage = computed(() => {
  const prePreviewMessage = getPrePreviewBlockingMessage();
  if (prePreviewMessage) {
    return prePreviewMessage;
  }

  switch (previewState.value) {
    case "noScopeCandidates":
      return "当前范围内没有可用于匹配的验收规格";
    case "embeddingUnavailable":
      return "请先配置可用的 Embedding 服务";
    case "emptyResults":
      return "未找到可匹配的数据";
    default:
      return "";
  }
});

const previewBlockingHint = computed(() => {
  switch (previewState.value) {
    case "noScopeCandidates":
      return "请调整客户、制程、机型范围，或先补充对应验收规格。";
    case "embeddingUnavailable":
      return previewFailureDetail.value || "当前未检测到可用的 Embedding 服务。";
    case "emptyResults":
      return "当前表格没有命中可匹配结果，请检查源项目/规格列是否选择正确。";
    default:
      return getPrePreviewBlockingMessage()
        ? "请前往 AI 服务配置启用至少一个带 Embedding 模型的服务。"
        : "";
  }
});

const resetPreviewState = () => {
  previewState.value = "none";
  previewFailureDetail.value = "";
};

const resolvePreviewFailure = (message?: string) => {
  const normalizedMessage = (message || "").trim();

  if (normalizedMessage.includes("范围内无候选数据")) {
    previewState.value = "noScopeCandidates";
    previewFailureDetail.value = normalizedMessage || "范围内无候选数据";
    return normalizedMessage || "范围内无候选数据";
  }

  if (normalizedMessage.includes("Embedding 服务不可用")) {
    previewState.value = "embeddingUnavailable";
    previewFailureDetail.value = normalizedMessage || "Embedding 服务不可用";
    return normalizedMessage || "Embedding 服务不可用";
  }

  previewState.value = "none";
  previewFailureDetail.value = normalizedMessage;
  return normalizedMessage || "匹配预览失败";
};

const getRequestErrorMessage = (error: any) => {
  return (
    error?.response?.data?.message ||
    error?.response?.data?.error?.message ||
    error?.message ||
    ""
  );
};

// 计算属性
const canGoNext = computed(() => {
  switch (currentStep.value) {
    case 0:
      return uploadedFile.value !== null && !loadingUploadedFileTables.value;
    case 1:
      return selectedTableCount.value > 0;
    case 2:
      return true;
    case 3:
      return allPreviewItems.value.length > 0;
    default:
      return false;
  }
});

const buildDefaultTableConfig = (
  table: TableInfo,
  selected: boolean
): BatchTableConfigItem => {
  const usedStartRow = Math.max(1, table.usedRangeStartRow ?? 1);
  const totalColumns = Math.max(table.columnCount, table.headers.length, 1);
  const clampColumnIndex = (preferredIndex: number) =>
    Math.min(preferredIndex, totalColumns - 1);
  const matchedWordColumns = isExcelFile.value
    ? {}
    : matchWordTableColumnsByRules(table.headers, wordColumnMappingRules.value, {
        fallbackToSequential: true
      });

  return {
    tableIndex: table.index,
    projectColumnIndex: clampColumnIndex(matchedWordColumns.projectColumnIndex ?? 0),
    specificationColumnIndex: clampColumnIndex(
      matchedWordColumns.specificationColumnIndex ?? 1
    ),
    acceptanceColumnIndex: clampColumnIndex(
      matchedWordColumns.acceptanceColumnIndex ?? 2
    ),
    remarkColumnIndex:
      matchedWordColumns.remarkColumnIndex !== undefined
        ? clampColumnIndex(matchedWordColumns.remarkColumnIndex)
        : totalColumns > 3
          ? 3
          : undefined,
    headerRowStart: usedStartRow,
    headerRowCount: 1,
    dataStartRow: usedStartRow + 1,
    filterEmptySourceRows: undefined,
    selected,
    tableInfo: table
  };
};

// 文件上传完成
const handleFileUploaded = async (file: FileUploadResponse) => {
  invalidatePendingPreview();
  stopLlmStream();
  resetPreviewState();
  uploadedFile.value = file;
  batchTableConfigs.value = [];
  batchPreviewResults.value = [];
  taskId.value = null;
  lastDownloadFailed.value = false;
  loadingUploadedFileTables.value = true;

  let tables: TableInfo[] = [];
  let tableMetaLoaded = false;
  try {
    const tablesRes = await getFileTables(file.fileId);
    if (tablesRes.code === 0) {
      tables = tablesRes.data;
      tableMetaLoaded = true;
    } else {
      throw new Error(tablesRes.message || "获取表格列表失败");
    }

    if (file.fileType !== 1) {
      const rulesRes = await getEffectiveColumnMappingRules();
      if (rulesRes.code === 0) {
        wordColumnMappingRules.value = rulesRes.data || [];
      } else {
        wordColumnMappingRules.value = [];
        ElMessage.warning(rulesRes.message || "加载列映射规则失败，已按默认列位初始化");
      }
    } else {
      wordColumnMappingRules.value = [];
    }
  } catch {
    ElMessage.warning("获取表格列表失败");
  } finally {
    if (uploadedFile.value?.fileId === file.fileId) {
      uploadedFile.value = {
        ...uploadedFile.value,
        tableCount: tables.length,
        tableCountReady: true
      };
    }
    loadingUploadedFileTables.value = false;
  }

  if (uploadedFile.value?.fileId !== file.fileId) return;
  if (!tableMetaLoaded) return;

  allTables.value = tables;
  batchTableConfigs.value = tables.map(t =>
    buildDefaultTableConfig(t, tables.length === 1)
  );
};

// 执行批量匹配预览
const doPreview = async () => {
  if (!ensurePermission("btn:matching:preview-batch", "权限不足，无法执行匹配预览")) {
    return;
  }
  if (!uploadedFile.value) return;

  const requestVersion = ++previewRequestVersion;
  const fileId = uploadedFile.value.fileId;
  stopLlmStream();

  const selectedConfigs = batchTableConfigs.value.filter((t) => t.selected);
  if (selectedConfigs.length === 0) {
    ElMessage.warning("请至少选择一个表格");
    return;
  }

  const prePreviewBlockingMessage = getPrePreviewBlockingMessage();
  if (prePreviewBlockingMessage) {
    ElMessage.warning(prePreviewBlockingMessage);
    return;
  }

  resetPreviewState();
  batchPreviewResults.value = [];
  detailItem.value = null;
  detailVisible.value = false;
  taskId.value = null;
  lastDownloadFailed.value = false;
  loading.value = true;
  const previewRequestId = createPreviewRequestId();
  startPreviewProgressPolling(previewRequestId);
  stopPreviewRequest();
  const controller = new AbortController();
  previewAbortController.value = controller;
  try {
    const scope = matchConfigRef.value?.getScope() ?? {
      customerId: undefined,
      processId: undefined,
      machineModelId: undefined
    };

    const res = await batchPreviewMatch({
      fileId: uploadedFile.value.fileId,
      previewRequestId,
      tables: selectedConfigs.map((t) => ({
        tableIndex: t.tableIndex,
        projectColumnIndex: t.projectColumnIndex,
        specificationColumnIndex: t.specificationColumnIndex,
        acceptanceColumnIndex: t.acceptanceColumnIndex,
        remarkColumnIndex: t.remarkColumnIndex,
        headerRowStart: t.headerRowStart,
        headerRowCount: t.headerRowCount,
        dataStartRow: t.dataStartRow,
        filterEmptySourceRows: getEffectiveFilterEmptySourceRows(t)
      })),
      customerId: scope.customerId,
      processId: scope.processId,
      machineModelId: scope.machineModelId,
      config: matchConfig.value
    }, {
      signal: controller.signal
    });

    if (res.code === 0) {
      if (
        requestVersion !== previewRequestVersion ||
        currentStep.value !== 3 ||
        uploadedFile.value?.fileId !== fileId ||
        previewAbortController.value !== controller
      ) {
        return;
      }

      markPreviewProgressCompleted();
      batchPreviewResults.value = res.data.tables;
      if (res.data.totalMatched === 0) {
        previewState.value = "emptyResults";
        previewFailureDetail.value = "未找到可匹配的数据";
        ElMessage.warning("未找到可匹配的数据");
      } else {
        resetPreviewState();
      }
      startLlmStream();
    } else {
      if (currentPreviewRequestId.value === previewRequestId) {
        stopPreviewProgressPolling();
      }
      if (requestVersion !== previewRequestVersion) return;
      ElMessage.error(resolvePreviewFailure(res.message));
    }
  } catch (error: any) {
    if (
      requestVersion !== previewRequestVersion ||
      controller.signal.aborted ||
      error?.code === "ERR_CANCELED" ||
      error?.name === "CanceledError" ||
      error?.isCancelRequest
    ) {
      return;
    }
    if (currentPreviewRequestId.value === previewRequestId) {
      stopPreviewProgressPolling();
    }
    ElMessage.error(resolvePreviewFailure(getRequestErrorMessage(error)));
  } finally {
    if (previewAbortController.value === controller) {
      previewAbortController.value = null;
    }
    if (requestVersion === previewRequestVersion) {
      loading.value = false;
    }
  }
};

const stopLlmStream = () => {
  const controller = llmStreamController.value;
  controller?.abort();
  if (llmStreamController.value === controller) {
    llmStreamController.value = null;
  }
  llmStreaming.value = false;
};

const getHighConfidenceThreshold = () =>
  Math.min(
    Math.max(matchConfig.value.highConfidenceThreshold ?? DEFAULT_HIGH_CONFIDENCE_THRESHOLD, 0.5),
    1
  );
const getAmbiguityMargin = () =>
  Math.min(Math.max(matchConfig.value.ambiguityMargin ?? DEFAULT_AMBIGUITY_MARGIN, 0), 1);

const startLlmStream = async () => {
  if (!canLlmStream.value) {
    return;
  }
  stopLlmStream();

  if (!allPreviewItems.value.length) return;

  const scope = matchConfigRef.value?.getScope() ?? {
    customerId: undefined,
    processId: undefined,
    machineModelId: undefined
  };

  const llmItems = batchPreviewResults.value.flatMap((tableResult) =>
    tableResult.items
      .filter(item => shouldStreamMatchReview(item.bestMatch))
      .map((item) => ({
        tableIndex: tableResult.tableIndex,
        rowIndex: item.rowIndex,
        sourceProject: item.sourceProject,
        sourceSpecification: item.sourceSpecification,
        bestMatchSpecId: item.bestMatch?.specId,
        bestMatchScore: item.bestMatch?.score,
        scoreDetails: item.bestMatch?.scoreDetails,
        decision: item.bestMatch?.decision,
        llmEquivalenceVerdict: item.bestMatch?.llmEquivalence?.verdict,
        isAmbiguous: item.bestMatch?.isAmbiguous ?? false,
        evidenceSummary: item.bestMatch?.evidenceSummary ?? [],
        conflictSummary: item.bestMatch?.conflictSummary ?? []
      }))
  );

  if (!llmItems.length) {
    llmStreaming.value = false;
    return;
  }

  const controller = new AbortController();
  llmStreamController.value = controller;
  llmStreaming.value = true;

  const payload = createMatchLlmStreamRequest({
    customerId: scope.customerId,
    processId: scope.processId,
    machineModelId: scope.machineModelId,
    items: llmItems,
    config: matchConfig.value
  });

  try {
    const response = await requestMatchLlmStream(payload, controller.signal);

    if (!response.ok || !response.body) {
      const message = "LLM流式输出不可用，已转为人工确认";
      finalizeInterruptedLlmStreamRows(message);
      stopLlmStream();
      ElMessage.warning(message);
      return;
    }

    const reader = response.body.getReader();
    const decoder = new TextDecoder("utf-8");
    let buffer = "";

    while (true) {
      if (
        controller.signal.aborted ||
        llmStreamController.value !== controller
      ) {
        break;
      }

      const { value, done } = await reader.read();
      if (done) break;

      buffer += decoder.decode(value, { stream: true });
      const parts = buffer.split("\n\n");
      buffer = parts.pop() || "";

      for (const part of parts) {
        if (
          controller.signal.aborted ||
          llmStreamController.value !== controller
        ) {
          break;
        }
        handleSseEvent(part);
      }
    }
  } catch {
    if (!controller.signal.aborted) {
      ElMessage.warning("LLM流式输出中断，已降级");
    }
  } finally {
    if (llmStreamController.value === controller) {
      if (!controller.signal.aborted) {
        finalizeInterruptedLlmStreamRows();
      }
      llmStreamController.value = null;
      llmStreaming.value = false;
    }
  }
};

const handleSseEvent = (raw: string) => {
  const lines = raw.split("\n").filter((line) => line.trim().length > 0);
  let event = "message";
  const dataLines: string[] = [];
  for (const line of lines) {
    if (line.startsWith("event:")) {
      event = line.replace("event:", "").trim();
    } else if (line.startsWith("data:")) {
      dataLines.push(line.replace("data:", "").trim());
    }
  }

  if (dataLines.length === 0) return;

  try {
    const data = JSON.parse(dataLines.join("\n"));
    applySseUpdate(event as MatchLlmStreamEvent, data as MatchLlmStreamEventData);
  } catch {
    // ignore malformed chunk
  }
};

const applySseUpdate = (
  event: MatchLlmStreamEvent,
  data: MatchLlmStreamEventData
) => {
  if (data.tableIndex === undefined || data.tableIndex === null) {
    return;
  }

  const tableResult = batchPreviewResults.value.find(
    tableResult => tableResult.tableIndex === data.tableIndex
  );
  const row = tableResult?.items.find((item) => item.rowIndex === data.rowIndex);
  if (!row) return;

  applyMatchLlmStreamEventToPreviewItem(row, event, data);
};

// 显示详情
const handleShowDetail = (item: MatchPreviewItem) => {
  detailItem.value = item;
  detailVisible.value = true;
};

// 选择变化
const handleSelect = (
  _tableIndex: number,
  _rowIndex: number,
  _spec: MatchResult | null
) => {
  // 可用于实时更新统计
};

const downloadTaskResult = async (currentTaskId: string) => {
  downloadingResult.value = true;
  try {
    const blob = await downloadFillResult(currentTaskId);
    const originalName = uploadedFile.value?.fileName || "filled.docx";
    triggerBrowserDownload(blob, originalName);
    lastDownloadFailed.value = false;
    return true;
  } catch {
    lastDownloadFailed.value = true;
    return false;
  } finally {
    downloadingResult.value = false;
  }
};

const handleDownloadLastResult = async () => {
  if (!taskId.value) return;
  if (!ensurePermission("btn:matching:download", "权限不足，无法下载填充结果")) {
    return;
  }

  const downloaded = await downloadTaskResult(taskId.value);
  if (downloaded) {
    ElMessage.success(isExcelFile.value ? "Excel 下载完成" : "结果文件下载完成");
    return;
  }

  ElMessage.warning(isExcelFile.value ? "Excel 下载失败，请稍后重试" : "结果文件下载失败，请稍后重试");
};

const cloneExecutionHistoryBestMatch = (bestMatch?: MatchResult) => {
  if (!bestMatch) return undefined;

  return {
    ...bestMatch,
    scoreDetails: { ...(bestMatch.scoreDetails ?? {}) },
    evidenceSummary: [...(bestMatch.evidenceSummary ?? [])],
    conflictSummary: [...(bestMatch.conflictSummary ?? [])],
    issues: [...(bestMatch.issues ?? [])],
    entities: [...(bestMatch.entities ?? [])],
    llmEquivalence: bestMatch.llmEquivalence
      ? { ...bestMatch.llmEquivalence }
      : undefined,
    topCandidates: (bestMatch.topCandidates ?? []).map(candidate => ({
      ...candidate,
      scoreDetails: { ...(candidate.scoreDetails ?? {}) },
      evidenceSummary: [...(candidate.evidenceSummary ?? [])],
      conflictSummary: [...(candidate.conflictSummary ?? [])],
      issues: [...(candidate.issues ?? [])],
      entities: [...(candidate.entities ?? [])],
      llmEquivalence: candidate.llmEquivalence
        ? { ...candidate.llmEquivalence }
        : undefined
    }))
  };
};

const buildExecutionHistoryPreviewTables = (tableIndexes: number[]) => {
  const selectedTableIndexes = new Set(tableIndexes);

  return batchPreviewResults.value
    .filter(result => selectedTableIndexes.has(result.tableIndex))
    .map(result => ({
      tableIndex: result.tableIndex,
      items: result.items.map(item => ({
        rowIndex: item.rowIndex,
        sourceProject: item.sourceProject,
        sourceSpecification: item.sourceSpecification,
        bestMatch: cloneExecutionHistoryBestMatch(item.bestMatch),
        llmReviewDraft: item.llmReviewDraft,
        llmReviewError: item.llmReviewError,
        llmReviewStage: item.llmReviewStage,
        noMatchReason: item.noMatchReason,
        hasMatch: item.hasMatch,
        confidenceLevel: item.confidenceLevel
      }))
    }));
};

// 执行填充
const handleExecute = async () => {
  if (
    !ensurePermission("btn:matching-fill:execute-batch", "权限不足，无法执行智能填充")
  ) {
    return;
  }
  if (!uploadedFile.value) return;
  if (llmStreaming.value) {
    ElMessage.warning("AI 仍在处理中，请等待完成后再执行填充");
    return;
  }

  const selectedConfigs = batchTableConfigs.value.filter((t) => t.selected);
  if (selectedConfigs.length === 0) return;

  const scope = matchConfigRef.value?.getScope() ?? {
    customerId: undefined,
    processId: undefined,
    machineModelId: undefined
  };

  // 获取各表格的选择结果
  const allSelections = batchPreviewTabsRef.value?.getAllSelections();
  if (!allSelections || allSelections.size === 0) {
    ElMessage.warning("请至少选择一项匹配结果");
    return;
  }

  // 构建批量填充请求
  const tables = selectedConfigs
    .map((config) => {
      const selections = allSelections.get(config.tableIndex) || [];
      if (selections.length === 0) return null;
      return {
        tableIndex: config.tableIndex,
        acceptanceColumnIndex: config.acceptanceColumnIndex,
        remarkColumnIndex: config.remarkColumnIndex,
        projectColumnIndex: config.projectColumnIndex,
        specificationColumnIndex: config.specificationColumnIndex,
        headerRowStart: config.headerRowStart,
        headerRowCount: config.headerRowCount,
        dataStartRow: config.dataStartRow,
        filterEmptySourceRows: getEffectiveFilterEmptySourceRows(config),
        mappings: selections.map((s) => ({
          rowIndex: s.rowIndex,
          specId: s.specId,
          manualConfirmed: s.manualConfirmed,
          reviewApprovalToken: s.reviewApprovalToken,
          overrideAcceptance: s.overrideAcceptance,
          overrideRemark: s.overrideRemark
        }))
      };
    })
  .filter(Boolean) as Array<{
    tableIndex: number;
    projectColumnIndex: number;
    specificationColumnIndex: number;
    acceptanceColumnIndex: number;
    remarkColumnIndex?: number;
    mappings: Array<{
      rowIndex: number;
      specId?: number;
      manualConfirmed?: boolean;
      reviewApprovalToken?: string;
      overrideAcceptance?: string;
      overrideRemark?: string;
    }>;
  }>;

  if (tables.length === 0) {
    ElMessage.warning("请至少选择一项匹配结果");
    return;
  }

  const previewTables = buildExecutionHistoryPreviewTables(
    selectedConfigs.map(config => config.tableIndex)
  );

  executing.value = true;
  try {
    const res = await batchExecuteFill({
      fileId: uploadedFile.value.fileId,
      customerId: scope.customerId,
      processId: scope.processId,
      machineModelId: scope.machineModelId,
      config: {
        ...matchConfig.value,
        highConfidenceThreshold: getHighConfidenceThreshold()
      },
      previewTables,
      tables
    });

    if (res.code === 0) {
      taskId.value = res.data.taskId;
      if (canDownloadFillResult.value) {
        const downloaded = await downloadTaskResult(res.data.taskId);
        if (downloaded) {
          ElMessage.success(
            isExcelFile.value
              ? `填充完成，共填充 ${res.data.filledCount} 条，Excel 已下载`
              : `填充完成，共填充 ${res.data.filledCount} 条，结果文件已下载`
          );
        } else {
          ElMessage.warning(
            isExcelFile.value
              ? "填充完成，但 Excel 下载失败，请使用下方入口重新下载结果"
              : "填充完成，但结果文件下载失败，请使用下方入口重新下载结果"
          );
        }
      } else {
        lastDownloadFailed.value = false;
        if (isExcelFile.value) {
          ElMessage.success(
            `填充完成，共填充 ${res.data.filledCount} 条，可稍后下载 Excel 结果`
          );
        } else {
          ElMessage.success(
            `填充完成，共填充 ${res.data.filledCount} 条，可稍后下载结果文件`
          );
        }
      }
    } else {
      ElMessage.error(res.message || "填充失败");
    }
  } catch {
    ElMessage.error("填充失败");
  } finally {
    executing.value = false;
  }
};

// 步骤切换
const goNext = () => {
  if (currentStep.value === 2) {
    if (!ensurePermission("btn:matching:preview-batch", "权限不足，无法执行匹配预览")) {
      return;
    }
    const prePreviewBlockingMessage = getPrePreviewBlockingMessage();
    if (prePreviewBlockingMessage) {
      ElMessage.warning(prePreviewBlockingMessage);
      return;
    }
  }
  if (!canGoNext.value || currentStep.value >= steps.length - 1) return;
  currentStep.value++;
  if (currentStep.value === 3) {
    doPreview();
  }
};

const goPrev = () => {
  if (currentStep.value > 0) currentStep.value--;
};

// 重新开始
const handleRestart = () => {
  invalidatePendingPreview();
  stopLlmStream();
  resetPreviewState();
  loadingUploadedFileTables.value = false;
  currentStep.value = 0;
  uploadedFile.value = null;
  allTables.value = [];
  batchTableConfigs.value = [];
  batchPreviewResults.value = [];
  taskId.value = null;
  lastDownloadFailed.value = false;
  matchConfig.value = { ...defaultMatchConfig };
};
</script>

<template>
  <div class="page smart-fill">
    <div class="page-header">
      <div>
        <div class="page-title">智能填充</div>
        <div class="page-subtitle">匹配验收规格并批量回写文档</div>
      </div>
    </div>
    <!-- 步骤条 -->
    <el-card class="mb-4">
      <el-steps :active="currentStep" finish-status="success">
        <el-step
          v-for="(step, index) in steps"
          :key="index"
          :title="step.title"
          :description="step.description"
        />
      </el-steps>
    </el-card>

    <!-- 步骤内容 -->
    <el-card class="step-content">
      <!-- 步骤1: 上传文件 -->
      <div v-show="currentStep === 0" class="step-panel">
        <h3 class="step-title">上传目标文档</h3>
        <p class="step-desc">请选择需要填充验收标准的 Word/Excel 文档</p>
        <FileUpload
          v-if="canUploadSourceFile"
          v-model="uploadedFile"
          @uploaded="handleFileUploaded"
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
      </div>

      <!-- 步骤2: 选择表格 + 配置列索引 -->
      <div v-show="currentStep === 1" class="step-panel">
        <h3 class="step-title">选择表格并配置列索引</h3>
        <p class="step-desc">
          勾选需要填充的表格，并为每个表格指定各列索引（从0开始）。
          Word 会按列映射规则自动预填，Excel 仍需按实际内容手工调整并刷新表头。
        </p>

        <BatchTableConfig
          v-if="batchTableConfigs.length > 0"
          v-model="batchTableConfigs"
          :file-id="uploadedFile?.fileId"
          :is-excel="isExcelFile"
          :tables="allTables"
        />

        <el-empty
          v-else-if="uploadedFile"
          description="未检测到表格，请确认文档格式"
        />
      </div>

      <!-- 步骤3: 配置匹配 -->
      <div v-show="currentStep === 2" class="step-panel">
        <h3 class="step-title">配置匹配参数</h3>
        <p class="step-desc">设置匹配范围和算法参数</p>
        <MatchConfig
          ref="matchConfigRef"
          v-model="matchConfig"
          :allow-llm="canLlmStream"
        />
        <el-alert
          v-if="previewBlockingMessage"
          type="warning"
          :closable="false"
          show-icon
          :title="previewBlockingMessage"
          :description="previewBlockingHint"
          class="preview-blocking-alert"
        />
      </div>

      <!-- 步骤4: 预览确认 -->
      <div v-show="currentStep === 3" class="step-panel">
        <h3 class="step-title">匹配预览</h3>
        <p class="step-desc">确认匹配结果，可手动调整选择</p>

        <!-- LLM 流式处理提示 -->
        <el-alert
          v-if="llmStreaming"
          title="AI 正在处理中..."
          description="LLM 正在逐行复核中，请等待完成后再执行填充"
          type="info"
          show-icon
          :closable="false"
          class="llm-streaming-alert"
        />

        <!-- 匹配进行中遮罩 -->
        <div v-if="loading" class="loading-overlay">
          <el-icon class="is-loading" :size="32"><Loading /></el-icon>
          <p class="loading-text">正在匹配中，请耐心等待...</p>
          <div class="preview-progress-panel">
            <div class="preview-progress-panel__header">
              <span>{{ previewProgressStageText }}</span>
              <span>{{ previewProgressPercent }}%</span>
            </div>
            <el-progress
              :percentage="previewProgressPercent"
              :stroke-width="10"
              :show-text="false"
            />
            <div class="preview-progress-panel__meta">
              <span>{{ previewProgressDetailText }}</span>
              <span v-if="previewProgressCounterText">
                {{ previewProgressCounterText }}
              </span>
              <span>已等待 {{ previewElapsedSeconds }} 秒</span>
            </div>
          </div>
          <p class="loading-hint">
            正在对 {{ selectedTableCount }} 个表格执行 Embedding
            向量匹配，视数据量可能需要数十秒
          </p>
        </div>

        <el-empty
          v-if="!loading && previewBlockingMessage"
          :description="previewBlockingMessage"
          class="preview-empty-state"
        >
          <template #description>
            <div class="preview-empty-state__body">
              <div class="preview-empty-state__title">{{ previewBlockingMessage }}</div>
              <div v-if="previewBlockingHint" class="preview-empty-state__hint">
                {{ previewBlockingHint }}
              </div>
            </div>
          </template>
        </el-empty>

        <el-empty
          v-else-if="!loading && batchPreviewResults.length === 0"
          description="当前没有预览结果"
          class="preview-empty-state"
        >
          <template #description>
            <div class="preview-empty-state__body">
              <div class="preview-empty-state__title">当前没有预览结果</div>
              <div class="preview-empty-state__hint">
                页面状态可能已失效，请返回上一步重新匹配。
              </div>
            </div>
          </template>
          <el-button v-if="!taskId" @click="goPrev">返回上一步</el-button>
        </el-empty>

        <BatchPreviewTabs
          v-else
          ref="batchPreviewTabsRef"
          :results="batchPreviewResults"
          :loading="loading"
          :high-confidence-threshold="getHighConfidenceThreshold()"
          :ambiguity-margin="getAmbiguityMargin()"
          :llm-streaming="llmStreaming"
          @select="handleSelect"
          @show-detail="handleShowDetail"
        />

        <!-- 填充完成提示（紧凑内联） -->
        <el-alert
          v-if="taskId"
          :title="
            isExcelFile
              ? '填充完成 — 内容已回写到当前上传文档'
              : '填充完成 — 已生成结果文档（源文档保持不变）'
          "
          :description="
            lastDownloadFailed
              ? '本次自动下载未完成，请使用下方入口重新下载结果。'
              : canDownloadFillResult
                ? '如需再次获取结果文件，可使用下方下载入口。'
                : '当前账号没有下载权限，可稍后由有权限用户下载结果。'
          "
          type="success"
          show-icon
          closable
          class="fill-done-alert"
        />

        <!-- 操作按钮 -->
        <div v-if="allPreviewItems.length > 0" class="action-bar">
          <el-button v-if="canPreviewMatching" @click="doPreview" :loading="loading">
            重新匹配
          </el-button>
          <el-button
            v-if="canExecuteFill"
            type="primary"
            :loading="executing"
            :disabled="!!taskId || llmStreaming || loading"
            @click="handleExecute"
          >
            执行填充
          </el-button>
          <el-button
            v-if="taskId && canDownloadFillResult"
            :loading="downloadingResult"
            @click="handleDownloadLastResult"
          >
            重新下载结果
          </el-button>
          <el-button v-if="taskId && canUploadSourceFile" @click="handleRestart">
            继续填充其他文档
          </el-button>
        </div>
      </div>

      <!-- 步骤按钮 -->
      <div class="step-actions">
        <el-button v-if="currentStep > 0 && !taskId" @click="goPrev">
          上一步
        </el-button>
        <el-button
          v-if="currentStep < steps.length - 1"
          type="primary"
          :disabled="!canGoNext || (currentStep === 2 && !canPreviewMatching)"
          @click="goNext"
        >
          下一步
        </el-button>
      </div>
    </el-card>

    <!-- 详情弹窗 -->
    <ScoreDetailDialog
      v-model:visible="detailVisible"
      :item="detailItem"
      :ambiguity-margin="getAmbiguityMargin()"
      :high-confidence-threshold="getHighConfidenceThreshold()"
    />
  </div>
</template>

<style scoped>
.smart-fill {
  padding: 24px;
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.mb-4 {
  margin-bottom: 16px;
}

.step-content {
  min-height: 500px;
}

.step-panel {
  padding: 20px 0;
}

.step-title {
  font-size: 18px;
  font-weight: 600;
  color: var(--color-text);
  margin-bottom: 8px;
}

.step-desc {
  font-size: 14px;
  color: #6b7280;
  margin-bottom: 24px;
}

.upload-meta-alert {
  margin-top: 16px;
}

.preview-blocking-alert {
  margin-top: 16px;
}

.action-bar {
  margin-top: 20px;
  display: flex;
  gap: 12px;
}

.fill-done-alert {
  margin-top: 16px;
}

.llm-streaming-alert {
  margin-bottom: 12px;
}

.preview-empty-state {
  padding: 32px 0;
}

.preview-empty-state__body {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.preview-empty-state__title {
  font-size: 14px;
  font-weight: 600;
  color: var(--color-text);
}

.preview-empty-state__hint {
  font-size: 12px;
  color: #6b7280;
  line-height: 1.6;
}

.step-actions {
  margin-top: 32px;
  padding-top: 16px;
  border-top: 1px solid var(--el-border-color-lighter);
  display: flex;
  justify-content: center;
  gap: 16px;
}

.loading-overlay {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 60px 20px;
  color: var(--el-color-primary);
}

.loading-text {
  margin-top: 16px;
  font-size: 16px;
  font-weight: 500;
  color: var(--color-text);
}

.preview-progress-panel {
  width: min(560px, 100%);
  margin-top: 20px;
  padding: 16px;
  border-radius: 12px;
  border: 1px solid #dbeafe;
  background: #f8fbff;
}

.preview-progress-panel__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 12px;
  font-size: 14px;
  font-weight: 600;
  color: var(--color-text);
}

.preview-progress-panel__meta {
  margin-top: 12px;
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  gap: 8px 16px;
  font-size: 12px;
  color: #6b7280;
}

.loading-hint {
  margin-top: 8px;
  font-size: 13px;
  color: #9ca3af;
  text-align: center;
}
</style>
