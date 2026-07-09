import { http } from "@/utils/http";
import type { ApiResponse, PagedData, PagedRequest } from "./customer";

/** Word文件信息 */
export interface WordFile {
  id: number;
  fileName: string;
  fileType: number;
  fileHash: string;
  uploadedAt: string;
  specCount: number;
}

/** 文件上传响应 */
export interface FileUploadResponse {
  fileId: number;
  fileName: string;
  fileType: number;
  fileHash: string;
  isDuplicate: boolean;
  tableCount: number;
  tableCountReady: boolean;
}

/** 表格信息 */
export interface TableInfo {
  index: number;
  name?: string;
  rowCount: number;
  columnCount: number;
  isNested: boolean;
  previewText?: string;
  headers: string[];
  hasMergedCells: boolean;
  usedRangeStartRow?: number;
  usedRangeStartColumn?: number;
}

/** 表格数据 */
export interface TableData {
  tableIndex: number;
  headers: string[];
  rows: string[][];
  structuredRows?: StructuredCellValue[][];
  totalRows: number;
  columnCount: number;
}

/** 结构化单元格值 */
export interface StructuredCellValue {
  parts: StructuredCellPart[];
}

export interface StructuredCellPart {
  type: "text" | "table" | string;
  text?: string;
  table?: StructuredTableValue;
}

export interface StructuredTableValue {
  rowCount: number;
  columnCount: number;
  rows: StructuredCellValue[][];
}

/** 列映射配置 */
export interface ColumnMapping {
  projectColumn?: number;
  specificationColumn?: number;
  acceptanceColumn?: number;
  remarkColumn?: number;
  headerRowIndex: number;
  dataStartRowIndex: number;
}

/** 导入数据请求 */
export interface ImportDuplicateCheckOptions {
  enableSemanticDuplicateCheck?: boolean;
  embeddingServiceId?: number;
  semanticTopK?: number;
  semanticMinScore?: number;
  enableLlmDuplicateReview?: boolean;
  llmServiceId?: number;
  llmPassScore?: number;
  highConfidenceThreshold?: number;
}

export interface ImportDataRequest {
  fileId: number;
  tableIndex: number;
  customerId: number;
  processId?: number;
  machineModelId?: number;
  mapping: ColumnMapping;
  isSpecificationOnly?: boolean;
  cleanupSourceFile?: boolean;
  previewSkippedRows?: boolean;
  confirmedDifferenceKeys?: string[];
  partiallyConfirmedDifferenceKeys?: string[];
  skippedDifferenceKeys?: string[];
  excludedRowIndexes?: number[];
  duplicateCheckOptions?: ImportDuplicateCheckOptions;
}

/** 导入结果 */
export interface ImportResult {
  successCount: number;
  failedCount: number;
  skippedCount: number;
  totalCount: number;
  errors: ImportError[];
  skippedRows?: ImportSkippedRow[];
  requiresConfirmation?: boolean;
  pendingCount?: number;
  pendingDifferences?: ImportPendingDifference[];
  projectBackfilledFromSpecification?: boolean;
}

/** 导入错误详情 */
export interface ImportError {
  rowIndex: number;
  message: string;
}

/** 跳过详情 */
export interface ImportSkippedRow {
  rowIndex: number;
  message: string;
  rowValues?: string[];
}

/** 待确认差异详情 */
export interface ImportPendingDifference {
  key: string;
  matchType: "exact" | "conflict" | "semantic" | string;
  rowIndex: number;
  rowValues?: string[];
  incomingProject: string;
  incomingSpecification: string;
  incomingAcceptance?: string;
  incomingRemark?: string;
  existingSpecId: number;
  existingProject: string;
  existingSpecification: string;
  existingAcceptance?: string;
  existingRemark?: string;
  embeddingScore?: number;
  llmScore?: number;
  finalScore?: number;
  isHighConfidence?: boolean;
  reviewReason?: string;
  reviewCommentary?: string;
}

const baseUrl = "/api/documents";
const importRequestTimeout = 300000;

/** 获取已上传的文件列表 */
export const getFileList = (params?: PagedRequest) => {
  return http.request<ApiResponse<PagedData<WordFile>>>("get", baseUrl, {
    params
  });
};

/** 上传Word文件 */
export const uploadFile = (file: File) => {
  const formData = new FormData();
  formData.append("file", file);
  return http.request<ApiResponse<FileUploadResponse>>(
    "post",
    `${baseUrl}/upload`,
    {
      data: formData,
      headers: {
        "Content-Type": "multipart/form-data"
      }
    }
  );
};

/** 获取文件中的表格列表 */
export const getFileTables = (fileId: number) => {
  return http.request<ApiResponse<TableInfo[]>>(
    "get",
    `${baseUrl}/${fileId}/tables`
  );
};

/** 获取表格数据预览 */
export const getTablePreview = (
  fileId: number,
  tableIndex: number,
  options?: {
    previewRows?: number;
    headerRowIndex?: number;
    headerRowCount?: number;
    dataStartRowIndex?: number;
    dataEndRowIndex?: number;
  }
) => {
  return http.request<ApiResponse<TableData>>(
    "get",
    `${baseUrl}/${fileId}/tables/${tableIndex}/preview`,
    {
      params: options
    }
  );
};

/** 导入表格数据 */
export const importData = (data: ImportDataRequest) => {
  return http.request<ApiResponse<ImportResult>>("post", `${baseUrl}/import`, {
    data,
    timeout: importRequestTimeout
  });
};

/** Excel 导入请求（列号/行号均为 1-based） */
export interface ExcelImportDataRequest {
  fileId: number;
  sheetIndex: number;
  customerId: number;
  processId?: number;
  machineModelId?: number;
  headerRowStart: number;
  headerRowCount: number;
  dataStartRow: number;
  dataEndRow?: number;
  projectColumn?: number;
  specificationColumn: number;
  acceptanceColumn?: number;
  remarkColumn?: number;
  cleanupSourceFile?: boolean;
  previewSkippedRows?: boolean;
  confirmedDifferenceKeys?: string[];
  partiallyConfirmedDifferenceKeys?: string[];
  skippedDifferenceKeys?: string[];
  excludedRowIndexes?: number[];
  duplicateCheckOptions?: ImportDuplicateCheckOptions;
  isSpecificationOnly?: boolean;
}

/** Excel 导入（按列序号） */
export const importExcelData = (data: ExcelImportDataRequest) => {
  return http.request<ApiResponse<ImportResult>>(
    "post",
    `${baseUrl}/excel/import`,
    {
      data,
      timeout: importRequestTimeout
    }
  );
};

/** 删除已上传的文件 */
export const deleteFile = (fileId: number) => {
  return http.request<ApiResponse<void>>("delete", `${baseUrl}/${fileId}`);
};
