import { http } from "@/utils/http";
import type { ApiResponse } from "./customer";

export type DashboardPeriodPreset = "last7" | "last30" | "custom";

export interface DashboardSummaryRequest {
  range?: DashboardPeriodPreset;
  from?: string;
  to?: string;
  orgUnitId?: number;
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
  dailyTrend: DashboardDailyTrend[];
  recentExecutions: DashboardRecentExecution[];
}

export interface DashboardDailyTrend {
  date: string;
  importedSpecCount: number;
  smartFillTaskCount: number;
}

export interface DashboardRecentExecution {
  id: number;
  taskId: string;
  taskType: string;
  sourceFileName: string;
  totalRowCount: number;
  adoptedRowCount: number;
  createdAt: string;
}

const baseUrl = "/api/dashboard";

export const getDashboardSummary = (
  params?: DashboardSummaryRequest,
  signal?: AbortSignal
) => {
  return http.request<ApiResponse<DashboardSummary>>(
    "get",
    `${baseUrl}/summary`,
    { params, signal }
  );
};
