import { http } from "@/utils/http";
import type { ApiResponse, PagedData, PagedRequest } from "./customer";

export interface SynonymGroup {
  id: number;
  words: string[];
  createdAt: string;
  updatedAt?: string | null;
}

export interface UpsertSynonymGroupRequest {
  /** 至少2个词，第一个为标准词 */
  words: string[];
}

export interface SynonymGroupListRequest extends PagedRequest {}

const baseUrl = "/api/synonyms";

export const getSynonymGroupList = (params?: SynonymGroupListRequest) => {
  return http.request<ApiResponse<PagedData<SynonymGroup>>>("get", baseUrl, {
    params
  });
};

export const createSynonymGroup = (data: UpsertSynonymGroupRequest) => {
  return http.request<ApiResponse<SynonymGroup>>("post", baseUrl, { data });
};

export const updateSynonymGroup = (id: number, data: UpsertSynonymGroupRequest) => {
  return http.request<ApiResponse<void>>("put", `${baseUrl}/${id}`, { data });
};

export const deleteSynonymGroup = (id: number) => {
  return http.request<ApiResponse<void>>("delete", `${baseUrl}/${id}`);
};

