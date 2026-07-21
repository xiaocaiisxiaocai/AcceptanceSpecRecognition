import type { AiServiceSelection } from "@/api/ai-service";

export interface AiAssistSelectionState {
  enabled: boolean;
  serviceId?: number;
}

export const resolveAiAssistSelectionState = (
  selection: AiServiceSelection
): AiAssistSelectionState => {
  if (selection.status !== "available" || selection.serviceId == null) {
    return { enabled: false, serviceId: undefined };
  }

  return {
    enabled: true,
    serviceId: selection.serviceId
  };
};
