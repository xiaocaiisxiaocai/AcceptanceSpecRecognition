import { http } from "@/utils/http";
import type { ApiResponse } from "./customer";

export interface OrgUnit {
  id: number;
  parentId?: number | null;
  unitType: number;
  code: string;
  name: string;
  path: string;
  depth: number;
  sort: number;
  isActive: boolean;
  children?: OrgUnit[];
}

export interface CreateOrgUnitRequest {
  parentId?: number | null;
  unitType: number;
  code: string;
  name: string;
  sort: number;
  isActive: boolean;
}

export interface UpdateOrgUnitRequest {
  code: string;
  name: string;
  sort: number;
  isActive: boolean;
}

const baseUrl = "/api/org-units";

export const getOrgUnitTree = () => {
  return http.request<ApiResponse<OrgUnit[]>>("get", `${baseUrl}/tree`);
};

export const getOrgUnitFlat = () => {
  return http.request<ApiResponse<OrgUnit[]>>("get", `${baseUrl}/flat`);
};

export const createOrgUnit = (data: CreateOrgUnitRequest) => {
  return http.request<ApiResponse<OrgUnit>>("post", baseUrl, { data });
};

export const updateOrgUnit = (id: number, data: UpdateOrgUnitRequest) => {
  return http.request<ApiResponse<OrgUnit>>("put", `${baseUrl}/${id}`, { data });
};

export const deleteOrgUnit = (id: number) => {
  return http.request<ApiResponse<void>>("delete", `${baseUrl}/${id}`);
};
