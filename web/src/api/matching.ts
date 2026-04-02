import { http } from "@/utils/http";
import type { PureHttpRequestConfig } from "@/utils/http/types.d";
import type { ApiResponse } from "./customer";
import type { TableData, TableInfo } from "./document";

export const DEFAULT_MIN_SCORE_THRESHOLD = 0.9;
export const DEFAULT_HIGH_CONFIDENCE_THRESHOLD = 0.98;
export const DEFAULT_RECALL_TOP_K = 2;
export const MAX_RECALL_TOP_K = 3;
export const DEFAULT_AMBIGUITY_MARGIN = 0.02;
export const DEFAULT_LLM_ENTITY_RESOLUTION_TOP_CANDIDATES = 2;
export const MAX_LLM_ENTITY_RESOLUTION_TOP_CANDIDATES = 3;
export const LLM_REVIEW_PASS_THRESHOLD = 90;

/** 匹配策略 */
export enum MatchingStrategy {
  /** 单阶段匹配（仅按 Embedding 排序） */
  SingleStage = 1,
  /** 多阶段匹配（Embedding 召回 + 证据重排） */
  MultiStage = 2
}

/** 匹配配置 */
export interface MatchConfig {
  /** 匹配策略 */
  matchingStrategy?: MatchingStrategy;
  /** 选定的 Embedding 服务ID（为空则自动选择） */
  embeddingServiceId?: number;
  /** 选定的 LLM 服务ID（为空则自动选择） */
  llmServiceId?: number;
  /** 最小匹配阈值 */
  minScoreThreshold?: number;
  /** 高置信自动采用阈值 */
  highConfidenceThreshold?: number;
  /** 多阶段模式下第一阶段召回数量 */
  recallTopK?: number;
  /** 多阶段模式下的歧义分差阈值 */
  ambiguityMargin?: number;
  /** 是否启用 LLM 实体判别 */
  useLlmEntityResolution?: boolean;
  /** 启用实体判别时参与复判的候选数量 */
  llmEntityResolutionTopCandidates?: number;
  /** 判定为同一实体的最低置信度 */
  llmEntityPositiveConfidenceThreshold?: number;
  /** 判定为实体冲突并降级人工复核的最低置信度 */
  llmEntityConflictReviewConfidenceThreshold?: number;
  /** 判定为实体冲突并直接拒绝的最低置信度 */
  llmEntityConflictRejectConfidenceThreshold?: number;
  /** 是否启用LLM复核 */
  useLlmReview?: boolean;
  /** 是否启用LLM生成建议 */
  useLlmSuggestion?: boolean;
  /** 是否对完全无匹配的行也生成建议 */
  suggestNoMatchRows?: boolean;
  /** 生成建议触发阈值 */
  llmSuggestionScoreThreshold?: number;
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

/** 匹配预览请求 */
export interface MatchPreviewRequest {
  /** 文件ID（文件模式） */
  fileId?: number;
  /** 表格索引（文件模式） */
  tableIndex?: number;
  /** 项目列索引（必须由用户指定，0-based） */
  projectColumnIndex?: number;
  /** 规格列索引（必须由用户指定，0-based） */
  specificationColumnIndex?: number;
  /** Excel 表头起始行（1-based，可选） */
  headerRowStart?: number;
  /** Excel 表头行数（可选） */
  headerRowCount?: number;
  /** Excel 数据起始行（1-based，可选） */
  dataStartRow?: number;
  /** 待匹配的文本列表（直接模式） */
  items?: MatchSourceItem[];
  /** 目标客户ID（限定匹配范围） */
  customerId?: number;
  /** 目标制程ID（限定匹配范围） */
  processId?: number;
  /** 目标机型ID（限定匹配范围） */
  machineModelId?: number;
  /** 匹配配置 */
  config?: MatchConfig;
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
  /** 是否存在硬冲突 */
  hasHardConflict?: boolean;
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
  /** 匹配策略 */
  matchingStrategy: MatchingStrategy;
  /** 第一阶段召回候选数 */
  recalledCandidateCount: number;
  /** 是否为高歧义样本 */
  isAmbiguous: boolean;
  /** Top1 与 Top2 的最终分差 */
  scoreGap?: number;
  /** 重排摘要 */
  rerankSummary?: string;
  /** LLM复核得分（0-100） */
  llmScore?: number;
  /** LLM复核原因 */
  llmReason?: string;
  /** LLM评论 */
  llmCommentary?: string;
  /** 是否经过LLM复核 */
  isLlmReviewed?: boolean;
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
  /** 是否存在硬冲突 */
  hasHardConflict?: boolean;
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
}

/** LLM生成建议 */
export interface LlmSuggestion {
  /** 验收标准建议 */
  acceptance?: string;
  /** 备注建议 */
  remark?: string;
  /** 生成理由 */
  reason?: string;
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
  /** LLM生成建议 */
  llmSuggestion?: LlmSuggestion;
  /** LLM生成建议流式内容 */
  llmSuggestionDraft?: string;
  /** LLM复核流式内容 */
  llmReviewDraft?: string;
  /** LLM复核错误 */
  llmReviewError?: string;
  /** LLM生成错误 */
  llmSuggestionError?: string;
  /** 不匹配原因 */
  noMatchReason?: string;
  /** 是否有匹配 */
  hasMatch: boolean;
  /** 置信度级别 */
  confidenceLevel: "high" | "medium" | "low" | "none";
}

/** 匹配预览响应 */
export interface MatchPreviewResponse {
  /** 匹配结果列表 */
  items: MatchPreviewItem[];
  /** 总匹配数 */
  totalMatched: number;
  /** 高置信度匹配数 */
  highConfidenceCount: number;
  /** 中置信度匹配数 */
  mediumConfidenceCount: number;
  /** 低置信度匹配数 */
  lowConfidenceCount: number;
  /** 高歧义样本数 */
  ambiguousCount: number;
}

/** 填充映射 */
export interface FillMapping {
  /** 行索引 */
  rowIndex: number;
  /** 选择的验收规格ID */
  specId?: number;
  /** 匹配得分（0-1） */
  matchScore?: number;
  /** LLM 复核得分（0-100） */
  llmReviewScore?: number;
  /** 是否已由用户人工确认 */
  manualConfirmed?: boolean;
  /** 是否使用LLM生成建议 */
  useLlmSuggestion?: boolean;
  /** LLM生成的验收标准 */
  acceptance?: string;
  /** LLM生成的备注 */
  remark?: string;
}

/** 执行填充请求 */
export interface ExecuteFillRequest {
  /** 文件ID */
  fileId: number;
  /** 表格索引 */
  tableIndex: number;
  /** 验收列索引（必须由用户指定，0-based） */
  acceptanceColumnIndex: number;
  /** 备注列索引（可选，0-based） */
  remarkColumnIndex?: number;
  /** 项目列索引（用于严格复用快照） */
  projectColumnIndex?: number;
  /** 规格列索引（用于严格复用快照） */
  specificationColumnIndex?: number;
  /** Excel 表头起始行（1-based，可选） */
  headerRowStart?: number;
  /** Excel 表头行数（可选） */
  headerRowCount?: number;
  /** Excel 数据起始行（1-based，可选） */
  dataStartRow?: number;
  /** 是否过滤项目/规格均为空的行 */
  filterEmptySourceRows?: boolean;
  /** 高置信自动采用阈值 */
  highConfidenceThreshold?: number;
  /** 填充映射列表 */
  mappings: FillMapping[];
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

/** 相似度计算请求 */
export interface SimilarityRequest {
  /** 文本1 */
  text1: string;
  /** 文本2 */
  text2: string;
  /** 匹配配置 */
  config?: MatchConfig;
}

/** 相似度计算响应 */
export interface SimilarityResponse {
  /** 综合得分 */
  totalScore: number;
  /** 各算法得分详情 */
  scores: Record<string, number>;
}

const baseUrl = "/api/matching";

/** 匹配预览（长超时：5分钟） */
export const previewMatch = (data: MatchPreviewRequest) => {
  return http.request<ApiResponse<MatchPreviewResponse>>(
    "post",
    `${baseUrl}/preview`,
    { data, timeout: 300000 }
  );
};

/** 执行填充（长超时：5分钟） */
export const executeFill = (data: ExecuteFillRequest) => {
  return http.request<ApiResponse<ExecuteFillResponse>>(
    "post",
    `${baseUrl}/execute`,
    { data, timeout: 300000 }
  );
};

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

/** 计算两个文本的相似度 */
export const computeSimilarity = (data: SimilarityRequest) => {
  return http.request<ApiResponse<SimilarityResponse>>(
    "post",
    `${baseUrl}/similarity`,
    {
      data
    }
  );
};

/** 默认匹配配置 */
export const defaultMatchConfig: MatchConfig = {
  matchingStrategy: MatchingStrategy.SingleStage,
  minScoreThreshold: DEFAULT_MIN_SCORE_THRESHOLD,
  highConfidenceThreshold: DEFAULT_HIGH_CONFIDENCE_THRESHOLD,
  recallTopK: DEFAULT_RECALL_TOP_K,
  ambiguityMargin: DEFAULT_AMBIGUITY_MARGIN,
  useLlmEntityResolution: false,
  llmEntityResolutionTopCandidates: DEFAULT_LLM_ENTITY_RESOLUTION_TOP_CANDIDATES,
  llmEntityPositiveConfidenceThreshold: 0.85,
  llmEntityConflictReviewConfidenceThreshold: 0.7,
  llmEntityConflictRejectConfidenceThreshold: 0.9,
  useLlmReview: true,
  useLlmSuggestion: false,
  suggestNoMatchRows: false,
  llmSuggestionScoreThreshold: 0.75,
  llmParallelism: 3,
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
}

/** 批量预览请求 */
export interface BatchPreviewRequest {
  /** 文件ID */
  fileId: number;
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

/** 批量表格填充映射 */
export interface BatchTableFillMapping {
  /** 表格索引 */
  tableIndex: number;
  /** 验收列索引 */
  acceptanceColumnIndex: number;
  /** 备注列索引 */
  remarkColumnIndex?: number;
  /** 项目列索引 */
  projectColumnIndex?: number;
  /** 规格列索引 */
  specificationColumnIndex?: number;
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
  /** 高置信自动采用阈值 */
  highConfidenceThreshold?: number;
  /** 各表格的填充映射 */
  tables: BatchTableFillMapping[];
}

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

export interface StrictReusePreviewRequest {
  /** 来源填充任务ID */
  sourceTaskId: string;
  /** 目标文件ID列表 */
  targetFileIds: number[];
}

export interface StrictReusePreviewFileResult {
  /** 文件ID */
  fileId: number;
  /** 文件名 */
  fileName: string;
  /** 是否可应用 */
  canApply: boolean;
  /** 失败原因 */
  errors: string[];
}

export interface StrictReusePreviewResponse {
  /** 来源填充任务ID */
  sourceTaskId: string;
  /** 来源文件名 */
  sourceFileName: string;
  /** 来源文件类型 */
  sourceFileType: number;
  /** 是否严格模式 */
  isStrictMode: boolean;
  /** 是否使用 AI */
  usesAi: boolean;
  /** 可直接应用数量 */
  readyCount: number;
  /** 总文件数 */
  totalCount: number;
  /** 逐文件预检结果 */
  files: StrictReusePreviewFileResult[];
}

export interface StrictReuseExecuteRequest {
  /** 来源填充任务ID */
  sourceTaskId: string;
  /** 目标文件ID列表 */
  targetFileIds: number[];
}

export interface StrictReuseExecuteFileResult {
  /** 文件ID */
  fileId: number;
  /** 文件名 */
  fileName: string;
  /** 是否成功 */
  success: boolean;
  /** 结果说明 */
  message: string;
}

export interface StrictReuseExecuteResponse {
  /** 执行任务ID */
  taskId: string;
  /** 成功数量 */
  successCount: number;
  /** 失败数量 */
  failedCount: number;
  /** 下载地址 */
  downloadUrl: string;
  /** 下载文件名 */
  downloadFileName: string;
  /** 逐文件执行结果 */
  files: StrictReuseExecuteFileResult[];
}

export const strictReusePreview = (data: StrictReusePreviewRequest) => {
  return http.request<ApiResponse<StrictReusePreviewResponse>>(
    "post",
    `${baseUrl}/reuse/strict/preview`,
    { data, timeout: 300000 }
  );
};

export const strictReuseExecute = (data: StrictReuseExecuteRequest) => {
  return http.request<ApiResponse<StrictReuseExecuteResponse>>(
    "post",
    `${baseUrl}/reuse/strict/execute`,
    { data, timeout: 300000 }
  );
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
