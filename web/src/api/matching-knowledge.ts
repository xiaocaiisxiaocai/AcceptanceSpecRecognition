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

export interface MatchingKnowledgeConfig {
  entityAliases: Record<string, string>;
  unitAliases: Record<string, string>;
  unitFactors: Record<string, number>;
  fieldAliases: Record<string, string>;
  conflictPairs: ConflictPair[];
}

export interface UpdateMatchingKnowledgeRequest {
  entityAliases: Record<string, string>;
  unitAliases: Record<string, string>;
  unitFactors: Record<string, number>;
  fieldAliases: Record<string, string>;
  conflictPairs: ConflictPair[];
}

const baseUrl = "/api/matching-knowledge";

export const getMatchingKnowledge = () => {
  return http.request<ApiResponse<MatchingKnowledgeConfig>>("get", baseUrl);
};

export const updateMatchingKnowledge = (
  data: UpdateMatchingKnowledgeRequest
) => {
  return http.request<ApiResponse<MatchingKnowledgeConfig>>("put", baseUrl, {
    data
  });
};

export const resetMatchingKnowledge = () => {
  return http.request<ApiResponse<MatchingKnowledgeConfig>>(
    "post",
    `${baseUrl}/reset`
  );
};
