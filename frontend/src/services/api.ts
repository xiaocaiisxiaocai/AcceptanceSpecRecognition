import axios from 'axios';
import type {
  MatchQuery,
  MatchResult,
  BatchRequest,
  BatchResult,
  BatchProgress,
  HistoryRecord,
  SystemConfig,
  KeywordLibrary,
  AuditQueryResult,
  AuditStats,
  TestConnectionResult,
  PromptConfig,
  StreamingMatchCallbacks,
  PreprocessResult,
  StreamingStatusEvent,
  StreamingAnalysisEvent,
} from '../types';

const API_BASE_URL = import.meta.env.VITE_API_URL || 'http://localhost:5000/api';

const api = axios.create({
  baseURL: API_BASE_URL,
  headers: {
    'Content-Type': 'application/json',
  },
});

// 匹配相关API
export const matchApi = {
  // 单条匹配
  match: async (query: MatchQuery): Promise<MatchResult> => {
    const response = await api.post<MatchResult>('/match', query);
    return response.data;
  },

  // 批量匹配
  matchBatch: async (request: BatchRequest): Promise<BatchResult> => {
    const response = await api.post<BatchResult>('/match/batch', request);
    return response.data;
  },

  // 获取批量处理进度
  getBatchProgress: async (taskId: string): Promise<BatchProgress> => {
    const response = await api.get<BatchProgress>(`/match/batch/${taskId}/progress`);
    return response.data;
  },

  // 取消批量处理
  cancelBatch: async (taskId: string): Promise<void> => {
    await api.post(`/match/batch/${taskId}/cancel`);
  },

  // 确认匹配结果
  confirmMatch: async (recordId: string, accepted: boolean, feedback?: string): Promise<void> => {
    await api.post('/match/confirm', { recordId, accepted, feedback });
  },

  /**
   * 流式匹配（SSE）- 实时返回 LLM 思考过程
   * @param query 匹配查询
   * @param callbacks 回调函数集合
   * @returns 包含 abort 方法的控制器对象
   */
  matchStream: (
    query: MatchQuery,
    callbacks: StreamingMatchCallbacks
  ): { abort: () => void } => {
    const controller = new AbortController();

    // 启动异步流式请求
    (async () => {
      try {
        const response = await fetch(`${API_BASE_URL}/match/stream`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(query),
          signal: controller.signal,
        });

        if (!response.ok) {
          const errorText = await response.text();
          callbacks.onError?.(`请求失败: ${response.status} ${errorText}`);
          return;
        }

        const reader = response.body?.getReader();
        if (!reader) {
          callbacks.onError?.('无法获取响应流');
          return;
        }

        const decoder = new TextDecoder();
        let buffer = '';

        while (true) {
          const { done, value } = await reader.read();
          if (done) break;

          buffer += decoder.decode(value, { stream: true });

          // 按双换行符分割 SSE 事件
          const events = buffer.split('\n\n');
          buffer = events.pop() || ''; // 保留最后一个可能不完整的事件

          for (const eventStr of events) {
            if (!eventStr.trim()) continue;

            // 解析 SSE 事件
            const eventMatch = eventStr.match(/^event:\s*(.+)$/m);
            const dataMatch = eventStr.match(/^data:\s*(.+)$/m);

            if (eventMatch && dataMatch) {
              const eventType = eventMatch[1].trim();
              let data: unknown;

              try {
                data = JSON.parse(dataMatch[1]);
              } catch {
                console.warn('解析 SSE 数据失败:', dataMatch[1]);
                continue;
              }

              // 根据事件类型调用相应回调
              switch (eventType) {
                case 'status':
                  callbacks.onStatus?.(data as StreamingStatusEvent);
                  break;
                case 'preprocess':
                  callbacks.onPreprocess?.(data as PreprocessResult);
                  break;
                case 'thinking':
                  callbacks.onThinking?.(data as StreamingAnalysisEvent);
                  break;
                case 'result':
                  callbacks.onResult?.(data as MatchResult);
                  break;
                case 'error':
                  callbacks.onError?.((data as { error: string }).error);
                  break;
                case 'done':
                  callbacks.onDone?.(data as { durationMs: number });
                  break;
                default:
                  console.log('未知 SSE 事件:', eventType, data);
              }
            }
          }
        }
      } catch (err) {
        if ((err as Error).name === 'AbortError') {
          console.log('流式请求已取消');
        } else {
          callbacks.onError?.((err as Error).message);
        }
      }
    })();

    return {
      abort: () => controller.abort(),
    };
  },
};

// 历史记录相关API
export const historyApi = {
  // 获取所有历史记录
  getAll: async (search?: string): Promise<HistoryRecord[]> => {
    const params = search ? { search } : {};
    const response = await api.get<HistoryRecord[]>('/history', { params });
    return response.data;
  },

  // 获取单条历史记录
  getById: async (id: string): Promise<HistoryRecord> => {
    const response = await api.get<HistoryRecord>(`/history/${id}`);
    return response.data;
  },

  // 创建历史记录
  create: async (record: Partial<HistoryRecord>): Promise<HistoryRecord> => {
    const response = await api.post<HistoryRecord>('/history', record);
    return response.data;
  },

  // 更新历史记录
  update: async (id: string, record: Partial<HistoryRecord>): Promise<HistoryRecord> => {
    const response = await api.put<HistoryRecord>(`/history/${id}`, record);
    return response.data;
  },

  // 删除历史记录
  delete: async (id: string): Promise<void> => {
    await api.delete(`/history/${id}`);
  },

  // 批量删除历史记录
  deleteBatch: async (ids: string[]): Promise<void> => {
    await api.post('/history/batch-delete', { ids });
  },
};

// 配置相关API
export const configApi = {
  // 获取系统配置
  getConfig: async (): Promise<SystemConfig> => {
    const response = await api.get<SystemConfig>('/config');
    return response.data;
  },

  // 更新配置
  updateConfig: async (config: Partial<SystemConfig>): Promise<void> => {
    await api.put('/config', config);
  },

  // 测试Embedding连接
  testEmbedding: async (params: { baseUrl: string; apiKey: string; model: string }): Promise<TestConnectionResult> => {
    const response = await api.post<TestConnectionResult>('/config/test/embedding', params);
    return response.data;
  },

  // 测试LLM连接
  testLLM: async (params: { baseUrl: string; apiKey: string; model: string; temperature?: number }): Promise<TestConnectionResult> => {
    const response = await api.post<TestConnectionResult>('/config/test/llm', params);
    return response.data;
  },

  // 获取关键字库
  getKeywords: async (): Promise<KeywordLibrary> => {
    const response = await api.get<KeywordLibrary>('/config/keywords');
    return response.data;
  },

  // 更新关键字库
  updateKeywords: async (keywords: KeywordLibrary): Promise<void> => {
    await api.put('/config/keywords', keywords);
  },

  // 获取系统提示词
  getPrompts: async (): Promise<PromptConfig> => {
    const response = await api.get<PromptConfig>('/config/prompts');
    return response.data;
  },

  // 更新系统提示词
  updatePrompts: async (prompts: Partial<PromptConfig>): Promise<void> => {
    await api.put('/config/prompts', prompts);
  },

  // 重置系统提示词为默认值
  resetPrompts: async (): Promise<void> => {
    await api.post('/config/prompts/reset');
  },

  // 清除所有缓存
  clearCache: async (): Promise<{ message: string }> => {
    const response = await api.delete<{ message: string }>('/config/cache');
    return response.data;
  },

  // 获取缓存统计信息
  getCacheStats: async (): Promise<{
    vectorCacheHits: number;
    vectorCacheMisses: number;
    llmCacheHits: number;
    llmCacheMisses: number;
    resultCacheHits: number;
    resultCacheMisses: number;
  }> => {
    const response = await api.get('/config/cache/stats');
    return response.data;
  },
};

// 审计日志相关API
export const auditApi = {
  // 查询审计日志
  query: async (params: {
    startTime?: string;
    endTime?: string;
    actionType?: string;
    page?: number;
    pageSize?: number;
  }): Promise<AuditQueryResult> => {
    const response = await api.get<AuditQueryResult>('/audit', { params });
    return response.data;
  },

  // 获取统计信息
  getStats: async (startTime?: string, endTime?: string): Promise<AuditStats> => {
    const response = await api.get<AuditStats>('/audit/stats', {
      params: { startTime, endTime },
    });
    return response.data;
  },

  // 清除所有审计日志
  clear: async (): Promise<void> => {
    await api.delete('/audit');
  },
};

export default api;
