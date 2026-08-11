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
  referenceCount: number;
  referenceVersion: number;
  lastReferencedAtUtc?: string | null;
  importedAt: string;
  updatedAt?: string | null;
  ownerOrgUnitId?: number | null;
}

export type SpecReferenceHistorySort = "oldest" | "newest";

export interface SpecReferenceHistoryItem {
  id: number;
  referenceOrdinal?: number | null;
  referenceVersion: number;
  isCurrentVersion: boolean;
  referencedAtUtc: string;
}

export interface SpecReferenceHistoryResponse {
  specId: number;
  currentReferenceVersion: number;
  currentReferenceCount: number;
  recordedReferenceCount: number;
  untrackedReferenceCount: number;
  includePreviousVersions: boolean;
  sort: SpecReferenceHistorySort;
  items: SpecReferenceHistoryItem[];
  total: number;
  page: number;
  pageSize: number;
}

export interface SpecReferenceHistoryRequest {
  page: number;
  pageSize: number;
  includePreviousVersions?: boolean;
  sort?: SpecReferenceHistorySort;
}

/** 创建验收规格请求 */
export interface CreateSpecRequest {
  businessOrgUnitId?: number;
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
  expectedReferenceVersion?: number;
  changeReason?: string;
  project: string;
  specification: string;
  acceptance?: string;
  remark?: string;
}

export type SpecContentVersionSort = "oldest" | "newest";

export interface SpecContentVersionItem {
  version: number;
  changedAtUtc: string;
  changedByUserId?: number | null;
  changedByNameSnapshot?: string | null;
  changeSource: string;
  changeReason?: string | null;
  restoredFromVersion?: number | null;
  isMigrationBaseline: boolean;
  changedFields: string[];
}

export interface SpecContentVersionHistory {
  specId: number;
  currentVersion: number;
  earliestAvailableVersion: number;
  hasUnavailableEarlierVersions: boolean;
  sort: SpecContentVersionSort;
  items: SpecContentVersionItem[];
  total: number;
  page: number;
  pageSize: number;
}

export interface SpecContentVersionDetail extends SpecContentVersionItem {
  specId: number;
  project: string;
  specification: string;
  acceptance?: string | null;
  remark?: string | null;
}

export interface SpecContentFieldDiff {
  before?: string | null;
  after?: string | null;
  changed: boolean;
}

export interface SpecContentVersionDiff {
  specId: number;
  fromVersion: number;
  toVersion: number;
  fields: Record<
    "project" | "specification" | "acceptance" | "remark",
    SpecContentFieldDiff
  >;
}

/** 验收规格列表请求参数 */
export interface SpecListRequest extends PagedRequest {
  orgUnitId?: number;
  customerId?: number;
  processId?: number;
  machineModelId?: number;
  processIdIsNull?: boolean;
  machineModelIdIsNull?: boolean;
  globalSearch?: boolean;
  importedFrom?: string;
  importedTo?: string;
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
  orgUnitId?: number;
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
  orgUnitId?: number;
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

export interface SpecRemarkReplaceRequest {
  orgUnitId: number;
  searchText: string;
  replacementText: string;
}

export interface SpecRemarkReplacePreviewRequest
  extends SpecRemarkReplaceRequest {
  page: number;
  pageSize: number;
}

export interface SpecRemarkReplaceSample {
  specId: number;
  project: string;
  beforePreview: string;
  afterPreview: string;
}

export interface SpecRemarkReplacePreviewResponse {
  affectedSpecCount: number;
  matchCount: number;
  confirmationToken: string;
  samplePage: number;
  samplePageSize: number;
  sampleTotal: number;
  samples: SpecRemarkReplaceSample[];
}

export interface SpecRemarkReplaceExecuteRequest
  extends SpecRemarkReplaceRequest {
  expectedAffectedSpecCount: number;
  expectedMatchCount: number;
  confirmationToken: string;
}

export interface SpecRemarkReplaceResult {
  updatedSpecCount: number;
  replacedMatchCount: number;
}

const baseUrl = "/api/specs";

/** 获取验收规格分组汇总 */
export const getSpecGroups = (params?: { orgUnitId?: number }) => {
  return http.request<ApiResponse<SpecGroup[]>>("get", `${baseUrl}/groups`, {
    params
  });
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

/** 获取验收规格逐次引用时间 */
export const getSpecReferenceHistory = (
  id: number,
  params: SpecReferenceHistoryRequest
) => {
  return http.request<ApiResponse<SpecReferenceHistoryResponse>>(
    "get",
    `${baseUrl}/${id}/reference-history`,
    { params }
  );
};

/** 获取验收规格完整内容版本列表 */
export const getSpecContentVersions = (
  id: number,
  params: {
    page: number;
    pageSize: number;
    sort?: SpecContentVersionSort;
  }
) => {
  return http.request<ApiResponse<SpecContentVersionHistory>>(
    "get",
    `${baseUrl}/${id}/content-versions`,
    { params }
  );
};

/** 获取验收规格指定内容版本 */
export const getSpecContentVersion = (id: number, version: number) => {
  return http.request<ApiResponse<SpecContentVersionDetail>>(
    "get",
    `${baseUrl}/${id}/content-versions/${version}`
  );
};

/** 比较验收规格的两个内容版本 */
export const getSpecContentVersionDiff = (
  id: number,
  fromVersion: number,
  toVersion: number
) => {
  return http.request<ApiResponse<SpecContentVersionDiff>>(
    "get",
    `${baseUrl}/${id}/content-version-diff`,
    { params: { fromVersion, toVersion } }
  );
};

/** 将旧内容恢复为新的当前版本 */
export const restoreSpecContentVersion = (
  id: number,
  version: number,
  data: { expectedCurrentVersion: number; reason?: string }
) => {
  return http.request<ApiResponse<AcceptanceSpec>>(
    "post",
    `${baseUrl}/${id}/content-versions/${version}/restore`,
    { data }
  );
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
export const semanticSearchSpecs = (
  data: SpecSemanticSearchRequest,
  signal?: AbortSignal
) => {
  return http.request<ApiResponse<SpecSemanticSearchResponse>>(
    "post",
    `${baseUrl}/semantic-search`,
    { data, signal }
  );
};

/** 预览部门内验收规格备注批量替换 */
export const previewSpecRemarkReplace = (
  data: SpecRemarkReplacePreviewRequest
) => {
  return http.request<ApiResponse<SpecRemarkReplacePreviewResponse>>(
    "post",
    `${baseUrl}/remark-replace/preview`,
    { data }
  );
};

/** 执行部门内验收规格备注批量替换 */
export const executeSpecRemarkReplace = (
  data: SpecRemarkReplaceExecuteRequest
) => {
  return http.request<ApiResponse<SpecRemarkReplaceResult>>(
    "post",
    `${baseUrl}/remark-replace`,
    { data }
  );
};
