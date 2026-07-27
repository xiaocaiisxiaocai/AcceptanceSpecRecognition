import { beforeEach, describe, expect, it, vi } from "vitest";

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

const deferred = <T>() => {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((onResolve, onReject) => {
    resolve = onResolve;
    reject = onReject;
  });
  return { promise, resolve, reject };
};

const createActions = (getCurrentTaskId = () => null as string | null) => ({
  getCurrentTaskId,
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
  beforeEach(() => {
    apiMocks.getMatchingTaskStatus.mockReset();
    messageMocks.error.mockReset();
  });

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
    const actions = createActions(() => "completed-task");
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
    const actions = createActions(() => "running-task");
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
    const actions = createActions(() => "failed-task");
    const activation = useSmartFillActivation(actions);

    await activation.reconcileOnActivation("failed-task");

    expect(actions.stopProgress).toHaveBeenCalledOnce();
    expect(actions.invalidateStaleResponse).toHaveBeenCalledOnce();
    expect(actions.restoreDownload).not.toHaveBeenCalled();
    expect(messageMocks.error).toHaveBeenCalledWith(
      "任务执行失败，请重新执行填充"
    );
  });

  it("A 运行中响应迟到时不能恢复 A 轮询或改变新任务 B", async () => {
    let currentTaskId: string | null = "task-a";
    const request = deferred<any>();
    apiMocks.getMatchingTaskStatus.mockReturnValueOnce(request.promise);
    const actions = createActions(() => currentTaskId);
    const activation = useSmartFillActivation(actions);
    const pending = activation.reconcileOnActivation("task-a");

    currentTaskId = "task-b";
    activation.cancelReconciliation();
    request.resolve({
      code: 0,
      data: {
        taskId: "task-a",
        status: "running",
        canDownload: false,
        updatedAt: "2026-07-27T00:00:00Z"
      }
    });
    await pending;

    expect(currentTaskId).toBe("task-b");
    expect(actions.resumeProgress).not.toHaveBeenCalled();
    expect(actions.stopProgress).not.toHaveBeenCalled();
    expect(actions.invalidateStaleResponse).not.toHaveBeenCalled();
  });

  it("A 完成响应迟到时不能恢复 A 下载或改变新任务 B", async () => {
    let currentTaskId: string | null = "task-a";
    const request = deferred<any>();
    apiMocks.getMatchingTaskStatus.mockReturnValueOnce(request.promise);
    const actions = createActions(() => currentTaskId);
    const activation = useSmartFillActivation(actions);
    const pending = activation.reconcileOnActivation("task-a");

    currentTaskId = "task-b";
    activation.cancelReconciliation();
    request.resolve({
      code: 0,
      data: {
        taskId: "task-a",
        status: "completed",
        canDownload: true,
        updatedAt: "2026-07-27T00:00:00Z"
      }
    });
    await pending;

    expect(currentTaskId).toBe("task-b");
    expect(actions.abortScope).not.toHaveBeenCalled();
    expect(actions.invalidatePreview).not.toHaveBeenCalled();
    expect(actions.stopProgress).not.toHaveBeenCalled();
    expect(actions.stopStream).not.toHaveBeenCalled();
    expect(actions.cancelRecognition).not.toHaveBeenCalled();
    expect(actions.resumeProgress).not.toHaveBeenCalled();
    expect(actions.restoreDownload).not.toHaveBeenCalled();
    expect(actions.invalidateStaleResponse).not.toHaveBeenCalled();
    expect(messageMocks.error).not.toHaveBeenCalled();
  });

  it("A 失败响应迟到且当前任务已清空时不作废新流程状态", async () => {
    let currentTaskId: string | null = "task-a";
    const request = deferred<any>();
    apiMocks.getMatchingTaskStatus.mockReturnValueOnce(request.promise);
    const actions = createActions(() => currentTaskId);
    const activation = useSmartFillActivation(actions);
    const pending = activation.reconcileOnActivation("task-a");

    currentTaskId = null;
    activation.cancelReconciliation();
    request.resolve({
      code: 0,
      data: {
        taskId: "task-a",
        status: "failed",
        canDownload: false,
        updatedAt: "2026-07-27T00:00:00Z"
      }
    });
    await pending;

    expect(actions.stopProgress).not.toHaveBeenCalled();
    expect(actions.invalidateStaleResponse).not.toHaveBeenCalled();
    expect(messageMocks.error).not.toHaveBeenCalled();
  });

  it("A 状态请求迟到拒绝时不能清空当前任务 B", async () => {
    let currentTaskId: string | null = "task-a";
    const request = deferred<any>();
    apiMocks.getMatchingTaskStatus.mockReturnValueOnce(request.promise);
    const actions = createActions(() => currentTaskId);
    const activation = useSmartFillActivation(actions);
    const pending = activation.reconcileOnActivation("task-a");

    currentTaskId = "task-b";
    activation.cancelReconciliation();
    request.reject(new Error("旧 A 请求失败"));
    await pending;

    expect(currentTaskId).toBe("task-b");
    expect(actions.stopProgress).not.toHaveBeenCalled();
    expect(actions.invalidateStaleResponse).not.toHaveBeenCalled();
    expect(messageMocks.error).not.toHaveBeenCalled();
  });
});
