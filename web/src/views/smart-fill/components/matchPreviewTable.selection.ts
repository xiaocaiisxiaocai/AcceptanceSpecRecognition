import type { BatchTablePreviewResult, MatchPreviewItem } from "@/api/matching";
import {
  canUseMatchPreviewBestMatch,
  isHighConfidenceMatchPreview,
  isNoAnswerPlaceholderRow
} from "./matchPreviewTable.formatters.ts";
import type {
  EditedBackfillItem,
  MatchPreviewEditOverride,
  PersistedSelection,
  MatchPreviewSelection
} from "./matchPreviewTable.types";

export const hasMatchPreviewOverrideValue = (
  value?: MatchPreviewEditOverride | null
) =>
  !!value &&
  (value.overrideAcceptance !== undefined ||
    value.overrideRemark !== undefined);

export const hasManualFillOverrideValue = (
  value?: MatchPreviewEditOverride | null
) =>
  !!value &&
  (!isBlankOverrideText(value.overrideAcceptance) ||
    !isBlankOverrideText(value.overrideRemark));

const isBlankOverrideText = (value?: string) =>
  value === undefined || value.trim().length === 0;

export const cloneMatchPreviewOverride = (
  value?: MatchPreviewEditOverride | null
): MatchPreviewEditOverride | undefined => {
  if (!hasMatchPreviewOverrideValue(value)) {
    return undefined;
  }

  return {
    overrideAcceptance: value?.overrideAcceptance,
    overrideRemark: value?.overrideRemark
  };
};

export const collectMatchPreviewSelections = (
  items: MatchPreviewItem[],
  selectedSpecs: Map<number, MatchPreviewSelection | null>,
  editedOverrides: Map<number, MatchPreviewEditOverride>,
  manualClearedRows: Set<number> = new Set()
) => {
  const selections: Array<{
    rowIndex: number;
    selected?: boolean;
    specId?: number;
    manualConfirmed?: boolean;
    manualFill?: boolean;
    manualCleared?: boolean;
    reviewApprovalToken?: string;
    overrideAcceptance?: string;
    overrideRemark?: string;
  }> = [];

  const rowIndexes = new Set<number>([
    ...selectedSpecs.keys(),
    ...editedOverrides.keys(),
    ...manualClearedRows
  ]);

  rowIndexes.forEach(rowIndex => {
    const selection = selectedSpecs.get(rowIndex) ?? null;
    const override = editedOverrides.get(rowIndex);
    const manualCleared = manualClearedRows.has(rowIndex);
    if (!selection && !manualCleared && !hasMatchPreviewOverrideValue(override))
      return;

    const item = items.find(i => i.rowIndex === rowIndex);
    if (!item) return;
    if (selection?.type === "manual" && !hasManualFillOverrideValue(override)) {
      return;
    }

    selections.push({
      rowIndex,
      selected: !!selection,
      specId: selection?.type === "best" ? item.bestMatch?.specId : undefined,
      manualConfirmed: selection?.manualConfirmed,
      manualFill: selection?.type === "manual",
      manualCleared: manualCleared ? true : undefined,
      reviewApprovalToken: selection?.reviewApprovalToken,
      overrideAcceptance: override?.overrideAcceptance,
      overrideRemark: override?.overrideRemark
    });
  });

  return selections;
};

export const collectEditedBackfillItems = (
  items: MatchPreviewItem[],
  editedOverrides: Map<number, MatchPreviewEditOverride>,
  selectedSpecs?: Map<number, MatchPreviewSelection | null>,
  manualClearedRows: Set<number> = new Set()
): EditedBackfillItem[] =>
  [...editedOverrides.entries()]
    .map((entry): EditedBackfillItem | null => {
      const [rowIndex, override] = entry;
      if (!hasMatchPreviewOverrideValue(override)) return null;
      if (manualClearedRows.has(rowIndex)) return null;
      if (selectedSpecs && !selectedSpecs.get(rowIndex)) return null;
      const item = items.find(i => i.rowIndex === rowIndex);
      if (!item) return null;
      if (!item.bestMatch && !hasManualFillOverrideValue(override)) return null;

      return {
        rowIndex,
        specId: item.bestMatch?.specId,
        sourceProject: item.sourceProject,
        sourceSpecification: item.sourceSpecification,
        originalAcceptance: item.bestMatch?.acceptance,
        originalRemark: item.bestMatch?.remark,
        overrideAcceptance: override.overrideAcceptance,
        overrideRemark: override.overrideRemark,
        actionType: item.bestMatch ? "update" : "create"
      };
    })
    .filter((item): item is EditedBackfillItem => !!item);

const buildDefaultSelectionForItem = (
  item: MatchPreviewItem,
  existing?: PersistedSelection
): PersistedSelection => {
  const manualClearedField =
    existing?.manualCleared === true ? { manualCleared: true } : {};
  const base = {
    rowIndex: item.rowIndex,
    overrideAcceptance: existing?.overrideAcceptance,
    overrideRemark: existing?.overrideRemark
  };

  if (!item.bestMatch || isNoAnswerPlaceholderRow(item)) {
    const keepManualFill =
      existing?.selected === true &&
      existing.manualFill === true &&
      hasMatchPreviewOverrideValue(existing);
    return {
      ...base,
      selected: keepManualFill,
      specId: undefined,
      manualConfirmed: keepManualFill ? true : undefined,
      manualFill: keepManualFill ? true : undefined,
      ...(keepManualFill ? {} : manualClearedField),
      reviewApprovalToken: undefined
    };
  }

  if (item.llmReviewStage === "streaming") {
    return {
      ...base,
      selected: false,
      specId: item.bestMatch.specId,
      manualConfirmed: undefined,
      manualFill: false,
      ...manualClearedField,
      reviewApprovalToken: undefined
    };
  }

  if (!canUseMatchPreviewBestMatch(item, "manual")) {
    return {
      ...base,
      selected: false,
      specId: item.bestMatch.specId,
      manualConfirmed: undefined,
      manualFill: false,
      ...manualClearedField,
      reviewApprovalToken: undefined
    };
  }

  if (existing?.manualCleared === true) {
    return {
      ...base,
      ...manualClearedField,
      selected: false,
      specId: item.bestMatch.specId,
      manualConfirmed: undefined,
      manualFill: false,
      reviewApprovalToken: undefined
    };
  }

  if (item.bestMatch.reviewApprovalToken) {
    return {
      ...base,
      selected: true,
      specId: item.bestMatch.specId,
      manualConfirmed: false,
      manualFill: false,
      reviewApprovalToken: item.bestMatch.reviewApprovalToken
    };
  }

  if (isHighConfidenceMatchPreview(item)) {
    return {
      ...base,
      selected: true,
      specId: item.bestMatch.specId,
      manualConfirmed: false,
      manualFill: false,
      reviewApprovalToken: undefined
    };
  }

  return {
    ...base,
    selected: existing?.manualConfirmed === true,
    specId: item.bestMatch.specId,
    manualConfirmed: existing?.manualConfirmed === true ? true : undefined,
    manualFill: false,
    ...(existing?.manualConfirmed === true ? {} : manualClearedField),
    reviewApprovalToken: undefined
  };
};

export const reconcileMatchPreviewSelectionCache = (
  results: BatchTablePreviewResult[],
  selectionCache: Map<number, PersistedSelection[]>
) => {
  results.forEach(tableResult => {
    const cached = selectionCache.get(tableResult.tableIndex);
    if (!cached) {
      return;
    }

    const existingByRow = new Map(cached.map(item => [item.rowIndex, item]));
    selectionCache.set(
      tableResult.tableIndex,
      tableResult.items.map(item =>
        buildDefaultSelectionForItem(item, existingByRow.get(item.rowIndex))
      )
    );
  });
};

export const reconcileBatchPreviewSelectionCache = (
  results: BatchTablePreviewResult[],
  selectionCache: Map<number, PersistedSelection[]>
) => {
  const currentTableIndexes = new Set(
    results.map(tableResult => tableResult.tableIndex)
  );

  for (const tableIndex of selectionCache.keys()) {
    if (!currentTableIndexes.has(tableIndex)) {
      selectionCache.delete(tableIndex);
    }
  }

  reconcileMatchPreviewSelectionCache(results, selectionCache);
};
