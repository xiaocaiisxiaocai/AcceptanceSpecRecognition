import { http } from "@/utils/http";
import type { ApiResponse, PagedData, PagedRequest } from "./customer";

export interface SystemUser {
  id: number;
  companyId: number;
  username: string;
  nickname: string;
  avatar: string;
  roles: string[];
  permissions: string[];
  isActive: boolean;
  permissionVersion: number;
  orgUnits: SystemUserOrgUnit[];
  createdAt: string;
  updatedAt?: string | null;
}

export interface SystemUserOrgUnit {
  orgUnitId: number;
  orgUnitName: string;
  orgUnitType: number;
  isPrimary: boolean;
  startAt?: string | null;
  endAt?: string | null;
}

export interface SystemUserListRequest extends PagedRequest {
  isActive?: boolean;
}

export interface CreateSystemUserRequest {
  username: string;
  password: string;
  nickname: string;
  avatar?: string | null;
  roles: string[];
  primaryOrgUnitId?: number | null;
  orgUnitIds?: number[];
  roleStartAt?: string | null;
  roleEndAt?: string | null;
  orgStartAt?: string | null;
  orgEndAt?: string | null;
  isActive: boolean;
}

export interface UpdateSystemUserRequest {
  nickname: string;
  avatar?: string | null;
  roles: string[];
  primaryOrgUnitId?: number | null;
  orgUnitIds?: number[];
  roleStartAt?: string | null;
  roleEndAt?: string | null;
  orgStartAt?: string | null;
  orgEndAt?: string | null;
  isActive: boolean;
}

export interface UpdateSystemUserStatusRequest {
  isActive: boolean;
}

export interface ResetSystemUserPasswordRequest {
  newPassword: string;
}

const baseUrl = "/api/system-users";

export const getSystemUserList = (params?: SystemUserListRequest) => {
  return http.request<ApiResponse<PagedData<SystemUser>>>("get", baseUrl, {
    params
  });
};

export const getSystemUserById = (id: number) => {
  return http.request<ApiResponse<SystemUser>>("get", `${baseUrl}/${id}`);
};

export const createSystemUser = (data: CreateSystemUserRequest) => {
  return http.request<ApiResponse<SystemUser>>("post", baseUrl, { data });
};

export const updateSystemUser = (id: number, data: UpdateSystemUserRequest) => {
  return http.request<ApiResponse<SystemUser>>("put", `${baseUrl}/${id}`, {
    data
  });
};

export const updateSystemUserStatus = (
  id: number,
  data: UpdateSystemUserStatusRequest
) => {
  return http.request<ApiResponse<SystemUser>>(
    "put",
    `${baseUrl}/${id}/status`,
    { data }
  );
};

export const resetSystemUserPassword = (
  id: number,
  data: ResetSystemUserPasswordRequest
) => {
  return http.request<ApiResponse<void>>("put", `${baseUrl}/${id}/password`, {
    data
  });
};

export const deleteSystemUser = (id: number) => {
  return http.request<ApiResponse<void>>("delete", `${baseUrl}/${id}`);
};
