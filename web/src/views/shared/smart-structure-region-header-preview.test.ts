import { describe, expect, it, vi } from "vitest";
import { createSmartStructureHeaderPreviewLoader } from "./smart-structure-region-header-preview";

const input = {
  regionId: "region-1",
  fileId: 9,
  tableIndex: 2,
  baseRow: 3,
  dataStartRow: 8,
  minimumColumnCount: 4
};

describe("smart-structure-region-header-preview", () => {
  it("只加载数据起始行的上一行，不扩展合并或多行表头", async () => {
    const request = vi.fn().mockResolvedValue({
      code: 0,
      data: {
        headers: ["项目", "规格", "验收", "备注"],
        rows: [["项目 A", "规格 A", "OK", "已确认"]],
        columnCount: 4
      }
    });
    const loader = createSmartStructureHeaderPreviewLoader(request);

    await expect(loader.load(input)).resolves.toEqual({
      status: "applied",
      headers: ["项目", "规格", "验收", "备注"],
      startRowValues: ["项目 A", "规格 A", "OK", "已确认"],
      endRowValues: ["项目 A", "规格 A", "OK", "已确认"]
    });
    expect(request).toHaveBeenCalledTimes(1);
    expect(request).toHaveBeenCalledWith(9, 2, {
      previewRows: 1,
      headerRowIndex: 4,
      headerRowCount: 1,
      dataStartRowIndex: 5
    });
  });

  it("同时读取数据起始行和结束行的单元格内容", async () => {
    const request = vi
      .fn()
      .mockResolvedValueOnce({
        code: 0,
        data: {
          headers: ["项目", "规格", "验收", "备注"],
          rows: [["起始项目", "起始规格", "OK", "起始备注"]],
          columnCount: 4
        }
      })
      .mockResolvedValueOnce({
        code: 0,
        data: {
          headers: ["项目", "规格", "验收", "备注"],
          rows: [["结束项目", "结束规格", "NG", "结束备注"]],
          columnCount: 4
        }
      });
    const loader = createSmartStructureHeaderPreviewLoader(request);

    await expect(
      loader.load({
        ...input,
        dataEndRow: 15
      })
    ).resolves.toEqual({
      status: "applied",
      headers: ["项目", "规格", "验收", "备注"],
      startRowValues: ["起始项目", "起始规格", "OK", "起始备注"],
      endRowValues: ["结束项目", "结束规格", "NG", "结束备注"]
    });
    expect(request).toHaveBeenNthCalledWith(2, 9, 2, {
      previewRows: 1,
      headerRowIndex: 4,
      headerRowCount: 1,
      dataStartRowIndex: 12,
      dataEndRowIndex: 12
    });
  });

  it("结束单元格预览失败时仍保留已读取的表头和起始内容", async () => {
    const request = vi
      .fn()
      .mockResolvedValueOnce({
        code: 0,
        data: {
          headers: ["项目", "规格", "验收", "备注"],
          rows: [["起始项目", "起始规格", "OK", "起始备注"]],
          columnCount: 4
        }
      })
      .mockResolvedValueOnce({
        code: 500,
        message: "结束单元格预览失败",
        data: { headers: [], rows: [], columnCount: 0 }
      });
    const loader = createSmartStructureHeaderPreviewLoader(request);

    await expect(
      loader.load({
        ...input,
        dataEndRow: 15
      })
    ).resolves.toEqual({
      status: "applied",
      headers: ["项目", "规格", "验收", "备注"],
      startRowValues: ["起始项目", "起始规格", "OK", "起始备注"],
      endRowValues: ["", "", "", ""],
      warning: "结束单元格预览失败"
    });
  });

  it("连续修改同一区域时丢弃较早返回的表头", async () => {
    let resolveFirst:
      | ((value: {
          code: number;
          data: { headers: string[]; rows: string[][]; columnCount: number };
        }) => void)
      | undefined;
    const request = vi
      .fn()
      .mockImplementationOnce(
        () =>
          new Promise(resolve => {
            resolveFirst = resolve;
          })
      )
      .mockResolvedValueOnce({
        code: 0,
        data: {
          headers: ["新表头"],
          rows: [["新内容"]],
          columnCount: 1
        }
      });
    const loader = createSmartStructureHeaderPreviewLoader(request);

    const first = loader.load(input);
    const second = loader.load({ ...input, dataStartRow: 9 });
    await expect(second).resolves.toEqual({
      status: "applied",
      headers: ["新表头", "", "", ""],
      startRowValues: ["新内容", "", "", ""],
      endRowValues: ["新内容", "", "", ""]
    });
    expect(request).toHaveBeenLastCalledWith(9, 2, {
      previewRows: 1,
      headerRowIndex: 5,
      headerRowCount: 1,
      dataStartRowIndex: 6
    });
    resolveFirst?.({
      code: 0,
      data: {
        headers: ["旧表头"],
        rows: [["旧内容"]],
        columnCount: 1
      }
    });
    await expect(first).resolves.toEqual({ status: "stale" });
  });

  it("字段列超过主预览窗口时按单元格窗口补取起止内容", async () => {
    const request = vi
      .fn()
      .mockResolvedValueOnce({
        code: 0,
        data: {
          headers: Array.from({ length: 100 }, (_, index) => `列${index + 1}`),
          rows: [Array.from({ length: 100 }, (_, index) => `值${index + 1}`)],
          columnCount: 100
        }
      })
      .mockResolvedValueOnce({
        code: 0,
        data: {
          headers: Array.from({ length: 100 }, (_, index) => `列${index + 1}`),
          rows: [
            Array.from({ length: 100 }, (_, index) => `结束值${index + 1}`)
          ],
          columnCount: 100
        }
      })
      .mockResolvedValueOnce({
        code: 0,
        data: {
          headers: ["第 120 列"],
          rows: [["第 120 列起始内容"]],
          columnCount: 1
        }
      })
      .mockResolvedValueOnce({
        code: 0,
        data: {
          headers: ["第 120 列"],
          rows: [["第 120 列结束内容"]],
          columnCount: 1
        }
      });
    const loader = createSmartStructureHeaderPreviewLoader(request);

    const result = await loader.load({
      ...input,
      dataEndRow: 15,
      minimumColumnCount: 120,
      startValueColumnIndexes: [119]
    });

    expect(result.status).toBe("applied");
    if (result.status !== "applied") return;
    expect(result.startRowValues).toHaveLength(120);
    expect(result.startRowValues[119]).toBe("第 120 列起始内容");
    expect(result.endRowValues[119]).toBe("第 120 列结束内容");
    expect(request).toHaveBeenNthCalledWith(3, 9, 2, {
      previewRows: 1,
      headerRowIndex: 4,
      headerRowCount: 1,
      dataStartRowIndex: 5,
      dataEndRowIndex: 5,
      rowOffset: 0,
      columnOffset: 119,
      previewColumns: 1
    });
    expect(request).toHaveBeenNthCalledWith(4, 9, 2, {
      previewRows: 1,
      headerRowIndex: 4,
      headerRowCount: 1,
      dataStartRowIndex: 12,
      dataEndRowIndex: 12,
      rowOffset: 0,
      columnOffset: 119,
      previewColumns: 1
    });
  });

  it("多个区域并发加载时按区域隔离起始单元格内容", async () => {
    const request = vi.fn(
      async (
        _fileId: number,
        _tableIndex: number,
        options: { dataStartRowIndex: number }
      ) => ({
        code: 0,
        data: {
          headers: [`表头 ${options.dataStartRowIndex}`],
          rows: [[`内容 ${options.dataStartRowIndex}`]],
          columnCount: 1
        }
      })
    );
    const loader = createSmartStructureHeaderPreviewLoader(request);

    const [first, second] = await Promise.all([
      loader.load(input),
      loader.load({
        ...input,
        regionId: "region-2",
        dataStartRow: 20
      })
    ]);

    expect(first).toEqual({
      status: "applied",
      headers: ["表头 5", "", "", ""],
      startRowValues: ["内容 5", "", "", ""],
      endRowValues: ["内容 5", "", "", ""]
    });
    expect(second).toEqual({
      status: "applied",
      headers: ["表头 17", "", "", ""],
      startRowValues: ["内容 17", "", "", ""],
      endRowValues: ["内容 17", "", "", ""]
    });
  });

  it("请求失败保留草稿，由调用方只显示区域错误", async () => {
    const loader = createSmartStructureHeaderPreviewLoader(async () => ({
      code: 500,
      message: "表头预览失败",
      data: { headers: [], rows: [], columnCount: 0 }
    }));

    await expect(loader.load(input)).resolves.toEqual({
      status: "error",
      message: "表头预览失败"
    });
  });
});
