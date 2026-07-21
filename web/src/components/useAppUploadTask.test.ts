import { describe, expect, it, vi } from "vitest";
import type { AxiosProgressEvent } from "axios";
import type { UploadRequestOptions } from "element-plus";
import { useAppUploadTask } from "./useAppUploadTask";

const uploadOptions = {
  file: new File(["content"], "sample.xlsx")
} as UploadRequestOptions;

describe("useAppUploadTask", () => {
  it("以真实进度区分上传与服务端处理阶段", async () => {
    let resolveRequest!: () => void;
    let reportProgress!: (event: AxiosProgressEvent) => void;
    const request = vi.fn(
      (_options, context) =>
        new Promise<void>(resolve => {
          resolveRequest = resolve;
          reportProgress = context.onUploadProgress;
        })
    );
    const task = useAppUploadTask(request);

    const pending = task.execute(uploadOptions);
    reportProgress({ loaded: 25, total: 100 } as AxiosProgressEvent);
    expect(task.phase.value).toBe("uploading");
    expect(task.progressPercent.value).toBe(25);
    expect(task.progressText.value).toBe("25%");

    reportProgress({ loaded: 100, total: 100 } as AxiosProgressEvent);
    expect(task.phase.value).toBe("processing");

    resolveRequest();
    await pending;
    expect(task.phase.value).toBe("success");
  });

  it("总长度未知时显示已上传字节", async () => {
    let reportProgress!: (event: AxiosProgressEvent) => void;
    const task = useAppUploadTask(async (_options, context) => {
      reportProgress = context.onUploadProgress;
      context.onUploadProgress({ loaded: 1536 } as AxiosProgressEvent);
    });

    await task.execute(uploadOptions);

    expect(reportProgress).toBeTypeOf("function");
    expect(task.progressText.value).toBe("已上传 1.5 KB");
    expect(task.phase.value).toBe("success");
  });

  it("主动取消会中止信号、恢复空闲且不显示失败", async () => {
    let requestSignal!: AbortSignal;
    const task = useAppUploadTask(
      (_options, context) =>
        new Promise<void>((_resolve, reject) => {
          requestSignal = context.signal;
          context.signal.addEventListener("abort", () => {
            reject(new DOMException("上传已取消", "AbortError"));
          });
        })
    );

    const pending = task.execute(uploadOptions);
    task.cancel();
    await pending;

    expect(requestSignal.aborted).toBe(true);
    expect(task.phase.value).toBe("idle");
    expect(task.errorMessage.value).toBe("");
  });
});
