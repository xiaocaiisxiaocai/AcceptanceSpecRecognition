import { describe, expect, it, vi } from "vitest";

vi.mock("@/api/ai-service", () => ({
  getAiServiceSelection: vi.fn()
}));

import {
  getRuntimeAiPurposeResult,
  loadRuntimeAiSelectionsSettled,
  waitForRuntimeAiSelection
} from "./runtime-ai-selection-loader";

describe("runtime AI selection loader", () => {
  it("isolates a failed purpose from a healthy purpose", async () => {
    const request = vi.fn(async (purpose: "embedding" | "llm") => {
      if (purpose === "llm") throw new TypeError("network unavailable");
      return {
        code: 0,
        message: "",
        data: { status: "available" as const, serviceId: 7 }
      };
    });

    const results = await loadRuntimeAiSelectionsSettled(
      ["embedding", "llm"],
      undefined,
      request
    );

    expect(getRuntimeAiPurposeResult(results, "embedding")).toMatchObject({
      kind: "success",
      selection: { status: "available", serviceId: 7 }
    });
    expect(getRuntimeAiPurposeResult(results, "llm")).toMatchObject({
      kind: "transient-error",
      selection: { status: "checking" }
    });
  });

  it("treats an application response error as unavailable", async () => {
    const results = await loadRuntimeAiSelectionsSettled(
      ["embedding"],
      undefined,
      vi.fn().mockResolvedValue({ code: 503, message: "not configured" })
    );

    expect(results[0]).toMatchObject({
      kind: "response-error",
      selection: { status: "unavailable", message: "not configured" }
    });
  });

  it("marks an aborted request as cancelled rather than unavailable", async () => {
    const controller = new AbortController();
    controller.abort();
    const error = new DOMException("aborted", "AbortError");
    const results = await loadRuntimeAiSelectionsSettled(
      ["llm"],
      controller.signal,
      vi.fn().mockRejectedValue(error)
    );

    expect(results[0]).toMatchObject({
      kind: "cancelled",
      selection: { status: "checking" }
    });
  });

  it("retries a transient status error before accepting an available service", async () => {
    const request = vi
      .fn()
      .mockRejectedValueOnce(new TypeError("temporary network error"))
      .mockResolvedValueOnce({
        code: 0,
        data: { status: "available", serviceId: 19 }
      });

    await expect(
      waitForRuntimeAiSelection("llm", {
        request,
        retryDelayMs: 0,
        maxAttempts: 2
      })
    ).resolves.toEqual({ status: "available", serviceId: 19 });
    expect(request).toHaveBeenCalledTimes(2);
  });
});
