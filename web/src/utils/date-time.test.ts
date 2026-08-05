import { describe, expect, it } from "vitest";
import {
  formatApiUtcDateTime,
  normalizeApiUtcDateTime,
  parseApiUtcDateTime,
  toApiUtcDateTime
} from "./date-time";

describe("API UTC 时间契约", () => {
  it("将 MySQL 读回后缺少时区的时间恢复为 UTC", () => {
    expect(normalizeApiUtcDateTime("2026-08-05T05:34:48.123456")).toBe(
      "2026-08-05T05:34:48.123456Z"
    );
    expect(parseApiUtcDateTime("2026-08-05T05:34:48")?.toISOString()).toBe(
      "2026-08-05T05:34:48.000Z"
    );
    expect(
      formatApiUtcDateTime("2026-08-05T05:34:48", "zh-CN", {
        timeZone: "Asia/Shanghai",
        hour12: false
      })
    ).toBe("2026/8/5 13:34:48");
  });

  it("保留 API 已提供的 UTC 或偏移量", () => {
    expect(normalizeApiUtcDateTime("2026-08-05T05:34:48Z")).toBe(
      "2026-08-05T05:34:48Z"
    );
    expect(normalizeApiUtcDateTime("2026-08-05T13:34:48+08:00")).toBe(
      "2026-08-05T13:34:48+08:00"
    );
  });

  it("将用户选择的本地时间点转换成带 Z 的 UTC 查询参数", () => {
    const selected = new Date("2026-08-05T13:34:48+08:00");
    expect(toApiUtcDateTime(selected)).toBe("2026-08-05T05:34:48.000Z");
    expect(toApiUtcDateTime(new Date(Number.NaN))).toBeUndefined();
  });
});
