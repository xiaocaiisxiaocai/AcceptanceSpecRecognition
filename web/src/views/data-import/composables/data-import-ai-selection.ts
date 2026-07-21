import type { AiServiceSelection } from "@/api/ai-service";
import type { ImportDuplicateAiConfig } from "../dataImport.types";
import { getRuntimeAiSelectionServiceId } from "@/utils/runtime-ai-selection";

export const applyDataImportRuntimeAiSelections = (
  config: ImportDuplicateAiConfig,
  embedding: AiServiceSelection,
  llm: AiServiceSelection
) => {
  config.embeddingServiceId = getRuntimeAiSelectionServiceId(embedding);
  config.llmServiceId = getRuntimeAiSelectionServiceId(llm);

  if (embedding.status === "unavailable") {
    config.enableSemanticDuplicateCheck = false;
    config.enableLlmDuplicateReview = false;
  } else if (llm.status === "unavailable") {
    config.enableLlmDuplicateReview = false;
  }
};
