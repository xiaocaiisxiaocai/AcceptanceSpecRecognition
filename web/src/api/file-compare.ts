import { http } from "@/utils/http";
import type { ApiResponse } from "./customer";
import type { FileUploadResponse } from "./document";

export interface FileCompareUploadResponse {
  fileA: FileUploadResponse;
  fileB: FileUploadResponse;
}

export interface FileComparePreviewRequest {
  fileIdA: number;
  fileIdB: number;
}

export interface FileCompareLocation {
  documentType: string;
  tableIndex?: number;
  sheetName?: string;
  rowIndex?: number;
  columnIndex?: number;
  address?: string;
}

export interface FileCompareDiffItem {
  diffType: "Unchanged" | "Added" | "Removed" | "Modified";
  location: FileCompareLocation;
  originalText?: string;
  currentText?: string;
  displayLocation?: string;
}

export interface FileComparePreviewResponse {
  fileType: number;
  items: FileCompareDiffItem[];
  addedCount: number;
  removedCount: number;
  modifiedCount: number;
  unchangedCount: number;
  totalCount: number;
}

const baseUrl = "/api/file-compare";

export const uploadCompareFiles = (fileA: File, fileB: File) => {
  const form = new FormData();
  form.append("fileA", fileA);
  form.append("fileB", fileB);
  return http.request<ApiResponse<FileCompareUploadResponse>>("post", `${baseUrl}/upload`, {
    data: form,
    headers: {
      "Content-Type": "multipart/form-data"
    }
  });
};

export const previewCompare = (data: FileComparePreviewRequest) => {
  return http.request<ApiResponse<FileComparePreviewResponse>>("post", `${baseUrl}/preview`, {
    data
  });
};

export const downloadCompare = (data: FileComparePreviewRequest) => {
  return http.request<Blob>(
    "post",
    `${baseUrl}/download`,
    {
      data,
      responseType: "blob"
    }
  );
};
