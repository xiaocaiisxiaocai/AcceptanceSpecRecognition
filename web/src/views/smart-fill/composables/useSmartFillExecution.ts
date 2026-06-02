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
  buildSmartFillExecuteRequest,
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
  getEffectiveFilterEmptySourceRows: (tableConfig: {
    filterEmptySourceRows?: boolean;
  }) => boolean;
  pendingExecuteRequest: Ref<BatchExecuteFillRequest | null>;
  selectedBackfillCandidates: ComputedRef<SmartFillBackfillCandidate[]>;
  closeBackfillDialog: () => void;
  openBackfillDialog: (
    request: BatchExecuteFillRequest,
    candidates: SmartFillBackfillCandidate[]
  ) => void;
  setBackfillingSpecs: (value: boolean) => void;
  clearPendingExecuteRequest: () => void;
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
  getEffectiveFilterEmptySourceRows,
  pendingExecuteRequest,
  selectedBackfillCandidates,
  closeBackfillDialog,
  openBackfillDialog,
  setBackfillingSpecs,
  clearPendingExecuteRequest,
  onDownload
}: UseSmartFillExecutionOptions) {
  const executing = ref(false);
  const downloadingResult = ref(false);
  const taskId = ref<string | null>(null);
  const lastDownloadFailed = ref(false);

  const getHighConfidenceThreshold = () =>
    Math.min(
      Math.max(
        matchConfig.value.highConfidenceThreshold ?? DEFAULT_HIGH_CONFIDENCE_THRESHOLD,
        0.5
      ),
      1
    );

  const getAmbiguityMargin = () =>
    Math.min(Math.max(matchConfig.value.ambiguityMargin ?? DEFAULT_AMBIGUITY_MARGIN, 0), 1);

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
    if (!ensurePermission("btn:matching:download", "权限不足，无法下载填充结果")) {
      return;
    }

    const downloaded = await downloadTaskResult(taskId.value);
    if (downloaded) {
      ElMessage.success(isExcelFile.value ? "Excel 下载完成" : "结果文件下载完成");
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
    return buildSmartFillExecuteRequest({
      uploadedFileId: uploadedFile.value?.fileId,
      scope,
      selectedConfigs,
      allSelections: allSelections as Map<number, SmartFillSelection[]>,
      matchConfig: matchConfig.value,
      highConfidenceThreshold: getHighConfidenceThreshold(),
      previewResults: batchPreviewResults.value,
      resolveFilterEmptySourceRows: getEffectiveFilterEmptySourceRows
    });
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

  const executePendingWithoutBackfill = async () => {
    const request = pendingExecuteRequest.value;
    if (!request) return;

    closeBackfillDialog();
    executing.value = true;
    try {
      await runExecuteFill(request);
    } catch {
      ElMessage.error("填充失败");
    } finally {
      clearPendingExecuteRequest();
      executing.value = false;
    }
  };

  const confirmBackfillAndExecute = async () => {
    const request = pendingExecuteRequest.value;
    if (!request) return;

    const selected = selectedBackfillCandidates.value;
    if (
      selected.some(item => item.actionType === "create") &&
      !request.customerId
    ) {
      ElMessage.warning("回填新增规格前，请先选择客户范围");
      return;
    }

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
        ElMessage.success(`已回填 ${res.data.totalCount} 条验收规格`);
      }

      closeBackfillDialog();
      await runExecuteFill(request);
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

    const allSelections = batchPreviewTabsRef.value?.getAllSelections();
    if (!allSelections || allSelections.size === 0) {
      ElMessage.warning("请至少选择一项匹配结果");
      return;
    }

    const executeRequest = buildExecuteFillRequest(
      getScope(),
      selectedConfigs,
      allSelections
    );
    if (!executeRequest) {
      ElMessage.warning("请至少选择一项匹配结果");
      return;
    }

    const editedItems = batchPreviewTabsRef.value
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
    } catch {
      ElMessage.error("填充失败");
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
