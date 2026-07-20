import { ref, type Ref } from "vue";
import { ElMessage } from "element-plus";
import {
  batchPreviewMatch,
  type BatchTablePreviewResult,
  type MatchConfig
} from "@/api/matching";
import type { FileUploadResponse } from "@/api/document";
import { getRequestErrorMessage } from "@/utils/error-message";
import { ensurePermission } from "@/utils/permission-guard";
import type { BatchTableConfigItem } from "../components/batchTableConfig.types";
import type { SmartFillScope } from "../smartFillExecution.helpers";

type UseSmartFillPreviewRequestOptions = {
  currentStep: Ref<number>;
  uploadedFile: Ref<FileUploadResponse | null>;
  batchTableConfigs: Ref<BatchTableConfigItem[]>;
  batchPreviewResults: Ref<BatchTablePreviewResult[]>;
  matchConfig: Ref<MatchConfig>;
  loading: Ref<boolean>;
  taskId: Ref<string | null>;
  lastDownloadFailed: Ref<boolean>;
  getScope: () => SmartFillScope;
  stopLlmStream: () => void;
  startLlmStream: () => void;
  getEffectiveFilterEmptySourceRows: (tableConfig: {
    filterEmptySourceRows?: boolean;
  }) => boolean;
  getPrePreviewBlockingMessage: () => string;
  resetPreviewState: () => void;
  markPreviewEmptyResults: () => void;
  resolvePreviewFailure: (message: string) => string;
  createPreviewRequestId: () => string;
  startPreviewProgressPolling: (
    requestId: string,
    isLoading: () => boolean
  ) => void;
  stopPreviewProgressPolling: () => void;
  resetPreviewProgress: () => void;
  markPreviewProgressCompleted: () => void;
  getCurrentPreviewRequestId: () => string | null;
  isPreviewStep?: () => boolean;
  clearPreviewDetail: () => void;
  /** 由调用方提供的预览请求发起函数，接收 data 和 controller（AbortController），返回请求 Promise */
  onSendPreview?: (
    data: Parameters<typeof batchPreviewMatch>[0],
    controller: AbortController
  ) => ReturnType<typeof batchPreviewMatch>;
};

export function useSmartFillPreviewRequest({
  currentStep,
  uploadedFile,
  batchTableConfigs,
  batchPreviewResults,
  matchConfig,
  loading,
  taskId,
  lastDownloadFailed,
  getScope,
  stopLlmStream,
  startLlmStream,
  getEffectiveFilterEmptySourceRows,
  getPrePreviewBlockingMessage,
  resetPreviewState,
  markPreviewEmptyResults,
  resolvePreviewFailure,
  createPreviewRequestId,
  startPreviewProgressPolling,
  stopPreviewProgressPolling,
  resetPreviewProgress,
  markPreviewProgressCompleted,
  getCurrentPreviewRequestId,
  isPreviewStep,
  clearPreviewDetail,
  onSendPreview
}: UseSmartFillPreviewRequestOptions) {
  const previewAbortController = ref<AbortController | null>(null);
  let previewRequestVersion = 0;

  const stopPreviewRequest = () => {
    const controller = previewAbortController.value;
    controller?.abort();
    if (previewAbortController.value === controller) {
      previewAbortController.value = null;
    }
  };

  const invalidatePendingPreview = () => {
    stopPreviewRequest();
    stopPreviewProgressPolling();
    resetPreviewProgress();
    previewRequestVersion++;
    loading.value = false;
  };

  const doPreview = async () => {
    if (
      !ensurePermission(
        "btn:matching:preview-batch",
        "权限不足，无法执行匹配预览"
      )
    ) {
      return;
    }
    if (!uploadedFile.value) return;

    const requestVersion = ++previewRequestVersion;
    const fileId = uploadedFile.value.fileId;
    stopLlmStream();

    const selectedConfigs = batchTableConfigs.value.filter(t => t.selected);
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
    clearPreviewDetail();
    taskId.value = null;
    lastDownloadFailed.value = false;
    loading.value = true;
    const previewRequestId = createPreviewRequestId();
    startPreviewProgressPolling(previewRequestId, () => loading.value);
    stopPreviewRequest();
    const controller = new AbortController();
    previewAbortController.value = controller;

    try {
      const scope = getScope();
      const requestData = {
        fileId: uploadedFile.value.fileId,
        previewRequestId,
        tables: selectedConfigs.map(t => ({
          tableIndex: t.tableIndex,
          projectColumnIndex: t.projectColumnIndex,
          specificationColumnIndex: t.specificationColumnIndex,
          acceptanceColumnIndex: t.acceptanceColumnIndex,
          remarkColumnIndex: t.remarkColumnIndex,
          headerRowStart: t.headerRowStart,
          headerRowCount: t.headerRowCount,
          dataStartRow: t.dataStartRow,
          dataEndRow: t.dataEndRow,
          regions: t.regions,
          filterEmptySourceRows: getEffectiveFilterEmptySourceRows(t)
        })),
        customerId: scope.customerId,
        processId: scope.processId,
        machineModelId: scope.machineModelId,
        config: matchConfig.value
      };
      const doRequest = onSendPreview
        ? (data: typeof requestData, ctrl: AbortController) =>
            onSendPreview(data, ctrl)
        : (data: typeof requestData, ctrl: AbortController) =>
            batchPreviewMatch(data, { signal: ctrl.signal });
      const res = await doRequest(requestData, controller);

      if (res.code === 0) {
        if (
          requestVersion !== previewRequestVersion ||
          (isPreviewStep ? !isPreviewStep() : currentStep.value !== 3) ||
          uploadedFile.value?.fileId !== fileId ||
          previewAbortController.value !== controller
        ) {
          return;
        }

        markPreviewProgressCompleted();
        batchPreviewResults.value = res.data.tables;
        const hasPreviewRows = res.data.tables.some(
          table => table.items.length > 0
        );
        if (!hasPreviewRows) {
          markPreviewEmptyResults();
          ElMessage.warning("未找到可匹配的数据");
        } else {
          resetPreviewState();
          if (res.data.totalMatched === 0) {
            ElMessage.warning("当前没有完全命中的数据，可在未命中行中手工填写");
          }
        }
        if (matchConfig.value.exactMatchOnly) {
          return;
        }
        startLlmStream();
      } else {
        if (getCurrentPreviewRequestId() === previewRequestId) {
          stopPreviewProgressPolling();
        }
        if (requestVersion !== previewRequestVersion) return;
        ElMessage.error(resolvePreviewFailure(res.message));
      }
    } catch (error: unknown) {
      const requestError = error as {
        code?: string;
        name?: string;
        isCancelRequest?: boolean;
      };
      if (
        requestVersion !== previewRequestVersion ||
        controller.signal.aborted ||
        requestError.code === "ERR_CANCELED" ||
        requestError.name === "CanceledError" ||
        requestError.isCancelRequest
      ) {
        return;
      }
      if (getCurrentPreviewRequestId() === previewRequestId) {
        stopPreviewProgressPolling();
      }
      ElMessage.error(resolvePreviewFailure(getRequestErrorMessage(error, "")));
    } finally {
      if (previewAbortController.value === controller) {
        previewAbortController.value = null;
      }
      if (requestVersion === previewRequestVersion) {
        loading.value = false;
      }
    }
  };

  return {
    doPreview,
    stopPreviewRequest,
    invalidatePendingPreview,
    previewAbortController
  };
}
