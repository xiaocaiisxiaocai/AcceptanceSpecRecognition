export enum AiServicePurposeValue {
  None = 0,
  Llm = 1,
  Embedding = 2
}

export type AiServiceConfigLike = {
  serviceType: number;
  purpose: number;
  priority: number;
  isDisabled: boolean;
  createdAt: string;
  updatedAt?: string | null;
};

const serviceTypeLabels = new Map<number, string>([
  [0, "OpenAI"],
  [1, "Azure OpenAI"],
  [2, "Ollama"],
  [3, "LM Studio"],
  [4, "OpenAI Compatible"]
]);

const hasPurposeValue = (value: number, flag: AiServicePurposeValue) =>
  (value & flag) === flag;

export const getServiceTypeLabel = (
  value: AiServiceConfigLike["serviceType"]
) => serviceTypeLabels.get(value) || "-";

export const pickConfigByPurpose = <TConfig extends AiServiceConfigLike>(
  configs: TConfig[],
  purpose: AiServicePurposeValue
) => {
  const enabledConfigs = configs.filter(item => !item.isDisabled);
  const exact = enabledConfigs.find(item => item.purpose === purpose);
  if (exact) return exact;
  return (
    enabledConfigs.find(item => hasPurposeValue(item.purpose, purpose)) || null
  );
};

export const countEnabledConfigsByPurpose = (
  configs: AiServiceConfigLike[],
  purpose: AiServicePurposeValue
) =>
  configs.filter(
    item => !item.isDisabled && hasPurposeValue(item.purpose, purpose)
  ).length;

export const buildAiServiceConfigSummary = <
  TConfig extends AiServiceConfigLike
>(
  configs: TConfig[]
) => ({
  llmConfig: pickConfigByPurpose(configs, AiServicePurposeValue.Llm),
  embeddingConfig: pickConfigByPurpose(
    configs,
    AiServicePurposeValue.Embedding
  ),
  llmCount: countEnabledConfigsByPurpose(configs, AiServicePurposeValue.Llm),
  embeddingCount: countEnabledConfigsByPurpose(
    configs,
    AiServicePurposeValue.Embedding
  )
});

export const shouldShowAllConfigsByDefault = (
  configs: AiServiceConfigLike[]
) => {
  const summary = buildAiServiceConfigSummary(configs);
  return summary.llmCount > 1 || summary.embeddingCount > 1;
};

export const getDefaultPriority = (
  configs: AiServiceConfigLike[],
  purpose: number
) => {
  const samePurpose = configs
    .filter(item => item.purpose === purpose)
    .map(item => item.priority ?? 0);
  if (samePurpose.length === 0) return 0;
  return Math.max(...samePurpose) + 1;
};
