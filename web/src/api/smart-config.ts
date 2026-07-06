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

export type SmartConfigSource =
  | "Template"
  | "Rule"
  | "Llm"
  | "Fused"
  | string;

export type SmartConfigRecognizedFieldName =
  | "Project"
  | "Specification"
  | "Acceptance"
  | "Remark"
  | string;

export interface SmartConfigRecognizeRequest {
  fileId: number;
  customerId?: number;
}

export interface SmartConfigRecognizedField {
  field: SmartConfigRecognizedFieldName;
  columnIndex?: number;
  header?: string | null;
  confidence: number;
  source: SmartConfigSource;
}

export interface SmartConfigRecognitionIssue {
  code: string;
  severity: "Info" | "Warning" | "Error" | string;
  field?: string | null;
  message: string;
}

export interface SmartConfigRecognizedTable {
  tableIndex: number;
  tableName?: string | null;
  headers: string[];
  headerRowIndex: number;
  headerRowCount: number;
  dataStartRowIndex: number;
  dataEndRowIndex?: number;
  projectColumnIndex?: number;
  specificationColumnIndex?: number;
  acceptanceColumnIndex?: number;
  remarkColumnIndex?: number;
  isSpecificationOnly: boolean;
  confidence: number;
  source: SmartConfigSource;
  decision: SmartConfigDecision;
  tableKind?: SmartConfigTableKind;
  recommendation?: SmartConfigRecommendation;
  rankingScore?: number;
  skipReason?: string | null;
  issues?: SmartConfigRecognitionIssue[];
  fields: SmartConfigRecognizedField[];
}

export interface SmartConfigRecognizeResult {
  fileId: number;
  tables: SmartConfigRecognizedTable[];
}

export interface SmartConfigLearnedColumn {
  header: string;
  targetField: ColumnMappingTargetField;
}

export interface SmartConfigConfirmRequest {
  customerId: number;
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
}

export interface SmartConfigConfirmResult {
  templateSaved: boolean;
  templateId: number;
  learnedRuleCount: number;
  learnedRoutingRuleCount: number;
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
