import { http } from "@/utils/http";
import type { ApiResponse, PagedData, PagedRequest } from "./customer";

/** 制程类型 */
export interface Process {
  id: number;
  name: string;
  createdAt: string;
  specCount: number;
}

/** 创建制程请求 */
export interface CreateProcessRequest {
  name: string;
}

/** 更新制程请求 */
export interface UpdateProcessRequest {
  name: string;
}

/** 制程列表请求参数 */
export interface ProcessListRequest extends PagedRequest {
}

/** 验收规格类型（简化版，用于制程详情） */
export interface AcceptanceSpec {
  id: number;
  customerId: number;
  processId: number;
  processName: string;
  customerName: string;
  project: string;
  specification: string;
  acceptance?: string;
  remark?: string;
  importedAt: string;
}

const baseUrl = "/api/processes";

/** 获取制程列表 */
export const getProcessList = (params?: ProcessListRequest) => {
  return http.request<ApiResponse<PagedData<Process>>>("get", baseUrl, {
    params
  });
};

/** 获取制程详情 */
export const getProcess = (id: number) => {
  return http.request<ApiResponse<Process>>("get", `${baseUrl}/${id}`);
};

/** 创建制程 */
export const createProcess = (data: CreateProcessRequest) => {
  return http.request<ApiResponse<Process>>("post", baseUrl, { data });
};

/** 更新制程 */
export const updateProcess = (id: number, data: UpdateProcessRequest) => {
  return http.request<ApiResponse<Process>>("put", `${baseUrl}/${id}`, {
    data
  });
};

/** 删除制程 */
export const deleteProcess = (id: number) => {
  return http.request<ApiResponse<void>>("delete", `${baseUrl}/${id}`);
};

/** 获取制程的验收规格列表 */
export const getProcessSpecs = (
  processId: number,
  params?: PagedRequest
) => {
  return http.request<ApiResponse<PagedData<AcceptanceSpec>>>(
    "get",
    `${baseUrl}/${processId}/specs`,
    { params }
  );
};
