import { computed, ref } from "vue";
import type { UploadRequestOptions } from "element-plus";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { ApiResponse } from "@/api/customer";
import type { BatchReplyTargetUploadResponse } from "@/api/matching";
import { getBatchReplyTargetTables } from "@/api/matching";
import type {
  BatchReplySourceFileState,
  BatchReplyTargetState
} from "../batch-reply-state";

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
  getBatchReplyTargetTables: vi.fn(),
  uploadBatchReplyTargets: vi.fn()
}));

import { useBatchReplyTargetUploads } from "./useBatchReplyTargetUploads";

describe("useBatchReplyTargetUploads", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("上传完成前取消时不写入目标文件且不弹失败提示", async () => {
    let resolveUpload!: (
      value: ApiResponse<BatchReplyTargetUploadResponse>
    ) => void;
    const controller = new AbortController();
    const targetFiles = ref<BatchReplyTargetState[]>([]);
    const sourceFile = ref<BatchReplySourceFileState | null>({
      sessionId: "session-1",
      sourceFileName: "source.xlsx",
      sourceFileType: 1,
      tableCount: 1
    });
    const uploads = useBatchReplyTargetUploads({
      sourceFile,
      targetFiles,
      sourceSessionId: computed(() => "session-1"),
      targetAccept: computed(() => ".xlsx"),
      selectedSourceTableOptions: computed(() => []),
      activeRootTab: ref("target"),
      onUploadTargets: () =>
        new Promise(resolve => {
          resolveUpload = resolve;
        })
    });

    const pending = uploads.handleTargetFileChange(
      {
        file: new File(["target"], "target.xlsx", { lastModified: 1 })
      } as UploadRequestOptions,
      {
        signal: controller.signal,
        onUploadProgress: vi.fn()
      }
    );
    controller.abort();
    expect(uploads.targetUploading.value).toBe(false);
    resolveUpload({
      code: 0,
      message: "",
      data: {
        sessionId: "session-1",
        files: [
          {
            targetId: "target-1",
            fileName: "target.xlsx",
            fileType: 1,
            tableCount: 1
          }
        ]
      }
    });
    await expect(pending).rejects.toMatchObject({ name: "AbortError" });

    expect(targetFiles.value).toEqual([]);
    expect(messages.error).not.toHaveBeenCalled();
    expect(uploads.targetUploading.value).toBe(false);
  });

  it("重置流程后不接收旧上传请求的成功结果", async () => {
    let resolveUpload!: (
      value: ApiResponse<BatchReplyTargetUploadResponse>
    ) => void;
    const targetFiles = ref<BatchReplyTargetState[]>([]);
    const uploads = useBatchReplyTargetUploads({
      sourceFile: ref<BatchReplySourceFileState | null>({
        sessionId: "session-1",
        sourceFileName: "source.xlsx",
        sourceFileType: 1,
        tableCount: 1
      }),
      targetFiles,
      sourceSessionId: computed(() => "session-1"),
      targetAccept: computed(() => ".xlsx"),
      selectedSourceTableOptions: computed(() => []),
      activeRootTab: ref("target"),
      onUploadTargets: () =>
        new Promise(resolve => {
          resolveUpload = resolve;
        })
    });

    const pending = uploads.handleTargetFileChange(
      {
        file: new File(["target"], "target.xlsx", { lastModified: 1 })
      } as UploadRequestOptions,
      { signal: new AbortController().signal, onUploadProgress: vi.fn() }
    );
    uploads.resetTargetUploadState();
    resolveUpload({
      code: 0,
      message: "",
      data: {
        sessionId: "session-1",
        files: [
          {
            targetId: "target-1",
            fileName: "target.xlsx",
            fileType: 1,
            tableCount: 1
          }
        ]
      }
    });

    await expect(pending).rejects.toMatchObject({ name: "AbortError" });
    expect(targetFiles.value).toEqual([]);
    expect(messages.success).not.toHaveBeenCalled();
    expect(messages.error).not.toHaveBeenCalled();
  });

  it("读取目标表格期间重置时不写入旧目标文件", async () => {
    let resolveTables!: (
      value: Awaited<ReturnType<typeof getBatchReplyTargetTables>>
    ) => void;
    vi.mocked(getBatchReplyTargetTables).mockImplementationOnce(
      () =>
        new Promise(resolve => {
          resolveTables = resolve;
        })
    );
    const targetFiles = ref<BatchReplyTargetState[]>([]);
    const uploads = useBatchReplyTargetUploads({
      sourceFile: ref<BatchReplySourceFileState | null>({
        sessionId: "session-1",
        sourceFileName: "source.xlsx",
        sourceFileType: 1,
        tableCount: 1
      }),
      targetFiles,
      sourceSessionId: computed(() => "session-1"),
      targetAccept: computed(() => ".xlsx"),
      selectedSourceTableOptions: computed(() => []),
      activeRootTab: ref("target"),
      onUploadTargets: async () => ({
        code: 0,
        message: "",
        data: {
          sessionId: "session-1",
          files: [
            {
              targetId: "target-1",
              fileName: "target.xlsx",
              fileType: 1,
              tableCount: 1
            }
          ]
        }
      })
    });

    const pending = uploads.handleTargetFileChange(
      {
        file: new File(["target"], "target.xlsx", { lastModified: 1 })
      } as UploadRequestOptions,
      { signal: new AbortController().signal, onUploadProgress: vi.fn() }
    );
    await vi.waitFor(() =>
      expect(getBatchReplyTargetTables).toHaveBeenCalledTimes(1)
    );
    uploads.resetTargetUploadState();
    resolveTables({ code: 0, message: "", data: [] });

    await expect(pending).rejects.toMatchObject({ name: "AbortError" });
    expect(targetFiles.value).toEqual([]);
    expect(messages.success).not.toHaveBeenCalled();
    expect(messages.error).not.toHaveBeenCalled();
  });
});
