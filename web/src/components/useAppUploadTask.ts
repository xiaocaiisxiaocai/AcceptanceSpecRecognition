import { computed, ref } from "vue";
import type { AxiosProgressEvent } from "axios";
import type { UploadRequestOptions } from "element-plus";
import { getRequestErrorMessage } from "@/utils/error-message";
import {
  formatUploadBytes,
  isUploadRequestCancelled
} from "@/utils/upload-request";

export type AppUploadPhase =
  | "idle"
  | "uploading"
  | "processing"
  | "success"
  | "failure";

export interface AppUploadRequestContext {
  signal: AbortSignal;
  onUploadProgress: (event: AxiosProgressEvent) => void;
}

export type AppUploadRequest = (
  options: UploadRequestOptions,
  context: AppUploadRequestContext
) => Promise<void>;

export const useAppUploadTask = (
  request: AppUploadRequest,
  fallbackError = "上传失败，请重试"
) => {
  const phase = ref<AppUploadPhase>("idle");
  const progressPercent = ref<number | null>(null);
  const loadedBytes = ref(0);
  const totalBytes = ref<number | null>(null);
  const errorMessage = ref("");
  let requestVersion = 0;
  let controller: AbortController | undefined;

  const active = computed(
    () => phase.value === "uploading" || phase.value === "processing"
  );
  const progressText = computed(() => {
    if (progressPercent.value !== null) return `${progressPercent.value}%`;
    if (loadedBytes.value > 0)
      return `已上传 ${formatUploadBytes(loadedBytes.value)}`;
    return "正在建立上传连接";
  });

  const reset = () => {
    phase.value = "idle";
    progressPercent.value = null;
    loadedBytes.value = 0;
    totalBytes.value = null;
    errorMessage.value = "";
  };

  const updateProgress = (version: number, event: AxiosProgressEvent) => {
    if (version !== requestVersion || controller?.signal.aborted) return;
    loadedBytes.value = Math.max(0, event.loaded);
    totalBytes.value =
      typeof event.total === "number" && event.total > 0 ? event.total : null;
    const ratio =
      totalBytes.value !== null
        ? loadedBytes.value / totalBytes.value
        : typeof event.progress === "number"
          ? event.progress
          : null;
    progressPercent.value =
      ratio === null
        ? null
        : Math.min(100, Math.max(0, Math.round(ratio * 100)));
    if (progressPercent.value === 100) phase.value = "processing";
  };

  const execute = async (options: UploadRequestOptions) => {
    controller?.abort();
    const version = ++requestVersion;
    const requestController = new AbortController();
    controller = requestController;
    phase.value = "uploading";
    progressPercent.value = null;
    loadedBytes.value = 0;
    totalBytes.value = null;
    errorMessage.value = "";

    try {
      await request(options, {
        signal: requestController.signal,
        onUploadProgress: event => updateProgress(version, event)
      });
      if (version !== requestVersion || requestController.signal.aborted)
        return;
      phase.value = "success";
    } catch (error) {
      if (
        version !== requestVersion ||
        requestController.signal.aborted ||
        isUploadRequestCancelled(error)
      ) {
        return;
      }
      phase.value = "failure";
      errorMessage.value = getRequestErrorMessage(error, fallbackError);
      throw error;
    } finally {
      if (version === requestVersion && controller === requestController)
        controller = undefined;
    }
  };

  const cancel = () => {
    if (!controller) return;
    requestVersion += 1;
    controller.abort();
    controller = undefined;
    reset();
  };

  return {
    phase,
    active,
    progressPercent,
    progressText,
    loadedBytes,
    totalBytes,
    errorMessage,
    execute,
    cancel,
    reset
  };
};
