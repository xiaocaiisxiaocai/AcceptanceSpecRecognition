import { describe, expect, it } from "vitest";
import type { AiServiceSelection } from "@/api/ai-service";
import { resolveAiAssistSelectionState } from "./ai-selection-state";

const available = (serviceId: number, name: string): AiServiceSelection => ({
  status: "available",
  serviceId,
  name
});

describe("resolveAiAssistSelectionState", () => {
  it("automatically enables only when LLM and Embedding are both ready", () => {
    expect(
      resolveAiAssistSelectionState(
        available(7, "LLM"),
        available(8, "Embedding")
      )
    ).toEqual({ enabled: true, serviceId: 7 });
  });

  it.each(["checking", "unavailable"] as const)(
    "does not enable while LLM status is %s",
    status => {
      expect(
        resolveAiAssistSelectionState({ status }, available(8, "Embedding"))
      ).toEqual({ enabled: false, serviceId: undefined });
    }
  );

  it.each(["checking", "unavailable"] as const)(
    "does not enable while Embedding status is %s",
    status => {
      expect(
        resolveAiAssistSelectionState(available(7, "LLM"), { status })
      ).toEqual({ enabled: false, serviceId: undefined });
    }
  );
});
