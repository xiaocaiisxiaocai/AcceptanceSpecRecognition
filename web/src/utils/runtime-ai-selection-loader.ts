import {
  getAiServiceSelection,
  type AiServiceSelection
} from "@/api/ai-service";

export type RuntimeAiPurpose = "embedding" | "llm";

export type RuntimeAiPurposeLoadResult = {
  purpose: RuntimeAiPurpose;
  kind: "success" | "response-error" | "transient-error" | "cancelled";
  selection: AiServiceSelection;
  error?: unknown;
};

export type RuntimeAiSelectionRefreshResult = {
  current: boolean;
  version: number;
  embedding?: AiServiceSelection;
  llm?: AiServiceSelection;
};

type SelectionRequest = typeof getAiServiceSelection;

const isCancellationError = (error: unknown, signal?: AbortSignal) =>
  signal?.aborted === true ||
  (error instanceof Error &&
    (error.name === "CanceledError" || error.name === "AbortError"));

/**
 * Independently resolves every AI purpose so a failed LLM status request never
 * hides a healthy Embedding service (and vice versa).
 */
export const loadRuntimeAiSelectionsSettled = async (
  purposes: readonly RuntimeAiPurpose[],
  signal?: AbortSignal,
  request: SelectionRequest = getAiServiceSelection
) => {
  const settled = await Promise.allSettled(
    purposes.map(purpose => request(purpose, signal))
  );

  return settled.map<RuntimeAiPurposeLoadResult>((result, index) => {
    const purpose = purposes[index];
    if (result.status === "rejected") {
      if (isCancellationError(result.reason, signal)) {
        return {
          purpose,
          kind: "cancelled",
          selection: { status: "checking" },
          error: result.reason
        };
      }
      return {
        purpose,
        kind: "transient-error",
        selection: {
          status: "checking",
          message: `${purpose === "embedding" ? "Embedding" : "LLM"} 服务状态暂时无法确认，正在重试`
        },
        error: result.reason
      };
    }

    if (result.value.code !== 0) {
      return {
        purpose,
        kind: "response-error",
        selection: {
          status: "unavailable",
          message: result.value.message || "AI 服务当前不可用"
        }
      };
    }

    return {
      purpose,
      kind: "success",
      selection: result.value.data
    };
  });
};

export const getRuntimeAiPurposeResult = (
  results: readonly RuntimeAiPurposeLoadResult[],
  purpose: RuntimeAiPurpose
) => results.find(result => result.purpose === purpose);
