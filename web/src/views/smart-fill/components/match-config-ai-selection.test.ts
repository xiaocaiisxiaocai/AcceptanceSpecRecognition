import { describe, expect, it } from "vitest";
import type { MatchConfig } from "@/api/matching";
import { applyMatchConfigRuntimeAiSelections } from "./match-config-ai-selection";

const createConfig = (overrides: Partial<MatchConfig> = {}) =>
  ({
    enableLlmEquivalenceAdjudication: false,
    enableLlmSemanticPriority: false,
    ...overrides
  }) as MatchConfig;

describe("smart fill runtime AI selection", () => {
  it("automatically applies available service ids", () => {
    const config = createConfig();

    applyMatchConfigRuntimeAiSelections(
      config,
      { status: "available", serviceId: 3 },
      { status: "available", serviceId: 8 }
    );

    expect(config.embeddingServiceId).toBe(3);
    expect(config.llmServiceId).toBe(8);
  });

  it.each(["checking", "unavailable"] as const)(
    "never treats %s embedding state as available",
    status => {
      const config = createConfig({ exactMatchOnly: false });

      applyMatchConfigRuntimeAiSelections(
        config,
        { status, serviceId: 3 },
        { status: "available", serviceId: 8 }
      );

      expect(config.embeddingServiceId).toBeUndefined();
      expect(config.exactMatchOnly).toBe(false);
    }
  );

  it("disables optional LLM strategies when LLM is unavailable", () => {
    const config = createConfig({
      enableLlmEquivalenceAdjudication: true,
      enableLlmSemanticPriority: true
    });

    applyMatchConfigRuntimeAiSelections(
      config,
      { status: "available", serviceId: 3 },
      { status: "unavailable", serviceId: 8 }
    );

    expect(config.llmServiceId).toBeUndefined();
    expect(config.enableLlmEquivalenceAdjudication).toBe(false);
    expect(config.enableLlmSemanticPriority).toBe(false);
  });

  it("keeps requested LLM strategies while a transient check is retrying", () => {
    const config = createConfig({
      enableLlmEquivalenceAdjudication: true,
      enableLlmSemanticPriority: true
    });

    applyMatchConfigRuntimeAiSelections(
      config,
      { status: "available", serviceId: 3 },
      { status: "checking" }
    );

    expect(config.llmServiceId).toBeUndefined();
    expect(config.enableLlmEquivalenceAdjudication).toBe(true);
    expect(config.enableLlmSemanticPriority).toBe(true);
  });
});
