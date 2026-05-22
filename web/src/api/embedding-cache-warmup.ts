import { http } from "@/utils/http";
import type { ApiResponse } from "./customer";

export interface EmbeddingCacheWarmupOptions {
  enabled: boolean;
  runOnStartup: boolean;
  runAtLocalTime?: string | null;
  intervalHours: number;
  batchSize: number;
  maxItemsPerRun: number;
}

export interface EmbeddingCacheWarmupStatus {
  isRunning: boolean;
  lastStartedAt?: string | null;
  lastFinishedAt?: string | null;
  lastSucceeded?: boolean | null;
  lastError?: string | null;
  lastBatchSize?: number | null;
  lastMaxItemsPerRun?: number | null;
}

export interface EmbeddingCacheWarmupLastResult {
  startedAt: string;
  finishedAt: string;
  succeeded: boolean;
  error?: string | null;
  batchSize: number;
  maxItemsPerRun: number;
}

export interface EmbeddingCacheWarmupInfo {
  options: EmbeddingCacheWarmupOptions;
  status: EmbeddingCacheWarmupStatus;
  lastResult?: EmbeddingCacheWarmupLastResult | null;
}

const baseUrl = "/api/embedding-cache-warmup";

export const getEmbeddingCacheWarmupInfo = () => {
  return http.request<ApiResponse<EmbeddingCacheWarmupInfo>>("get", baseUrl);
};

export const updateEmbeddingCacheWarmupOptions = (
  data: EmbeddingCacheWarmupOptions
) => {
  return http.request<ApiResponse<EmbeddingCacheWarmupInfo>>(
    "put",
    `${baseUrl}/options`,
    { data }
  );
};

export const runEmbeddingCacheWarmup = () => {
  return http.request<ApiResponse<EmbeddingCacheWarmupInfo>>(
    "post",
    `${baseUrl}/run`,
    undefined,
    { timeout: 300000 }
  );
};
