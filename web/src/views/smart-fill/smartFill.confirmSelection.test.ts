import { describe, expect, it, vi } from "vitest";
import type {
  SmartConfigConfirmRequest,
  SmartConfigRecognizedTable
} from "@/api/smart-config";
import { runSmartFillConfirmSelection } from "./smartFill.confirmSelection";

const table = (
  tableIndex: number,
  decision: SmartConfigRecognizedTable["decision"] = "NeedConfirm"
) =>
  ({
    tableIndex,
    decision,
    tableName: `Sheet${tableIndex + 1}`
  }) as SmartConfigRecognizedTable;
const request = (tableIndex: number, userModifiedStructure = false) =>
  ({ tableIndex, userModifiedStructure }) as SmartConfigConfirmRequest;

describe("smartFill.confirmSelection", () => {
  it("只按顺序确认已选待确认 Sheet，未选和未修改自动采用 Sheet 不学习", async () => {
    const confirm = vi.fn(
      async (
        _table: SmartConfigRecognizedTable,
        _request: SmartConfigConfirmRequest
      ) => true
    );
    const result = await runSmartFillConfirmSelection({
      tables: [table(0), table(1), table(2, "AutoApply")],
      selectedTableIndexes: [2, 0],
      draftRequests: new Map([
        [0, request(0)],
        [1, request(1)],
        [2, request(2)]
      ]),
      confirm
    });

    expect(result).toMatchObject({
      success: true,
      confirmedTableIndexes: [0],
      skippedTableIndexes: [2]
    });
    expect(confirm).toHaveBeenCalledTimes(1);
    expect(confirm.mock.calls[0]?.[0].tableIndex).toBe(0);
  });

  it("任一已选 Sheet 缺少草稿时不开始学习", async () => {
    const confirm = vi.fn(
      async (
        _table: SmartConfigRecognizedTable,
        _request: SmartConfigConfirmRequest
      ) => true
    );
    const result = await runSmartFillConfirmSelection({
      tables: [table(0), table(1)],
      selectedTableIndexes: [0, 1],
      draftRequests: new Map([[0, request(0)]]),
      confirm
    });

    expect(result).toMatchObject({
      success: false,
      failure: "missing-draft",
      failedTableIndex: 1
    });
    expect(confirm).not.toHaveBeenCalled();
  });

  it("确认失败后停止后续 Sheet 并返回失败位置", async () => {
    const confirm = vi
      .fn<
        (
          table: SmartConfigRecognizedTable,
          request: SmartConfigConfirmRequest
        ) => Promise<boolean>
      >()
      .mockResolvedValueOnce(true)
      .mockResolvedValueOnce(false);
    const result = await runSmartFillConfirmSelection({
      tables: [table(0), table(1), table(2)],
      selectedTableIndexes: [0, 1, 2],
      draftRequests: new Map([
        [0, request(0)],
        [1, request(1)],
        [2, request(2)]
      ]),
      confirm
    });

    expect(result).toMatchObject({
      success: false,
      failure: "confirm-failed",
      failedTableIndex: 1,
      confirmedTableIndexes: [0]
    });
    expect(confirm).toHaveBeenCalledTimes(2);
  });

  it("用户修改过的自动采用 Sheet 仍需学习", async () => {
    const confirm = vi.fn(
      async (
        _table: SmartConfigRecognizedTable,
        _request: SmartConfigConfirmRequest
      ) => true
    );
    const result = await runSmartFillConfirmSelection({
      tables: [table(0, "AutoApply")],
      selectedTableIndexes: [0],
      draftRequests: new Map([[0, request(0, true)]]),
      confirm
    });

    expect(result.confirmedTableIndexes).toEqual([0]);
    expect(confirm).toHaveBeenCalledTimes(1);
  });
});
