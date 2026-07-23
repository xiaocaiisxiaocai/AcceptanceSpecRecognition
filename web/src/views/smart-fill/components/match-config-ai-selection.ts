import type { AiServiceSelection } from "@/api/ai-service";
import type { MatchConfig } from "@/api/matching";
import { getRuntimeAiSelectionServiceId } from "@/utils/runtime-ai-selection";

export const applyMatchConfigRuntimeAiSelections = (
  config: MatchConfig,
  embedding: AiServiceSelection,
  llm: AiServiceSelection
) => {
  config.embeddingServiceId = getRuntimeAiSelectionServiceId(embedding);
  config.llmServiceId = getRuntimeAiSelectionServiceId(llm);

  if (llm.status === "unavailable") {
    config.enableLlmEquivalenceAdjudication = false;
    config.enableLlmSemanticPriority = false;
  }
};
