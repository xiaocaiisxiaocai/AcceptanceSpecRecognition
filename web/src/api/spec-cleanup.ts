import { http } from "@/utils/http";
import type { ApiResponse, PagedData } from "./customer";
import type {
  SpecCleanupCategory,
  SpecCleanupReason,
  SpecCleanupScanStatus
} from "./spec-cleanup-types";

export {
  SpecCleanupCategory,
  SpecCleanupReason,
  SpecCleanupScanStatus
} from "./spec-cleanup-types";

const baseUrl = "/api/spec-cleanup";

export interface SpecCleanupScanStatusResponse {
  id: string;
  status: SpecCleanupScanStatus;
  newItemGraceDays: number;
  unusedDays: number;
  totalCount: number;
  processedCount: number;
  recommendedCleanupCount: number;
  manualReviewCount: number;
  healthyCount: number;
  createdAtUtc: string;
  startedAtUtc?: string | null;
  completedAtUtc?: string | null;
  errorMessage?: string | null;
}

export interface SpecCleanupScanItem {
  id: number;
  acceptanceSpecId: number;
  project: string;
  specification: string;
  acceptance?: string | null;
  remark?: string | null;
  customerName: string;
  processName?: string | null;
  referenceVersion: number;
  currentReferenceCount: number;
  recordedReferenceCount: number;
  untrackedReferenceCount: number;
  lastReferencedAtUtc?: string | null;
  contentActivityAtUtc: string;
  category: SpecCleanupCategory;
  reason: SpecCleanupReason;
  reviewStatus: number;
}

export interface QuarantinedAcceptanceSpec {
  id: number;
  project: string;
  specification: string;
  acceptance?: string | null;
  remark?: string | null;
  customerName: string;
  processName?: string | null;
  referenceVersion: number;
  quarantinedAtUtc: string;
  quarantineExpiresAtUtc: string;
  quarantinedByUserId?: number | null;
  quarantineReason?: string | null;
  sourceScanId?: string | null;
}

export interface IgnoredAcceptanceSpec {
  id: number;
  project: string;
  specification: string;
  acceptance?: string | null;
  remark?: string | null;
  customerName: string;
  processName?: string | null;
  referenceVersion: number;
  ignoredAtUtc?: string | null;
  ignoredByUserId?: number | null;
  ignoreReason?: string | null;
}

export interface SpecCleanupActionResult {
  itemId: number;
  acceptanceSpecId?: number | null;
  success: boolean;
  message: string;
}

export interface SpecCleanupBatchResult {
  successCount: number;
  failedCount: number;
  items: SpecCleanupActionResult[];
}

export interface SpecCleanupActionItem {
  scanItemId: number;
  reason?: string;
}

export const startSpecCleanupScan = (data: {
  newItemGraceDays: number;
  unusedDays: number;
}) =>
  http.request<ApiResponse<SpecCleanupScanStatusResponse>>(
    "post",
    `${baseUrl}/scans`,
    { data }
  );

export const getSpecCleanupScanStatus = (scanId: string) =>
  http.request<ApiResponse<SpecCleanupScanStatusResponse>>(
    "get",
    `${baseUrl}/scans/${scanId}`
  );

export const getSpecCleanupScanItems = (
  scanId: string,
  params: { category: SpecCleanupCategory; page: number; pageSize: number }
) =>
  http.request<ApiResponse<PagedData<SpecCleanupScanItem>>>(
    "get",
    `${baseUrl}/scans/${scanId}/items`,
    { params }
  );

export const cancelSpecCleanupScan = (scanId: string) =>
  http.request<ApiResponse<void>>("post", `${baseUrl}/scans/${scanId}/cancel`);

export const keepSpecCleanupItems = (data: SpecCleanupActionItem[]) =>
  http.request<ApiResponse<SpecCleanupBatchResult>>(
    "post",
    `${baseUrl}/items/keep`,
    { data }
  );

export const ignoreSpecCleanupItems = (data: SpecCleanupActionItem[]) =>
  http.request<ApiResponse<SpecCleanupBatchResult>>(
    "post",
    `${baseUrl}/items/ignore`,
    { data }
  );

export const quarantineSpecCleanupItems = (data: SpecCleanupActionItem[]) =>
  http.request<ApiResponse<SpecCleanupBatchResult>>(
    "post",
    `${baseUrl}/items/quarantine`,
    { data }
  );

export const getQuarantinedSpecs = (params: {
  page: number;
  pageSize: number;
}) =>
  http.request<ApiResponse<PagedData<QuarantinedAcceptanceSpec>>>(
    "get",
    `${baseUrl}/quarantine`,
    { params }
  );

export const getIgnoredSpecs = (params: { page: number; pageSize: number }) =>
  http.request<ApiResponse<PagedData<IgnoredAcceptanceSpec>>>(
    "get",
    `${baseUrl}/ignored`,
    { params }
  );

export const unignoreSpecs = (specIds: number[]) =>
  http.request<ApiResponse<SpecCleanupBatchResult>>(
    "post",
    `${baseUrl}/ignored/restore`,
    { data: { specIds } }
  );

export const restoreQuarantinedSpecs = (specIds: number[]) =>
  http.request<ApiResponse<SpecCleanupBatchResult>>(
    "post",
    `${baseUrl}/quarantine/restore`,
    { data: { specIds } }
  );

export const permanentlyDeleteQuarantinedSpecs = (
  items: Array<{ specId: number; referenceVersion: number }>
) =>
  http.request<ApiResponse<SpecCleanupBatchResult>>(
    "post",
    `${baseUrl}/quarantine/permanent-delete`,
    { data: { items, confirmPermanentDelete: true } }
  );
