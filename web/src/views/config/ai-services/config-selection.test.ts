import { describe, expect, it } from "vitest";
import {
  AiServicePurposeValue,
  shouldShowAllConfigsByDefault
} from "./config-selection";

enum AiServiceTypeValue {
  Ollama = 2
}

type AiServiceConfigFixture = {
  id: number;
  name: string;
  serviceType: number;
  purpose: number;
  priority: number;
  endpoint: string;
  embeddingModel: string | null;
  llmModel: string | null;
  disableThinking: boolean;
  isDisabled: boolean;
  defaultRecallTopK: number;
  hasApiKey: boolean;
  createdAt: string;
  updatedAt: string | null;
};

const createConfig = (
  id: number,
  purpose: AiServicePurposeValue,
  priority: number,
  isDisabled = false
): AiServiceConfigFixture => ({
  id,
  name: `配置${id}`,
  serviceType: AiServiceTypeValue.Ollama,
  purpose,
  priority,
  endpoint: "http://127.0.0.1:11434/api",
  embeddingModel:
    purpose === AiServicePurposeValue.Embedding ? `embedding-${id}` : null,
  llmModel: purpose === AiServicePurposeValue.Llm ? `llm-${id}` : null,
  disableThinking: false,
  isDisabled,
  defaultRecallTopK: 2,
  hasApiKey: false,
  createdAt: "2026-07-03T00:00:00Z",
  updatedAt: null
});

describe("ai-services config-selection", () => {
  it("存在多个启用 LLM 配置时默认展开完整配置表，避免后备服务被摘要隐藏", () => {
    const configs = [
      createConfig(1, AiServicePurposeValue.Llm, 0),
      createConfig(2, AiServicePurposeValue.Llm, 2),
      createConfig(3, AiServicePurposeValue.Embedding, 0)
    ];

    expect(shouldShowAllConfigsByDefault(configs)).toBe(true);
  });

  it("只有禁用的后备配置时不默认展开完整配置表", () => {
    const configs = [
      createConfig(1, AiServicePurposeValue.Llm, 0),
      createConfig(2, AiServicePurposeValue.Llm, 2, true),
      createConfig(3, AiServicePurposeValue.Embedding, 0)
    ];

    expect(shouldShowAllConfigsByDefault(configs)).toBe(false);
  });
});
