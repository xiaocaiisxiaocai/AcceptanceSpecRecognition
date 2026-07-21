import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createAiSelectionRetryController } from "./ai-selection-retry";

describe("AI selection checking retry", () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it("retries checking and stops after the refreshed selection is available", async () => {
    const refresh = vi.fn();
    const retry = createAiSelectionRetryController({ refresh });

    retry.schedule([{ status: "checking" }]);
    await vi.advanceTimersByTimeAsync(1500);
    expect(refresh).toHaveBeenCalledTimes(1);

    retry.schedule([{ status: "available", serviceId: 5 }]);
    await vi.advanceTimersByTimeAsync(1500);
    expect(refresh).toHaveBeenCalledTimes(1);
    expect(retry.attempts).toBe(0);
  });

  it("caps repeated checking refreshes", async () => {
    const refresh = vi.fn();
    const retry = createAiSelectionRetryController({
      refresh,
      delayMs: 10,
      maxAttempts: 2
    });

    retry.schedule([{ status: "checking" }]);
    await vi.advanceTimersByTimeAsync(10);
    retry.schedule([{ status: "checking" }]);
    await vi.advanceTimersByTimeAsync(10);
    retry.schedule([{ status: "checking" }]);
    await vi.advanceTimersByTimeAsync(10);

    expect(refresh).toHaveBeenCalledTimes(2);
    expect(retry.attempts).toBe(2);
  });

  it("cancels a pending retry", async () => {
    const refresh = vi.fn();
    const retry = createAiSelectionRetryController({ refresh });

    retry.schedule([{ status: "checking" }]);
    retry.cancel();
    await vi.advanceTimersByTimeAsync(1500);

    expect(refresh).not.toHaveBeenCalled();
    expect(retry.attempts).toBe(0);
  });
});
