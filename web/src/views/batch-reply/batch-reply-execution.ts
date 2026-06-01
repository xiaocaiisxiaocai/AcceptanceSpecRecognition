import type {
  BatchReplyExecuteRequest,
  BatchReplyExecuteResponse
} from "@/api/matching";
import {
  toBatchTableConfig,
  type BatchReplyTableConfigItem
} from "./batch-reply-table-config";

type ExecutableTarget = {
  targetId: string;
  configs: BatchReplyTableConfigItem[];
};

export const buildBatchReplyExecuteRequest = (params: {
  sessionId: string;
  sourceConfigs: BatchReplyTableConfigItem[];
  executableTargets: ExecutableTarget[];
}): BatchReplyExecuteRequest => ({
  sessionId: params.sessionId,
  sourceTables: params.sourceConfigs.map(toBatchTableConfig),
  targets: params.executableTargets.map(target => ({
    targetId: target.targetId,
    tables: target.configs.filter(item => item.selected).map(toBatchTableConfig)
  }))
});

export const buildBatchReplyExecuteSuccessMessage = (
  result: Pick<BatchReplyExecuteResponse, "successCount" | "failedCount">
) =>
  result.failedCount > 0
    ? `批量回复完成，成功 ${result.successCount} 份，失败 ${result.failedCount} 份`
    : `批量回复完成，成功 ${result.successCount} 份`;

export const triggerBrowserDownload = (blob: Blob, fileName: string) => {
  const url = window.URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = fileName;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  window.URL.revokeObjectURL(url);
};
