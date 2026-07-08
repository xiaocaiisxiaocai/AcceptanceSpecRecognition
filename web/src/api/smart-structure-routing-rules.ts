import { http } from "@/utils/http";
import type { ApiResponse } from "./customer";

export type SmartStructureRoutingMatchScope =
  | "Any"
  | "TableName"
  | "Headers"
  | "SampleRows";

export type SmartStructureRoutingMatchMode = "Contains" | "Equals" | "Regex";

export type SmartStructureRoutingRuleSource =
  | "BuiltinSeed"
  | "Manual"
  | "AiSuggested";

export type SmartStructureRoutingRecommendation =
  | "Recommended"
  | "NeedConfirm"
  | "Optional"
  | "Skip"
  | string;

export interface SmartStructureRoutingRule {
  id: number;
  name: string;
  tableKind: string;
  recommendation: SmartStructureRoutingRecommendation;
  matchScope: SmartStructureRoutingMatchScope;
  matchMode: SmartStructureRoutingMatchMode;
  pattern: string;
  weight: number;
  priority: number;
  enabled: boolean;
  source: SmartStructureRoutingRuleSource;
  customerId?: number | null;
  createdAt: string;
  updatedAt?: string | null;
}

export interface CreateSmartStructureRoutingRuleRequest {
  name: string;
  tableKind: string;
  recommendation: SmartStructureRoutingRecommendation;
  matchScope: SmartStructureRoutingMatchScope;
  matchMode: SmartStructureRoutingMatchMode;
  pattern: string;
  weight: number;
  priority: number;
  enabled: boolean;
  source?: SmartStructureRoutingRuleSource;
  customerId?: number;
}

export type UpdateSmartStructureRoutingRuleRequest =
  CreateSmartStructureRoutingRuleRequest;

const baseUrl = "/api/smart-structure-routing-rules";

export const getSmartStructureRoutingRules = (enabled?: boolean) => {
  return http.request<ApiResponse<SmartStructureRoutingRule[]>>("get", baseUrl, {
    params: enabled === undefined ? undefined : { enabled }
  });
};

export const getEffectiveSmartStructureRoutingRules = (
  customerId?: number
) => {
  return http.request<ApiResponse<SmartStructureRoutingRule[]>>(
    "get",
    `${baseUrl}/effective`,
    {
      params: customerId === undefined ? undefined : { customerId }
    }
  );
};

export const createSmartStructureRoutingRule = (
  data: CreateSmartStructureRoutingRuleRequest
) => {
  return http.request<ApiResponse<SmartStructureRoutingRule>>("post", baseUrl, {
    data
  });
};

export const updateSmartStructureRoutingRule = (
  id: number,
  data: UpdateSmartStructureRoutingRuleRequest
) => {
  return http.request<ApiResponse<SmartStructureRoutingRule>>(
    "put",
    `${baseUrl}/${id}`,
    {
      data
    }
  );
};

export const deleteSmartStructureRoutingRule = (id: number) => {
  return http.request<ApiResponse<void>>("delete", `${baseUrl}/${id}`);
};
