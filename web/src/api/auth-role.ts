import { http } from "@/utils/http";
import type { ApiResponse } from "./customer";

export interface RoleDataScope {
  resource: string;
  scopeType: number;
  orgUnitIds: number[];
}

export interface AuthRole {
  id: number;
  code: string;
  name: string;
  description: string;
  isBuiltIn: boolean;
  isActive: boolean;
  permissionCodes: string[];
  dataScopes: RoleDataScope[];
}

export interface CreateAuthRoleRequest {
  code: string;
  name: string;
  description?: string;
  isActive: boolean;
  permissionCodes: string[];
  dataScopes: RoleDataScope[];
}

export interface UpdateAuthRoleRequest {
  name: string;
  description?: string;
  isActive: boolean;
  permissionCodes: string[];
  dataScopes: RoleDataScope[];
}

const baseUrl = "/api/auth-roles";

export const getAuthRoleList = (params?: { keyword?: string }) => {
  return http.request<ApiResponse<AuthRole[]>>("get", baseUrl, { params });
};

export const getAuthRoleById = (id: number) => {
  return http.request<ApiResponse<AuthRole>>("get", `${baseUrl}/${id}`);
};

export const createAuthRole = (data: CreateAuthRoleRequest) => {
  return http.request<ApiResponse<AuthRole>>("post", baseUrl, { data });
};

export const updateAuthRole = (id: number, data: UpdateAuthRoleRequest) => {
  return http.request<ApiResponse<AuthRole>>("put", `${baseUrl}/${id}`, { data });
};

export const deleteAuthRole = (id: number) => {
  return http.request<ApiResponse<void>>("delete", `${baseUrl}/${id}`);
};
