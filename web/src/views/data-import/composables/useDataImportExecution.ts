import { ref } from "vue";
import type {
  CombinedImportResult,
  DifferenceDecision,
  ImportDuplicateAiConfig
} from "../dataImport.types";

export const createDefaultImportDuplicateAiConfig = (
  serviceIds: Pick<
    ImportDuplicateAiConfig,
    "embeddingServiceId" | "llmServiceId"
  > = {}
): ImportDuplicateAiConfig => ({
  enableSemanticDuplicateCheck: false,
  embeddingServiceId: serviceIds.embeddingServiceId,
  semanticTopK: 3,
  semanticMinScore: 0.75,
  enableLlmDuplicateReview: false,
  llmServiceId: serviceIds.llmServiceId,
  llmPassScore: 0.9,
  highConfidenceThreshold: 0.95
});

export function useDataImportExecution() {
  const importing = ref(false);
  const importResult = ref<CombinedImportResult | null>(null);
  const pendingImportAggregate = ref<CombinedImportResult | null>(null);
  const committedImportAggregate = ref<CombinedImportResult | null>(null);
  const differenceDecisionMap = ref<
    Record<string, DifferenceDecision | undefined>
  >({});
  const differenceConfirmDialogVisible = ref(false);

  return {
    importing,
    importResult,
    pendingImportAggregate,
    committedImportAggregate,
    differenceDecisionMap,
    differenceConfirmDialogVisible
  };
}
