import { describe, expect, it } from "vitest";
import type {
  BatchTablePreviewResult,
  MatchPreviewItem,
  MatchResult
} from "@/api/matching";
import { collectSmartFillBackfillCandidates } from "./smartFillBackfill.helpers";

const match = (
  specId: number,
  decision: MatchResult["decision"],
  selectionMode: MatchResult["selectionMode"]
): MatchResult => ({
  specId,
  project: `原项目${specId}`,
  specification: `原规格${specId}`,
  acceptance: `原验收${specId}`,
  remark: `原备注${specId}`,
  score: 0.8,
  embeddingScore: 0.8,
  scoreDetails: {},
  decision,
  selectionMode,
  topCandidates: [],
  recalledCandidateCount: 1,
  isAmbiguous: false
});

const row = (
  rowIndex: number,
  bestMatch: MatchResult | undefined,
  confidenceLevel: MatchPreviewItem["confidenceLevel"] = "medium"
): MatchPreviewItem => ({
  rowIndex,
  sourceProject: `新项目${rowIndex}`,
  sourceSpecification: `新规格${rowIndex}`,
  bestMatch,
  hasMatch: !!bestMatch,
  confidenceLevel
});

const table = (
  tableIndex: number,
  items: MatchPreviewItem[]
): BatchTablePreviewResult => ({
  tableIndex,
  items,
  totalMatched: items.filter(item => item.bestMatch).length,
  highConfidenceCount: items.filter(item => item.confidenceLevel === "high")
    .length,
  mediumConfidenceCount: items.filter(item => item.confidenceLevel === "medium")
    .length,
  lowConfidenceCount: items.filter(item => item.confidenceLevel === "low")
    .length,
  ambiguousCount: items.filter(item => item.bestMatch?.isAmbiguous).length
});

describe("collectSmartFillBackfillCandidates", () => {
  it("跨 Sheet 汇总普通可填充和已确认记录，排除精确直达、未确认、未选择及无匹配，并默认覆盖已有", () => {
    const results = [
      table(0, [
        row(0, match(10, "autoApply", "embeddingTop1"), "high"),
        row(1, match(11, "autoApply", "exactShortcut"), "high"),
        row(2, match(12, "manualReview", "embeddingTop1")),
        row(3, undefined)
      ]),
      table(2, [row(5, match(20, "manualReview", "aiRerank"))])
    ];
    const selections = new Map([
      [
        0,
        [
          { rowIndex: 0, selected: true, specId: 10 },
          { rowIndex: 1, selected: true, specId: 11 },
          { rowIndex: 2, selected: true, specId: 12 },
          { rowIndex: 3, selected: true, manualFill: true }
        ]
      ],
      [
        2,
        [
          {
            rowIndex: 5,
            selected: true,
            specId: 20,
            manualConfirmed: true,
            overrideRemark: "当前备注"
          }
        ]
      ]
    ]);

    const candidates = collectSmartFillBackfillCandidates(results, selections, {
      0: "工作表1",
      2: "流程图"
    });

    expect(candidates).toHaveLength(2);
    expect(candidates.map(item => item.sheetName)).toEqual([
      "工作表1",
      "流程图"
    ]);
    expect(candidates.map(item => item.category)).toEqual([
      "fillable",
      "review"
    ]);
    expect(candidates.every(item => item.decision === "overwrite")).toBe(true);
    expect(candidates[1].overrideRemark).toBe("当前备注");
  });
});
