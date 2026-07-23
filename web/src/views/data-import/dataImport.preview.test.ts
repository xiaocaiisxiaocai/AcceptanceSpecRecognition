import { describe, expect, it, vi } from "vitest";
import {
  DATA_IMPORT_PREVIEW_WINDOW_ROWS,
  loadBoundedFullTablePreview
} from "./dataImport.preview";
import type { TableData } from "@/api/document";

const createWindow = (
  rowOffset: number,
  rowCount: number,
  totalRows: number
): TableData => ({
  tableIndex: 0,
  headers: ["项目", "规格"],
  rows: Array.from({ length: rowCount }, (_, index) => [
    `row-${rowOffset + index}`
  ]),
  structuredRows: Array.from({ length: rowCount }, () => []),
  totalRows,
  columnCount: 2,
  rowOffset,
  columnOffset: 0,
  totalColumns: 2
});

describe("data import bounded full preview", () => {
  it("使用正数有界窗口分页加载并合并全部行", async () => {
    const loadWindow = vi.fn(
      async ({
        rowOffset,
        previewRows
      }: {
        rowOffset: number;
        previewRows: number;
      }) =>
        createWindow(rowOffset, Math.min(previewRows, 1200 - rowOffset), 1200)
    );

    const result = await loadBoundedFullTablePreview({ loadWindow });

    expect(loadWindow.mock.calls.map(([request]) => request)).toEqual([
      { rowOffset: 0, previewRows: DATA_IMPORT_PREVIEW_WINDOW_ROWS },
      { rowOffset: 500, previewRows: DATA_IMPORT_PREVIEW_WINDOW_ROWS },
      { rowOffset: 1000, previewRows: DATA_IMPORT_PREVIEW_WINDOW_ROWS }
    ]);
    expect(result.rows).toHaveLength(1200);
    expect(result.rows[1199]).toEqual(["row-1199"]);
    expect(result.structuredRows).toHaveLength(1200);
  });

  it("空区域只请求一个合法窗口", async () => {
    const loadWindow = vi.fn(async () => createWindow(0, 0, 0));

    const result = await loadBoundedFullTablePreview({ loadWindow });

    expect(loadWindow).toHaveBeenCalledOnce();
    expect(loadWindow).toHaveBeenCalledWith({
      rowOffset: 0,
      previewRows: DATA_IMPORT_PREVIEW_WINDOW_ROWS
    });
    expect(result.rows).toEqual([]);
  });

  it("服务端未返回剩余窗口数据时明确失败而不是无限重试", async () => {
    await expect(
      loadBoundedFullTablePreview({
        loadWindow: async () => createWindow(0, 0, 10)
      })
    ).rejects.toThrow("完整预览分页未返回剩余数据");
  });
});
