import { http } from "@/utils/http";
import type { ApiResponse } from "./customer";

export type DashboardPeriodPreset = "last7" | "last30" | "custom";

export interface DashboardSummaryRequest {
  range?: DashboardPeriodPreset;
  from?: string;
  to?: string;
}

export interface DashboardSummary {
  periodPreset: DashboardPeriodPreset;
  periodStart: string;
  periodEnd: string;
  customerTotal: number;
  processTotal: number;
  specTotal: number;
  importedSpecCount: number;
  smartFillTaskCount: number;
  smartFillTotalRows: number;
  smartFillMatchedRows: number;
  smartFillAdoptedRows: number;
  matchingRate: number;
  adoptionRate: number;
}

const baseUrl = "/api/dashboard";

export const getDashboardSummary = (params?: DashboardSummaryRequest) => {
  return http.request<ApiResponse<DashboardSummary>>(
    "get",
    `${baseUrl}/summary`,
    { params }
  );
};
