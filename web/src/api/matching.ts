import { http } from "@/utils/http";
import type { ApiResponse } from "./customer";

/** 匹配配置 */
export interface MatchConfig {
  /** 选定的 Embedding 服务ID（为空则自动选择） */
  embeddingServiceId?: number;
  /** 选定的 LLM 服务ID（为空则自动选择） */
  llmServiceId?: number;
  /** 最小匹配阈值 */
  minScoreThreshold?: number;
  /** 是否启用LLM复核 */
  useLlmReview?: boolean;
  /** 是否启用LLM生成建议 */
  useLlmSuggestion?: boolean;
  /** 生成建议触发阈值 */
  llmSuggestionScoreThreshold?: number;
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
  /** 综合得分（0-1） */
  score: number;
  /** 各算法得分详情 */
  scoreDetails: Record<string, number>;
  /** LLM复核得分（0-100） */
  llmScore?: number;
  /** LLM复核原因 */
  llmReason?: string;
  /** LLM评论 */
  llmCommentary?: string;
  /** 是否经过LLM复核 */
  isLlmReviewed?: boolean;
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

/** 匹配预览 */
export const previewMatch = (data: MatchPreviewRequest) => {
  return http.request<ApiResponse<MatchPreviewResponse>>(
    "post",
    `${baseUrl}/preview`,
    {
      data
    }
  );
};

/** 执行填充 */
export const executeFill = (data: ExecuteFillRequest) => {
  return http.request<ApiResponse<ExecuteFillResponse>>(
    "post",
    `${baseUrl}/execute`,
    {
      data
    }
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
  minScoreThreshold: 0.3,
  useLlmReview: false,
  useLlmSuggestion: false,
  llmSuggestionScoreThreshold: 0.6
};
