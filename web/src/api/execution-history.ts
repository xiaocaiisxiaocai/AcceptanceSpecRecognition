import { http } from "@/utils/http";
import type { ApiResponse, PagedData, PagedRequest } from "./customer";
import type { MatchResult } from "./matching";

export interface ExecutionHistoryListRequest extends PagedRequest {
  taskType?: string;
}

export interface ExecutionHistoryListItem {
  id: number;
  taskId: string;
  taskType: string;
  sourceFileId?: number;
  sourceFileName: string;
  sourceFileType?: number;
  fileCount: number;
  totalRowCount: number;
  matchedRowCount: number;
  adoptedRowCount: number;
  unmatchedRowCount: number;
  skippedRowCount: number;
  notAdoptedRowCount: number;
  manualSelectedRowCount: number;
  smartFillSummary?: ExecutionHistorySmartFillSummary;
  createdAt: string;
}

export interface ExecutionHistorySmartFillSummary {
  exactMatchedRowCount?: number | null;
  aiMatchedRowCount?: number | null;
  manualConfirmedRowCount?: number | null;
  manualEditedRowCount?: number | null;
  notUsedRowCount?: number | null;
  hasPlaybackArchive: boolean;
}

export interface ExecutionHistoryRow {
  rowIndex: number;
  project: string;
  specification: string;
  matchedSpecId?: number;
  matchedProject?: string;
  matchedSpecification?: string;
  acceptance?: string;
  remark?: string;
  confidencePercent: number;
  status: string;
  isManualSelected: boolean;
  acceptanceColumnIndex?: number;
  remarkColumnIndex?: number;
}

export interface ExecutionHistorySheet {
  sheetIndex: number;
  sheetName: string;
  rows: ExecutionHistoryRow[];
}

export interface ExecutionHistoryFile {
  fileName: string;
  fileType?: number;
  sheets: ExecutionHistorySheet[];
}

export interface ExecutionHistorySmartFillPreviewSnapshot {
  confidenceLevel: "high" | "medium" | "low" | "none";
  noMatchReason?: string;
  bestMatch?: MatchResult;
}

export interface ExecutionHistorySmartFillExecutionSnapshot {
  selectedSpecId?: number;
  selectedProject?: string;
  selectedSpecification?: string;
  finalAcceptance?: string;
  finalRemark?: string;
  overrideAcceptance?: string;
  overrideRemark?: string;
  manualConfirmed: boolean;
  manualEdited: boolean;
  status: string;
}

export interface ExecutionHistorySmartFillRow {
  regionId?: string;
  regionIndex?: number;
  acceptanceColumnIndex?: number;
  remarkColumnIndex?: number;
  rowIndex: number;
  sourceProject: string;
  sourceSpecification: string;
  status: string;
  matchOrigin: "exact" | "ai" | "none";
  isManualConfirmed: boolean;
  isManualEdited: boolean;
  displayTags: string[];
  previewSnapshot: ExecutionHistorySmartFillPreviewSnapshot;
  executionSnapshot: ExecutionHistorySmartFillExecutionSnapshot;
}

export interface ExecutionHistorySmartFillSheet {
  sheetIndex: number;
  sheetName: string;
  rows: ExecutionHistorySmartFillRow[];
}

export interface ExecutionHistorySmartFillFile {
  fileName: string;
  fileType?: number;
  sheets: ExecutionHistorySmartFillSheet[];
}

export interface ExecutionHistorySmartFillPlayback {
  payloadVersion: number;
  isLegacy: boolean;
  isSlimmed?: boolean;
  hasFullArchive?: boolean;
  fullArchiveRelativePath?: string;
  legacyMessage?: string;
  files: ExecutionHistorySmartFillFile[];
}

export interface ExecutionHistoryBatchReplyDetail {
  files: ExecutionHistoryFile[];
}

export interface ExecutionHistoryDetail extends ExecutionHistoryListItem {
  files: ExecutionHistoryFile[];
  smartFillPlayback?: ExecutionHistorySmartFillPlayback;
  batchReplyDetail?: ExecutionHistoryBatchReplyDetail;
}

const baseUrl = "/api/execution-history";

export const getExecutionHistoryList = (
  params?: ExecutionHistoryListRequest,
  signal?: AbortSignal
) => {
  return http.request<ApiResponse<PagedData<ExecutionHistoryListItem>>>(
    "get",
    baseUrl,
    { params, signal }
  );
};

export const getExecutionHistoryDetail = (id: number) => {
  return http.request<ApiResponse<ExecutionHistoryDetail>>(
    "get",
    `${baseUrl}/${id}`
  );
};

export const getExecutionHistorySmartFillRow = (
  id: number,
  params: {
    fileIndex: number;
    sheetIndex: number;
    rowIndex: number;
  }
) => {
  return http.request<ApiResponse<ExecutionHistorySmartFillRow>>(
    "get",
    `${baseUrl}/${id}/smart-fill/rows`,
    { params }
  );
};
