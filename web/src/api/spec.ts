import { http } from "@/utils/http";
import type { ApiResponse, PagedData, PagedRequest } from "./customer";

/** 验收规格类型 */
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

/** 创建验收规格请求 */
export interface CreateSpecRequest {
  customerId: number;
  processId: number;
  project: string;
  specification: string;
  acceptance?: string;
  remark?: string;
}

/** 更新验收规格请求 */
export interface UpdateSpecRequest {
  project: string;
  specification: string;
  acceptance?: string;
  remark?: string;
}

/** 验收规格列表请求参数 */
export interface SpecListRequest extends PagedRequest {
  customerId?: number;
  processId?: number;
}

/** 导入规格项 */
export interface SpecImportItem {
  project: string;
  specification: string;
  acceptance?: string;
  remark?: string;
}

/** 批量导入请求 */
export interface BatchImportSpecsRequest {
  customerId: number;
  processId: number;
  wordFileId: number;
  items: SpecImportItem[];
}

/** 批量导入结果 */
export interface BatchImportResult {
  successCount: number;
  failedCount: number;
  totalCount: number;
}

const baseUrl = "/api/specs";

/** 获取验收规格列表 */
export const getSpecList = (params?: SpecListRequest) => {
  return http.request<ApiResponse<PagedData<AcceptanceSpec>>>("get", baseUrl, {
    params
  });
};

/** 获取验收规格详情 */
export const getSpec = (id: number) => {
  return http.request<ApiResponse<AcceptanceSpec>>("get", `${baseUrl}/${id}`);
};

/** 创建验收规格 */
export const createSpec = (data: CreateSpecRequest) => {
  return http.request<ApiResponse<AcceptanceSpec>>("post", baseUrl, { data });
};

/** 更新验收规格 */
export const updateSpec = (id: number, data: UpdateSpecRequest) => {
  return http.request<ApiResponse<AcceptanceSpec>>("put", `${baseUrl}/${id}`, {
    data
  });
};

/** 删除验收规格 */
export const deleteSpec = (id: number) => {
  return http.request<ApiResponse<void>>("delete", `${baseUrl}/${id}`);
};

/** 批量导入验收规格 */
export const batchImportSpecs = (data: BatchImportSpecsRequest) => {
  return http.request<ApiResponse<BatchImportResult>>(
    "post",
    `${baseUrl}/batch-import`,
    { data }
  );
};

/** 批量删除验收规格 */
export const batchDeleteSpecs = (ids: number[]) => {
  return http.request<ApiResponse<void>>("delete", `${baseUrl}/batch`, {
    data: ids
  });
};
