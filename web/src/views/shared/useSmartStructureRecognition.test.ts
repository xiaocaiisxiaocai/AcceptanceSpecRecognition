import { describe, expect, it, vi } from "vitest";
import type { SmartConfigRecognizeResult } from "@/api/smart-config";

const apiMocks = vi.hoisted(() => ({
  recognizeSmartConfig: vi.fn(),
  confirmSmartConfig: vi.fn()
}));

vi.mock("@/api/smart-config", () => apiMocks);
vi.mock("element-plus", () => ({
  ElMessage: {
    error: vi.fn(),
    warning: vi.fn(),
    success: vi.fn()
  }
}));

import { useSmartStructureRecognition } from "./useSmartStructureRecognition";

const deferred = <T>() => {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((onResolve, onReject) => {
    resolve = onResolve;
    reject = onReject;
  });
  return { promise, resolve, reject };
};

const result = (
  fileId: number,
  tableName: string
): SmartConfigRecognizeResult => ({
  fileId,
  tables: [
    {
      tableIndex: 0,
      tableName,
      headers: ["项目", "规格", "验收", "备注"],
      headerRowIndex: 0,
      headerRowCount: 1,
      dataStartRowIndex: 1,
      projectColumnIndex: 0,
      specificationColumnIndex: 1,
      acceptanceColumnIndex: 2,
      remarkColumnIndex: 3,
      isSpecificationOnly: false,
      confidence: 0.9,
      source: "Rule",
      decision: "AutoApply",
      fields: []
    }
  ]
});

describe("useSmartStructureRecognition", () => {
  it("reset 后发起新文件识别时忽略更晚返回的旧请求", async () => {
    const requestA = deferred<any>();
    const requestB = deferred<any>();
    apiMocks.recognizeSmartConfig
      .mockReset()
      .mockReturnValueOnce(requestA.promise)
      .mockReturnValueOnce(requestB.promise);
    const state = useSmartStructureRecognition();

    const pendingA = state.recognize(10, 1);
    state.reset();
    const pendingB = state.recognize(20, 1);

    requestB.resolve({ code: 0, data: result(20, "B文件") });
    await pendingB;
    requestA.resolve({ code: 0, data: result(10, "A文件") });

    expect(await pendingA).toBeNull();
    expect(state.recognitionResult.value?.fileId).toBe(20);
    expect(state.recognizedTables.value[0]?.tableName).toBe("B文件");
  });

  it("新识别开始和失败时清空旧结果", async () => {
    const requestB = deferred<any>();
    apiMocks.recognizeSmartConfig
      .mockReset()
      .mockResolvedValueOnce({ code: 0, data: result(10, "旧文件") })
      .mockReturnValueOnce(requestB.promise);
    const state = useSmartStructureRecognition();

    await state.recognize(10, 1);
    const pendingB = state.recognize(20, 1);

    expect(state.recognizedTables.value).toEqual([]);
    requestB.reject(new Error("识别失败"));
    await pendingB;
    expect(state.recognizedTables.value).toEqual([]);
    expect(state.recognitionError.value).toBe("识别失败");
  });
});
