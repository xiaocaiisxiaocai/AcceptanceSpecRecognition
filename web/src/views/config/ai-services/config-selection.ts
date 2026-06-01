import { AiServicePurpose, type AiServiceConfig } from "@/api/ai-service";
import { serviceTypeOptions } from "./constants";
import { hasPurpose } from "./utils";

export const getServiceTypeLabel = (value: AiServiceConfig["serviceType"]) =>
  serviceTypeOptions.find(x => x.value === value)?.label || "-";

export const pickConfigByPurpose = (
  configs: AiServiceConfig[],
  purpose: AiServicePurpose
) => {
  const enabledConfigs = configs.filter(item => !item.isDisabled);
  const exact = enabledConfigs.find(item => item.purpose === purpose);
  if (exact) return exact;
  return enabledConfigs.find(item => hasPurpose(item.purpose, purpose)) || null;
};

export const countEnabledConfigsByPurpose = (
  configs: AiServiceConfig[],
  purpose: AiServicePurpose
) =>
  configs.filter(item => !item.isDisabled && hasPurpose(item.purpose, purpose))
    .length;

export const buildAiServiceConfigSummary = (configs: AiServiceConfig[]) => ({
  llmConfig: pickConfigByPurpose(configs, AiServicePurpose.Llm),
  embeddingConfig: pickConfigByPurpose(configs, AiServicePurpose.Embedding),
  llmCount: countEnabledConfigsByPurpose(configs, AiServicePurpose.Llm),
  embeddingCount: countEnabledConfigsByPurpose(
    configs,
    AiServicePurpose.Embedding
  )
});

export const getDefaultPriority = (
  configs: AiServiceConfig[],
  purpose: AiServicePurpose
) => {
  const samePurpose = configs
    .filter(item => item.purpose === purpose)
    .map(item => item.priority ?? 0);
  if (samePurpose.length === 0) return 0;
  return Math.max(...samePurpose) + 1;
};
