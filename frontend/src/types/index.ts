// 历史记录
export interface HistoryRecord {
  id: string;
  project: string;
  technicalSpec: string;
  actualSpec: string;
  remark: string;
  embedding?: number[];
  createdAt: string;
  updatedAt: string;
}

// 匹配查询
export interface MatchQuery {
  project: string;
  technicalSpec: string;
}

// 匹配候选
export interface MatchCandidate {
  record: HistoryRecord;
  similarityScore: number;
  highlightedActualSpec: string;
  highlightedRemark: string;
  explanation?: MatchExplanation;
}

// 匹配解释
export interface MatchExplanation {
  embeddingSimilarity: number;
  matchedSynonyms: string[];
  preprocessingSteps: PreprocessingStep[];
  llmAdjustedScore?: number;
  llmReasoning?: string;
}

// 预处理步骤
export interface PreprocessingStep {
  type: string;
  before: string;
  after: string;
}

// 置信度等级
export type ConfidenceLevel = 'Success' | 'Low';

// 匹配结果
export interface MatchResult {
  query: MatchQuery;
  bestMatch: MatchCandidate | null;
  similarityScore: number;
  confidence: ConfidenceLevel;
  isLowConfidence: boolean;
  llmAnalysis?: LLMAnalysisResult;
  /** 匹配模式：Embedding / LLM+Embedding */
  matchMode: string;
  /** 匹配耗时（毫秒） */
  durationMs: number;
}

// LLM分析结果
export interface LLMAnalysisResult {
  recommendedIndex: number;
  adjustedScores: number[];
  reasoning: string;
  conflicts: ConflictInfo[];
}

// 冲突信息
export interface ConflictInfo {
  type: string;
  queryValue: string;
  candidateValue: string;
  description: string;
}

// 批量处理请求
export interface BatchRequest {
  queries: MatchQuery[];
  taskName?: string;
}

// 批量处理进度
export interface BatchProgress {
  taskId: string;
  total: number;
  completed: number;
  failed: number;
  status: string;
  startedAt: string;
  completedAt?: string;
}

// 批量处理结果
export interface BatchResult {
  taskId: string;
  results: MatchResult[];
  summary: BatchSummary;
}

// 批量处理汇总
export interface BatchSummary {
  totalCount: number;
  successCount: number;
  lowConfidenceCount: number;
}

// 系统配置
export interface SystemConfig {
  embedding: EmbeddingConfig;
  llm: LLMConfig;
  matching: MatchingConfig;
  cache?: CacheConfig;
  preprocessing?: PreprocessingConfig;
}

export interface EmbeddingConfig {
  model: string;
  provider: string;
  dimension?: number;
  dimensions?: number;
  apiKey?: string;
  baseUrl?: string;
}

export interface LLMConfig {
  model: string;
  provider: string;
  timeoutSeconds?: number;
  maxTokens?: number;
  temperature?: number;
  apiKey?: string;
  baseUrl?: string;
}

export interface MatchingConfig {
  enableLLM: boolean;
  matchSuccessThreshold: number;
  embeddingWeight: number;
  llmWeight: number;
}

// 缓存配置
export interface CacheConfig {
  /** 启用向量缓存 */
  enableVectorCache: boolean;
  /** 启用LLM结果缓存 */
  enableLLMCache: boolean;
  /** 启用完整匹配结果缓存 */
  enableResultCache: boolean;
  /** 向量缓存TTL（分钟） */
  vectorCacheTtlMinutes?: number;
  /** LLM缓存TTL（分钟） */
  llmCacheTtlMinutes?: number;
  /** 结果缓存TTL（分钟） */
  resultCacheTtlMinutes?: number;
}

// 预处理配置
export interface PreprocessingConfig {
  /** 启用繁简体转换 */
  enableChineseSimplification: boolean;
}

// 关键字库
export interface KeywordLibrary {
  version: string;
  keywords: KeywordEntry[];
  updatedAt: string;
}

export interface KeywordEntry {
  id: string;
  keyword: string;
  synonyms: string[];
  category: string;
  style: HighlightStyle;
}

export interface HighlightStyle {
  color: string;
  backgroundColor: string;
  fontWeight?: string;
}

// 审计日志
export interface AuditLogEntry {
  id: string;
  actionType: string;
  timestamp: string;
  details?: string;
  recordId?: string;
}

export interface AuditQueryResult {
  entries: AuditLogEntry[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface AuditStats {
  totalQueries: number;
  totalConfirms: number;
  totalRejects: number;
  totalConfigChanges: number;
  totalHistoryCreates: number;
  totalHistoryUpdates: number;
}

// 测试连接结果
export interface TestConnectionResult {
  success: boolean;
  message: string;
  details?: Record<string, unknown>;
}

// 系统提示词配置
export interface PromptConfig {
  unifiedAnalysisPrompt: string;
  updatedAt?: string;
}

// ==================== 流式分析相关类型 ====================

// 思考步骤类型
export type ThinkingStepType = 'extract' | 'equivalence' | 'compare' | 'conflict' | 'confidence' | 'conclusion';

// 思考步骤
export interface ThinkingStep {
  step: ThinkingStepType;
  title: string;
  content: Record<string, unknown>;
  timestamp: string;
}

// 流式分析事件
export interface StreamingAnalysisEvent {
  eventType: 'thinking' | 'result' | 'error' | 'done';
  thinkingStep?: ThinkingStep;
  result?: LLMAnalysisResult;
  error?: string;
}

// 预处理结果
export interface PreprocessResult {
  preprocessedText: string;
  candidates: MatchCandidate[];
  bestMatch: MatchCandidate | null;
  bestScore: number;
}

// 流式状态事件
export interface StreamingStatusEvent {
  stage: 'preprocessing' | 'thinking' | 'done';
  message: string;
}

// 流式匹配状态
export interface StreamingMatchState {
  status: 'idle' | 'preprocessing' | 'thinking' | 'done' | 'error';
  statusMessage?: string;
  thinkingSteps: ThinkingStep[];
  currentStep?: ThinkingStep;
  preprocessResult?: PreprocessResult;
  result?: MatchResult;
  error?: string;
  durationMs?: number;
}

// 流式匹配回调
export interface StreamingMatchCallbacks {
  onStatus?: (data: StreamingStatusEvent) => void;
  onPreprocess?: (data: PreprocessResult) => void;
  onThinking?: (event: StreamingAnalysisEvent) => void;
  onResult?: (result: MatchResult) => void;
  onError?: (error: string) => void;
  onDone?: (data: { durationMs: number }) => void;
}
