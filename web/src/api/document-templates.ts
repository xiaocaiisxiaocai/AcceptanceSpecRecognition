import { http } from "@/utils/http";
import type { ApiResponse, PagedData } from "./customer";

export interface DocumentTemplateListItem {
  id: number;
  customerId: number;
  customerName: string;
  templateName: string;
  tableKind: string;
  recommendation: string;
  regionCount: number;
  usageCount: number;
  userModifiedStructure: boolean;
  confirmedAt?: string | null;
  lastUsedAt?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface DocumentTemplateRegion {
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
}

export interface DocumentTemplateDetail
  extends Omit<DocumentTemplateListItem, "regionCount"> {
  regions: DocumentTemplateRegion[];
}

export interface DocumentTemplateListParams {
  page?: number;
  pageSize?: number;
  customerId?: number;
  keyword?: string;
}

const baseUrl = "/api/document-templates";

export const getDocumentTemplates = (params: DocumentTemplateListParams) =>
  http.request<ApiResponse<PagedData<DocumentTemplateListItem>>>(
    "get",
    baseUrl,
    { params }
  );

export const getDocumentTemplate = (id: number) =>
  http.request<ApiResponse<DocumentTemplateDetail>>("get", `${baseUrl}/${id}`);

export const deleteDocumentTemplate = (id: number) =>
  http.request<ApiResponse<void>>("delete", `${baseUrl}/${id}`);
