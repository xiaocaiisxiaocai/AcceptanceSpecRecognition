const normalizeDisplayValue = (value?: string | null) =>
  value?.trim().toLocaleLowerCase() ?? "";

export const getDistinctAiServiceModel = (
  serviceName?: string | null,
  modelName?: string | null
) => {
  const trimmedModelName = modelName?.trim() ?? "";
  if (
    !trimmedModelName ||
    normalizeDisplayValue(serviceName) === normalizeDisplayValue(modelName)
  ) {
    return "";
  }

  return trimmedModelName;
};
