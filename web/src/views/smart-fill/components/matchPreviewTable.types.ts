export type MatchPreviewSelection = {
  type: "best" | "manual";
  manualConfirmed: boolean;
  reviewApprovalToken?: string;
};

export type MatchPreviewEditForm = {
  overrideAcceptance: string;
  overrideRemark: string;
};

export type MatchPreviewEditOverride = {
  overrideAcceptance?: string;
  overrideRemark?: string;
};

export type PersistedSelection = {
  rowIndex: number;
  selected?: boolean;
  specId?: number;
  manualConfirmed?: boolean;
  manualFill?: boolean;
  manualCleared?: boolean;
  reviewApprovalToken?: string;
  overrideAcceptance?: string;
  overrideRemark?: string;
};

export type EditedBackfillItem = {
  rowIndex: number;
  specId?: number;
  sourceProject: string;
  sourceSpecification: string;
  originalAcceptance?: string;
  originalRemark?: string;
  overrideAcceptance?: string;
  overrideRemark?: string;
  actionType: "update" | "create";
};
