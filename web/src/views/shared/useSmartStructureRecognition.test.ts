import { describe, expect, it, vi } from "vitest";
import type { SmartConfigRecognizeResult } from "@/api/smart-config";

const apiMocks = vi.hoisted(() => ({
  recognizeSmartConfig: vi.fn(),
  confirmSmartConfig: vi.fn()
}));
const aiServiceMocks = vi.hoisted(() => ({
  getAiServiceSelection: vi.fn()
}));
const messageMocks = vi.hoisted(() => ({
  error: vi.fn(),
  warning: vi.fn(),
  success: vi.fn()
}));

vi.mock("@/api/smart-config", () => apiMocks);
vi.mock("@/api/ai-service", () => aiServiceMocks);
vi.mock("element-plus", () => ({
  ElMessage: messageMocks
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
  it("识别前刷新运行状态，不把过期 LLM 服务继续提交给后端", async () => {
    aiServiceMocks.getAiServiceSelection.mockResolvedValue({
      code: 0,
      data: { status: "unavailable", message: "暂不可用" }
    });
    apiMocks.recognizeSmartConfig.mockResolvedValue({
      code: 0,
      data: result(9, "规则识别")
    });
    const state = useSmartStructureRecognition();

    await state.recognize(9, 1, {
      enableLlmAssistance: true,
      llmServiceId: 99
    });

    expect(aiServiceMocks.getAiServiceSelection).toHaveBeenCalledWith("llm");
    expect(apiMocks.recognizeSmartConfig).toHaveBeenCalledWith(
      expect.objectContaining({
        enableLlmAssistance: false,
        llmServiceId: undefined
      })
    );
    expect(messageMocks.warning).toHaveBeenCalledWith(
      "AI 服务尚未就绪，本次先使用规则识别"
    );
  });

  it("reset 后发起新文件识别时忽略更晚返回的旧请求", async () => {
    const requestA = deferred<any>();
    const requestB = deferred<any>();
    apiMocks.recognizeSmartConfig
      .mockReset()
      .mockReturnValueOnce(requestA.promise)
      .mockReturnValueOnce(requestB.promise);
    const state = useSmartStructureRecognition();

    const pendingA = state.recognize(10, 1);
    expect(state.recognitionAttempted.value).toBe(false);
    state.reset();
    const pendingB = state.recognize(20, 1);

    requestB.resolve({ code: 0, data: result(20, "B文件") });
    await pendingB;
    requestA.resolve({ code: 0, data: result(10, "A文件") });

    expect(await pendingA).toBeNull();
    expect(state.recognitionResult.value?.fileId).toBe(20);
    expect(state.recognizedTables.value[0]?.tableName).toBe("B文件");
    expect(state.recognitionAttempted.value).toBe(true);
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
    expect(state.recognitionAttempted.value).toBe(true);
  });

  it("reset 换文件后忽略旧文件更晚返回的确认结果", async () => {
    const requestA = deferred<any>();
    apiMocks.recognizeSmartConfig
      .mockReset()
      .mockResolvedValueOnce({ code: 0, data: result(10, "A文件") })
      .mockResolvedValueOnce({ code: 0, data: result(20, "B文件") });
    apiMocks.confirmSmartConfig
      .mockReset()
      .mockReturnValueOnce(requestA.promise);
    messageMocks.success.mockClear();
    const state = useSmartStructureRecognition();

    await state.recognize(10, 1);
    const pendingConfirm = state.confirm({
      customerId: 1,
      fileId: 10,
      tableIndex: 0,
      headers: ["项目", "规格", "验收", "备注"],
      projectColumnIndex: 0,
      specificationColumnIndex: 1,
      acceptanceColumnIndex: 2,
      remarkColumnIndex: 3,
      headerRowIndex: 0,
      headerRowCount: 1,
      dataStartRowIndex: 1,
      isSpecificationOnly: false,
      learnedColumns: []
    });

    state.reset();
    await state.recognize(20, 1);
    requestA.resolve({
      code: 0,
      data: {
        templateSaved: true,
        templateId: 1,
        learnedRuleCount: 1,
        promotedGlobalRuleCount: 0,
        learningSucceeded: true
      }
    });

    expect(await pendingConfirm).toBeNull();
    expect(state.lastConfirmResult.value).toBeNull();
    expect(state.recognitionResult.value?.fileId).toBe(20);
    expect(state.confirmingTableIndex.value).toBeNull();
    expect(messageMocks.success).not.toHaveBeenCalled();
  });

  it("客户变化后拒绝使用旧客户的识别上下文确认", async () => {
    apiMocks.recognizeSmartConfig
      .mockReset()
      .mockResolvedValueOnce({ code: 0, data: result(10, "A客户") });
    apiMocks.confirmSmartConfig.mockReset();
    messageMocks.warning.mockClear();
    const state = useSmartStructureRecognition();

    await state.recognize(10, 1);
    const confirmed = await state.confirm({
      customerId: 2,
      fileId: 10,
      tableIndex: 0,
      headers: ["项目", "规格", "验收", "备注"],
      projectColumnIndex: 0,
      specificationColumnIndex: 1,
      acceptanceColumnIndex: 2,
      remarkColumnIndex: 3,
      headerRowIndex: 0,
      headerRowCount: 1,
      dataStartRowIndex: 1,
      isSpecificationOnly: false,
      learnedColumns: []
    });

    expect(confirmed).toBeNull();
    expect(apiMocks.confirmSmartConfig).not.toHaveBeenCalled();
    expect(messageMocks.warning).toHaveBeenCalledWith(
      "客户已变更，请重新识别后再确认结构"
    );
  });

  it("确认期间串行化其他表格请求，避免请求结果互相取消", async () => {
    const requestA = deferred<any>();
    apiMocks.recognizeSmartConfig
      .mockReset()
      .mockResolvedValueOnce({ code: 0, data: result(10, "多表") });
    apiMocks.confirmSmartConfig
      .mockReset()
      .mockReturnValueOnce(requestA.promise);
    messageMocks.warning.mockClear();
    const state = useSmartStructureRecognition();
    await state.recognize(10, 1);

    const baseRequest = {
      customerId: 1,
      fileId: 10,
      headers: ["项目", "规格", "验收", "备注"],
      projectColumnIndex: 0,
      specificationColumnIndex: 1,
      acceptanceColumnIndex: 2,
      remarkColumnIndex: 3,
      headerRowIndex: 0,
      headerRowCount: 1,
      dataStartRowIndex: 1,
      isSpecificationOnly: false,
      learnedColumns: []
    };
    const pendingA = state.confirm({ ...baseRequest, tableIndex: 0 });
    const blockedB = await state.confirm({ ...baseRequest, tableIndex: 1 });

    expect(blockedB).toBeNull();
    expect(apiMocks.confirmSmartConfig).toHaveBeenCalledTimes(1);
    expect(messageMocks.warning).toHaveBeenCalledWith(
      "正在确认其他表格，请稍候"
    );

    requestA.resolve({
      code: 0,
      data: {
        templateSaved: true,
        templateId: 1,
        learnedRuleCount: 0,
        promotedGlobalRuleCount: 0,
        learningSucceeded: true
      }
    });
    await expect(pendingA).resolves.toMatchObject({ templateSaved: true });
    expect(state.confirmingTableIndex.value).toBeNull();
  });
});
