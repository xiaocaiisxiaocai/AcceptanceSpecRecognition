import type { AiServiceSelection } from "@/api/ai-service";

export const isRuntimeAiSelectionAvailable = (
  selection: AiServiceSelection
): selection is AiServiceSelection & { serviceId: number } =>
  selection.status === "available" && selection.serviceId != null;

export const getRuntimeAiSelectionServiceId = (
  selection: AiServiceSelection
) =>
  isRuntimeAiSelectionAvailable(selection) ? selection.serviceId : undefined;

export const getRuntimeAiSelectionStatusText = (
  selection: AiServiceSelection,
  purpose: "LLM" | "Embedding"
) => {
  if (isRuntimeAiSelectionAvailable(selection)) {
    return `自动使用 ${selection.name || `${purpose} 服务`}`;
  }

  if (selection.status === "checking") {
    return `正在检测 ${purpose} 服务可用性`;
  }

  return selection.message || `当前没有运行可用的 ${purpose} 服务`;
};
