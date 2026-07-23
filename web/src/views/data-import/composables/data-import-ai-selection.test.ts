import { describe, expect, it } from "vitest";
import { createDefaultImportDuplicateAiConfig } from "./useDataImportExecution";
import { applyDataImportRuntimeAiSelections } from "./data-import-ai-selection";

describe("data import runtime AI selection", () => {
  it("automatically applies available service ids", () => {
    const config = {
      ...createDefaultImportDuplicateAiConfig(),
      enableSemanticDuplicateCheck: true,
      enableLlmDuplicateReview: true
    };

    applyDataImportRuntimeAiSelections(
      config,
      { status: "available", serviceId: 3 },
      { status: "available", serviceId: 8 }
    );

    expect(config.embeddingServiceId).toBe(3);
    expect(config.llmServiceId).toBe(8);
    expect(config.enableSemanticDuplicateCheck).toBe(true);
    expect(config.enableLlmDuplicateReview).toBe(true);
  });

  it("keeps the requested mode pending while embedding is checking", () => {
    const config = {
      ...createDefaultImportDuplicateAiConfig(),
      enableSemanticDuplicateCheck: true,
      enableLlmDuplicateReview: true
    };

    applyDataImportRuntimeAiSelections(
      config,
      { status: "checking", serviceId: 3 },
      { status: "available", serviceId: 8 }
    );

    expect(config.embeddingServiceId).toBeUndefined();
    expect(config.enableSemanticDuplicateCheck).toBe(true);
    expect(config.enableLlmDuplicateReview).toBe(true);
  });

  it("restores the automatic service id when checking becomes available", () => {
    const config = {
      ...createDefaultImportDuplicateAiConfig(),
      enableSemanticDuplicateCheck: true
    };

    applyDataImportRuntimeAiSelections(
      config,
      { status: "checking" },
      { status: "checking" }
    );
    expect(config.embeddingServiceId).toBeUndefined();

    applyDataImportRuntimeAiSelections(
      config,
      { status: "available", serviceId: 21 },
      { status: "available", serviceId: 34 }
    );
    expect(config.embeddingServiceId).toBe(21);
    expect(config.llmServiceId).toBe(34);
    expect(config.enableSemanticDuplicateCheck).toBe(true);
  });

  it("falls back to rule checks while embedding is unavailable", () => {
    const config = {
      ...createDefaultImportDuplicateAiConfig(),
      enableSemanticDuplicateCheck: true,
      enableLlmDuplicateReview: true
    };

    applyDataImportRuntimeAiSelections(
      config,
      { status: "unavailable", serviceId: 3 },
      { status: "available", serviceId: 8 }
    );

    expect(config.embeddingServiceId).toBeUndefined();
    expect(config.enableSemanticDuplicateCheck).toBe(false);
    expect(config.enableLlmDuplicateReview).toBe(false);
  });

  it("keeps embedding checks but disables unavailable LLM review", () => {
    const config = {
      ...createDefaultImportDuplicateAiConfig(),
      enableSemanticDuplicateCheck: true,
      enableLlmDuplicateReview: true
    };

    applyDataImportRuntimeAiSelections(
      config,
      { status: "available", serviceId: 3 },
      { status: "unavailable", serviceId: 8 }
    );

    expect(config.embeddingServiceId).toBe(3);
    expect(config.enableSemanticDuplicateCheck).toBe(true);
    expect(config.llmServiceId).toBeUndefined();
    expect(config.enableLlmDuplicateReview).toBe(false);
  });
});
