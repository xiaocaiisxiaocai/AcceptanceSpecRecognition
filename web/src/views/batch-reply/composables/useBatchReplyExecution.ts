import { ref, type ComputedRef, type Ref } from "vue";
import { ElMessage } from "element-plus";
import {
  downloadBatchReplyResult,
  executeBatchReply,
  type BatchReplyExecuteResponse
} from "@/api/matching";
import { ensurePermission } from "@/utils/permission-guard";
import {
  buildBatchReplyExecuteRequest,
  buildBatchReplyExecuteSuccessMessage,
  triggerBrowserDownload
} from "../batch-reply-execution";
import type { BatchReplyTableConfigItem } from "../batch-reply-table-config";
import {
  BATCH_REPLY_DOWNLOAD_FAILED_MESSAGE,
  type BatchReplyTargetState
} from "../batch-reply-state";

type UseBatchReplyExecutionParams = {
  sourceSessionId: ComputedRef<string>;
  selectedSourceConfigs: ComputedRef<BatchReplyTableConfigItem[]>;
  executableTargets: ComputedRef<BatchReplyTargetState[]>;
  activeRootTab: Ref<string>;
};

export const useBatchReplyExecution = (
  params: UseBatchReplyExecutionParams
) => {
  const executeResult = ref<BatchReplyExecuteResponse | null>(null);
  const executing = ref(false);
  const downloadError = ref("");
  const downloadLoading = ref(false);
  let activeDownloadRequest: {
    result: BatchReplyExecuteResponse;
    promise: Promise<void>;
  } | null = null;

  const retryDownload = (): Promise<void> => {
    if (activeDownloadRequest) {
      return activeDownloadRequest.promise;
    }

    const result = executeResult.value;
    if (!result) return Promise.resolve();
    if (
      !ensurePermission(
        "api:batch-reply:download",
        "权限不足，无法下载批量回复结果"
      )
    ) {
      return Promise.resolve();
    }

    downloadLoading.value = true;
    downloadError.value = "";
    const requestState = {
      result,
      promise: Promise.resolve()
    };
    activeDownloadRequest = requestState;
    requestState.promise = (async () => {
      try {
        const blob = await downloadBatchReplyResult(result.taskId);
        if (
          activeDownloadRequest !== requestState ||
          executeResult.value?.taskId !== result.taskId
        ) {
          return;
        }
        triggerBrowserDownload(blob, result.downloadFileName);
      } catch {
        if (
          activeDownloadRequest === requestState &&
          executeResult.value?.taskId === result.taskId
        ) {
          downloadError.value = BATCH_REPLY_DOWNLOAD_FAILED_MESSAGE;
        }
      } finally {
        if (activeDownloadRequest === requestState) {
          activeDownloadRequest = null;
          downloadLoading.value = false;
        }
      }
    })();
    return requestState.promise;
  };

  const executeReadyTargets = async () => {
    if (executing.value || downloadLoading.value) {
      return;
    }

    if (
      !ensurePermission("btn:batch-reply:execute", "权限不足，无法执行批量回复")
    ) {
      return;
    }

    if (!params.sourceSessionId.value) {
      ElMessage.warning("请先上传来源文件");
      return;
    }

    if (params.selectedSourceConfigs.value.length === 0) {
      ElMessage.warning("请至少选择一个来源表");
      return;
    }

    if (params.executableTargets.value.length === 0) {
      ElMessage.warning("请至少完成一个目标文件的逐表预览");
      return;
    }

    executing.value = true;
    let result: BatchReplyExecuteResponse | null = null;
    try {
      const res = await executeBatchReply(
        buildBatchReplyExecuteRequest({
          sessionId: params.sourceSessionId.value,
          sourceConfigs: params.selectedSourceConfigs.value,
          executableTargets: params.executableTargets.value
        })
      );

      if (res.code !== 0) {
        ElMessage.error(res.message || "批量回复执行失败");
        return;
      }

      executeResult.value = res.data;
      result = res.data;
      params.activeRootTab.value = "result";
      ElMessage.success(buildBatchReplyExecuteSuccessMessage(res.data));
    } catch {
      ElMessage.error("批量回复执行失败");
    } finally {
      executing.value = false;
    }

    if (result) {
      await retryDownload();
    }
  };

  return {
    downloadError,
    downloadLoading,
    executeResult,
    executing,
    executeReadyTargets,
    retryDownload
  };
};
