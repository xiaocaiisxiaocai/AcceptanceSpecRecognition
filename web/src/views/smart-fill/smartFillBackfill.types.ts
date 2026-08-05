export type SmartFillSpecWriteDecision = "overwrite" | "create" | "skip";

export type SmartFillBackfillCandidate = {
  tableIndex: number;
  sheetName: string;
  rowIndex: number;
  specId: number;
  category: "fillable" | "review";
  sourceProject: string;
  sourceSpecification: string;
  originalProject: string;
  originalSpecification: string;
  originalAcceptance?: string;
  originalRemark?: string;
  overrideAcceptance?: string;
  overrideRemark?: string;
  decision: SmartFillSpecWriteDecision;
};
