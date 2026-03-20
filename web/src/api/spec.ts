import { http } from "@/utils/http";
import type { ApiResponse, PagedData, PagedRequest } from "./customer";

/** 验收规格类型 */
export interface AcceptanceSpec {
  id: number;
  customerId: number;
  processId?: number;
  machineModelId?: number;
  processName: string;
  machineModelName: string;
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
  processId?: number;
  machineModelId?: number;
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
  machineModelId?: number;
  processIdIsNull?: boolean;
  machineModelIdIsNull?: boolean;
}

/** 验收规格分组汇总 */
export interface SpecGroup {
  customerId: number;
  customerName: string;
  machineModelId?: number;
  machineModelName?: string;
  processId?: number;
  processName?: string;
  specCount: number;
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
  processId?: number;
  machineModelId?: number;
  wordFileId: number;
  items: SpecImportItem[];
}

/** 批量导入结果 */
export interface BatchImportResult {
  successCount: number;
  failedCount: number;
  totalCount: number;
}

export interface SpecDuplicateItem {
  id: number;
  project: string;
  specification: string;
  acceptance?: string;
  remark?: string;
  importedAt: string;
}

export interface SpecDuplicateGroup {
  groupType: "exact" | "similar";
  project: string;
  specificationPreview: string;
  reason: string;
  similarityScore: number;
  itemCount: number;
  items: SpecDuplicateItem[];
}

export interface SpecDuplicateDetectionResult {
  scannedCount: number;
  exactGroupCount: number;
  similarGroupCount: number;
  exactGroups: SpecDuplicateGroup[];
  similarGroups: SpecDuplicateGroup[];
}

export interface SpecDuplicateDetectionRequest {
  keyword?: string;
  customerId?: number;
  processId?: number;
  machineModelId?: number;
  processIdIsNull?: boolean;
  machineModelIdIsNull?: boolean;
  minSimilarity?: number;
  maxGroups?: number;
}

export interface SpecSemanticSearchRequest {
  queries: string[];
  customerId?: number;
  processId?: number;
  machineModelId?: number;
  processIdIsNull?: boolean;
  machineModelIdIsNull?: boolean;
  topK?: number;
  minScore?: number;
  embeddingServiceId?: number;
}

export interface SpecSemanticSearchItem extends AcceptanceSpec {
  score: number;
}

export interface SpecSemanticSearchGroup {
  queryIndex: number;
  queryText: string;
  totalHits: number;
  items: SpecSemanticSearchItem[];
}

export interface SpecSemanticSearchResponse {
  queryCount: number;
  candidateCount: number;
  embeddingModel?: string;
  groups: SpecSemanticSearchGroup[];
}

const baseUrl = "/api/specs";

/** 获取验收规格分组汇总 */
export const getSpecGroups = () => {
  return http.request<ApiResponse<SpecGroup[]>>("get", `${baseUrl}/groups`);
};

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

/** 规格重复/近重复排查 */
export const detectSpecDuplicateGroups = (
  params: SpecDuplicateDetectionRequest
) => {
  return http.request<ApiResponse<SpecDuplicateDetectionResult>>(
    "get",
    `${baseUrl}/duplicate-groups`,
    { params }
  );
};

/** 规格语义搜索 */
export const semanticSearchSpecs = (data: SpecSemanticSearchRequest) => {
  return http.request<ApiResponse<SpecSemanticSearchResponse>>(
    "post",
    `${baseUrl}/semantic-search`,
    { data }
  );
};
