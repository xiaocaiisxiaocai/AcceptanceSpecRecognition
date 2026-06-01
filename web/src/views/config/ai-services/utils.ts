import { AiServicePurpose } from "@/api/ai-service";

export const hasPurpose = (value: number, flag: AiServicePurpose) =>
  (value & flag) === flag;

export const setRowLoading = (
  state: Record<string, boolean>,
  id: string | number,
  value: boolean
) => {
  if (id === null || id === undefined || id === "") return;
  state[String(id)] = value;
};

export const isRowLoading = (
  state: Record<string, boolean>,
  id?: string | number | null
) => {
  if (id === null || id === undefined || id === "") return false;
  return !!state[String(id)];
};

export const formatValue = (value?: string | number | null) => {
  if (value === null || value === undefined || value === "") return "-";
  return String(value);
};

export const normalizePurpose = (value: number) => {
  if (value === AiServicePurpose.Llm || value === AiServicePurpose.Embedding) {
    return value;
  }
  if (value === AiServicePurpose.None) return AiServicePurpose.Llm;
  return value;
};

export const formatPurpose = (purpose: number) => {
  const labels: string[] = [];
  if (hasPurpose(purpose, AiServicePurpose.Llm)) labels.push("LLM");
  if (hasPurpose(purpose, AiServicePurpose.Embedding)) labels.push("Embedding");
  return labels.length ? labels.join(" / ") : "-";
};
