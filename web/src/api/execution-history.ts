import { http } from "@/utils/http";
import type { ApiResponse, PagedData, PagedRequest } from "./customer";

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
  createdAt: string;
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

export interface ExecutionHistoryDetail extends ExecutionHistoryListItem {
  files: ExecutionHistoryFile[];
}

const baseUrl = "/api/execution-history";

export const getExecutionHistoryList = (params?: ExecutionHistoryListRequest) => {
  return http.request<ApiResponse<PagedData<ExecutionHistoryListItem>>>(
    "get",
    baseUrl,
    { params }
  );
};

export const getExecutionHistoryDetail = (id: number) => {
  return http.request<ApiResponse<ExecutionHistoryDetail>>(
    "get",
    `${baseUrl}/${id}`
  );
};
