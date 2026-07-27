import type { SpecSemanticSearchRequest } from "@/api/spec";

export const buildSemanticSearchScopeKey = (
  request: SpecSemanticSearchRequest
) =>
  JSON.stringify({
    customerId: request.customerId ?? null,
    machineModelId: request.machineModelId ?? null,
    processId: request.processId ?? null,
    queries: request.queries,
    topK: request.topK ?? null,
    minScore: request.minScore ?? null
  });
