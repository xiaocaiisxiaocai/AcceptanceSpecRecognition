import { describe, expect, it } from "vitest";
import {
  SpecCleanupReason,
  SpecCleanupScanStatus
} from "@/api/spec-cleanup-types";
import {
  cleanupProgress,
  cleanupReasonLabel,
  cleanupStatusLabel,
  failedActionItemIds
} from "./cleanup-view";

describe("spec cleanup view model", () => {
  it("keeps migration history explicitly uncertain", () => {
    expect(
      cleanupReasonLabel(SpecCleanupReason.UntrackedHistoricalReferences)
    ).toBe("历史时间不可追溯");
  });

  it("reports server progress without exceeding 100 percent", () => {
    expect(cleanupProgress(20, 80)).toBe(25);
    expect(cleanupProgress(120, 100)).toBe(100);
    expect(cleanupProgress(0, 0)).toBe(100);
  });

  it("labels terminal and running states", () => {
    expect(cleanupStatusLabel(SpecCleanupScanStatus.Running)).toBe("扫描中");
    expect(cleanupStatusLabel(SpecCleanupScanStatus.Failed)).toBe("扫描失败");
  });

  it("keeps only failed rows selected after a batch action", () => {
    expect(
      failedActionItemIds([
        { itemId: 101, success: true },
        { itemId: 102, success: false },
        { itemId: 103, success: true }
      ])
    ).toEqual([102]);
  });
});
