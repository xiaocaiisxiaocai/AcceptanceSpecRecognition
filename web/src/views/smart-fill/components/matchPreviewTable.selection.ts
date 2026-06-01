import type { MatchPreviewItem } from "@/api/matching";
import type {
  EditedBackfillItem,
  MatchPreviewEditOverride,
  MatchPreviewSelection
} from "./matchPreviewTable.types";

export const hasMatchPreviewOverrideValue = (
  value?: MatchPreviewEditOverride | null
) =>
  !!value &&
  (value.overrideAcceptance !== undefined || value.overrideRemark !== undefined);

export const cloneMatchPreviewOverride = (
  value?: MatchPreviewEditOverride | null
): MatchPreviewEditOverride | undefined => {
  if (!hasMatchPreviewOverrideValue(value)) {
    return undefined;
  }

  return {
    overrideAcceptance: value.overrideAcceptance,
    overrideRemark: value.overrideRemark
  };
};

export const collectMatchPreviewSelections = (
  items: MatchPreviewItem[],
  selectedSpecs: Map<number, MatchPreviewSelection | null>,
  editedOverrides: Map<number, MatchPreviewEditOverride>
) => {
  const selections: Array<{
    rowIndex: number;
    selected?: boolean;
    specId?: number;
    manualConfirmed?: boolean;
    manualFill?: boolean;
    reviewApprovalToken?: string;
    overrideAcceptance?: string;
    overrideRemark?: string;
  }> = [];

  const rowIndexes = new Set<number>([
    ...selectedSpecs.keys(),
    ...editedOverrides.keys()
  ]);

  rowIndexes.forEach(rowIndex => {
    const selection = selectedSpecs.get(rowIndex) ?? null;
    const override = editedOverrides.get(rowIndex);
    if (!selection && !hasMatchPreviewOverrideValue(override)) return;

    const item = items.find(i => i.rowIndex === rowIndex);
    if (!item) return;

    selections.push({
      rowIndex,
      selected: !!selection,
      specId: selection?.type === "best" ? item.bestMatch?.specId : undefined,
      manualConfirmed: selection?.manualConfirmed,
      manualFill: selection?.type === "manual",
      reviewApprovalToken: selection?.reviewApprovalToken,
      overrideAcceptance: override?.overrideAcceptance,
      overrideRemark: override?.overrideRemark
    });
  });

  return selections;
};

export const collectEditedBackfillItems = (
  items: MatchPreviewItem[],
  editedOverrides: Map<number, MatchPreviewEditOverride>
): EditedBackfillItem[] =>
  [...editedOverrides.entries()]
    .map((entry): EditedBackfillItem | null => {
      const [rowIndex, override] = entry;
      if (!hasMatchPreviewOverrideValue(override)) return null;
      const item = items.find(i => i.rowIndex === rowIndex);
      if (!item) return null;

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
