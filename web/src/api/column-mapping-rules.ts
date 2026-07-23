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

export enum ColumnMappingRuleSource {
  Builtin = 1,
  Manual = 2,
  Learned = 3
}

export interface ColumnMappingRule {
  id: number;
  targetField: ColumnMappingTargetField;
  matchMode: ColumnMappingMatchMode;
  pattern: string;
  priority: number;
  enabled: boolean;
  source: ColumnMappingRuleSource;
  customerId?: number;
  createdAt: string;
  updatedAt?: string;
}

export interface CreateColumnMappingRuleRequest {
  targetField: ColumnMappingTargetField;
  matchMode: ColumnMappingMatchMode;
  pattern: string;
  priority: number;
  enabled: boolean;
  source?: ColumnMappingRuleSource;
  customerId?: number;
}

export type UpdateColumnMappingRuleRequest = CreateColumnMappingRuleRequest;

const baseUrl = "/api/column-mapping-rules";

export const getColumnMappingRules = (enabled?: boolean) => {
  return http.request<ApiResponse<ColumnMappingRule[]>>("get", baseUrl, {
    params: enabled === undefined ? undefined : { enabled }
  });
};

export const getEffectiveColumnMappingRules = (customerId?: number) => {
  return http.request<ApiResponse<ColumnMappingRule[]>>(
    "get",
    `${baseUrl}/effective`,
    {
      params: customerId === undefined ? undefined : { customerId }
    }
  );
};

export const createColumnMappingRule = (
  data: CreateColumnMappingRuleRequest
) => {
  return http.request<ApiResponse<ColumnMappingRule>>("post", baseUrl, {
    data
  });
};

export const updateColumnMappingRule = (
  id: number,
  data: UpdateColumnMappingRuleRequest
) => {
  return http.request<ApiResponse<ColumnMappingRule>>(
    "put",
    `${baseUrl}/${id}`,
    {
      data
    }
  );
};

export const deleteColumnMappingRule = (id: number) => {
  return http.request<ApiResponse<void>>("delete", `${baseUrl}/${id}`);
};

export const restoreColumnMappingRuleDefaults = (
  targetField?: ColumnMappingTargetField
) => {
  return http.request<ApiResponse<{ added: number }>>(
    "post",
    `${baseUrl}/restore-defaults`,
    {
      params: targetField === undefined ? undefined : { targetField }
    }
  );
};
