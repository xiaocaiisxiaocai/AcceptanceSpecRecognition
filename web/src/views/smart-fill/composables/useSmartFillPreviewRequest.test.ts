import { ref } from "vue";
import { beforeEach, describe, expect, it, vi } from "vitest";

const mocks = vi.hoisted(() => ({
  ensurePermission: vi.fn(() => true),
  warning: vi.fn(),
  error: vi.fn()
}));

vi.mock("@/utils/permission-guard", () => ({
  ensurePermission: mocks.ensurePermission
}));
vi.mock("@/api/matching", () => ({ batchPreviewMatch: vi.fn() }));
vi.mock("element-plus", () => ({
  ElMessage: { warning: mocks.warning, error: mocks.error }
}));

import { useSmartFillPreviewRequest } from "./useSmartFillPreviewRequest";

const oldResults = [{ tableIndex: 0, items: [{ rowIndex: 1 }] }] as any;
const newResults = [{ tableIndex: 0, items: [{ rowIndex: 2 }] }] as any;

function createState(sendPreview: ReturnType<typeof vi.fn>) {
  const batchPreviewResults = ref<any[]>(oldResults);
  const clearPreviewDetail = vi.fn();
  const resetPreviewState = vi.fn();
  const state = useSmartFillPreviewRequest({
    currentStep: ref(3),
    uploadedFile: ref({ fileId: 7 } as any),
    batchTableConfigs: ref([
      {
        selected: true,
        tableIndex: 0,
        regions: []
      }
    ] as any),
    batchPreviewResults,
    matchConfig: ref({ exactMatchOnly: true } as any),
    loading: ref(false),
    taskId: ref("existing-task"),
    lastDownloadFailed: ref(true),
    getScope: () => ({}),
    stopLlmStream: vi.fn(),
    startLlmStream: vi.fn(),
    getEffectiveFilterEmptySourceRows: () => true,
    getPrePreviewBlockingMessage: () => "",
    resetPreviewState,
    markPreviewEmptyResults: vi.fn(),
    resolvePreviewFailure: message => message || "预览失败",
    createPreviewRequestId: () => "request-1",
    startPreviewProgressPolling: vi.fn(),
    stopPreviewProgressPolling: vi.fn(),
    resetPreviewProgress: vi.fn(),
    markPreviewProgressCompleted: vi.fn(),
    getCurrentPreviewRequestId: () => "request-1",
    clearPreviewDetail,
    onSendPreview: sendPreview as any
  });

  return { state, batchPreviewResults, clearPreviewDetail, resetPreviewState };
}

describe("useSmartFillPreviewRequest", () => {
  beforeEach(() => {
    mocks.warning.mockReset();
    mocks.error.mockReset();
  });

  it("重新预览失败时保留上一次成功结果和详情", async () => {
    const {
      state,
      batchPreviewResults,
      clearPreviewDetail,
      resetPreviewState
    } = createState(vi.fn().mockRejectedValue(new Error("network")));

    await state.doPreview();

    expect(batchPreviewResults.value).toEqual(oldResults);
    expect(clearPreviewDetail).not.toHaveBeenCalled();
    expect(resetPreviewState).not.toHaveBeenCalled();
    expect(mocks.error).toHaveBeenCalledOnce();
  });

  it("重新预览成功后才原子替换旧结果", async () => {
    let resolveRequest!: (value: any) => void;
    const request = new Promise(resolve => (resolveRequest = resolve));
    const { state, batchPreviewResults, clearPreviewDetail } = createState(
      vi.fn(() => request)
    );

    const pending = state.doPreview();
    expect(batchPreviewResults.value).toEqual(oldResults);
    expect(clearPreviewDetail).not.toHaveBeenCalled();

    resolveRequest({
      code: 0,
      data: { tables: newResults, totalMatched: 1 }
    });
    await pending;

    expect(batchPreviewResults.value).toEqual(newResults);
    expect(clearPreviewDetail).toHaveBeenCalledOnce();
  });
});
