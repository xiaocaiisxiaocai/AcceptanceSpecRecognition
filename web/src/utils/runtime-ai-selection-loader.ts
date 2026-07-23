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

type RuntimeAiSelectionWaitOptions = {
  signal?: AbortSignal;
  request?: SelectionRequest;
  retryDelayMs?: number;
  maxAttempts?: number;
};

const isCancellationError = (error: unknown, signal?: AbortSignal) =>
  signal?.aborted === true ||
  (error instanceof Error &&
    (error.name === "CanceledError" || error.name === "AbortError"));

const waitForRetry = (delayMs: number, signal?: AbortSignal) =>
  new Promise<void>((resolve, reject) => {
    if (signal?.aborted) {
      const error = new Error("AI 服务状态检测已取消");
      error.name = "AbortError";
      reject(error);
      return;
    }

    const timer = globalThis.setTimeout(() => {
      signal?.removeEventListener("abort", handleAbort);
      resolve();
    }, delayMs);
    const handleAbort = () => {
      globalThis.clearTimeout(timer);
      signal?.removeEventListener("abort", handleAbort);
      const error = new Error("AI 服务状态检测已取消");
      error.name = "AbortError";
      reject(error);
    };
    signal?.addEventListener("abort", handleAbort, { once: true });
  });

/**
 * 在用户已经开启 AI 辅助时，为短暂的 checking 状态留出有限恢复窗口。
 * 该等待只用于即将发起的业务动作，避免后台探测刚启动就误降级为规则识别。
 */
export const waitForRuntimeAiSelection = async (
  purpose: RuntimeAiPurpose,
  {
    signal,
    request = getAiServiceSelection,
    retryDelayMs = 350,
    maxAttempts = 6
  }: RuntimeAiSelectionWaitOptions = {}
) => {
  let lastSelection: AiServiceSelection = { status: "checking" };

  for (let attempt = 0; attempt < Math.max(1, maxAttempts); attempt += 1) {
    let response: Awaited<ReturnType<SelectionRequest>>;
    try {
      response = await request(purpose, signal);
    } catch (error) {
      if (isCancellationError(error, signal)) throw error;
      if (attempt >= Math.max(1, maxAttempts) - 1) throw error;
      await waitForRetry(retryDelayMs, signal);
      continue;
    }
    if (response.code !== 0) {
      return {
        status: "unavailable",
        message: response.message || "AI 服务当前不可用"
      } satisfies AiServiceSelection;
    }

    lastSelection = response.data;
    if (lastSelection.status !== "checking") return lastSelection;
    if (attempt < Math.max(1, maxAttempts) - 1) {
      await waitForRetry(retryDelayMs, signal);
    }
  }

  return lastSelection;
};

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
