import type {
  BatchReplyExecuteResponse,
  BatchReplyTablePreviewResponse
} from "@/api/matching";
import type { TableInfo } from "@/api/document";
import type {
  BatchReplyTableConfigItem,
  SourceTableOption
} from "./batch-reply-table-config";
import { isTargetExecutable } from "./batch-reply-table-config";

export const BATCH_REPLY_DOWNLOAD_FAILED_MESSAGE =
  "批量回复已执行成功，但结果下载失败，请重试下载";

export type BatchReplySourceFileState = {
  sessionId: string;
  sourceFileName: string;
  sourceFileType: number;
  tableCount: number;
};

export type BatchReplyTargetState = {
  targetId: string;
  fileName: string;
  fileType: number;
  tableCount: number;
  size: number;
  signature: string;
  tables: TableInfo[];
  configs: BatchReplyTableConfigItem[];
  previewResults: Record<number, BatchReplyTablePreviewResponse | null>;
  previewLoadingTableIndexes: number[];
};

export type BatchReplyPermissionState = {
  canUploadSourceFile: boolean;
  canUploadTargetFile: boolean;
  canPreviewBatchReply: boolean;
  canExecuteBatchReply: boolean;
  canDownloadBatchReply: boolean;
};

export type BatchReplyDerivedState<TTarget> = {
  sourceSessionId: string;
  sourceIsExcel: boolean;
  targetAccept: ".xlsx" | ".docx";
  selectedSourceConfigs: BatchReplyTableConfigItem[];
  selectedSourceTableOptions: SourceTableOption[];
  executableTargets: TTarget[];
  duplicateDialogVisible: boolean;
  executeDisabled: boolean;
};

type TargetExecutableState = {
  configs: BatchReplyTableConfigItem[];
  previewResults: Record<number, { canApply?: boolean } | null>;
};

export const buildBatchReplyPermissionState = (
  hasPermission: (permission: string) => boolean
): BatchReplyPermissionState => ({
  canUploadSourceFile: hasPermission("api:batch-reply:upload-source"),
  canUploadTargetFile: hasPermission("api:batch-reply:upload"),
  canPreviewBatchReply: hasPermission("btn:batch-reply:preview"),
  canExecuteBatchReply: hasPermission("btn:batch-reply:execute"),
  canDownloadBatchReply: hasPermission("api:batch-reply:download")
});

export const buildSelectedSourceTableOptions = (
  configs: BatchReplyTableConfigItem[]
): SourceTableOption[] =>
  configs.map(item => ({
    value: item.tableIndex,
    label: item.tableInfo.name || `来源表 ${item.tableIndex + 1}`
  }));

export const buildBatchReplyDerivedState = <
  TTarget extends TargetExecutableState
>(params: {
  sourceFile: BatchReplySourceFileState | null;
  sourceConfigs: BatchReplyTableConfigItem[];
  targetFiles: TTarget[];
  duplicateDialogVisible: boolean;
  permissions: Pick<
    BatchReplyPermissionState,
    "canExecuteBatchReply" | "canDownloadBatchReply"
  >;
}): BatchReplyDerivedState<TTarget> => {
  const sourceSessionId = params.sourceFile?.sessionId ?? "";
  const sourceIsExcel = params.sourceFile?.sourceFileType === 1;
  const selectedSourceConfigs = params.sourceConfigs.filter(
    item => item.selected
  );
  const executableTargets = params.targetFiles.filter(isTargetExecutable);

  return {
    sourceSessionId,
    sourceIsExcel,
    targetAccept: sourceIsExcel ? ".xlsx" : ".docx",
    selectedSourceConfigs,
    selectedSourceTableOptions: buildSelectedSourceTableOptions(
      selectedSourceConfigs
    ),
    executableTargets,
    duplicateDialogVisible: params.duplicateDialogVisible,
    executeDisabled:
      executableTargets.length === 0 ||
      !params.permissions.canExecuteBatchReply ||
      !params.permissions.canDownloadBatchReply
  };
};

export const getBatchReplyResultFiles = (
  executeResult: BatchReplyExecuteResponse | null
) => executeResult?.files ?? [];
