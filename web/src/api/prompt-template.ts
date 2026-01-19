import { http } from "@/utils/http";
import type { ApiResponse, PagedData, PagedRequest } from "./customer";

export interface PromptTemplate {
  id: number;
  name: string;
  content: string;
  isDefault: boolean;
  createdAt: string;
  updatedAt?: string | null;
}

export interface CreatePromptTemplateRequest {
  name: string;
  content: string;
  isDefault: boolean;
}

export interface UpdatePromptTemplateRequest extends CreatePromptTemplateRequest {}

export interface PromptTemplateListRequest extends PagedRequest {}

const baseUrl = "/api/prompt-templates";

export const getPromptTemplateList = (params?: PromptTemplateListRequest) => {
  return http.request<ApiResponse<PagedData<PromptTemplate>>>("get", baseUrl, {
    params
  });
};

export const getPromptTemplate = (id: number) => {
  return http.request<ApiResponse<PromptTemplate>>("get", `${baseUrl}/${id}`);
};

export const getDefaultPromptTemplate = () => {
  return http.request<ApiResponse<PromptTemplate>>("get", `${baseUrl}/default`);
};

export const createPromptTemplate = (data: CreatePromptTemplateRequest) => {
  return http.request<ApiResponse<PromptTemplate>>("post", baseUrl, { data });
};

export const updatePromptTemplate = (
  id: number,
  data: UpdatePromptTemplateRequest
) => {
  return http.request<ApiResponse<PromptTemplate>>("put", `${baseUrl}/${id}`, {
    data
  });
};

export const deletePromptTemplate = (id: number) => {
  return http.request<ApiResponse<void>>("delete", `${baseUrl}/${id}`);
};

export const setDefaultPromptTemplate = (id: number) => {
  return http.request<ApiResponse<void>>(
    "post",
    `${baseUrl}/${id}/set-default`
  );
};

