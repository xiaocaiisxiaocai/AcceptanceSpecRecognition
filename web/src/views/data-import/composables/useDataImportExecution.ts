import { ref } from "vue";
import type {
  CombinedImportResult,
  DifferenceDecision,
  ImportDuplicateAiConfig
} from "../dataImport.types";

export const createDefaultImportDuplicateAiConfig =
  (): ImportDuplicateAiConfig => ({
    enableSemanticDuplicateCheck: false,
    embeddingServiceId: undefined,
    semanticTopK: 3,
    semanticMinScore: 0.75,
    enableLlmDuplicateReview: false,
    llmServiceId: undefined,
    llmPassScore: 0.9,
    highConfidenceThreshold: 0.95
  });

export function useDataImportExecution() {
  const importing = ref(false);
  const importResult = ref<CombinedImportResult | null>(null);
  const pendingImportAggregate = ref<CombinedImportResult | null>(null);
  const committedImportAggregate = ref<CombinedImportResult | null>(null);
  const previewSkippedRows = ref(false);
  const differenceDecisionMap = ref<Record<string, DifferenceDecision | undefined>>(
    {}
  );
  const differenceConfirmDialogVisible = ref(false);

  return {
    importing,
    importResult,
    pendingImportAggregate,
    committedImportAggregate,
    previewSkippedRows,
    differenceDecisionMap,
    differenceConfirmDialogVisible
  };
}
