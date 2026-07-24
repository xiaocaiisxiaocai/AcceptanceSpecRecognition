import { ref, type ComputedRef, type Ref } from "vue";
import { ElMessage } from "element-plus";
import {
  backfillSmartFillSpecs,
  batchExecuteFill,
  downloadFillResult,
  DEFAULT_AMBIGUITY_MARGIN,
  DEFAULT_HIGH_CONFIDENCE_THRESHOLD,
  type BatchExecuteFillRequest,
  type BatchTablePreviewResult,
  type MatchConfig
} from "@/api/matching";
import type { FileUploadResponse } from "@/api/document";
import { ensurePermission } from "@/utils/permission-guard";
import { getRequestErrorMessage } from "@/utils/error-message";
import type { BatchTableConfigItem } from "../components/batchTableConfig.types";
import type SmartFillPreviewStep from "../components/SmartFillPreviewStep.vue";
import {
  applyBackfilledItemsToPreviewResults,
  buildSmartFillExecuteRequest,
  refreshBackfilledExecuteRequest,
  type SmartFillScope,
  type SmartFillSelection
} from "../smartFillExecution.helpers";
import { triggerBrowserDownload } from "../smartFillDownload.helpers";
import type { SmartFillBackfillCandidate } from "./useSmartFillBackfillState";

type SmartFillPreviewStepRef = InstanceType<typeof SmartFillPreviewStep> | null;
type SmartFillPreviewSelections = ReturnType<
  NonNullable<SmartFillPreviewStepRef>["getAllSelections"]
>;

type UseSmartFillExecutionOptions = {
  uploadedFile: Ref<FileUploadResponse | null>;
  isExcelFile: ComputedRef<boolean>;
  batchTableConfigs: Ref<BatchTableConfigItem[]>;
  batchPreviewResults: Ref<BatchTablePreviewResult[]>;
  matchConfig: Ref<MatchConfig>;
  llmStreaming: Ref<boolean>;
  canDownloadFillResult: ComputedRef<boolean>;
  batchPreviewTabsRef: Ref<SmartFillPreviewStepRef>;
  getScope: () => SmartFillScope;
  pendingExecuteRequest: Ref<BatchExecuteFillRequest | null>;
  selectedBackfillCandidates: ComputedRef<SmartFillBackfillCandidate[]>;
  closeBackfillDialog: () => void;
  openBackfillDialog: (
    request: BatchExecuteFillRequest,
    candidates: SmartFillBackfillCandidate[]
  ) => void;
  setBackfillingSpecs: (value: boolean) => void;
  clearPendingExecuteRequest: () => void;
  ensureRuntimeAiReady: () => Promise<boolean>;
  /** 由调用方提供的文件下载触发器，默认使用 triggerBrowserDownload */
  onDownload?: (blob: Blob, fileName: string) => void;
};

export function useSmartFillExecution({
  uploadedFile,
  isExcelFile,
  batchTableConfigs,
  batchPreviewResults,
  matchConfig,
  llmStreaming,
  canDownloadFillResult,
  batchPreviewTabsRef,
  getScope,
  pendingExecuteRequest,
  selectedBackfillCandidates,
  closeBackfillDialog,
  openBackfillDialog,
  setBackfillingSpecs,
  clearPendingExecuteRequest,
  ensureRuntimeAiReady,
  onDownload
}: UseSmartFillExecutionOptions) {
  const executing = ref(false);
  const downloadingResult = ref(false);
  const taskId = ref<string | null>(null);
  const lastDownloadFailed = ref(false);
  let lastExecutionIdentity: { fingerprint: string; requestId: string } | null =
    null;

  const createExecutionRequestId = () => {
    if (typeof globalThis.crypto?.randomUUID === "function") {
      return globalThis.crypto.randomUUID().replaceAll("-", "");
    }
    return `${Date.now().toString(36)}${Math.random().toString(36).slice(2)}`;
  };

  const getHighConfidenceThreshold = () =>
    Math.min(
      Math.max(
        matchConfig.value.highConfidenceThreshold ??
          DEFAULT_HIGH_CONFIDENCE_THRESHOLD,
        0.5
      ),
      1
    );

  const getAmbiguityMargin = () =>
    Math.min(
      Math.max(
        matchConfig.value.ambiguityMargin ?? DEFAULT_AMBIGUITY_MARGIN,
        0
      ),
      1
    );

  const downloadTaskResult = async (currentTaskId: string) => {
    downloadingResult.value = true;
    try {
      const blob = await downloadFillResult(currentTaskId);
      const originalName = uploadedFile.value?.fileName || "filled.docx";
      if (onDownload) {
        onDownload(blob, originalName);
      } else {
        triggerBrowserDownload(blob, originalName);
      }
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
    if (
      !ensurePermission("btn:matching:download", "权限不足，无法下载填充结果")
    ) {
      return;
    }

    const downloaded = await downloadTaskResult(taskId.value);
    if (downloaded) {
      ElMessage.success(
        isExcelFile.value ? "Excel 下载完成" : "结果文件下载完成"
      );
      return;
    }

    ElMessage.warning(
      isExcelFile.value
        ? "Excel 下载失败，请稍后重试"
        : "结果文件下载失败，请稍后重试"
    );
  };

  const buildExecuteFillRequest = (
    scope: SmartFillScope,
    selectedConfigs: BatchTableConfigItem[],
    allSelections: SmartFillPreviewSelections
  ): BatchExecuteFillRequest | null => {
    const request = buildSmartFillExecuteRequest({
      uploadedFileId: uploadedFile.value?.fileId,
      scope,
      selectedConfigs,
      allSelections: allSelections as Map<number, SmartFillSelection[]>,
      matchConfig: matchConfig.value,
      highConfidenceThreshold: getHighConfidenceThreshold(),
      previewResults: batchPreviewResults.value
    });
    if (!request) return null;

    const fingerprint = JSON.stringify({
      fileId: request.fileId,
      customerId: request.customerId,
      processId: request.processId,
      machineModelId: request.machineModelId,
      config: request.config,
      tables: request.tables
    });
    if (lastExecutionIdentity?.fingerprint !== fingerprint) {
      lastExecutionIdentity = {
        fingerprint,
        requestId: createExecutionRequestId()
      };
    }
    request.executionRequestId = lastExecutionIdentity.requestId;
    return request;
  };

  const runExecuteFill = async (request: BatchExecuteFillRequest) => {
    const res = await batchExecuteFill(request);

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
  };

  const withCurrentRuntimeAiConfig = (
    request: BatchExecuteFillRequest
  ): BatchExecuteFillRequest => ({
    ...request,
    config: {
      ...request.config,
      embeddingServiceId: matchConfig.value.embeddingServiceId,
      llmServiceId: matchConfig.value.llmServiceId,
      enableLlmEquivalenceAdjudication:
        matchConfig.value.enableLlmEquivalenceAdjudication,
      enableLlmSemanticPriority: matchConfig.value.enableLlmSemanticPriority
    }
  });

  const executePendingWithoutBackfill = async () => {
    if (!pendingExecuteRequest.value) return;
    if (!(await ensureRuntimeAiReady())) return;
    const pendingRequest = pendingExecuteRequest.value;
    if (!pendingRequest) return;
    const request = withCurrentRuntimeAiConfig(pendingRequest);

    closeBackfillDialog();
    executing.value = true;
    try {
      await runExecuteFill(request);
    } catch (error) {
      ElMessage.error(getRequestErrorMessage(error, "填充失败"));
    } finally {
      clearPendingExecuteRequest();
      executing.value = false;
    }
  };

  const confirmBackfillAndExecute = async () => {
    const initialRequest = pendingExecuteRequest.value;
    if (!initialRequest) return;

    const selected = selectedBackfillCandidates.value;
    if (
      selected.some(item => item.actionType === "create") &&
      !initialRequest.customerId
    ) {
      ElMessage.warning("回填新增规格前，请先选择客户范围");
      return;
    }
    if (!(await ensureRuntimeAiReady())) return;
    const pendingRequest = pendingExecuteRequest.value;
    if (!pendingRequest) return;
    const request = withCurrentRuntimeAiConfig(pendingRequest);

    setBackfillingSpecs(true);
    executing.value = true;
    try {
      if (selected.length > 0) {
        const res = await backfillSmartFillSpecs({
          customerId: request.customerId,
          processId: request.processId,
          machineModelId: request.machineModelId,
          items: selected.map(item => ({
            specId: item.specId,
            sourceProject: item.sourceProject,
            sourceSpecification: item.sourceSpecification,
            overrideAcceptance: item.overrideAcceptance,
            overrideRemark: item.overrideRemark
          }))
        });
        if (res.code !== 0) {
          ElMessage.error(res.message || "回填验收规格失败");
          return;
        }
        batchPreviewResults.value = applyBackfilledItemsToPreviewResults(
          batchPreviewResults.value,
          selected
        );
        ElMessage.success(`已回填 ${res.data.totalCount} 条验收规格`);
      }

      closeBackfillDialog();
      const executeRequest = refreshBackfilledExecuteRequest(request, selected);
      await runExecuteFill(executeRequest);
      clearPendingExecuteRequest();
    } catch (error) {
      ElMessage.error(getRequestErrorMessage(error, "回填或填充失败"));
    } finally {
      setBackfillingSpecs(false);
      executing.value = false;
    }
  };

  const handleExecute = async () => {
    if (
      !ensurePermission(
        "btn:matching-fill:execute-batch",
        "权限不足，无法执行智能填充"
      )
    ) {
      return;
    }
    if (!uploadedFile.value) return;
    if (llmStreaming.value) {
      ElMessage.warning("AI 仍在处理中，请等待完成后再执行填充");
      return;
    }

    const selectedConfigs = batchTableConfigs.value.filter(t => t.selected);
    if (selectedConfigs.length === 0) return;

    const allSelections = batchPreviewTabsRef.value?.getAllSelections();
    if (!allSelections || allSelections.size === 0) {
      ElMessage.warning("请至少选择一项匹配结果");
      return;
    }
    if (!(await ensureRuntimeAiReady())) return;

    const executeRequest = buildExecuteFillRequest(
      getScope(),
      selectedConfigs,
      allSelections
    );
    if (!executeRequest) {
      ElMessage.warning("请至少选择一项匹配结果");
      return;
    }

    const editedItems =
      batchPreviewTabsRef.value
        ?.getAllEditedBackfillItems()
        .map((item: Omit<SmartFillBackfillCandidate, "selected">) => ({
          ...item,
          selected: true
        })) ?? [];
    if (editedItems.length > 0) {
      openBackfillDialog(executeRequest, editedItems);
      return;
    }

    executing.value = true;
    try {
      await runExecuteFill(executeRequest);
    } catch (error) {
      ElMessage.error(getRequestErrorMessage(error, "填充失败"));
    } finally {
      executing.value = false;
    }
  };

  const resetExecutionState = () => {
    taskId.value = null;
    lastDownloadFailed.value = false;
    executing.value = false;
    downloadingResult.value = false;
  };

  return {
    executing,
    downloadingResult,
    taskId,
    lastDownloadFailed,
    getHighConfidenceThreshold,
    getAmbiguityMargin,
    handleDownloadLastResult,
    executePendingWithoutBackfill,
    confirmBackfillAndExecute,
    handleExecute,
    resetExecutionState
  };
}
