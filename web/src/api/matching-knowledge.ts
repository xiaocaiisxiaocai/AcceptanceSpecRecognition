import { http } from "@/utils/http";
import type { ApiResponse } from "./customer";
import type { SpecListRequest } from "./spec";

export interface MatchingKnowledgeGroup {
  items: string[];
}

export interface MatchingKnowledgeConflictGroup {
  leftItems: string[];
  rightItems: string[];
}

export interface MatchingKnowledgeLayer {
  entityGroups: MatchingKnowledgeGroup[];
  unitGroups: MatchingKnowledgeGroup[];
  unitFactors: Record<string, number>;
  fieldGroups: MatchingKnowledgeGroup[];
  conflictGroups: MatchingKnowledgeConflictGroup[];
}

export type UpdateMatchingKnowledgeRequest = MatchingKnowledgeLayer;
export type MatchingKnowledgeDraftCategory =
  | "entityAliases"
  | "unitAliases"
  | "fieldAliases"
  | "conflictPairs";

export interface MatchingKnowledgeDraftSpecFilter
  extends Omit<SpecListRequest, "page" | "pageSize"> {}

export interface GenerateMatchingKnowledgeDraftRequest {
  category: MatchingKnowledgeDraftCategory;
  specFilter?: MatchingKnowledgeDraftSpecFilter;
  llmServiceId?: number;
}

export interface MatchingKnowledgeDraftItem {
  key: string;
  value: string;
  evidenceSnippet: string;
  reason: string;
  status: "ready" | "duplicate" | "conflict" | string;
  statusMessage?: string;
}

export interface MatchingKnowledgeDraftResponse {
  category: MatchingKnowledgeDraftCategory;
  items: MatchingKnowledgeDraftItem[];
}

const baseUrl = "/api/matching-knowledge";

export const getMatchingKnowledge = () => {
  return http.request<ApiResponse<MatchingKnowledgeLayer>>("get", baseUrl);
};

export const updateMatchingKnowledge = (
  data: UpdateMatchingKnowledgeRequest
) => {
  return http.request<ApiResponse<MatchingKnowledgeLayer>>("put", baseUrl, {
    data
  });
};

export const clearMatchingKnowledge = () => {
  return http.request<ApiResponse<MatchingKnowledgeLayer>>(
    "post",
    `${baseUrl}/clear`
  );
};

export const restoreDefaultMatchingKnowledge = () => {
  return http.request<ApiResponse<MatchingKnowledgeLayer>>(
    "post",
    `${baseUrl}/restore-defaults`
  );
};

export const generateMatchingKnowledgeDraft = (
  data: GenerateMatchingKnowledgeDraftRequest
) => {
  return http.request<ApiResponse<MatchingKnowledgeDraftResponse>>(
    "post",
    `${baseUrl}/drafts/generate`,
    {
      data
    },
    {
      timeout: 120000
    }
  );
};
