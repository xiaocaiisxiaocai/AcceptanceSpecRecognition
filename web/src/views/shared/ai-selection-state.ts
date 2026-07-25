import type { AiServiceSelection } from "@/api/ai-service";

export interface AiAssistSelectionState {
  enabled: boolean;
  serviceId?: number;
}

export const resolveAiAssistSelectionState = (
  llmSelection: AiServiceSelection,
  embeddingSelection: AiServiceSelection
): AiAssistSelectionState => {
  const llmReady =
    llmSelection.status === "available" && llmSelection.serviceId != null;
  const embeddingReady =
    embeddingSelection.status === "available" &&
    embeddingSelection.serviceId != null;

  if (!llmReady || !embeddingReady) {
    return { enabled: false, serviceId: undefined };
  }

  return {
    enabled: true,
    serviceId: llmSelection.serviceId
  };
};
