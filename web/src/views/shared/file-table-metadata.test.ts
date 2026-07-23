import { beforeEach, describe, expect, it, vi } from "vitest";

const apiMocks = vi.hoisted(() => ({
  getFileTables: vi.fn()
}));

vi.mock("@/api/document", () => ({
  getFileTables: apiMocks.getFileTables
}));

import {
  invalidateFileTables,
  loadFileTablesOnce
} from "./file-table-metadata";

describe("file table metadata loading", () => {
  beforeEach(() => {
    invalidateFileTables();
    apiMocks.getFileTables.mockReset();
  });

  it("同一文件并发读取时只发起一个请求", async () => {
    apiMocks.getFileTables.mockResolvedValue({
      code: 0,
      data: [{ index: 0, name: "Sheet1" }]
    });

    const [first, second] = await Promise.all([
      loadFileTablesOnce(41),
      loadFileTablesOnce(41)
    ]);

    expect(apiMocks.getFileTables).toHaveBeenCalledTimes(1);
    expect(first).toBe(second);
  });

  it("失败请求不会进入缓存且允许重试", async () => {
    apiMocks.getFileTables
      .mockRejectedValueOnce(new Error("timeout"))
      .mockResolvedValueOnce({ code: 0, data: [] });

    await expect(loadFileTablesOnce(42)).rejects.toThrow("timeout");
    await expect(loadFileTablesOnce(42, { force: true })).resolves.toEqual([]);
    expect(apiMocks.getFileTables).toHaveBeenCalledTimes(2);
  });
});
