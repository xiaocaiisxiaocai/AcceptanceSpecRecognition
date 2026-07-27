import { computed, ref } from "vue";
import { beforeEach, describe, expect, it, vi } from "vitest";
import {
  downloadBatchReplyResult,
  executeBatchReply,
  type BatchReplyExecuteResponse
} from "@/api/matching";
import type { BatchReplyTableConfigItem } from "../batch-reply-table-config";
import type { BatchReplyTargetState } from "../batch-reply-state";

const browserDownloads = vi.hoisted(() => ({
  trigger: vi.fn()
}));
const messages = vi.hoisted(() => ({
  error: vi.fn(),
  success: vi.fn(),
  warning: vi.fn()
}));
vi.mock("element-plus", () => ({ ElMessage: messages }));
vi.mock("@/utils/permission-guard", () => ({
  ensurePermission: vi.fn(() => true)
}));
vi.mock("@/api/matching", () => ({
  downloadBatchReplyResult: vi.fn(),
  executeBatchReply: vi.fn()
}));
vi.mock("../batch-reply-execution", async importOriginal => {
  const actual =
    await importOriginal<typeof import("../batch-reply-execution")>();
  return {
    ...actual,
    triggerBrowserDownload: browserDownloads.trigger
  };
});

import { useBatchReplyExecution } from "./useBatchReplyExecution";

const deferred = <T>() => {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(complete => {
    resolve = complete;
  });
  return { promise, resolve };
};

const tableInfo = {
  index: 0,
  name: "Sheet1",
  rowCount: 3,
  columnCount: 4,
  isNested: false,
  headers: ["项目", "规格", "验收", "备注"],
  hasMergedCells: false
};

const tableConfig: BatchReplyTableConfigItem = {
  tableIndex: 0,
  sourceTableIndex: 0,
  projectColumnIndex: 0,
  specificationColumnIndex: 1,
  acceptanceColumnIndex: 2,
  remarkColumnIndex: 3,
  headerRowStart: 1,
  headerRowCount: 1,
  dataStartRow: 2,
  filterEmptySourceRows: true,
  duplicateResolutions: [],
  selected: true,
  tableInfo
};

const executeResponse: BatchReplyExecuteResponse = {
  taskId: "task-1",
  successCount: 1,
  failedCount: 0,
  downloadUrl: "/download/task-1",
  downloadFileName: "批量回复结果.zip",
  files: [
    {
      targetId: "target-1",
      fileName: "target.xlsx",
      success: true,
      message: "处理成功"
    }
  ]
};

const target: BatchReplyTargetState = {
  targetId: "target-1",
  fileName: "target.xlsx",
  fileType: 1,
  tableCount: 1,
  size: 128,
  signature: "target.xlsx:128:1",
  tables: [tableInfo],
  configs: [tableConfig],
  previewLoadingTableIndexes: [],
  previewResults: {
    0: {
      targetId: "target-1",
      fileName: "target.xlsx",
      tableIndex: 0,
      sourceTableIndex: 0,
      canApply: true,
      errors: [],
      rows: [],
      duplicateGroups: []
    }
  }
};

describe("useBatchReplyExecution", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("执行成功但下载失败时保留 taskId 并只报告下载失败", async () => {
    vi.mocked(executeBatchReply).mockResolvedValue({
      code: 0,
      message: "",
      data: executeResponse
    });
    vi.mocked(downloadBatchReplyResult).mockRejectedValue(
      new Error("download failed")
    );
    const activeRootTab = ref("target");
    const execution = useBatchReplyExecution({
      sourceSessionId: computed(() => "session-1"),
      selectedSourceConfigs: computed(() => [tableConfig]),
      executableTargets: computed(() => [target]),
      activeRootTab
    });

    await execution.executeReadyTargets();

    expect(execution.executeResult.value?.taskId).toBe("task-1");
    expect(activeRootTab.value).toBe("result");
    expect(execution.downloadError.value).toBe(
      "批量回复已执行成功，但结果下载失败，请重试下载"
    );
    expect(messages.error).not.toHaveBeenCalled();
  });

  it("自动下载进行中重复重试和再次执行都不会产生第二个请求", async () => {
    const pendingDownload = deferred<Blob>();
    vi.mocked(executeBatchReply).mockResolvedValue({
      code: 0,
      message: "",
      data: executeResponse
    });
    vi.mocked(downloadBatchReplyResult).mockReturnValue(
      pendingDownload.promise
    );
    const execution = useBatchReplyExecution({
      sourceSessionId: computed(() => "session-1"),
      selectedSourceConfigs: computed(() => [tableConfig]),
      executableTargets: computed(() => [target]),
      activeRootTab: ref("target")
    });

    const firstExecution = execution.executeReadyTargets();
    await vi.waitFor(() =>
      expect(downloadBatchReplyResult).toHaveBeenCalledTimes(1)
    );
    expect(execution.downloadLoading.value).toBe(true);

    const duplicateActions = [
      execution.retryDownload(),
      execution.retryDownload(),
      execution.executeReadyTargets()
    ];

    pendingDownload.resolve(new Blob(["result"]));
    await Promise.all([firstExecution, ...duplicateActions]);

    expect(downloadBatchReplyResult).toHaveBeenCalledTimes(1);
    expect(executeBatchReply).toHaveBeenCalledTimes(1);
    expect(browserDownloads.trigger).toHaveBeenCalledTimes(1);
    expect(execution.downloadLoading.value).toBe(false);
  });
});
