import { describe, expect, it } from "vitest";
import type { BatchTablePreviewResult } from "@/api/matching";
import type { PersistedSelection } from "./matchPreviewTable.types";
import { reconcileBatchPreviewSelectionCache } from "./matchPreviewTable.selection";

const createResult = (
  tableIndex: number,
  reviewApprovalToken = "token-before-refresh"
): BatchTablePreviewResult =>
  ({
    tableIndex,
    totalMatched: 1,
    highConfidenceCount: 1,
    mediumConfidenceCount: 0,
    lowConfidenceCount: 0,
    ambiguousCount: 0,
    items: [
      {
        rowIndex: 1,
        sourceProject: "项目",
        sourceSpecification: "规格",
        hasMatch: true,
        confidenceLevel: "high",
        bestMatch: {
          specId: 10,
          project: "项目",
          specification: "规格",
          acceptance: "原验收标准",
          remark: "原备注",
          score: 1,
          decision: "autoApply",
          reviewApprovalToken
        }
      }
    ]
  }) as BatchTablePreviewResult;

describe("reconcileBatchPreviewSelectionCache", () => {
  it("预览结果刷新时应保留已保存的验收标准和备注覆盖值", () => {
    const selectionCache = new Map<number, PersistedSelection[]>([
      [
        0,
        [
          {
            rowIndex: 1,
            selected: true,
            specId: 10,
            manualConfirmed: true,
            overrideAcceptance: "修改后的验收标准",
            overrideRemark: "修改后的备注"
          }
        ]
      ]
    ]);

    reconcileBatchPreviewSelectionCache(
      [createResult(0, "token-after-refresh")],
      selectionCache
    );

    expect(selectionCache.get(0)).toEqual([
      expect.objectContaining({
        rowIndex: 1,
        selected: true,
        specId: 10,
        overrideAcceptance: "修改后的验收标准",
        overrideRemark: "修改后的备注"
      })
    ]);
  });

  it("结果中已经移除的表格不应继续保留旧编辑状态", () => {
    const selectionCache = new Map<number, PersistedSelection[]>([
      [0, [{ rowIndex: 1, selected: true, specId: 10 }]],
      [1, [{ rowIndex: 2, selected: true, specId: 20 }]]
    ]);

    reconcileBatchPreviewSelectionCache([createResult(0)], selectionCache);

    expect(selectionCache.has(0)).toBe(true);
    expect(selectionCache.has(1)).toBe(false);
  });
});
