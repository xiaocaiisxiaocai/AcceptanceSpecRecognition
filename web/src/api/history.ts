import { http } from "@/utils/http";
import type { ApiResponse, PagedData } from "./customer";

export enum OperationType {
  Import = 0,
  Fill = 1,
  Delete = 2
}

export interface OperationHistory {
  id: number;
  operationType: OperationType;
  targetFile?: string | null;
  details?: string | null;
  canUndo: boolean;
  createdAt: string;
}

export interface HistoryListRequest {
  page?: number;
  pageSize?: number;
  operationType?: OperationType;
  canUndo?: boolean;
  keyword?: string;
  from?: string; // ISO
  to?: string; // ISO
}

const baseUrl = "/api/history";

export const getHistoryList = (params?: HistoryListRequest) => {
  return http.request<ApiResponse<PagedData<OperationHistory>>>("get", baseUrl, {
    params
  });
};

export const undoHistory = (id: number) => {
  return http.request<ApiResponse<void>>("post", `${baseUrl}/${id}/undo`);
};

