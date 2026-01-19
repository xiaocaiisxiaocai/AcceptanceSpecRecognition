import { http } from "@/utils/http";
import type { ApiResponse, PagedData, PagedRequest } from "./customer";

/** Word文件信息 */
export interface WordFile {
  id: number;
  fileName: string;
  fileHash: string;
  uploadedAt: string;
  specCount: number;
}

/** 文件上传响应 */
export interface FileUploadResponse {
  fileId: number;
  fileName: string;
  fileHash: string;
  isDuplicate: boolean;
  tableCount: number;
}

/** 表格信息 */
export interface TableInfo {
  index: number;
  rowCount: number;
  columnCount: number;
  isNested: boolean;
  previewText?: string;
  headers: string[];
  hasMergedCells: boolean;
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
export interface ImportDataRequest {
  fileId: number;
  tableIndex: number;
  customerId: number;
  processId: number;
  mapping: ColumnMapping;
}

/** 导入结果 */
export interface ImportResult {
  successCount: number;
  failedCount: number;
  skippedCount: number;
  totalCount: number;
  errors: ImportError[];
}

/** 导入错误详情 */
export interface ImportError {
  rowIndex: number;
  message: string;
}

const baseUrl = "/api/documents";

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
    dataStartRowIndex?: number;
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
    data
  });
};

/** 删除已上传的文件 */
export const deleteFile = (fileId: number) => {
  return http.request<ApiResponse<void>>("delete", `${baseUrl}/${fileId}`);
};
