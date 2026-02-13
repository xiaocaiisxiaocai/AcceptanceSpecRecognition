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
}

export interface UpdateAiServiceRequest extends CreateAiServiceRequest {}

export interface AiServiceTestResult {
  success: boolean;
  message: string;
  httpStatusCode?: number | null;
  elapsedMs: number;
}

export interface AiServiceModelsResult {
  llmModels: string[];
  embeddingModels: string[];
  message?: string | null;
}

export interface AiServiceListRequest extends PagedRequest {
  serviceType?: AiServiceType;
}

const baseUrl = "/api/ai-services";

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

export const deleteAiService = (id: number) => {
  return http.request<ApiResponse<void>>("delete", `${baseUrl}/${id}`);
};

export const testAiServiceConnection = (id: number) => {
  return http.request<ApiResponse<AiServiceTestResult>>(
    "post",
    `${baseUrl}/${id}/test`
  );
};

export const getAiServiceModels = (id: number) => {
  return http.request<ApiResponse<AiServiceModelsResult>>(
    "get",
    `${baseUrl}/${id}/models`
  );
};

