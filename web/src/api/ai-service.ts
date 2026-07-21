import { http } from "@/utils/http";
import type { ApiResponse, PagedData, PagedRequest } from "./customer";

export enum AiServiceType {
  OpenAI = 0,
  AzureOpenAI = 1,
  Ollama = 2,
  LMStudio = 3,
  CustomOpenAICompatible = 4
}

export enum AiServicePurpose {
  None = 0,
  Llm = 1,
  Embedding = 2
}

export interface AiServiceConfig {
  id: number;
  name: string;
  serviceType: AiServiceType;
  purpose: AiServicePurpose;
  priority: number;
  endpoint?: string | null;
  embeddingModel?: string | null;
  llmModel?: string | null;
  disableThinking: boolean;
  isDisabled: boolean;
  defaultRecallTopK: number;
  hasApiKey: boolean;
  createdAt: string;
  updatedAt?: string | null;
}

export interface AiServiceConfigDetail extends AiServiceConfig {
  apiKey?: string | null;
}

export interface CreateAiServiceRequest {
  name: string;
  serviceType: AiServiceType;
  purpose: AiServicePurpose;
  priority: number;
  apiKey?: string | null;
  endpoint?: string | null;
  embeddingModel?: string | null;
  llmModel?: string | null;
  disableThinking?: boolean;
  defaultRecallTopK?: number;
}

export type UpdateAiServiceRequest = CreateAiServiceRequest;

export type AiServiceConnectionTestMode = "quick" | "full";

export interface AiServiceTestResult {
  success: boolean;
  message: string;
  httpStatusCode?: number | null;
  elapsedMs: number;
  serviceElapsedMs?: number | null;
  targetModel?: string | null;
  targetEndpoint?: string | null;
  hostPort?: string | null;
}

export interface AiServiceModelsResult {
  llmModels: string[];
  embeddingModels: string[];
  message?: string | null;
}

export type AiServiceSelectionStatus = "available" | "checking" | "unavailable";

export interface AiServiceSelection {
  status: AiServiceSelectionStatus;
  serviceId?: number | null;
  name?: string | null;
  model?: string | null;
  checkedAt?: string | null;
  message?: string | null;
}

export interface AiServiceListRequest extends PagedRequest {
  serviceType?: AiServiceType;
}

export const sortAiServicesByPriority = (services: AiServiceConfig[]) =>
  [...services].sort((a, b) => {
    const priorityDiff = a.priority - b.priority;
    if (priorityDiff !== 0) {
      return priorityDiff;
    }

    const aTime = Date.parse(a.updatedAt || a.createdAt || "");
    const bTime = Date.parse(b.updatedAt || b.createdAt || "");
    return (
      (Number.isNaN(bTime) ? 0 : bTime) - (Number.isNaN(aTime) ? 0 : aTime)
    );
  });

const baseUrl = "/api/ai-services";

export const getAiServiceSelection = (
  purpose: "llm" | "embedding",
  signal?: AbortSignal
) =>
  http.request<ApiResponse<AiServiceSelection>>("get", `${baseUrl}/selection`, {
    params: { purpose },
    signal
  });

export const getAiServiceList = (params?: AiServiceListRequest) => {
  return http.request<ApiResponse<PagedData<AiServiceConfig>>>("get", baseUrl, {
    params
  });
};

export const getAiServiceById = (id: number) => {
  return http.request<ApiResponse<AiServiceConfigDetail>>(
    "get",
    `${baseUrl}/${id}`
  );
};

export const createAiService = (data: CreateAiServiceRequest) => {
  return http.request<ApiResponse<AiServiceConfig>>("post", baseUrl, { data });
};

export const updateAiService = (id: number, data: UpdateAiServiceRequest) => {
  return http.request<ApiResponse<AiServiceConfig>>("put", `${baseUrl}/${id}`, {
    data
  });
};

export const setAiServiceDisabled = (id: number, isDisabled: boolean) => {
  return http.request<ApiResponse<AiServiceConfig>>(
    "put",
    `${baseUrl}/${id}/disabled`,
    {
      data: { isDisabled }
    }
  );
};

export const deleteAiService = (id: number) => {
  return http.request<ApiResponse<void>>("delete", `${baseUrl}/${id}`);
};

export const testAiServiceConnection = (
  id: number,
  mode: AiServiceConnectionTestMode = "full"
) => {
  return http.request<ApiResponse<AiServiceTestResult>>(
    "post",
    `${baseUrl}/${id}/test`,
    {
      params: { mode }
    },
    { timeout: 300000 }
  );
};

export const getAiServiceModels = (id: number) => {
  return http.request<ApiResponse<AiServiceModelsResult>>(
    "get",
    `${baseUrl}/${id}/models`,
    undefined,
    { timeout: 30000 }
  );
};
