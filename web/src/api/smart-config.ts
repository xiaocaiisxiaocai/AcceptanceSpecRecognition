import { http } from "@/utils/http";
import type { ApiResponse } from "./customer";
import type { ColumnMappingTargetField } from "./column-mapping-rules";

export type SmartConfigDecision = "AutoApply" | "NeedConfirm" | "Reject";

export type SmartConfigRecommendation =
  | "Recommended"
  | "Optional"
  | "NeedConfirm"
  | "Skip";

export type SmartConfigTableKind =
  | "AcceptanceSpec"
  | "SafetySpec"
  | "EnvironmentalSpec"
  | "SecsSpec"
  | "Utility"
  | "Quotation"
  | "Layout"
  | "BomOrSpareParts"
  | "SignatureOrCover"
  | "Unknown"
  | string;

export type SmartConfigSource = "Template" | "Rule" | "Llm" | "Fused" | string;

export type SmartConfigRecognizedFieldName =
  | "Project"
  | "Specification"
  | "Acceptance"
  | "Remark"
  | string;

export interface SmartConfigRecognizeRequest {
  fileId: number;
  customerId?: number;
  enableLlmAssistance?: boolean;
  llmServiceId?: number;
}

export interface SmartConfigRecognizedField {
  field: SmartConfigRecognizedFieldName;
  columnIndex?: number | null;
  header?: string | null;
  confidence: number;
  source: SmartConfigSource;
}

export interface SmartConfigFieldCandidate {
  columnIndex: number;
  header: string;
  confidence: number;
  isRecommended: boolean;
  samples: string[];
}

export interface SmartConfigFieldConflict {
  field: SmartConfigRecognizedFieldName;
  recommendedColumnIndex?: number | null;
  candidates: SmartConfigFieldCandidate[];
}

export interface SmartConfigColumnSemanticRecallSuggestion {
  columnIndex: number;
  header: string;
  targetField: SmartConfigRecognizedFieldName;
  confidence: number;
  reason?: string | null;
  source: string;
}

export interface SmartConfigRecognitionIssue {
  code: string;
  severity: "Info" | "Warning" | "Error" | string;
  field?: string | null;
  message: string;
}

export interface SmartConfigRecognizedRegion {
  regionId: string;
  regionIndex: number;
  headers: string[];
  headerRowIndex: number;
  headerRowCount: number;
  dataStartRowIndex: number;
  dataEndRowIndex?: number | null;
  projectColumnIndex?: number | null;
  specificationColumnIndex?: number | null;
  acceptanceColumnIndex?: number | null;
  remarkColumnIndex?: number | null;
  isSpecificationOnly: boolean;
  confidence: number;
  source: SmartConfigSource;
  decision: SmartConfigDecision;
  issues?: SmartConfigRecognitionIssue[];
  fields: SmartConfigRecognizedField[];
  fieldConflicts?: SmartConfigFieldConflict[];
}
export interface SmartConfigRecognizedTable {
  tableIndex: number;
  tableName?: string | null;
  headers: string[];
  headerRowIndex: number;
  headerRowCount: number;
  dataStartRowIndex: number;
  dataEndRowIndex?: number | null;
  projectColumnIndex?: number | null;
  specificationColumnIndex?: number | null;
  acceptanceColumnIndex?: number | null;
  remarkColumnIndex?: number | null;
  isSpecificationOnly: boolean;
  confidence: number;
  source: SmartConfigSource;
  decision: SmartConfigDecision;
  tableKind?: SmartConfigTableKind;
  recommendation?: SmartConfigRecommendation;
  rankingScore?: number;
  skipReason?: string | null;
  issues?: SmartConfigRecognitionIssue[];
  semanticRecallSuggestions?: SmartConfigColumnSemanticRecallSuggestion[];
  fields: SmartConfigRecognizedField[];
  fieldConflicts?: SmartConfigFieldConflict[];
  regions?: SmartConfigRecognizedRegion[];
}

export interface SmartConfigRecognizeResult {
  fileId: number;
  tables: SmartConfigRecognizedTable[];
}

export interface SmartConfigLearnedColumn {
  header: string;
  targetField: ColumnMappingTargetField;
}

export interface SmartConfigConfirmRegion {
  regionId?: string;
  regionIndex: number;
  headers: string[];
  projectColumnIndex?: number;
  specificationColumnIndex: number;
  acceptanceColumnIndex?: number;
  remarkColumnIndex?: number;
  headerRowIndex: number;
  headerRowCount: number;
  dataStartRowIndex: number;
  dataEndRowIndex?: number;
  isSpecificationOnly: boolean;
}
export interface SmartConfigConfirmRequest {
  customerId: number;
  fileId?: number;
  tableIndex: number;
  templateName?: string;
  headers: string[];
  projectColumnIndex?: number;
  specificationColumnIndex: number;
  acceptanceColumnIndex?: number;
  remarkColumnIndex?: number;
  headerRowIndex: number;
  headerRowCount: number;
  dataStartRowIndex: number;
  dataEndRowIndex?: number;
  isSpecificationOnly: boolean;
  tableKind?: SmartConfigTableKind;
  recommendation?: SmartConfigRecommendation;
  userModifiedStructure?: boolean;
  learnedColumns: SmartConfigLearnedColumn[];
  regions?: SmartConfigConfirmRegion[];
}

export interface SmartConfigConfirmResult {
  templateSaved: boolean;
  templateId: number;
  learnedRuleCount: number;
  promotedGlobalRuleCount: number;
  learningSucceeded: boolean;
}

const baseUrl = "/api/smart-config";
const smartConfigRequestTimeout = 120000;

export const recognizeSmartConfig = (data: SmartConfigRecognizeRequest) => {
  return http.request<ApiResponse<SmartConfigRecognizeResult>>(
    "post",
    `${baseUrl}/recognize`,
    {
      data,
      timeout: smartConfigRequestTimeout
    }
  );
};

export const confirmSmartConfig = (data: SmartConfigConfirmRequest) => {
  return http.request<ApiResponse<SmartConfigConfirmResult>>(
    "post",
    `${baseUrl}/confirm`,
    {
      data,
      timeout: smartConfigRequestTimeout
    }
  );
};
