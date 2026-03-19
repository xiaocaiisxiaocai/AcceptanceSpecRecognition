import { http } from "@/utils/http";
import type { ApiResponse } from "./customer";

/** 匹配策略 */
export enum MatchingStrategy {
  /** 单阶段匹配（原有基础方式） */
  SingleStage = 1,
  /** 多阶段匹配（TopK 召回 + 规则重排） */
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
  /** 多阶段模式下第一阶段召回数量 */
  recallTopK?: number;
  /** 多阶段模式下的歧义分差阈值 */
  ambiguityMargin?: number;
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
  matchingStrategy: MatchingStrategy.MultiStage,
  minScoreThreshold: 0.65,
  recallTopK: 8,
  ambiguityMargin: 0.03,
  useLlmReview: false,
  useLlmSuggestion: true,
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
  /** 填充映射列表 */
  mappings: FillMapping[];
}

/** 批量执行填充请求 */
export interface BatchExecuteFillRequest {
  /** 文件ID */
  fileId: number;
  /** 各表格的填充映射 */
  tables: BatchTableFillMapping[];
}

/** 批量匹配预览（长超时：5分钟） */
export const batchPreviewMatch = (data: BatchPreviewRequest) => {
  return http.request<ApiResponse<BatchPreviewResponse>>(
    "post",
    `${baseUrl}/batch-preview`,
    { data, timeout: 300000 }
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
