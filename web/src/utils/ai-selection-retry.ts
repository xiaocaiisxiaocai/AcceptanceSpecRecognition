import type { AiServiceSelection } from "@/api/ai-service";

type RetryTimer = ReturnType<typeof globalThis.setTimeout>;

interface AiSelectionRetryOptions {
  refresh: () => void | Promise<void>;
  delayMs?: number;
  maxAttempts?: number;
  retryStatuses?: readonly AiServiceSelection["status"][];
  delayMsByStatus?: Partial<Record<AiServiceSelection["status"], number>>;
}

export const createAiSelectionRetryController = ({
  refresh,
  delayMs = 1500,
  maxAttempts = 10,
  retryStatuses = ["checking"],
  delayMsByStatus = {}
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
    const retryableSelection = selections.find(selection =>
      retryStatuses.includes(selection.status)
    );
    if (!retryableSelection) {
      attempts = 0;
      return;
    }
    if (attempts >= maxAttempts) return;

    attempts += 1;
    const retryDelayMs = delayMsByStatus[retryableSelection.status] ?? delayMs;
    timer = globalThis.setTimeout(() => {
      timer = undefined;
      void refresh();
    }, retryDelayMs);
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
