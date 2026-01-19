import { http } from "@/utils/http";
import type { ApiResponse } from "./customer";

export enum ColumnMappingTargetField {
  Project = 1,
  Specification = 2,
  Acceptance = 3,
  Remark = 4
}

export enum ColumnMappingMatchMode {
  Contains = 1,
  Equals = 2,
  Regex = 3
}

export interface ColumnMappingRule {
  id: number;
  targetField: ColumnMappingTargetField;
  matchMode: ColumnMappingMatchMode;
  pattern: string;
  priority: number;
  enabled: boolean;
  createdAt: string;
  updatedAt?: string;
}

export interface CreateColumnMappingRuleRequest {
  targetField: ColumnMappingTargetField;
  matchMode: ColumnMappingMatchMode;
  pattern: string;
  priority: number;
  enabled: boolean;
}

export type UpdateColumnMappingRuleRequest = CreateColumnMappingRuleRequest;

const baseUrl = "/api/column-mapping-rules";

export const getColumnMappingRules = (enabled?: boolean) => {
  return http.request<ApiResponse<ColumnMappingRule[]>>("get", baseUrl, {
    params: enabled === undefined ? undefined : { enabled }
  });
};

export const getEffectiveColumnMappingRules = () => {
  return http.request<ApiResponse<ColumnMappingRule[]>>(
    "get",
    `${baseUrl}/effective`
  );
};

export const createColumnMappingRule = (data: CreateColumnMappingRuleRequest) => {
  return http.request<ApiResponse<ColumnMappingRule>>("post", baseUrl, { data });
};

export const updateColumnMappingRule = (
  id: number,
  data: UpdateColumnMappingRuleRequest
) => {
  return http.request<ApiResponse<ColumnMappingRule>>("put", `${baseUrl}/${id}`, {
    data
  });
};

export const deleteColumnMappingRule = (id: number) => {
  return http.request<ApiResponse<void>>("delete", `${baseUrl}/${id}`);
};

