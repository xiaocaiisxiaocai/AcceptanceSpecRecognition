import { beforeEach, describe, expect, it, vi } from "vitest";

const request = vi.hoisted(() => vi.fn());

vi.mock("@/utils/http", () => ({
  http: { request },
  authorizedFetch: vi.fn()
}));

import { uploadFile } from "./document";
import { uploadBatchReplySource, uploadBatchReplyTargets } from "./matching";

describe("上传 API 传输控制", () => {
  beforeEach(() => {
    request.mockReset();
    request.mockResolvedValue({ code: 0, data: {} });
  });

  it.each([
    ["文档", () => uploadFile(new File(["a"], "a.xlsx"), transport)],
    [
      "批量回复来源",
      () => uploadBatchReplySource(new File(["a"], "a.xlsx"), transport)
    ],
    [
      "批量回复目标",
      () =>
        uploadBatchReplyTargets(
          "session-1",
          [new File(["b"], "b.xlsx")],
          transport
        )
    ]
  ])("%s 上传会透传 signal 与 onUploadProgress", async (_name, invoke) => {
    await invoke();

    const config = request.mock.calls[0]?.[2];
    expect(config.signal).toBe(transport.signal);
    expect(config.onUploadProgress).toBe(transport.onUploadProgress);
  });
});

const transport = {
  signal: new AbortController().signal,
  onUploadProgress: vi.fn()
};
