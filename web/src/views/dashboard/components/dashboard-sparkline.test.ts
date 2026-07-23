import { describe, expect, it } from "vitest";
import { buildSparklineGeometry } from "./dashboard-sparkline";

describe("buildSparklineGeometry", () => {
  it("returns an empty geometry when the API has no trend data", () => {
    expect(buildSparklineGeometry([])).toEqual({ points: "", areaPath: "" });
  });

  it("keeps points inside the view box and sanitizes invalid values", () => {
    const result = buildSparklineGeometry([0, Number.NaN, -2, 8]);
    const coordinates = result.points
      .split(" ")
      .map(point => point.split(",").map(Number));

    expect(coordinates).toHaveLength(4);
    expect(
      coordinates.every(([x, y]) => x >= 0 && x <= 100 && y >= 0 && y <= 36)
    ).toBe(true);
    expect(result.areaPath).toMatch(/^M 0\.00 36 L /);
  });
});
