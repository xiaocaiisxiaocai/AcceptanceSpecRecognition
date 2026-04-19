import {
  createAuthorizedFetchInit,
  ensureFetchResponseAuthHandled,
  http
} from "@/utils/http";
import type { PureHttpRequestConfig } from "@/utils/http/types.d";
import type { ApiResponse } from "./customer";
import type { TableData, TableInfo } from "./document";

export const DEFAULT_MIN_SCORE_THRESHOLD = 0.9;
export const DEFAULT_HIGH_CONFIDENCE_THRESHOLD = 0.98;
export const DEFAULT_RECALL_TOP_K = 2;
export const MAX_RECALL_TOP_K = 3;
export const DEFAULT_AMBIGUITY_MARGIN = 0.02;

/** 匹配配置 */
export interface MatchConfig {
  /** 选定的 Embedding 服务ID（为空则自动选择） */
  embeddingServiceId?: number;
  /** 选定的 LLM 服务ID（为空则自动选择） */
  llmServiceId?: number;
  /** 最小匹配阈值 */
  minScoreThreshold?: number;
  /** 高置信结果分层阈值 */
  highConfidenceThreshold?: number;
  /** 第一阶段召回数量 */
  recallTopK?: number;
  /** 歧义分差阈值 */
  ambiguityMargin?: number;
  /** LLM 并行处理数（1~10） */
  llmParallelism?: number;
  /** LLM 单行处理超时（秒） */
  llmRowTimeoutSeconds?: number;
  /** LLM 单行失败重试次数 */
  llmRetryCount?: number;
  /** LLM 熔断阈值（累计失败次数） */
  llmCircuitBreakFailures?: number;
  /** 是否过滤项目/规格均为空的行 */
  filterEmptySourceRows?: boolean;
}

/** 待匹配的源项 */
export interface MatchSourceItem {
  /** 行索引 */
  rowIndex: number;
  /** 项目名称 */
  project: string;
  /** 规格内容 */
  specification: string;
}

/** 匹配结果 */
export interface MatchIssue {
  /** 问题编码 */
  code: string;
  /** 严重级别 */
  severity: string;
  /** 问题所属字段 */
  fieldName?: string;
  /** 源值 */
  sourceValue?: string;
  /** 候选值 */
  candidateValue?: string;
  /** 用户说明 */
  message: string;
  /** 建议动作 */
  suggestedAction?: string;
}

/** 实体证据 */
export interface MatchEntityEvidence {
  /** 实体类型 */
  entityType: string;
  /** 源值 */
  sourceValue: string;
  /** 候选值 */
  candidateValue: string;
  /** 源归一化值 */
  normalizedSourceValue: string;
  /** 候选归一化值 */
  normalizedCandidateValue: string;
  /** 证据关系 */
  relation:
    | "exact"
    | "compatible"
    | "overlap"
    | "conflict"
    | "aliasSame"
    | "parentChild"
    | "possiblyRelated"
    | "unknown";
}

export type LlmEquivalenceVerdict = "equivalent" | "different" | "uncertain";
export type MatchSelectionMode =
  | "exactShortcut"
  | "embeddingTop1"
  | "aiRerank";

export type LlmEquivalenceReasonType =
  | "format_only"
  | "punctuation_only"
  | "equivalent_expression"
  | "symbol_equivalent"
  | "semantic_difference"
  | "symbol_conflict"
  | "uncertain";

export interface LlmEquivalenceResult {
  /** 裁决结论 */
  verdict: LlmEquivalenceVerdict;
  /** 原因类型 */
  reasonType: LlmEquivalenceReasonType;
  /** 中文原因 */
  reason?: string;
  /** 置信度（0-1） */
  confidence: number;
}

/** 匹配结果 */
export interface MatchResult {
  /** 匹配的验收规格ID */
  specId: number;
  /** 匹配的项目名称 */
  project: string;
  /** 匹配的规格内容 */
  specification: string;
  /** 匹配的验收标准 */
  acceptance?: string;
  /** 匹配的备注 */
  remark?: string;
  /** 综合得分（0-1） */
  score: number;
  /** Embedding 原始得分（0-1） */
  embeddingScore: number;
  /** 各算法得分详情 */
  scoreDetails: Record<string, number>;
  /** 最终决策 */
  decision?: "autoApply" | "manualReview" | "reject";
  /** 最终选定方式 */
  selectionMode?: MatchSelectionMode;
  /** 最终选定摘要 */
  selectionSummary?: string;
  /** 证据摘要 */
  evidenceSummary?: string[];
  /** 冲突摘要 */
  conflictSummary?: string[];
  /** 结构化问题列表 */
  issues?: MatchIssue[];
  /** 实体证据 */
  entities?: MatchEntityEvidence[];
  /** Top候选列表（含Top1） */
  topCandidates: MatchCandidateOption[];
  /** 第一阶段召回候选数 */
  recalledCandidateCount: number;
  /** 是否为高歧义样本 */
  isAmbiguous: boolean;
  /** Top1 与 Top2 的最终分差 */
  scoreGap?: number;
  /** 重排摘要 */
  rerankSummary?: string;
  /** AI 等价裁决 */
  llmEquivalence?: LlmEquivalenceResult;
  /** AI 复核分 */
  reviewScore?: number;
  /** AI 复核原因 */
  reviewReason?: string;
  /** AI 复核说明 */
  reviewCommentary?: string;
  /** AI 复核确认令牌 */
  reviewApprovalToken?: string;
}

/** 匹配详情中的候选项 */
export interface MatchCandidateOption {
  /** 候选排名（从1开始） */
  rank: number;
  /** 匹配的验收规格ID */
  specId: number;
  /** 匹配的项目名称 */
  project: string;
  /** 匹配的规格内容 */
  specification: string;
  /** 匹配的验收标准 */
  acceptance?: string;
  /** 匹配的备注 */
  remark?: string;
  /** 当前候选得分 */
  score: number;
  /** Embedding 原始得分（0-1） */
  embeddingScore: number;
  /** 各算法得分详情 */
  scoreDetails: Record<string, number>;
  /** 最终决策 */
  decision?: "autoApply" | "manualReview" | "reject";
  /** 最终选定方式 */
  selectionMode?: MatchSelectionMode;
  /** 最终选定摘要 */
  selectionSummary?: string;
  /** 证据摘要 */
  evidenceSummary?: string[];
  /** 冲突摘要 */
  conflictSummary?: string[];
  /** 结构化问题列表 */
  issues?: MatchIssue[];
  /** 实体证据 */
  entities?: MatchEntityEvidence[];
  /** 重排摘要 */
  rerankSummary?: string;
  /** AI 等价裁决 */
  llmEquivalence?: LlmEquivalenceResult;
}

/** 匹配预览项 */
export interface MatchPreviewItem {
  /** 行索引 */
  rowIndex: number;
  /** 源项目名称 */
  sourceProject: string;
  /** 源规格内容 */
  sourceSpecification: string;
  /** 最佳匹配结果 */
  bestMatch?: MatchResult;
  /** LLM复核流式内容 */
  llmReviewDraft?: string;
  /** LLM复核错误 */
  llmReviewError?: string;
  /** LLM复核阶段 */
  llmReviewStage?: "streaming" | "done" | "error";
  /** 不匹配原因 */
  noMatchReason?: string;
  /** 是否有匹配 */
  hasMatch: boolean;
  /** 置信度级别 */
  confidenceLevel: "high" | "medium" | "low" | "none";
}

/** 填充映射 */
export interface FillMapping {
  /** 行索引 */
  rowIndex: number;
  /** 选择的验收规格ID */
  specId?: number;
  /** 是否已由用户人工确认 */
  manualConfirmed?: boolean;
  /** 服务端签发的 AI 复核放行令牌 */
  reviewApprovalToken?: string;
  /** 本次导出的验收标准覆盖值 */
  overrideAcceptance?: string;
  /** 本次导出的备注覆盖值 */
  overrideRemark?: string;
}

/** 执行填充响应 */
export interface ExecuteFillResponse {
  /** 填充任务ID */
  taskId: string;
  /** 填充成功数量 */
  filledCount: number;
  /** 跳过数量 */
  skippedCount: number;
  /** 下载URL */
  downloadUrl: string;
}

const baseUrl = "/api/matching";

/** 下载填充结果 */
export const downloadFillResult = (taskId: string) => {
  return http.request<Blob>("get", `${baseUrl}/download/${taskId}`, {
    responseType: "blob"
  });
};

/** 获取下载URL */
export const getDownloadUrl = (taskId: string) => {
  return `${baseUrl}/download/${taskId}`;
};

/** 默认匹配配置 */
export const defaultMatchConfig: MatchConfig = {
  minScoreThreshold: DEFAULT_MIN_SCORE_THRESHOLD,
  highConfidenceThreshold: DEFAULT_HIGH_CONFIDENCE_THRESHOLD,
  recallTopK: DEFAULT_RECALL_TOP_K,
  ambiguityMargin: DEFAULT_AMBIGUITY_MARGIN,
  llmParallelism: 8,
  llmRowTimeoutSeconds: 45,
  llmRetryCount: 1,
  llmCircuitBreakFailures: 10,
  filterEmptySourceRows: true
};

// ===== 批量填充 =====

/** 批量表格配置 */
export interface BatchTableConfig {
  /** 表格索引 */
  tableIndex: number;
  /** 批量回复目标表对应的来源表索引（可选） */
  sourceTableIndex?: number;
  /** 项目列索引 */
  projectColumnIndex: number;
  /** 规格列索引 */
  specificationColumnIndex: number;
  /** 验收列索引 */
  acceptanceColumnIndex: number;
  /** 备注列索引（可选） */
  remarkColumnIndex?: number;
  /** Excel 表头起始行（1-based，可选） */
  headerRowStart?: number;
  /** Excel 表头行数（可选） */
  headerRowCount?: number;
  /** Excel 数据起始行（1-based，可选） */
  dataStartRow?: number;
  /** 是否过滤项目/规格均为空的行（表格级，可选；未传时走全局配置） */
  filterEmptySourceRows?: boolean;
  /** 重复项目/规格组合处理决议 */
  duplicateResolutions?: BatchReplyDuplicateResolution[];
}

/** 批量预览请求 */
export interface BatchPreviewRequest {
  /** 文件ID */
  fileId: number;
  /** 预览请求ID，用于轮询进度 */
  previewRequestId?: string;
  /** 各表格配置列表 */
  tables: BatchTableConfig[];
  /** 客户ID */
  customerId?: number;
  /** 制程ID */
  processId?: number;
  /** 机型ID */
  machineModelId?: number;
  /** 匹配配置 */
  config?: MatchConfig;
}

/** 单个表格的预览结果 */
export interface BatchTablePreviewResult {
  /** 表格索引 */
  tableIndex: number;
  /** 匹配结果列表 */
  items: MatchPreviewItem[];
  /** 总匹配数 */
  totalMatched: number;
  /** 高置信度 */
  highConfidenceCount: number;
  /** 中置信度 */
  mediumConfidenceCount: number;
  /** 低置信度 */
  lowConfidenceCount: number;
  /** 高歧义 */
  ambiguousCount: number;
}

/** 批量预览响应 */
export interface BatchPreviewResponse {
  /** 各表格预览结果 */
  tables: BatchTablePreviewResult[];
  /** 汇总匹配数 */
  totalMatched: number;
  /** 汇总高置信度 */
  highConfidenceCount: number;
  /** 汇总中置信度 */
  mediumConfidenceCount: number;
  /** 汇总低置信度 */
  lowConfidenceCount: number;
  /** 汇总高歧义 */
  ambiguousCount: number;
}

export interface BatchPreviewProgressResponse {
  requestId: string;
  status: "running" | "completed" | "failed";
  stage: string;
  stageText: string;
  detailText?: string;
  completedItems: number;
  totalItems: number;
  progressPercent: number;
  startedAt: string;
  updatedAt: string;
  elapsedMs: number;
}

/** 批量表格填充映射 */
export interface BatchTableFillMapping {
  /** 表格索引 */
  tableIndex: number;
  /** 验收列索引 */
  acceptanceColumnIndex: number;
  /** 备注列索引 */
  remarkColumnIndex?: number;
  /** 项目列索引（执行前重算当前最佳匹配必填） */
  projectColumnIndex: number;
  /** 规格列索引（执行前重算当前最佳匹配必填） */
  specificationColumnIndex: number;
  /** Excel 表头起始行（1-based，可选） */
  headerRowStart?: number;
  /** Excel 表头行数（可选） */
  headerRowCount?: number;
  /** Excel 数据起始行（1-based，可选） */
  dataStartRow?: number;
  /** 是否过滤项目/规格均为空的行 */
  filterEmptySourceRows?: boolean;
  /** 填充映射列表 */
  mappings: FillMapping[];
}

/** 批量执行填充请求 */
export interface BatchExecuteFillRequest {
  /** 文件ID */
  fileId: number;
  /** 客户ID */
  customerId?: number;
  /** 制程ID */
  processId?: number;
  /** 机型ID */
  machineModelId?: number;
  /** 匹配配置 */
  config?: MatchConfig;
  /** 各表格的填充映射 */
  tables: BatchTableFillMapping[];
}

export interface MatchLlmStreamItem {
  tableIndex: number;
  rowIndex: number;
  sourceProject: string;
  sourceSpecification: string;
  bestMatchSpecId?: number;
  bestMatchScore?: number;
  scoreDetails?: Record<string, number>;
  decision?: MatchResult["decision"];
  llmEquivalenceVerdict?: LlmEquivalenceVerdict;
  isAmbiguous: boolean;
  evidenceSummary: string[];
  conflictSummary: string[];
}

export interface MatchLlmStreamRequest {
  customerId?: number;
  processId?: number;
  machineModelId?: number;
  items: MatchLlmStreamItem[];
  config?: MatchConfig;
}

export type MatchLlmStreamEvent =
  | "review.start"
  | "review.delta"
  | "review.done"
  | "review.error";

export interface MatchLlmStreamBaseEventData {
  tableIndex?: number;
  rowIndex: number;
}

export interface MatchLlmStreamStartEventData extends MatchLlmStreamBaseEventData {}

export interface MatchLlmStreamDeltaEventData extends MatchLlmStreamBaseEventData {
  chunk?: string;
}

export interface MatchLlmStreamDoneEventData extends MatchLlmStreamBaseEventData {
  decision?: MatchResult["decision"];
  score?: number;
  reason?: string;
  commentary?: string;
  reviewApprovalToken?: string;
}

export interface MatchLlmStreamErrorEventData extends MatchLlmStreamBaseEventData {
  decision?: MatchResult["decision"];
  message?: string;
}

export type MatchLlmStreamEventData =
  | MatchLlmStreamStartEventData
  | MatchLlmStreamDeltaEventData
  | MatchLlmStreamDoneEventData
  | MatchLlmStreamErrorEventData;

/** 批量匹配预览（长超时：5分钟） */
export const batchPreviewMatch = (
  data: BatchPreviewRequest,
  config?: PureHttpRequestConfig
) => {
  return http.request<ApiResponse<BatchPreviewResponse>>(
    "post",
    `${baseUrl}/batch-preview`,
    { data, timeout: 300000 },
    config
  );
};

/** 批量执行填充（长超时：5分钟） */
export const batchExecuteFill = (data: BatchExecuteFillRequest) => {
  return http.request<ApiResponse<ExecuteFillResponse>>(
    "post",
    `${baseUrl}/batch-execute`,
    { data, timeout: 300000 }
  );
};

export const getBatchPreviewProgress = (requestId: string) => {
  return http.request<ApiResponse<BatchPreviewProgressResponse>>(
    "get",
    `${baseUrl}/batch-preview-progress/${requestId}`
  );
};

export const createMatchLlmStreamRequest = (
  data: MatchLlmStreamRequest
): MatchLlmStreamRequest => ({
  ...data,
  items: data.items.map(item => ({
    ...item,
    evidenceSummary: [...item.evidenceSummary],
    conflictSummary: [...item.conflictSummary]
  }))
});

export const requestMatchLlmStream = async (
  data: MatchLlmStreamRequest,
  signal?: AbortSignal
) => {
  const url = `${baseUrl}/llm-stream`;
  const init = await createAuthorizedFetchInit(url, {
    method: "POST",
    headers: {
      "Content-Type": "application/json"
    },
    body: JSON.stringify(data),
    signal
  });
  const response = await fetch(url, init);
  await ensureFetchResponseAuthHandled(response, url);
  return response;
};

export interface BatchReplySourceUploadResponse {
  sessionId: string;
  sourceFileName: string;
  sourceFileType: number;
  tableCount: number;
}

export interface BatchReplyUploadedTargetFile {
  targetId: string;
  fileName: string;
  fileType: number;
  tableCount: number;
}

export interface BatchReplyTargetUploadResponse {
  sessionId: string;
  files: BatchReplyUploadedTargetFile[];
}

export interface BatchReplyPreviewFileResult {
  targetId: string;
  fileName: string;
  canApply: boolean;
  errors: string[];
}

export interface BatchReplyPreviewResponse {
  sessionId: string;
  sourceFileName: string;
  sourceFileType: number;
  isStrictMode: boolean;
  usesAi: boolean;
  readyCount: number;
  totalCount: number;
  files: BatchReplyPreviewFileResult[];
}

export interface BatchReplyExecuteRequest {
  sessionId: string;
  sourceTables?: BatchTableConfig[];
  targets?: BatchReplyExecuteTargetRequest[];
}

export interface BatchReplyExecuteTargetRequest {
  targetId: string;
  tables: BatchTableConfig[];
}

export interface BatchReplyExecuteFileResult {
  targetId: string;
  fileName: string;
  success: boolean;
  message: string;
}

export interface BatchReplyExecuteResponse {
  taskId: string;
  successCount: number;
  failedCount: number;
  downloadUrl: string;
  downloadFileName: string;
  files: BatchReplyExecuteFileResult[];
}

export interface BatchReplyTablePreviewRow {
  rowIndex: number;
  project: string;
  specification: string;
  acceptance: string;
  remark?: string;
}

export type BatchReplyDuplicateStrategy = "keepFirst" | "keepLast" | "skip";

export interface BatchReplyDuplicateResolution {
  groupId: string;
  strategy: BatchReplyDuplicateStrategy;
}

export interface BatchReplyDuplicateRow {
  rowIndex: number;
  project: string;
  specification: string;
  acceptance?: string;
  remark?: string;
}

export interface BatchReplyDuplicateGroup {
  groupId: string;
  duplicateSource: "source" | "target";
  tableIndex: number;
  project: string;
  specification: string;
  rows: BatchReplyDuplicateRow[];
}

export interface BatchReplyTablePreviewRequest {
  sessionId: string;
  sourceTables: BatchTableConfig[];
  targetId: string;
  targetTable: BatchTableConfig;
}

export interface BatchReplyTablePreviewResponse {
  targetId: string;
  fileName: string;
  tableIndex: number;
  sourceTableIndex: number;
  canApply: boolean;
  errors: string[];
  duplicateGroups: BatchReplyDuplicateGroup[];
  rows: BatchReplyTablePreviewRow[];
}

const batchReplyBaseUrl = "/api/batch-reply";

export const uploadBatchReplySource = (file: File) => {
  const formData = new FormData();
  formData.append("file", file);
  return http.request<ApiResponse<BatchReplySourceUploadResponse>>(
    "post",
    `${batchReplyBaseUrl}/source/upload`,
    {
      data: formData,
      headers: {
        "Content-Type": "multipart/form-data"
      }
    }
  );
};

export const getBatchReplyTables = (sessionId: string) => {
  return http.request<ApiResponse<TableInfo[]>>(
    "get",
    `${batchReplyBaseUrl}/sessions/${sessionId}/tables`
  );
};

export const getBatchReplyTablePreview = (
  sessionId: string,
  tableIndex: number,
  options?: {
    previewRows?: number;
    headerRowIndex?: number;
    headerRowCount?: number;
    dataStartRowIndex?: number;
  }
) => {
  return http.request<ApiResponse<TableData>>(
    "get",
    `${batchReplyBaseUrl}/sessions/${sessionId}/tables/${tableIndex}/preview`,
    {
      params: options
    }
  );
};

export const uploadBatchReplyTargets = (sessionId: string, files: File[]) => {
  const formData = new FormData();
  formData.append("sessionId", sessionId);
  files.forEach(file => formData.append("targetFiles", file));

  return http.request<ApiResponse<BatchReplyTargetUploadResponse>>(
    "post",
    `${batchReplyBaseUrl}/targets/upload`,
    {
      data: formData,
      headers: {
        "Content-Type": "multipart/form-data"
      }
    }
  );
};

export const getBatchReplyTargetTables = (sessionId: string, targetId: string) => {
  return http.request<ApiResponse<TableInfo[]>>(
    "get",
    `${batchReplyBaseUrl}/sessions/${sessionId}/targets/${targetId}/tables`
  );
};

export const getBatchReplyTargetTablePreview = (
  sessionId: string,
  targetId: string,
  tableIndex: number,
  options?: {
    previewRows?: number;
    headerRowIndex?: number;
    headerRowCount?: number;
    dataStartRowIndex?: number;
  }
) => {
  return http.request<ApiResponse<TableData>>(
    "get",
    `${batchReplyBaseUrl}/sessions/${sessionId}/targets/${targetId}/tables/${tableIndex}/preview`,
    {
      params: options
    }
  );
};

export const previewBatchReplyTable = (
  data: BatchReplyTablePreviewRequest,
  config?: PureHttpRequestConfig
) => {
  return http.request<ApiResponse<BatchReplyTablePreviewResponse>>(
    "post",
    `${batchReplyBaseUrl}/table-preview`,
    {
      data,
      timeout: 300000
    },
    config
  );
};

export const previewBatchReply = (
  sessionId: string,
  tableConfigs: BatchTableConfig[],
  targetFiles: File[],
  config?: PureHttpRequestConfig
) => {
  const formData = new FormData();
  formData.append("sessionId", sessionId);
  formData.append("tableConfigsJson", JSON.stringify(tableConfigs));
  targetFiles.forEach(file => formData.append("targetFiles", file));

  return http.request<ApiResponse<BatchReplyPreviewResponse>>(
    "post",
    `${batchReplyBaseUrl}/preview`,
    {
      data: formData,
      timeout: 300000,
      headers: {
        "Content-Type": "multipart/form-data"
      }
    },
    config
  );
};

export const executeBatchReply = (data: BatchReplyExecuteRequest) => {
  return http.request<ApiResponse<BatchReplyExecuteResponse>>(
    "post",
    `${batchReplyBaseUrl}/execute`,
    {
      data,
      timeout: 300000
    }
  );
};

export const downloadBatchReplyResult = (taskId: string) => {
  return http.request<Blob>("get", `${batchReplyBaseUrl}/download/${taskId}`, {
    responseType: "blob"
  });
};
