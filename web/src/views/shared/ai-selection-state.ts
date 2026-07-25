import type { AiServiceSelection } from "@/api/ai-service";
import { isRuntimeAiSelectionAvailable } from "@/utils/runtime-ai-selection";

export interface AiAssistSelectionState {
  enabled: boolean;
  serviceId?: number;
}

export const resolveAiAssistSelectionState = (
  llmSelection: AiServiceSelection,
  embeddingSelection: AiServiceSelection
): AiAssistSelectionState => {
  if (
    !isRuntimeAiSelectionAvailable(llmSelection) ||
    !isRuntimeAiSelectionAvailable(embeddingSelection)
  ) {
    return { enabled: false, serviceId: undefined };
  }

  return {
    enabled: true,
    serviceId: llmSelection.serviceId
  };
};
