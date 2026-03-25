import { http } from "@/utils/http";
import type { ApiResponse, PagedData, PagedRequest } from "./customer";

export interface PromptTemplate {
  id: number;
  name: string;
  scene: string;
  displayName: string;
  content: string;
  isSystem: boolean;
  isDefault: boolean;
  usageDescription: string;
  availableVariables: string[];
  createdAt: string;
  updatedAt?: string | null;
}

export interface UpdatePromptTemplateRequest {
  displayName: string;
  content: string;
}

export interface PromptTemplatePreviewRequest {
  scene: string;
  content: string;
}

export interface PromptTemplatePreviewResponse {
  isValid: boolean;
  errors: string[];
  renderedPrompt: string;
  exampleJson?: string | null;
  structuredOutputIsValid: boolean;
  structuredOutputError?: string | null;
}

export interface PromptTemplateListRequest extends PagedRequest {}

const baseUrl = "/api/prompt-templates";

export const getPromptTemplateList = (params?: PromptTemplateListRequest) => {
  return http.request<ApiResponse<PagedData<PromptTemplate>>>("get", baseUrl, {
    params
  });
};

export const updatePromptTemplate = (
  id: number,
  data: UpdatePromptTemplateRequest
) => {
  return http.request<ApiResponse<PromptTemplate>>("put", `${baseUrl}/${id}`, {
    data
  });
};

export const previewPromptTemplate = (
  data: PromptTemplatePreviewRequest
) => {
  return http.request<ApiResponse<PromptTemplatePreviewResponse>>(
    "post",
    `${baseUrl}/preview`,
    { data }
  );
};

export const resetSystemPromptTemplate = (scene: string) => {
  return http.request<ApiResponse<PromptTemplate>>(
    "post",
    `${baseUrl}/reset-system/${scene}`
  );
};
