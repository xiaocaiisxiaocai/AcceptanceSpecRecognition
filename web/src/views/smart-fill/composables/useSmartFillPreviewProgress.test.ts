import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const mocks = vi.hoisted(() => ({ getBatchPreviewProgress: vi.fn() }));
vi.mock("@/api/matching", () => ({
  getBatchPreviewProgress: mocks.getBatchPreviewProgress
}));

import { useSmartFillPreviewProgress } from "./useSmartFillPreviewProgress";

describe("useSmartFillPreviewProgress", () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.stubGlobal("window", {
      setInterval: globalThis.setInterval,
      clearInterval: globalThis.clearInterval,
      setTimeout: globalThis.setTimeout,
      clearTimeout: globalThis.clearTimeout
    });
    mocks.getBatchPreviewProgress.mockReset();
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it("上一轮请求完成前不会发起重叠轮询", async () => {
    mocks.getBatchPreviewProgress.mockReturnValue(new Promise(() => {}));
    const state = useSmartFillPreviewProgress();

    state.startPreviewProgressPolling("request-1", () => true);
    await vi.advanceTimersByTimeAsync(3_000);

    expect(mocks.getBatchPreviewProgress).toHaveBeenCalledTimes(1);
    state.stopPreviewProgressPolling();
  });

  it("停止后忽略已经在途的旧响应", async () => {
    let resolveRequest!: (value: any) => void;
    mocks.getBatchPreviewProgress.mockReturnValue(
      new Promise(resolve => (resolveRequest = resolve))
    );
    const state = useSmartFillPreviewProgress();

    state.startPreviewProgressPolling("request-1", () => true);
    await vi.advanceTimersByTimeAsync(900);
    state.stopPreviewProgressPolling();
    resolveRequest({
      code: 0,
      data: { requestId: "request-1", status: "running", progressPercent: 60 }
    });
    await Promise.resolve();

    expect(state.previewProgress.value?.progressPercent).toBe(1);
  });

  it("主预览请求结束后停止计时和后续轮询", async () => {
    let loading = true;
    mocks.getBatchPreviewProgress.mockResolvedValue({
      code: 0,
      data: { requestId: "request-1", status: "running", progressPercent: 20 }
    });
    const state = useSmartFillPreviewProgress();

    state.startPreviewProgressPolling("request-1", () => loading);
    loading = false;
    await vi.advanceTimersByTimeAsync(900);
    await Promise.resolve();

    expect(state.currentPreviewRequestId.value).toBeNull();
    await vi.advanceTimersByTimeAsync(2_000);
    expect(mocks.getBatchPreviewProgress).toHaveBeenCalledTimes(1);
  });
});
