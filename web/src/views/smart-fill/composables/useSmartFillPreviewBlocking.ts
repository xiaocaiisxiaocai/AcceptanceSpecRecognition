import { computed, ref, toValue, type MaybeRefOrGetter } from "vue";
import type { MatchConfig } from "@/api/matching";

type SmartFillPreviewBlockingState =
  | "none"
  | "noScopeCandidates"
  | "embeddingUnavailable"
  | "emptyResults";

type MatchConfigServiceStatus = {
  hasAvailableEmbeddingService: boolean;
  hasAvailableLlmService: boolean;
};

interface UseSmartFillPreviewBlockingOptions {
  matchConfig: MaybeRefOrGetter<MatchConfig>;
  getMatchConfigServiceStatus: () => MatchConfigServiceStatus;
}

export function useSmartFillPreviewBlocking({
  matchConfig,
  getMatchConfigServiceStatus
}: UseSmartFillPreviewBlockingOptions) {
  const previewState = ref<SmartFillPreviewBlockingState>("none");
  const previewFailureDetail = ref("");

  const getPrePreviewBlockingMessage = () => {
    if (toValue(matchConfig).exactMatchOnly) {
      return "";
    }

    const { hasAvailableEmbeddingService } = getMatchConfigServiceStatus();
    if (!hasAvailableEmbeddingService) {
      return "请先配置可用的 Embedding 服务";
    }

    return "";
  };

  const previewBlockingMessage = computed(() => {
    const prePreviewMessage = getPrePreviewBlockingMessage();
    if (prePreviewMessage) {
      return prePreviewMessage;
    }

    if (
      toValue(matchConfig).exactMatchOnly &&
      previewState.value === "embeddingUnavailable"
    ) {
      return "";
    }

    switch (previewState.value) {
      case "noScopeCandidates":
        return "当前范围内没有可用于匹配的验收规格";
      case "embeddingUnavailable":
        return "请先配置可用的 Embedding 服务";
      case "emptyResults":
        return "未找到可匹配的数据";
      default:
        return "";
    }
  });

  const previewBlockingHint = computed(() => {
    switch (previewState.value) {
      case "noScopeCandidates":
        return "请调整客户、制程、机型范围，或先补充对应验收规格。";
      case "embeddingUnavailable":
        return (
          previewFailureDetail.value || "当前未检测到可用的 Embedding 服务。"
        );
      case "emptyResults":
        return "当前表格没有命中可匹配结果，请检查源项目/规格列是否选择正确。";
      default:
        return getPrePreviewBlockingMessage()
          ? "请前往 AI 服务配置启用至少一个带 Embedding 模型的服务。"
          : "";
    }
  });

  const resetPreviewState = () => {
    previewState.value = "none";
    previewFailureDetail.value = "";
  };

  const markPreviewEmptyResults = () => {
    previewState.value = "emptyResults";
    previewFailureDetail.value = "未找到可匹配的数据";
  };

  const resolvePreviewFailure = (message?: string) => {
    const normalizedMessage = (message || "").trim();

    if (normalizedMessage.includes("范围内无候选数据")) {
      previewState.value = "noScopeCandidates";
      previewFailureDetail.value = normalizedMessage || "范围内无候选数据";
      return normalizedMessage || "范围内无候选数据";
    }

    if (normalizedMessage.includes("Embedding 服务不可用")) {
      previewState.value = "embeddingUnavailable";
      previewFailureDetail.value = normalizedMessage || "Embedding 服务不可用";
      return normalizedMessage || "Embedding 服务不可用";
    }

    previewState.value = "none";
    previewFailureDetail.value = normalizedMessage;
    return normalizedMessage || "匹配预览失败";
  };

  return {
    previewState,
    previewFailureDetail,
    previewBlockingMessage,
    previewBlockingHint,
    getPrePreviewBlockingMessage,
    resetPreviewState,
    markPreviewEmptyResults,
    resolvePreviewFailure
  };
}
