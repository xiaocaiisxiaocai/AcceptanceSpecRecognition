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

export interface MoveOrgUnitRequest {
  newParentId: number;
}

export interface BusinessOrgOption {
  id: number;
  name: string;
  unitType: number;
  path: string;
  depth: number;
}

export interface BusinessOrgContext {
  requiresSelection: boolean;
  currentOrgUnitId?: number;
  currentOrgUnitName?: string;
  isCompanyFallback: boolean;
  options: BusinessOrgOption[];
}

const baseUrl = "/api/org-units";

export const getOrgUnitTree = () => {
  return http.request<ApiResponse<OrgUnit[]>>("get", `${baseUrl}/tree`);
};

export const getOrgUnitFlat = () => {
  return http.request<ApiResponse<OrgUnit[]>>("get", `${baseUrl}/flat`);
};

export const getBusinessOrgContext = () => {
  return http.request<ApiResponse<BusinessOrgContext>>(
    "get",
    `${baseUrl}/business-context`
  );
};

export const createOrgUnit = (data: CreateOrgUnitRequest) => {
  return http.request<ApiResponse<OrgUnit>>("post", baseUrl, { data });
};

export const updateOrgUnit = (id: number, data: UpdateOrgUnitRequest) => {
  return http.request<ApiResponse<OrgUnit>>("put", `${baseUrl}/${id}`, {
    data
  });
};

export const moveOrgUnit = (id: number, data: MoveOrgUnitRequest) => {
  return http.request<ApiResponse<OrgUnit>>("put", `${baseUrl}/${id}/move`, {
    data
  });
};

export const deleteOrgUnit = (id: number) => {
  return http.request<ApiResponse<void>>("delete", `${baseUrl}/${id}`);
};
