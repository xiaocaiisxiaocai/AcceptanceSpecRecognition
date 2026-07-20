import { describe, expect, it } from "vitest";
import { syncPrimaryBatchTableRegion } from "./batchTableConfig.helpers";
import type { BatchTableConfig } from "@/api/matching";

const config = (): BatchTableConfig => ({
  tableIndex: 0,
  projectColumnIndex: 2,
  specificationColumnIndex: 3,
  acceptanceColumnIndex: 8,
  remarkColumnIndex: 9,
  headerRowStart: 8,
  headerRowCount: 1,
  dataStartRow: 9,
  dataEndRow: 112,
  regions: [
    {
      regionId: "table-0-region-0",
      regionIndex: 0,
      projectColumnIndex: 0,
      specificationColumnIndex: 1,
      acceptanceColumnIndex: 2,
      remarkColumnIndex: 3,
      headerRowStart: 1,
      headerRowCount: 1,
      dataStartRow: 2,
      dataEndRow: 20
    },
    {
      regionId: "table-0-region-1",
      regionIndex: 1,
      projectColumnIndex: 2,
      specificationColumnIndex: 3,
      acceptanceColumnIndex: 8,
      remarkColumnIndex: 9,
      headerRowStart: 125,
      headerRowCount: 2,
      dataStartRow: 128,
      dataEndRow: 143
    }
  ]
});

describe("batch table config multi-region projection", () => {
  it("旧版表级高级设置只更新主区域并保留后续区域", () => {
    const source = config();
    const second = source.regions![1];

    const updated = syncPrimaryBatchTableRegion(source);

    expect(updated.regions).toHaveLength(2);
    expect(updated.regions?.[0]).toMatchObject({
      projectColumnIndex: 2,
      specificationColumnIndex: 3,
      acceptanceColumnIndex: 8,
      remarkColumnIndex: 9,
      headerRowStart: 8,
      dataStartRow: 9,
      dataEndRow: 112
    });
    expect(updated.regions?.[1]).toEqual(second);
  });

  it("没有区域的旧单区域配置保持兼容", () => {
    const source = { ...config(), regions: undefined };
    expect(syncPrimaryBatchTableRegion(source)).toBe(source);
  });
});
