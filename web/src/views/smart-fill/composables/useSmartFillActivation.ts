import { ElMessage } from "element-plus";
import { getMatchingTaskStatus } from "@/api/matching";

export interface SmartFillActivationActions {
  abortScope: () => void;
  invalidatePreview: () => void;
  stopProgress: () => void;
  stopStream: () => void;
  cancelRecognition: () => void;
  resumeProgress: (taskId: string) => void;
  restoreDownload: (taskId: string) => void;
  invalidateStaleResponse: () => void;
}

export function useSmartFillActivation(actions: SmartFillActivationActions) {
  let reconciliationVersion = 0;

  const pauseForDeactivation = () => {
    reconciliationVersion++;
    actions.abortScope();
    actions.invalidatePreview();
    actions.stopProgress();
    actions.stopStream();
    actions.cancelRecognition();
  };

  const reconcileOnActivation = async (taskId: string | null) => {
    if (!taskId) return;

    const requestVersion = ++reconciliationVersion;
    try {
      const response = await getMatchingTaskStatus(taskId);
      if (
        requestVersion !== reconciliationVersion ||
        response.code !== 0 ||
        response.data.taskId !== taskId
      ) {
        return;
      }

      if (response.data.status === "running") {
        actions.resumeProgress(taskId);
        return;
      }

      actions.stopProgress();
      if (response.data.status === "completed" && response.data.canDownload) {
        actions.restoreDownload(taskId);
        return;
      }

      actions.invalidateStaleResponse();
      ElMessage.error("任务执行失败，请重新执行填充");
    } catch {
      if (requestVersion !== reconciliationVersion) return;
      actions.stopProgress();
      actions.invalidateStaleResponse();
      ElMessage.error("任务状态已失效，请重新执行填充");
    }
  };

  return {
    pauseForDeactivation,
    reconcileOnActivation
  };
}
