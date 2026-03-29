import { http } from "@/utils/http";
import type { ApiResponse } from "./customer";

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
