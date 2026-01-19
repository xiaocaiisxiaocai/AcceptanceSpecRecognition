import { http } from "@/utils/http";
import type { ApiResponse, PagedData, PagedRequest } from "./customer";

export interface Keyword {
  id: number;
  word: string;
  createdAt: string;
}

export interface KeywordListRequest extends PagedRequest {}

export interface CreateKeywordRequest {
  word: string;
}

export interface UpdateKeywordRequest {
  word: string;
}

export interface BatchAddKeywordsRequest {
  words: string[];
}

const baseUrl = "/api/keywords";

export const getKeywordList = (params?: KeywordListRequest) => {
  return http.request<ApiResponse<PagedData<Keyword>>>("get", baseUrl, {
    params
  });
};

export const createKeyword = (data: CreateKeywordRequest) => {
  return http.request<ApiResponse<Keyword>>("post", baseUrl, { data });
};

export const updateKeyword = (id: number, data: UpdateKeywordRequest) => {
  return http.request<ApiResponse<void>>("put", `${baseUrl}/${id}`, { data });
};

export const deleteKeyword = (id: number) => {
  return http.request<ApiResponse<void>>("delete", `${baseUrl}/${id}`);
};

export const batchAddKeywords = (data: BatchAddKeywordsRequest) => {
  return http.request<ApiResponse<number>>("post", `${baseUrl}/batch`, { data });
};

