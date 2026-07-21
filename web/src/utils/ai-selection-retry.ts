import type { AiServiceSelection } from "@/api/ai-service";

type RetryTimer = ReturnType<typeof globalThis.setTimeout>;

interface AiSelectionRetryOptions {
  refresh: () => void | Promise<void>;
  delayMs?: number;
  maxAttempts?: number;
}

export const createAiSelectionRetryController = ({
  refresh,
  delayMs = 1500,
  maxAttempts = 10
}: AiSelectionRetryOptions) => {
  let timer: RetryTimer | undefined;
  let attempts = 0;

  const clearTimer = () => {
    if (timer === undefined) return;
    globalThis.clearTimeout(timer);
    timer = undefined;
  };

  const schedule = (selections: readonly AiServiceSelection[]) => {
    clearTimer();
    if (!selections.some(selection => selection.status === "checking")) {
      attempts = 0;
      return;
    }
    if (attempts >= maxAttempts) return;

    attempts += 1;
    timer = globalThis.setTimeout(() => {
      timer = undefined;
      void refresh();
    }, delayMs);
  };

  const cancel = () => {
    clearTimer();
    attempts = 0;
  };

  return {
    schedule,
    cancel,
    get attempts() {
      return attempts;
    }
  };
};
