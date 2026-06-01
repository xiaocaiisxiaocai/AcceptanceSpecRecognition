import { AiServicePurpose, AiServiceType } from "@/api/ai-service";

export const TEST_ACTION_LABEL = "完整测试";

export const serviceTypeOptions = [
  { label: "OpenAI", value: AiServiceType.OpenAI },
  { label: "Azure OpenAI", value: AiServiceType.AzureOpenAI },
  { label: "Ollama", value: AiServiceType.Ollama },
  { label: "LM Studio", value: AiServiceType.LMStudio },
  { label: "OpenAI Compatible", value: AiServiceType.CustomOpenAICompatible }
];

export const purposeOptions = [
  { label: "LLM", value: AiServicePurpose.Llm },
  { label: "Embedding", value: AiServicePurpose.Embedding }
];
