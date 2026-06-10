import { computed, ref, toValue, type MaybeRefOrGetter } from "vue";
import {
  getBatchPreviewProgress,
  type BatchPreviewProgressResponse
} from "@/api/matching";

interface UseSmartFillPreviewProgressOptions {
  selectedTableCount?: MaybeRefOrGetter<number>;
}

/**
 * 匹配预览进度管理
 * 封装进度轮询、计时器、中断与展示派生逻辑
 */
export function useSmartFillPreviewProgress(
  options: UseSmartFillPreviewProgressOptions = {}
) {
  const previewProgress = ref<BatchPreviewProgressResponse | null>(null);
  const previewElapsedSeconds = ref(0);
  const previewProgressPollTimer = ref<number | null>(null);
  const previewElapsedTimer = ref<number | null>(null);
  const currentPreviewRequestId = ref<string | null>(null);

  const clearPreviewProgressTimers = () => {
    if (
      previewProgressPollTimer.value !== null &&
      typeof window !== "undefined"
    ) {
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

  const createPreviewRequestId = () => {
    if (
      typeof crypto !== "undefined" &&
      typeof crypto.randomUUID === "function"
    ) {
      return crypto.randomUUID();
    }
    return `preview-${Date.now()}-${Math.random().toString(16).slice(2, 10)}`;
  };

  const fetchBatchPreviewProgress = async (
    requestId: string,
    isLoading: () => boolean
  ) => {
    try {
      const res = await getBatchPreviewProgress(requestId);
      if (currentPreviewRequestId.value !== requestId || res.code !== 0) {
        return;
      }
      previewProgress.value = res.data;
      if (res.data.status !== "running") {
        stopPreviewProgressPolling();
      }
    } catch (error: unknown) {
      if (currentPreviewRequestId.value !== requestId) return;
      if (!isLoading()) {
        stopPreviewProgressPolling();
        return;
      }
      const axiosError = error as { response?: { status?: number } };
      if (axiosError?.response?.status === 404) {
        stopPreviewProgressPolling();
      }
    }
  };

  const startPreviewProgressPolling = (
    requestId: string,
    isLoading: () => boolean
  ) => {
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

    if (typeof window === "undefined") return;

    previewElapsedTimer.value = window.setInterval(() => {
      previewElapsedSeconds.value += 1;
    }, 1000);

    previewProgressPollTimer.value = window.setInterval(() => {
      void fetchBatchPreviewProgress(requestId, isLoading);
    }, 900);
  };

  const markPreviewProgressCompleted = () => {
    const requestId = currentPreviewRequestId.value;
    if (!requestId) return;

    const now = new Date().toISOString();
    previewProgress.value = {
      requestId,
      status: "completed",
      stage: "completed",
      stageText: "匹配预览已完成",
      detailText:
        previewProgress.value?.detailText ||
        `已完成 ${previewProgress.value?.completedItems ?? 0}/${previewProgress.value?.totalItems ?? 0} 行`,
      completedItems:
        previewProgress.value?.totalItems ??
        previewProgress.value?.completedItems ??
        0,
      totalItems:
        previewProgress.value?.totalItems ??
        previewProgress.value?.completedItems ??
        0,
      progressPercent: 100,
      startedAt: previewProgress.value?.startedAt ?? now,
      updatedAt: now,
      elapsedMs: previewElapsedSeconds.value * 1000
    };
    stopPreviewProgressPolling();
  };

  const previewProgressStageText = computed(
    () => previewProgress.value?.stageText || "正在准备匹配任务"
  );
  const previewProgressDetailText = computed(() => {
    if (previewProgress.value?.detailText) {
      return previewProgress.value.detailText;
    }

    const selectedTableCount =
      options.selectedTableCount === undefined
        ? 0
        : toValue(options.selectedTableCount);
    if (selectedTableCount > 0) {
      return `已选择 ${selectedTableCount} 个表格，正在等待真实进度`;
    }

    return "正在等待真实进度";
  });
  const previewProgressPercent = computed(() =>
    Math.min(
      Math.max(Math.round(previewProgress.value?.progressPercent ?? 0), 0),
      100
    )
  );
  const previewProgressCounterText = computed(() => {
    if (!previewProgress.value?.totalItems) {
      return "";
    }

    return `${previewProgress.value.completedItems}/${previewProgress.value.totalItems} 行`;
  });

  return {
    previewProgress,
    previewElapsedSeconds,
    previewProgressStageText,
    previewProgressDetailText,
    previewProgressPercent,
    previewProgressCounterText,
    currentPreviewRequestId,
    clearPreviewProgressTimers,
    stopPreviewProgressPolling,
    resetPreviewProgress,
    createPreviewRequestId,
    startPreviewProgressPolling,
    markPreviewProgressCompleted
  };
}
