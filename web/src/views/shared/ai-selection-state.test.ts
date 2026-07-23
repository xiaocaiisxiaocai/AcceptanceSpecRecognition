import { describe, expect, it } from "vitest";
import { resolveAiAssistSelectionState } from "./ai-selection-state";

describe("resolveAiAssistSelectionState", () => {
  it("automatically enables the ready service", () => {
    expect(
      resolveAiAssistSelectionState({
        status: "available",
        serviceId: 7,
        name: "LLM"
      })
    ).toEqual({ enabled: true, serviceId: 7 });
  });

  it.each(["checking", "unavailable"] as const)(
    "does not claim AI is enabled while status is %s",
    status => {
      expect(resolveAiAssistSelectionState({ status })).toEqual({
        enabled: false,
        serviceId: undefined
      });
    }
  );
});
