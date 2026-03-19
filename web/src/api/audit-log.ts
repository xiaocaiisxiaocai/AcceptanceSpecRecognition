import { http } from "@/utils/http";
import type { ApiResponse, PagedData } from "./customer";

export enum AuditLogSource {
  BackendRequest = 0,
  FrontendEvent = 1
}

export enum AuditLogLevel {
  Information = 0,
  Warning = 1,
  Error = 2
}

export interface AuditLogListItem {
  id: number;
  source: AuditLogSource;
  level: AuditLogLevel;
  eventType: string;
  username?: string | null;
  requestMethod?: string | null;
  requestPath?: string | null;
  queryString?: string | null;
  statusCode?: number | null;
  durationMs?: number | null;
  clientIp?: string | null;
  userAgent?: string | null;
  clientTraceId?: string | null;
  clientId?: string | null;
  frontendRoute?: string | null;
  createdAt: string;
}

export interface AuditLogDetail extends AuditLogListItem {
  details?: string | null;
}

export interface AuditLogListRequest {
  page?: number;
  pageSize?: number;
  source?: AuditLogSource;
  level?: AuditLogLevel;
  username?: string;
  requestMethod?: string;
  keyword?: string;
  from?: string;
  to?: string;
  minStatusCode?: number;
  maxStatusCode?: number;
}

export interface DeleteAuditLogRangeRequest {
  from?: string;
  to?: string;
}

const baseUrl = "/api/audit-logs";

export const getAuditLogList = (params?: AuditLogListRequest) => {
  return http.request<ApiResponse<PagedData<AuditLogListItem>>>("get", baseUrl, {
    params
  });
};

export const getAuditLogDetail = (id: number) => {
  return http.request<ApiResponse<AuditLogDetail>>("get", `${baseUrl}/${id}`);
};

export const deleteAuditLogsByRange = (params: DeleteAuditLogRangeRequest) => {
  return http.request<ApiResponse<{ deletedCount: number }>>(
    "delete",
    `${baseUrl}/range`,
    { params }
  );
};
