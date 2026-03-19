import { http } from "@/utils/http";
import type { ApiResponse } from "./customer";

export interface AuthPermission {
  id: number;
  code: string;
  name: string;
  permissionType: number;
  resource: string;
  action: string;
}

const baseUrl = "/api/auth-permissions";

export const getAuthPermissionList = (params?: {
  permissionType?: number;
  keyword?: string;
}) => {
  return http.request<ApiResponse<AuthPermission[]>>("get", baseUrl, { params });
};
