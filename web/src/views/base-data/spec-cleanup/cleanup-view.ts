import {
  SpecCleanupReason,
  SpecCleanupScanStatus
} from "@/api/spec-cleanup-types";

export const cleanupReasonLabel = (reason: SpecCleanupReason): string => {
  const labels: Record<SpecCleanupReason, string> = {
    [SpecCleanupReason.NeverReferenced]: "从未引用",
    [SpecCleanupReason.LongUnused]: "长期未引用",
    [SpecCleanupReason.UntrackedHistoricalReferences]: "历史时间不可追溯",
    [SpecCleanupReason.CurrentVersionNeverReferenced]: "当前版本尚未引用",
    [SpecCleanupReason.RecentlyChanged]: "近期新增或修改",
    [SpecCleanupReason.RecentlyUsed]: "近期仍在使用"
  };
  return labels[reason] ?? "未知";
};

export const cleanupStatusLabel = (status: SpecCleanupScanStatus): string => {
  const labels: Record<SpecCleanupScanStatus, string> = {
    [SpecCleanupScanStatus.Pending]: "等待扫描",
    [SpecCleanupScanStatus.Running]: "扫描中",
    [SpecCleanupScanStatus.Completed]: "扫描完成",
    [SpecCleanupScanStatus.Cancelled]: "已取消",
    [SpecCleanupScanStatus.Failed]: "扫描失败"
  };
  return labels[status] ?? "未扫描";
};

export const cleanupProgress = (processed: number, total: number): number =>
  total <= 0 ? 100 : Math.min(100, Math.round((processed / total) * 100));

export const failedActionItemIds = (
  items: ReadonlyArray<{ itemId: number; success: boolean }>
): number[] => items.filter(item => !item.success).map(item => item.itemId);
