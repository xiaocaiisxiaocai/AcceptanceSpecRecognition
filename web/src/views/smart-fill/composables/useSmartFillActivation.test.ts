import { describe, expect, it, vi } from "vitest";

const apiMocks = vi.hoisted(() => ({
  getMatchingTaskStatus: vi.fn()
}));
const messageMocks = vi.hoisted(() => ({
  error: vi.fn()
}));

vi.mock("@/api/matching", () => apiMocks);
vi.mock("element-plus", () => ({
  ElMessage: messageMocks
}));

import { useSmartFillActivation } from "./useSmartFillActivation";

const createActions = () => ({
  abortScope: vi.fn(),
  invalidatePreview: vi.fn(),
  stopProgress: vi.fn(),
  stopStream: vi.fn(),
  cancelRecognition: vi.fn(),
  resumeProgress: vi.fn(),
  restoreDownload: vi.fn(),
  invalidateStaleResponse: vi.fn()
});

describe("useSmartFillActivation", () => {
  it("失活时停止当前页面拥有的后台工作", () => {
    const actions = createActions();
    const activation = useSmartFillActivation(actions);

    activation.pauseForDeactivation();

    expect(actions.abortScope).toHaveBeenCalledOnce();
    expect(actions.invalidatePreview).toHaveBeenCalledOnce();
    expect(actions.stopProgress).toHaveBeenCalledOnce();
    expect(actions.stopStream).toHaveBeenCalledOnce();
    expect(actions.cancelRecognition).toHaveBeenCalledOnce();
  });

  it("没有保留任务时激活不查询服务端", async () => {
    const actions = createActions();
    const activation = useSmartFillActivation(actions);

    await activation.reconcileOnActivation(null);

    expect(apiMocks.getMatchingTaskStatus).not.toHaveBeenCalled();
    expect(actions.resumeProgress).not.toHaveBeenCalled();
    expect(actions.restoreDownload).not.toHaveBeenCalled();
  });

  it("完成任务恢复下载能力并停止旧轮询", async () => {
    apiMocks.getMatchingTaskStatus.mockResolvedValueOnce({
      code: 0,
      data: {
        taskId: "completed-task",
        status: "completed",
        canDownload: true,
        updatedAt: "2026-07-27T00:00:00Z"
      }
    });
    const actions = createActions();
    const activation = useSmartFillActivation(actions);

    await activation.reconcileOnActivation("completed-task");

    expect(actions.stopProgress).toHaveBeenCalledOnce();
    expect(actions.restoreDownload).toHaveBeenCalledWith("completed-task");
    expect(actions.resumeProgress).not.toHaveBeenCalled();
  });

  it("运行中任务只恢复状态轮询", async () => {
    apiMocks.getMatchingTaskStatus.mockResolvedValueOnce({
      code: 0,
      data: {
        taskId: "running-task",
        status: "running",
        canDownload: false,
        updatedAt: "2026-07-27T00:00:00Z"
      }
    });
    const actions = createActions();
    const activation = useSmartFillActivation(actions);

    await activation.reconcileOnActivation("running-task");

    expect(actions.resumeProgress).toHaveBeenCalledWith("running-task");
    expect(actions.restoreDownload).not.toHaveBeenCalled();
    expect(actions.invalidateStaleResponse).not.toHaveBeenCalled();
  });

  it("失败任务停止轮询、作废旧响应并显示稳定中文提示", async () => {
    apiMocks.getMatchingTaskStatus.mockResolvedValueOnce({
      code: 0,
      data: {
        taskId: "failed-task",
        status: "failed",
        canDownload: false,
        updatedAt: "2026-07-27T00:00:00Z"
      }
    });
    const actions = createActions();
    const activation = useSmartFillActivation(actions);

    await activation.reconcileOnActivation("failed-task");

    expect(actions.stopProgress).toHaveBeenCalledOnce();
    expect(actions.invalidateStaleResponse).toHaveBeenCalledOnce();
    expect(actions.restoreDownload).not.toHaveBeenCalled();
    expect(messageMocks.error).toHaveBeenCalledWith(
      "任务执行失败，请重新执行填充"
    );
  });
});
