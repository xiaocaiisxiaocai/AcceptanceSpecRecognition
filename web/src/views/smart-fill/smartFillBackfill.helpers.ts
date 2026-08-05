import type { BatchTablePreviewResult } from "@/api/matching";
import { getSmartFillTableState } from "./components/scoreDetail.formatters";
import type { PersistedSelection } from "./components/matchPreviewTable.types";
import type { SmartFillBackfillCandidate } from "./smartFillBackfill.types";

export const collectSmartFillBackfillCandidates = (
  results: BatchTablePreviewResult[],
  selectionsByTable: Map<number, PersistedSelection[]>,
  tableNames: Record<number, string> = {}
): SmartFillBackfillCandidate[] => {
  const candidates: SmartFillBackfillCandidate[] = [];

  for (const tableResult of results) {
    const selectedByRow = new Map(
      (selectionsByTable.get(tableResult.tableIndex) ?? [])
        .filter(selection => selection.selected !== false)
        .map(selection => [selection.rowIndex, selection])
    );

    for (const item of tableResult.items) {
      const selection = selectedByRow.get(item.rowIndex);
      const bestMatch = item.bestMatch;
      if (!selection || selection.manualFill || !bestMatch) continue;
      if (bestMatch.selectionMode === "exactShortcut") continue;

      const { fillRecommendation } = getSmartFillTableState(item);
      if (fillRecommendation !== "fillable" && fillRecommendation !== "review")
        continue;
      if (
        fillRecommendation === "review" &&
        selection.manualConfirmed !== true &&
        !selection.reviewApprovalToken
      ) {
        continue;
      }

      candidates.push({
        tableIndex: tableResult.tableIndex,
        sheetName:
          tableNames[tableResult.tableIndex]?.trim() ||
          `Sheet ${tableResult.tableIndex + 1}`,
        rowIndex: item.rowIndex,
        specId: bestMatch.specId,
        category: fillRecommendation,
        sourceProject: item.sourceProject,
        sourceSpecification: item.sourceSpecification,
        originalProject: bestMatch.project,
        originalSpecification: bestMatch.specification,
        originalAcceptance: bestMatch.acceptance,
        originalRemark: bestMatch.remark,
        overrideAcceptance: selection.overrideAcceptance,
        overrideRemark: selection.overrideRemark,
        decision: "skip"
      });
    }
  }

  return candidates;
};
