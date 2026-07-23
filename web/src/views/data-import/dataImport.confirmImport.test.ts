import { describe, expect, it, vi } from "vitest";
import { runSmartStructureBatchConfirmImportAction } from "./dataImport.confirmImport";

describe("dataImport.confirmImport", () => {
  it("按 tableIndex 串行确认全部待确认 Sheet，随后只刷新和导入一次", async () => {
    const calls: string[] = [];
    const confirm = vi.fn(
      async (table: { tableIndex: number }, request: string) => {
        calls.push(`confirm:${table.tableIndex}:${request}`);
        return true;
      }
    );
    const refresh = vi.fn(async () => {
      calls.push("refresh");
    });
    const importData = vi.fn(async () => {
      calls.push("import");
    });

    const result = await runSmartStructureBatchConfirmImportAction({
      tables: [
        { tableIndex: 4, decision: "NeedConfirm" },
        { tableIndex: 1, decision: "NeedConfirm" }
      ],
      selectedTableIndexes: [4, 1],
      draftRequests: new Map([
        [1, "draft-1"],
        [4, "draft-4"]
      ]),
      confirm,
      refresh,
      importData
    });

    expect(calls).toEqual([
      "confirm:1:draft-1",
      "confirm:4:draft-4",
      "refresh",
      "import"
    ]);
    expect(refresh).toHaveBeenCalledOnce();
    expect(importData).toHaveBeenCalledOnce();
    expect(result).toMatchObject({
      success: true,
      phase: "completed",
      completed: 2,
      total: 2,
      confirmedTableIndexes: [1, 4],
      skippedTableIndexes: []
    });
  });

  it("跳过未选择和已自动采用的 Sheet", async () => {
    const confirm = vi.fn().mockResolvedValue(true);
    const refresh = vi.fn().mockResolvedValue(undefined);
    const importData = vi.fn().mockResolvedValue(undefined);

    const result = await runSmartStructureBatchConfirmImportAction({
      tables: [
        { tableIndex: 0, decision: "NeedConfirm" },
        { tableIndex: 1, decision: "AutoApply" },
        { tableIndex: 2, decision: "NeedConfirm" }
      ],
      selectedTableIndexes: [1, 2],
      draftRequests: new Map([[2, "draft-2"]]),
      confirm,
      refresh,
      importData
    });

    expect(confirm).toHaveBeenCalledOnce();
    expect(confirm).toHaveBeenCalledWith(
      { tableIndex: 2, decision: "NeedConfirm" },
      "draft-2"
    );
    expect(result.confirmedTableIndexes).toEqual([2]);
    expect(result.skippedTableIndexes).toEqual([1]);
    expect(result.total).toBe(1);
    expect(result.success).toBe(true);
  });

  it("已自动采用但被用户修改的 Sheet 仍会提交最终草稿确认", async () => {
    type Draft = { name: string; userModifiedStructure: boolean };
    const confirm = vi.fn().mockResolvedValue(true);
    const refresh = vi.fn().mockResolvedValue(undefined);
    const importData = vi.fn().mockResolvedValue(undefined);
    const modifiedDraft: Draft = {
      name: "modified-draft",
      userModifiedStructure: true
    };

    const result = await runSmartStructureBatchConfirmImportAction({
      tables: [{ tableIndex: 1, decision: "AutoApply" }],
      selectedTableIndexes: [1],
      draftRequests: new Map<number, Draft>([[1, modifiedDraft]]),
      requiresConfirmation: (table, request) =>
        table.decision !== "AutoApply" ||
        request?.userModifiedStructure === true,
      confirm,
      refresh,
      importData
    });

    expect(confirm).toHaveBeenCalledWith(
      { tableIndex: 1, decision: "AutoApply" },
      modifiedDraft
    );
    expect(result).toMatchObject({
      success: true,
      confirmedTableIndexes: [1],
      skippedTableIndexes: []
    });
  });

  it("缺少草稿时在任何确认前阻止流程并返回失败 Sheet", async () => {
    const confirm = vi.fn().mockResolvedValue(true);
    const refresh = vi.fn().mockResolvedValue(undefined);
    const importData = vi.fn().mockResolvedValue(undefined);

    const result = await runSmartStructureBatchConfirmImportAction({
      tables: [
        { tableIndex: 1, decision: "NeedConfirm" },
        { tableIndex: 3, decision: "NeedConfirm" }
      ],
      selectedTableIndexes: [3, 1],
      draftRequests: new Map([[1, "draft-1"]]),
      confirm,
      refresh,
      importData
    });

    expect(result).toMatchObject({
      success: false,
      phase: "failed",
      completed: 0,
      total: 2,
      failedTableIndex: 3,
      currentTableIndex: 3,
      failure: "missing-draft"
    });
    expect(confirm).not.toHaveBeenCalled();
    expect(refresh).not.toHaveBeenCalled();
    expect(importData).not.toHaveBeenCalled();
  });

  it("确认失败时立即停止后续 Sheet、刷新和导入", async () => {
    const confirm = vi
      .fn()
      .mockResolvedValueOnce(true)
      .mockResolvedValueOnce(false);
    const refresh = vi.fn().mockResolvedValue(undefined);
    const importData = vi.fn().mockResolvedValue(undefined);
    const progress = vi.fn();

    const result = await runSmartStructureBatchConfirmImportAction({
      tables: [
        { tableIndex: 1, decision: "NeedConfirm" },
        { tableIndex: 2, decision: "NeedConfirm" },
        { tableIndex: 3, decision: "NeedConfirm" }
      ],
      selectedTableIndexes: [1, 2, 3],
      draftRequests: new Map([
        [1, "draft-1"],
        [2, "draft-2"],
        [3, "draft-3"]
      ]),
      confirm,
      refresh,
      importData,
      onProgress: progress
    });

    expect(confirm).toHaveBeenCalledTimes(2);
    expect(result).toMatchObject({
      success: false,
      completed: 1,
      total: 3,
      failedTableIndex: 2,
      failure: "confirm-failed",
      confirmedTableIndexes: [1]
    });
    expect(progress).toHaveBeenLastCalledWith({
      phase: "failed",
      completed: 1,
      total: 3,
      currentTableIndex: 2
    });
    expect(refresh).not.toHaveBeenCalled();
    expect(importData).not.toHaveBeenCalled();
  });

  it("统一刷新失败时不执行导入", async () => {
    const confirm = vi.fn().mockResolvedValue(true);
    const refresh = vi.fn().mockResolvedValue(false);
    const importData = vi.fn().mockResolvedValue(undefined);

    const result = await runSmartStructureBatchConfirmImportAction({
      tables: [{ tableIndex: 1, decision: "NeedConfirm" }],
      selectedTableIndexes: [1],
      draftRequests: new Map([[1, "draft-1"]]),
      confirm,
      refresh,
      importData
    });

    expect(result).toMatchObject({
      success: false,
      completed: 1,
      failure: "refresh-failed"
    });
    expect(importData).not.toHaveBeenCalled();
  });
});
