import { http } from "@/utils/http";
import type { ApiResponse, PagedData, PagedRequest } from "./customer";

export enum AiServiceType {
  OpenAI = 0,
  AzureOpenAI = 1,
  Ollama = 2,
  LMStudio = 3,
  CustomOpenAICompatible = 4
}

export interface AiServiceConfig {
  id: number;
  name: string;
  serviceType: AiServiceType;
  endpoint?: string | null;
  embeddingModel?: string | null;
  llmModel?: string | null;
  isDefault: boolean;
  hasApiKey: boolean;
  createdAt: string;
  updatedAt?: string | null;
}

export interface CreateAiServiceRequest {
  name: string;
  serviceType: AiServiceType;
  apiKey?: string | null;
  endpoint?: string | null;
  embeddingModel?: string | null;
  llmModel?: string | null;
  isDefault: boolean;
}

export interface UpdateAiServiceRequest extends CreateAiServiceRequest {}

export interface AiServiceTestResult {
  success: boolean;
  message: string;
  httpStatusCode?: number | null;
  elapsedMs: number;
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

export const setDefaultAiService = (id: number) => {
  return http.request<ApiResponse<void>>(
    "post",
    `${baseUrl}/${id}/set-default`
  );
};

export const testAiServiceConnection = (id: number) => {
  return http.request<ApiResponse<AiServiceTestResult>>(
    "post",
    `${baseUrl}/${id}/test`
  );
};

