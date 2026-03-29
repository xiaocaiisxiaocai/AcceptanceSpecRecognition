import { http } from "@/utils/http";
import type { ApiResponse } from "./customer";
import type { SpecListRequest } from "./spec";

export interface DictionaryEntry {
  key: string;
  value: string;
}

export interface UnitFactorEntry {
  key: string;
  value: number;
}

export interface ConflictPair {
  left: string;
  right: string;
}

export interface MatchingKnowledgeLayer {
  entityAliases: Record<string, string>;
  unitAliases: Record<string, string>;
  unitFactors: Record<string, number>;
  fieldAliases: Record<string, string>;
  conflictPairs: ConflictPair[];
}

export interface MatchingKnowledgeView {
  builtIn: MatchingKnowledgeLayer;
  custom: MatchingKnowledgeLayer;
  effective: MatchingKnowledgeLayer;
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
  return http.request<ApiResponse<MatchingKnowledgeView>>("get", baseUrl);
};

export const updateMatchingKnowledge = (
  data: UpdateMatchingKnowledgeRequest
) => {
  return http.request<ApiResponse<MatchingKnowledgeView>>("put", baseUrl, {
    data
  });
};

export const resetMatchingKnowledge = () => {
  return http.request<ApiResponse<MatchingKnowledgeView>>(
    "post",
    `${baseUrl}/reset`
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
