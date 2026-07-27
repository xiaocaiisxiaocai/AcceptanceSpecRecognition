import { ElMessage } from "element-plus";
import { getMatchingTaskStatus } from "@/api/matching";

export interface SmartFillActivationActions {
  getCurrentTaskId: () => string | null;
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

  const cancelReconciliation = () => {
    reconciliationVersion++;
  };

  const isCurrentReconciliation = (requestVersion: number, taskId: string) =>
    requestVersion === reconciliationVersion &&
    actions.getCurrentTaskId() === taskId;

  const pauseForDeactivation = () => {
    cancelReconciliation();
    actions.abortScope();
    actions.invalidatePreview();
    actions.stopProgress();
    actions.stopStream();
    actions.cancelRecognition();
  };

  const reconcileOnActivation = async (taskId: string | null) => {
    if (!taskId || actions.getCurrentTaskId() !== taskId) return;

    const requestVersion = ++reconciliationVersion;
    try {
      const response = await getMatchingTaskStatus(taskId);
      if (
        !isCurrentReconciliation(requestVersion, taskId) ||
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
      if (!isCurrentReconciliation(requestVersion, taskId)) return;
      actions.stopProgress();
      actions.invalidateStaleResponse();
      ElMessage.error("任务状态已失效，请重新执行填充");
    }
  };

  return {
    cancelReconciliation,
    pauseForDeactivation,
    reconcileOnActivation
  };
}
